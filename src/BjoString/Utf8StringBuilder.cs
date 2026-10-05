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

using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace BjoString;

/// <summary>
/// A growable buffer of UTF-8 that becomes a <see cref="Utf8String"/>:
/// Bjolang's <c>StringBuilder</c>.
/// </summary>
///
/// <remarks>
/// Appends of strings, slices and scalars are valid by construction.
/// <see cref="AppendByte"/> is the exception, for scanners that copy input a
/// byte at a time: such bytes are only checked by <see cref="ToUtf8String"/>,
/// which throws if they did not form whole scalars.
/// </remarks>
public sealed class Utf8StringBuilder
{
    private byte[] _buffer;
    private int _length;
    // Where the unchecked bytes start, or -1 when every byte was checked.
    private int _unchecked = -1;

    public Utf8StringBuilder() : this(16) { }

    public Utf8StringBuilder(int capacity) => _buffer = new byte[Math.Max(capacity, 1)];

    public int ByteLength => _length;

    /// <summary>The byte at a byte index, for scanners that look back at what they wrote.</summary>
    public byte ByteAt(int index)
    {
        if ((uint)index >= (uint)_length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return _buffer[index];
    }

    /// <summary>The bytes so far, valid until the next append; for parsing a number in place.</summary>
    public ReadOnlySpan<byte> AsSpan() => _buffer.AsSpan(0, _length);

    private Span<byte> Reserve(int n)
    {
        if (_buffer.Length - _length < n)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, checked(_length + n)));
        }
        return _buffer.AsSpan(_length);
    }

    public Utf8StringBuilder Append(Rune r)
    {
        if (r.Value < 0x80)
        {
            Reserve(1)[0] = (byte)r.Value;
            _length++;
        }
        else
        {
            _length += r.EncodeToUtf8(Reserve(4));
        }
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
        _length += Encoding.UTF8.GetBytes(utf16, Reserve(n));
        return this;
    }

    public Utf8StringBuilder Append(string utf16) => Append(utf16.AsSpan());

    private Utf8StringBuilder AppendValid(ReadOnlySpan<byte> utf8)
    {
        utf8.CopyTo(Reserve(utf8.Length));
        _length += utf8.Length;
        return this;
    }

    /// <summary>Appends one byte, unchecked until <see cref="ToUtf8String"/>.</summary>
    public Utf8StringBuilder AppendByte(byte b)
    {
        if (b >= 0x80 && _unchecked < 0)
        {
            _unchecked = _length;
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
            Reserve(1)[0] = (byte)unit;
            _length++;
            return this;
        }
        if (!char.IsSurrogate(unit))
        {
            return Append(new Rune(unit));
        }
        // A surrogate is held as the three bytes UTF-8 would give it if it
        // were a scalar (as WTF-8 does), which no valid string contains; a low
        // one meeting a held high one replaces it with their pair's four bytes.
        if (char.IsLowSurrogate(unit) && _length >= 3 && _buffer[_length - 3] == 0xED
            && (_buffer[_length - 2] & 0xF0) == 0xA0)
        {
            char high = (char)(0xD000 | ((_buffer[_length - 2] & 0x3F) << 6) | (_buffer[_length - 1] & 0x3F));
            _length -= 3;
            return Append(new Rune(high, unit));
        }
        if (_unchecked < 0)
        {
            _unchecked = _length;
        }
        var held = Reserve(3);
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

    public void Clear()
    {
        _length = 0;
        _unchecked = -1;
    }

    /// <summary>
    /// The text so far, copied. Throws if bytes from <see cref="AppendByte"/>
    /// left an invalid sequence, since no string may hold one.
    /// </summary>
    public Utf8String ToUtf8String()
    {
        if (_unchecked >= 0)
        {
            if (!Utf8.IsValid(_buffer.AsSpan(_unchecked, _length - _unchecked)))
            {
                throw new InvalidOperationException(
                    "stringbuilder->string: the bytes added with stringbuilder-add-code! are not valid UTF-8.");
            }
            _unchecked = -1;
        }
        return _length == 0 ? default : new(AsSpan().ToArray());
    }

    /// <summary>The text so far as a .NET string, invalid bytes as U+FFFD.</summary>
    public override string ToString() => Encoding.UTF8.GetString(AsSpan());
}
