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

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace BjoString;

/// <summary>
/// A position in a string: a byte offset at the start of a scalar, or the end.
/// </summary>
///
/// <remarks>
/// <para>
/// The offset is opaque, enforced by the assembly boundary: it is internal,
/// visible only to Bjolang's runtime (which turns regex byte offsets into
/// cursors) and to the tests. So a cursor is only made by
/// <see cref="Start"/>/<see cref="End"/>, a search, or a step, and is on a
/// boundary by construction.
/// </para>
/// <para>
/// A cursor does not carry its string, so a cursor from another string can be
/// passed. The steps check that they start on a boundary, which costs one
/// compare on the non-ASCII path and keeps such a mistake from producing
/// invalid UTF-8 through <see cref="Substring"/>.
/// </para>
/// <para>
/// The offset is into the array the string is part of, so a slice uses the
/// cursors of the string it was taken from, and a cursor found in a slice can
/// be used on that string.
/// </para>
/// </remarks>
public readonly record struct StringCursor : IComparable<StringCursor>, IComparisonOperators<StringCursor, StringCursor, bool>
{
    internal int Offset { get; }

    internal StringCursor(int offset) => Offset = offset;

    /// <summary>A cursor on the first scalar.</summary>
    public static StringCursor Start(Utf8String s) => new(s.Start);

    /// <summary>The past-the-end cursor, the only one <see cref="AtEnd"/> answers true for.</summary>
    public static StringCursor End(Utf8String s) => new(s.End);

    // Each step takes a string that is all of its array inline, as the loops
    // that call it want, and a slice out of line, so that a loop over the
    // first carries no code for the second.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AtEnd(Utf8String s, StringCursor c) => c.Offset >= Utf8String.EndOf(s.Data);

    /// <summary>The scalar at the cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        if (Utf8String.IsWhole(d))
        {
            var a = Unsafe.As<byte[]>(d)!;
            return Cursors.Ref(a, 0, a.Length, c.Offset);
        }
        return RefPart(d, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rune RefPart(object? d, StringCursor c) =>
        d is Utf8String.Part p ? Cursors.Ref(p.Bytes, p.Start, p.End, c.Offset) : Cursors.Ref(default, 0, 0, c.Offset);

    /// <summary>The cursor on the next scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Next(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        if (Utf8String.IsWhole(d))
        {
            var a = Unsafe.As<byte[]>(d)!;
            return new(Cursors.Next(a, 0, a.Length, c.Offset));
        }
        return NextPart(d, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringCursor NextPart(object? d, StringCursor c) =>
        new(d is Utf8String.Part p ? Cursors.Next(p.Bytes, p.Start, p.End, c.Offset) : Cursors.Next(default, 0, 0, c.Offset));

    /// <summary>The cursor on the previous scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Prev(Utf8String s, StringCursor c)
    {
        var b = s.GetBounds();
        return new(Cursors.Prev(b.Bytes, b.Lo, b.Hi, c.Offset));
    }

    /// <summary>
    /// The scalar at the cursor and the cursor after it: one bounds check and
    /// one read of the lead byte for both.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        if (Utf8String.IsWhole(d))
        {
            var a = Unsafe.As<byte[]>(d)!;
            return Cursors.RefNext(a, 0, a.Length, c.Offset);
        }
        return RefNextPart(d, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Rune, StringCursor) RefNextPart(object? d, StringCursor c) =>
        d is Utf8String.Part p ? Cursors.RefNext(p.Bytes, p.Start, p.End, c.Offset) : Cursors.RefNext(default, 0, 0, c.Offset);

    /// <summary>The text between two cursors, the second exclusive.</summary>
    public static Utf8String Substring(Utf8String s, StringCursor start, StringCursor end) =>
        s.Substring(start, end);

    /// <summary>The number of scalars.</summary>
    public static int Count(Utf8String s) => s.Count();

    // Meaningful only between cursors on the same string, as indices are.
    public int CompareTo(StringCursor other) => Offset.CompareTo(other.Offset);

    public static bool operator <(StringCursor a, StringCursor b) => a.Offset < b.Offset;
    public static bool operator >(StringCursor a, StringCursor b) => a.Offset > b.Offset;
    public static bool operator <=(StringCursor a, StringCursor b) => a.Offset <= b.Offset;
    public static bool operator >=(StringCursor a, StringCursor b) => a.Offset >= b.Offset;

    public override string ToString() => $"#<string-cursor {Offset}>";
}

/// <summary>
/// Cursor steps over the bytes <c>[lo, hi)</c> of a string's array.
/// </summary>
internal static class Cursors
{
    // A read without a range check, for an index the step has just checked
    // against [lo, hi), which a string's bounds keep inside its array. The
    // second byte of a two-byte scalar is inside as well: the string is valid
    // UTF-8 and ends on a boundary.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte At(ReadOnlySpan<byte> s, int i) => Unsafe.Add(ref MemoryMarshal.GetReference(s), (nint)(uint)i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(ReadOnlySpan<byte> s, int lo, int hi, int i)
    {
        if ((uint)(i - lo) >= (uint)(hi - lo))
        {
            ThrowOutside("string-cursor-ref", i, hi);
        }
        uint b = At(s, i);
        if (b < 0x80)
        {
            return Unsafe.BitCast<uint, Rune>(b);
        }
        if (b - 0xC2 < 0x1E)
        {
            return Unsafe.BitCast<uint, Rune>(((b & 0x1F) << 6) | (At(s, i + 1) & 0x3Fu));
        }
        return DecodeAt(s, i, "string-cursor-ref").Rune;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(ReadOnlySpan<byte> s, int lo, int hi, int i)
    {
        if ((uint)(i - lo) >= (uint)(hi - lo))
        {
            ThrowOutside("string-cursor-next", i, hi);
        }
        byte b = At(s, i);
        if (b < 0x80)
        {
            return i + 1;
        }
        CheckLead(b, "string-cursor-next");
        return i + Utf8Ops.Width(b);
    }

    /// <summary>Steps back over continuation bytes, which lands on a boundary even from inside a scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Prev(ReadOnlySpan<byte> s, int lo, int hi, int i)
    {
        if ((uint)(i - lo - 1) >= (uint)(hi - lo))
        {
            ThrowBeforeStart(i, lo);
        }
        i--;
        while (i > lo && Utf8Ops.IsContinuation(At(s, i)))
        {
            i--;
        }
        return i;
    }

    /// <summary>The scalar and the offset after it, returned together in one register.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(ReadOnlySpan<byte> s, int lo, int hi, int i)
    {
        if ((uint)(i - lo) >= (uint)(hi - lo))
        {
            ThrowOutside("string-cursor-ref+next", i, hi);
        }
        uint b = At(s, i);
        if (b < 0x80)
        {
            return (Unsafe.BitCast<uint, Rune>(b), new(i + 1));
        }
        if (b - 0xC2 < 0x1E)
        {
            return (Unsafe.BitCast<uint, Rune>(((b & 0x1F) << 6) | (At(s, i + 1) & 0x3Fu)), new(i + 2));
        }
        var (r, width) = DecodeAt(s, i, "string-cursor-ref+next");
        return (r, new(i + width));
    }

    // Out of line: the ASCII and two-byte paths are what the loops inline.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Rune Rune, int Width) DecodeAt(ReadOnlySpan<byte> s, int i, string op)
    {
        if (Utf8Ops.IsContinuation(s[i]))
        {
            ThrowNotBoundary(op);
        }
        return Utf8Ops.DecodeMulti(s, i);
    }

    /// <summary>Whether a byte offset in <c>[0, length]</c> is on a scalar boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBoundary(ReadOnlySpan<byte> s, int i) =>
        (uint)i <= (uint)s.Length && (i == s.Length || !Utf8Ops.IsContinuation(s[i]));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckLead(byte b, string op)
    {
        if (Utf8Ops.IsContinuation(b))
        {
            ThrowNotBoundary(op);
        }
    }

    // The throws are out of line so that the steps stay small enough to inline
    // into the loops that call them, which is every string traversal. Not
    // NoInlining: the JIT has to look inside to learn that they never return,
    // and only then drops the bounds checks they already made.
    [DoesNotReturn]
    private static void ThrowOutside(string op, int i, int hi) =>
        throw new ArgumentOutOfRangeException("c", i >= hi
            ? $"{op}: the cursor is at the end of the string. Guard with (string-cursor-end? s c)."
            : $"{op}: the cursor is outside the slice.");

    [DoesNotReturn]
    private static void ThrowBeforeStart(int i, int lo) =>
        throw new ArgumentOutOfRangeException("c", i <= lo
            ? "string-cursor-prev: the cursor is at the start of the string."
            : "string-cursor-prev: the cursor is outside the slice.");

    [DoesNotReturn]
    public static void ThrowNotBoundary(string op) =>
        throw new ArgumentException(
            $"{op}: the cursor is not on a character boundary of this string; was it made from another string?", "c");
}
