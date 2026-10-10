/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * As a special exception to the Mozilla Public License, version 2.0, if you
 * compile your application source code and portions of this software are
 * embedded into the generated object code or executable form as a normal
 * consequence of the compilation process (such as inline functions,
 * templates, generics, or macros), you may redistribute such embedded portions
 * in such object code or executable form without complying with the source code
 * availability requirements or notice obligations of Section 3 of the MPL 2.0.
 */

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace BjoString;

/// <summary>
/// A growable buffer of UTF-8 that becomes a <see cref="Utf8String"/>:
/// Bjolang's <c>StringBuilder</c>.
/// </summary>
///
/// <remarks>
/// <para>
/// Appends of strings, slices and scalars are valid by construction.
/// <see cref="AppendByte"/> is the exception, for scanners that copy input a
/// byte at a time: such bytes are only checked by <see cref="ToUtf8String"/>,
/// which throws if they did not form whole scalars.
/// </para>
/// <para>
/// The text is held in one buffer while it is short, which doubles as it
/// grows, up to <see cref="ChunkSize"/>. Past that, a full buffer is kept as a
/// chunk and a new one is started, so that what was written is never copied
/// again until the string is made, and every buffer stays below the size of
/// the large object heap. Doubling one array instead copied a 125 MB text
/// eighteen times over into arrays the collector had to find new memory for.
/// </para>
/// </remarks>
public sealed class Utf8StringBuilder
{
    /// <summary>The size of a buffer once the text has outgrown one, below the
    /// 85,000 bytes at which .NET puts an array on the large object heap.</summary>
    public const int ChunkSize = 64 * 1024;

    // The buffer being written, and how much of it is written.
    private byte[] _buffer;
    private int _length;

    // The full buffers before it, made when the first one is kept, so that a
    // short builder is three fields and no larger than it was.
    private Chunks? _more;

    // Where the unchecked bytes start, or -1 when every byte was checked.
    private int _unchecked = -1;

    public Utf8StringBuilder() : this(16) { }

    public Utf8StringBuilder(int capacity) => _buffer = new byte[Math.Clamp(capacity, 1, Array.MaxLength)];

    public int ByteLength => _more is null ? _length : _more.Prefix + _length;

    // The buffers before the one being written, oldest first, and the byte
    // offset at which each ends: buffer i holds the bytes from Ends[i-1] (0 for
    // the first) to Ends[i]. Prefix is the bytes before the one being written,
    // the last of Ends, and 0 when there are none.
    private sealed class Chunks
    {
        public byte[][] Buffers = new byte[8][];
        public int[] Ends = new int[8];
        public int Count;
        public int Prefix;
    }

    /// <summary>The byte at a byte index, for scanners that look back at what they wrote.</summary>
    public byte ByteAt(int index)
    {
        int inBuffer = _more is null ? index : index - _more.Prefix;
        if ((uint)inBuffer < (uint)_length)
        {
            return _buffer[inBuffer];
        }
        return ByteInChunk(index);
    }

    private byte ByteInChunk(int index)
    {
        var more = _more;
        if (more is null || (uint)index >= (uint)more.Prefix)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        // The first buffer whose end is past the index.
        int lo = 0, hi = more.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (more.Ends[mid] > index) hi = mid; else lo = mid + 1;
        }
        int start = lo == 0 ? 0 : more.Ends[lo - 1];
        return more.Buffers[lo][index - start];
    }

    /// <summary>
    /// The bytes so far, valid until the next append; for parsing a number in
    /// place. A builder that has started a second buffer is made one buffer
    /// again first.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan()
    {
        if (_more is not null)
        {
            Consolidate();
        }
        return _buffer.AsSpan(0, _length);
    }

    // --- Appending ---------------------------------------------------------

    public Utf8StringBuilder Append(Rune r)
    {
        int length = _length;
        byte[] buffer = _buffer;
        if (r.Value < 0x80 && (uint)length < (uint)buffer.Length)
        {
            buffer[length] = (byte)r.Value;
            _length = length + 1;
            return this;
        }
        return AppendRuneSlow(r);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Utf8StringBuilder AppendRuneSlow(Rune r)
    {
        // Reserved on its own: it may start a new buffer, which sets _length,
        // and `_length += ...` would have read it before.
        var room = Reserve(4);
        _length += r.EncodeToUtf8(room);
        return this;
    }

    public Utf8StringBuilder Append(Utf8String s) => AppendValid(s.AsSpan());

    /// <summary>Appends UTF-8 bytes, which must be valid.</summary>
    public Utf8StringBuilder AppendUtf8(ReadOnlySpan<byte> utf8)
    {
        if (!Utf8.IsValid(utf8))
        {
            throw new ArgumentException("The bytes are not valid UTF-8.", nameof(utf8));
        }
        return AppendValid(utf8);
    }

    /// <summary>Appends UTF-16 text; an unpaired surrogate becomes U+FFFD.</summary>
    public Utf8StringBuilder Append(ReadOnlySpan<char> utf16)
    {
        int n = Encoding.UTF8.GetByteCount(utf16);
        if (n <= _buffer.Length - _length || n <= ChunkSize)
        {
            var room = Reserve(n);
            _length += Encoding.UTF8.GetBytes(utf16, room);
            return this;
        }
        // Too large for a buffer: encoded once, and copied in as text is.
        byte[] rented = ArrayPool<byte>.Shared.Rent(n);
        int written = Encoding.UTF8.GetBytes(utf16, rented);
        AppendValid(rented.AsSpan(0, written));
        ArrayPool<byte>.Shared.Return(rented);
        return this;
    }

    public Utf8StringBuilder Append(string utf16) => Append(utf16.AsSpan());

    private Utf8StringBuilder AppendValid(ReadOnlySpan<byte> utf8)
    {
        int length = _length;
        byte[] buffer = _buffer;
        if (utf8.Length <= buffer.Length - length)
        {
            utf8.CopyTo(buffer.AsSpan(length));
            _length = length + utf8.Length;
            return this;
        }
        return AppendValidSlow(utf8);
    }

    // What fits goes into this buffer, and the rest into the next, so a long
    // text is copied once.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Utf8StringBuilder AppendValidSlow(ReadOnlySpan<byte> utf8)
    {
        if (_buffer.Length < ChunkSize)
        {
            // Still one buffer: it grows, as a short builder's does.
            Grow(utf8.Length);
            utf8.CopyTo(_buffer.AsSpan(_length));
            _length += utf8.Length;
            return this;
        }
        int room = _buffer.Length - _length;
        utf8[..room].CopyTo(_buffer.AsSpan(_length));
        _length += room;
        var rest = utf8[room..];
        StartBuffer(rest.Length);
        rest.CopyTo(_buffer);
        _length = rest.Length;
        return this;
    }

    /// <summary>Appends one byte, unchecked until <see cref="ToUtf8String"/>.</summary>
    public Utf8StringBuilder AppendByte(byte b)
    {
        if (b >= 0x80 && _unchecked < 0)
        {
            _unchecked = ByteLength;
        }
        int length = _length;
        byte[] buffer = _buffer;
        if ((uint)length < (uint)buffer.Length)
        {
            buffer[length] = b;
            _length = length + 1;
            return this;
        }
        Reserve(1)[0] = b;
        _length++;
        return this;
    }

    /// <summary>
    /// Appends one UTF-16 code unit, for a reader taking text a unit at a time
    /// from a .NET <c>TextReader</c>. A surrogate pair given in two calls
    /// becomes its scalar; a surrogate left unpaired makes
    /// <see cref="ToUtf8String"/> throw.
    /// </summary>
    public Utf8StringBuilder AppendUtf16Unit(char unit)
    {
        if (unit < 0x80)
        {
            return Append(new Rune(unit));
        }
        if (!char.IsSurrogate(unit))
        {
            return Append(new Rune(unit));
        }
        // A surrogate is held as the three bytes UTF-8 would give it if it
        // were a scalar (as WTF-8 does), which no valid string contains; a low
        // one meeting a held high one replaces it with their pair's four bytes.
        // The three bytes are reserved together, so they are always in one
        // buffer, and nothing is written after them before the low one comes.
        if (char.IsLowSurrogate(unit) && _length >= 3 && _buffer[_length - 3] == 0xED
            && (_buffer[_length - 2] & 0xF0) == 0xA0)
        {
            char high = (char)(0xD000 | ((_buffer[_length - 2] & 0x3F) << 6) | (_buffer[_length - 1] & 0x3F));
            _length -= 3;
            return Append(new Rune(high, unit));
        }
        var held = Reserve(3);
        if (_unchecked < 0)
        {
            _unchecked = ByteLength;
        }
        held[0] = (byte)(0xE0 | (unit >> 12));
        held[1] = (byte)(0x80 | ((unit >> 6) & 0x3F));
        held[2] = (byte)(0x80 | (unit & 0x3F));
        _length += 3;
        return this;
    }

    // Numbers in the invariant culture, written straight into the buffer.
    public Utf8StringBuilder Append(int n) => AppendFormatted(n);

    public Utf8StringBuilder Append(long n) => AppendFormatted(n);

    public Utf8StringBuilder Append(byte n) => AppendFormatted(n);

    public Utf8StringBuilder Append(double d) => AppendFormatted(d);

    private Utf8StringBuilder AppendFormatted<T>(T value) where T : IUtf8SpanFormattable
    {
        int room = 32;
        int written;
        while (!value.TryFormat(Reserve(room), out written, default, CultureInfo.InvariantCulture))
        {
            room *= 2;
        }
        _length += written;
        return this;
    }

    // --- Room --------------------------------------------------------------

    /// <summary>At least <paramref name="n"/> free bytes in one piece, at the
    /// end of the buffer being written.</summary>
    private Span<byte> Reserve(int n)
    {
        if (_buffer.Length - _length < n)
        {
            if (_buffer.Length < ChunkSize)
            {
                Grow(n);
            }
            else
            {
                StartBuffer(n);
            }
        }
        return _buffer.AsSpan(_length);
    }

    // The one buffer, doubled until it holds n more bytes, and no larger than
    // a chunk unless n itself is.
    private void Grow(int n)
    {
        int needed = checked(_length + n);
        int size = Math.Max(Math.Min(_buffer.Length * 2, ChunkSize), needed);
        var grown = GC.AllocateUninitializedArray<byte>(size);
        _buffer.AsSpan(0, _length).CopyTo(grown);
        _buffer = grown;
    }

    // The buffer being written is kept as a chunk, and a new one of at least
    // n bytes is started.
    private void StartBuffer(int n)
    {
        var more = _more ??= new Chunks();
        if (more.Count == more.Buffers.Length)
        {
            Array.Resize(ref more.Buffers, more.Count * 2);
            Array.Resize(ref more.Ends, more.Count * 2);
        }
        more.Prefix = checked(more.Prefix + _length);
        more.Buffers[more.Count] = _buffer;
        more.Ends[more.Count] = more.Prefix;
        more.Count++;
        _buffer = GC.AllocateUninitializedArray<byte>(Math.Max(n, ChunkSize));
        _length = 0;
    }

    // The chunks and the buffer, copied into one buffer of their own.
    private void Consolidate()
    {
        int total = ByteLength;
        var one = GC.AllocateUninitializedArray<byte>(Math.Max(total, 1));
        CopyTo(one);
        _buffer = one;
        _length = total;
        DropChunks();
    }

    private void CopyTo(Span<byte> target)
    {
        int at = 0;
        if (_more is { } more)
        {
            for (int i = 0; i < more.Count; i++)
            {
                int start = i == 0 ? 0 : more.Ends[i - 1];
                int count = more.Ends[i] - start;
                more.Buffers[i].AsSpan(0, count).CopyTo(target[at..]);
                at += count;
            }
        }
        _buffer.AsSpan(0, _length).CopyTo(target[at..]);
    }

    private void DropChunks() => _more = null;

    // --- Results -----------------------------------------------------------

    /// <summary>Empties the builder and keeps its last buffer, so that a reader
    /// building many short strings does not allocate one each time.</summary>
    public void Clear()
    {
        _length = 0;
        _unchecked = -1;
        DropChunks();
    }

    /// <summary>
    /// The text so far, copied. Throws if bytes from <see cref="AppendByte"/>
    /// left an invalid sequence, since no string may hold one.
    /// </summary>
    public Utf8String ToUtf8String()
    {
        byte[] bytes;
        if (_more is null)
        {
            if (_length == 0)
            {
                _unchecked = -1;
                return default;
            }
            // A short string is the common case, and ToArray is the fastest
            // way to make one; an array that is not zeroed only pays for a
            // large one.
            bytes = _buffer.AsSpan(0, _length).ToArray();
        }
        else
        {
            bytes = GC.AllocateUninitializedArray<byte>(ByteLength);
            CopyTo(bytes);
        }
        if (_unchecked >= 0)
        {
            if (!Utf8.IsValid(bytes.AsSpan(_unchecked)))
            {
                throw new InvalidOperationException(
                    "stringbuilder->string: the bytes added with stringbuilder-add-code! are not valid UTF-8.");
            }
            _unchecked = -1;
        }
        return new(bytes);
    }

    /// <summary>The text so far as a .NET string, invalid bytes as U+FFFD.</summary>
    public override string ToString() => Encoding.UTF8.GetString(AsSpan());
}
