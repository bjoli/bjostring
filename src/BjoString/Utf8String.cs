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
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace BjoString;

/// <summary>
/// An immutable string of Unicode scalars, stored as UTF-8: Bjolang's
/// <c>string</c>.
/// </summary>
///
/// <remarks>
/// <para>
/// The bytes are always valid UTF-8. Every way in either validates, or
/// replaces what is not valid with U+FFFD, or builds from parts that are
/// valid already. That is what lets a cursor step by reading one lead byte,
/// lets the regex engine search the bytes as they are, and makes every
/// byte-level search land on scalar boundaries.
/// </para>
/// <para>
/// A string is a part of an array: the whole of it for a string that was
/// built, or the part a slice (<see cref="Slice"/>, <see cref="TrimSlice"/>,
/// <see cref="SplitSlices"/>) left, sharing the array of the string it was
/// taken from. A slice keeps that whole array alive, which is why only the
/// <c>Slice</c> operations share and <see cref="Copy"/> lets go.
/// <c>default</c> is the empty string.
/// </para>
/// <para>
/// The one field is the array itself when the string is all of it, and a
/// <see cref="Part"/> otherwise. A string is therefore the size of a
/// reference, as a .NET string is, and only a slice pays for its bounds: an
/// allocation when it is made, and a type test when it is read.
/// </para>
/// <para>
/// Cursors are offsets into the array, so a cursor found in a slice is a
/// cursor of the string it was taken from.
/// </para>
/// <para>
/// Order is byte order, which for UTF-8 is scalar order. It differs from
/// UTF-16 ordinal order only where an astral scalar meets U+E000..U+FFFF.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly partial struct Utf8String :
    IEquatable<Utf8String>, IComparable<Utf8String>, IComparable,
    IComparisonOperators<Utf8String, Utf8String, bool>
{
    /// <summary>The bounds of a slice in the array it shares.</summary>
    ///
    /// <remarks>
    /// <see cref="End"/> is laid out where an array keeps its length, the first
    /// word after the object's type, so that <see cref="EndOf"/> reads the end
    /// of either kind of string with one load and no type test.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit)]
    internal sealed class Part(byte[] bytes, int start, int length)
    {
        [FieldOffset(0)] public readonly int End = start + length;
        [FieldOffset(4)] public readonly int Start = start;
        [FieldOffset(8)] public readonly byte[] Bytes = bytes;

        public int Length => End - Start;
    }

    /// <summary>The first field of an object, which for an array is its length.</summary>
    private sealed class FirstInt
    {
#pragma warning disable CS0649 // Never written: only ever read through another object.
        public int Value;
#pragma warning restore CS0649
    }

    /// <summary>The past-the-end offset of a string with this data: the array's length or the part's end.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int EndOf(object? data) => data is null ? 0 : Unsafe.As<FirstInt>(data).Value;

    /// <summary>A <c>byte[]</c>, a <see cref="Part"/>, or null for the empty string.</summary>
    private readonly object? _data;

    /// <summary>Wraps bytes that are valid UTF-8 and that nothing else will write.</summary>
    internal Utf8String(byte[] bytes) => _data = bytes;

    /// <summary>Part of such bytes, already checked to be in range and on boundaries.</summary>
    internal Utf8String(byte[]? bytes, int start, int length) =>
        _data = bytes is null || (start == 0 && length == bytes.Length) ? bytes : new Part(bytes, start, length);

    public static Utf8String Empty => default;

    /// <summary>The text of a .NET string; an unpaired surrogate becomes U+FFFD.</summary>
    public static Utf8String FromUtf16(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(Utf8Ops.FromUtf16(s));
    }

    /// <summary>The text of UTF-16 code units; an unpaired surrogate becomes U+FFFD.</summary>
    public static Utf8String FromUtf16(ReadOnlySpan<char> s) => new(Utf8Ops.FromUtf16(s));

    /// <summary>A copy of UTF-8 bytes, which must be valid.</summary>
    public static Utf8String FromUtf8(ReadOnlySpan<byte> utf8)
    {
        if (!Utf8.IsValid(utf8))
        {
            throw new ArgumentException("The bytes are not valid UTF-8.", nameof(utf8));
        }
        return new(utf8.ToArray());
    }

    public static bool TryFromUtf8(ReadOnlySpan<byte> utf8, out Utf8String result)
    {
        if (!Utf8.IsValid(utf8))
        {
            result = default;
            return false;
        }
        result = new(utf8.ToArray());
        return true;
    }

    /// <summary>A copy of UTF-8 bytes with every invalid sequence replaced by U+FFFD.</summary>
    public static Utf8String FromUtf8Lossy(ReadOnlySpan<byte> utf8) => new(Utf8Ops.Lossy(utf8));

    public static Utf8String FromRune(Rune r)
    {
        var bytes = new byte[r.Utf8SequenceLength];
        r.EncodeToUtf8(bytes);
        return new(bytes);
    }

    /// <summary>
    /// A string of <paramref name="byteLength"/> bytes written in place by
    /// <paramref name="action"/>, as <c>string.Create</c> does, so that a
    /// producer needs no copy. The bytes must be valid UTF-8.
    /// </summary>
    public static Utf8String Create<TState>(int byteLength, TState state, SpanAction<byte, TState> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (byteLength == 0) return default;
        var bytes = new byte[byteLength];
        action(bytes, state);
        if (!Utf8.IsValid(bytes))
        {
            throw new ArgumentException("The bytes written are not valid UTF-8.", nameof(action));
        }
        return new(bytes);
    }

    /// <summary>
    /// The array, when the string is all of one: the path every member takes
    /// first. An exact type test, which is one compare where <c>is byte[]</c>
    /// would also have to admit an <c>sbyte[]</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWhole(object? data) => data is not null && data.GetType() == typeof(byte[]);

    internal object? Data => _data;

    /// <summary>The length in bytes: the storage length, not the number of scalars.</summary>
    public int ByteLength => IsWhole(_data) ? Unsafe.As<byte[]>(_data)!.Length : PartLength(_data);

    private static int PartLength(object? data) => data is Part p ? p.Length : 0;

    public bool IsEmpty => ByteLength == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> AsSpan() => IsWhole(_data) ? Unsafe.As<byte[]>(_data) : PartSpan(_data);

    // Without the span constructor's range check: the bounds were checked
    // when the slice was made.
    private static ReadOnlySpan<byte> PartSpan(object? data) =>
        data is Part p
            ? MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(p.Bytes), (nint)(uint)p.Start), p.Length)
            : default;

    /// <summary>The bytes between two cursors, the second exclusive.</summary>
    public ReadOnlySpan<byte> AsSpan(StringCursor start, StringCursor end)
    {
        CheckSpan(start, end, "AsSpan");
        return new(Bytes, start.Offset, end.Offset - start.Offset);
    }

    /// <summary>The bytes as memory, which is what the regex engine searches.</summary>
    public ReadOnlyMemory<byte> AsMemory() => new(Bytes, Start, ByteLength);

    /// <summary>
    /// The whole array the string is part of, which is what cursors index,
    /// and the string's bounds in it. Returned rather than given through
    /// <c>out</c>, and never through a call that takes the string's address,
    /// either of which would keep a loop's string and bounds in memory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Bounds GetBounds()
    {
        if (IsWhole(_data))
        {
            var b = Unsafe.As<byte[]>(_data)!;
            return new(b, 0, b.Length);
        }
        return PartBounds(_data);
    }

    internal static Bounds PartBounds(object? data) =>
        data is Part p ? new(p.Bytes, p.Start, p.End) : default;

    internal readonly struct Bounds(byte[]? bytes, int lo, int hi)
    {
        public readonly byte[]? Bytes = bytes;
        public readonly int Lo = lo;
        public readonly int Hi = hi;
    }

    internal byte[]? Bytes => _data is Part p ? p.Bytes : Unsafe.As<byte[]?>(_data);

    internal int Start => _data is Part p ? p.Start : 0;

    internal int End => EndOf(_data);

    /// <summary>The cursor at a byte index of <see cref="AsSpan()"/>, which must be on a boundary.</summary>
    internal StringCursor CursorAt(int index) => new(Start + index);

    /// <summary>The byte at a byte index.</summary>
    public byte ByteAt(int index) => AsSpan()[index];

    /// <summary>The number of scalars, counted: O(n), vectorized.</summary>
    public int Count() => Utf8Ops.CountScalars(AsSpan());

    public bool IsAscii() => Ascii.IsValid(AsSpan());

    public RuneEnumerator EnumerateRunes() => new(Bytes, Start, End);

    /// <summary>Whether this is all of its array, so that holding it holds nothing more.</summary>
    public bool IsCompact => _data is not Part;

    /// <summary>The same text in an array of its own: the string itself when it already is.</summary>
    public Utf8String Copy() => IsCompact ? this : IsEmpty ? default : new(AsSpan().ToArray());

    public bool Equals(Utf8String other) =>
        ReferenceEquals(_data, other._data) || AsSpan().SequenceEqual(other.AsSpan());

    /// <summary>
    /// Whether the text is exactly these bytes. What a match on a string
    /// literal compiles to, with the literal as a <c>u8</c> constant, so that
    /// matching allocates nothing.
    /// </summary>
    public bool ContentEquals(ReadOnlySpan<byte> utf8) => AsSpan().SequenceEqual(utf8);

    public override bool Equals(object? obj) => obj is Utf8String s && Equals(s);

    public override int GetHashCode() => Utf8Ops.Hash(AsSpan());

    public int CompareTo(Utf8String other) => Utf8Ops.Compare(AsSpan(), other.AsSpan());

    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        Utf8String s => CompareTo(s),
        _ => throw new ArgumentException("Not a Utf8String.", nameof(obj)),
    };

    public static bool operator ==(Utf8String a, Utf8String b) => a.Equals(b);
    public static bool operator !=(Utf8String a, Utf8String b) => !a.Equals(b);
    public static bool operator <(Utf8String a, Utf8String b) => a.CompareTo(b) < 0;
    public static bool operator >(Utf8String a, Utf8String b) => a.CompareTo(b) > 0;
    public static bool operator <=(Utf8String a, Utf8String b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Utf8String a, Utf8String b) => a.CompareTo(b) >= 0;

    /// <summary>The text as a .NET (UTF-16) string; allocates.</summary>
    public override string ToString() => Encoding.UTF8.GetString(AsSpan());

    /// <summary>Throws unless both cursors are in this string, on boundaries, and in order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckSpan(StringCursor start, StringCursor end, string op)
    {
        if (start.Offset > end.Offset)
        {
            throw new ArgumentException($"{op}: the start cursor is after the end cursor.", nameof(start));
        }
        var bounds = GetBounds();
        if (start.Offset < bounds.Lo || end.Offset > bounds.Hi)
        {
            throw new ArgumentOutOfRangeException(nameof(start), $"{op}: the cursors are outside the string.");
        }
        ReadOnlySpan<byte> b = bounds.Bytes;
        if (!Cursors.IsBoundary(b, start.Offset) || !Cursors.IsBoundary(b, end.Offset))
        {
            Cursors.ThrowNotBoundary(op);
        }
    }
}
