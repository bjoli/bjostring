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
[NonUserCode]
public readonly record struct StringCursor : IComparable<StringCursor>, IComparisonOperators<StringCursor, StringCursor, bool>
{
    internal int Offset { get; }

    internal StringCursor(int offset) => Offset = offset;

    /// <summary>A cursor on the first scalar.</summary>
    public static StringCursor Start(Utf8String s) => new(s.Start);

    /// <summary>The past-the-end cursor, the only one <see cref="AtEnd"/> answers true for.</summary>
    public static StringCursor End(Utf8String s) => new(s.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AtEnd(Utf8String s, StringCursor c) => c.Offset >= Utf8String.EndOf(s.Data);

    /// <summary>The scalar at the cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        int i = c.Offset;
        byte[]? a;
        int hi;
        if (Utf8String.IsWhole(d))
        {
            a = Unsafe.As<byte[]>(d)!;
            hi = a.Length;
        }
        else if (d is not null)
        {
            var p = Unsafe.As<Utf8String.Part>(d);
            a = p.Bytes;
            hi = p.End;
            if (i < p.Start) i = -1;
        }
        else
        {
            a = null;
            hi = 0;
        }
        return Cursors.Ref(a!, hi, i);
    }

    /// <summary>The cursor on the next scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Next(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        int i = c.Offset;
        byte[]? a;
        int hi;
        if (Utf8String.IsWhole(d))
        {
            a = Unsafe.As<byte[]>(d)!;
            hi = a.Length;
        }
        else if (d is not null)
        {
            var p = Unsafe.As<Utf8String.Part>(d);
            a = p.Bytes;
            hi = p.End;
            if (i < p.Start) i = -1;
        }
        else
        {
            a = null;
            hi = 0;
        }
        return new(Cursors.Next(a!, hi, i));
    }

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
        int i = c.Offset;
        byte[]? a;
        int hi;
        if (Utf8String.IsWhole(d))
        {
            a = Unsafe.As<byte[]>(d)!;
            hi = a.Length;
        }
        else if (d is not null)
        {
            var p = Unsafe.As<Utf8String.Part>(d);
            a = p.Bytes;
            hi = p.End;
            if (i < p.Start) i = -1;
        }
        else
        {
            a = null;
            hi = 0;
        }
        return Cursors.RefNext(a!, hi, i);
    }

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
[NonUserCode]
internal static class Cursors
{
    // A read without a range check, for an index the step has just checked
    // against [lo, hi), which a string's bounds keep inside its array.
    // The continuation bytes of a multi-byte scalar are inside as well: the
    // string is valid UTF-8 and ends on a boundary.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte At(byte[] s, int i) =>
        Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(byte[] s, int hi, int i)
    {
        if ((uint)i >= (uint)hi)
        {
            ThrowOutside("string-cursor-ref", i, hi);
        }
        uint b = At(s, i);
        if (b < 0x80)
        {
            return Unsafe.BitCast<uint, Rune>(b);
        }
        if (b < 0xE0)
        {
            if (b < 0xC2)
            {
                ThrowNotBoundary("string-cursor-ref");
            }
            return Unsafe.BitCast<uint, Rune>(((b & 0x1F) << 6) | (At(s, i + 1) & 0x3Fu));
        }
        if (b < 0xF0)
        {
            return Unsafe.BitCast<uint, Rune>(((b & 0x0F) << 12) | ((At(s, i + 1) & 0x3Fu) << 6) | (At(s, i + 2) & 0x3Fu));
        }
        return Unsafe.BitCast<uint, Rune>(((b & 0x07) << 18) | ((At(s, i + 1) & 0x3Fu) << 12) | ((At(s, i + 2) & 0x3Fu) << 6) | (At(s, i + 3) & 0x3Fu));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(byte[] s, int hi, int i)
    {
        if ((uint)i >= (uint)hi)
        {
            ThrowOutside("string-cursor-next", i, hi);
        }
        uint b = At(s, i);
        if (b < 0x80)
        {
            return i + 1;
        }
        if (b < 0xC2)
        {
            ThrowNotBoundary("string-cursor-next");
        }
        return i + (b < 0xE0 ? 2 : b < 0xF0 ? 3 : 4);
    }

    /// <summary>Steps back over continuation bytes, which lands on a boundary even from inside a scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Prev(byte[]? s, int lo, int hi, int i)
    {
        if ((uint)(i - lo - 1) >= (uint)(hi - lo))
        {
            ThrowBeforeStart(i, lo);
        }
        i--;
        while (i > lo && Utf8Ops.IsContinuation(At(s!, i)))
        {
            i--;
        }
        return i;
    }

    /// <summary>The scalar and the offset after it, returned together in one register.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(byte[] s, int hi, int i)
    {
        if ((uint)i >= (uint)hi)
        {
            ThrowOutside("string-cursor-ref+next", i, hi);
        }
        uint b = At(s, i);
        if (b < 0x80)
        {
            return (Unsafe.BitCast<uint, Rune>(b), new(i + 1));
        }
        if (b < 0xE0)
        {
            if (b < 0xC2)
            {
                ThrowNotBoundary("string-cursor-ref+next");
            }
            return (Unsafe.BitCast<uint, Rune>(((b & 0x1F) << 6) | (At(s, i + 1) & 0x3Fu)), new(i + 2));
        }
        if (b < 0xF0)
        {
            return (Unsafe.BitCast<uint, Rune>(((b & 0x0F) << 12) | ((At(s, i + 1) & 0x3Fu) << 6) | (At(s, i + 2) & 0x3Fu)), new(i + 3));
        }
        return (Unsafe.BitCast<uint, Rune>(((b & 0x07) << 18) | ((At(s, i + 1) & 0x3Fu) << 12) | ((At(s, i + 2) & 0x3Fu) << 6) | (At(s, i + 3) & 0x3Fu)), new(i + 4));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(byte[]? s, int lo, int hi, int i) =>
        RefNext(s!, i >= lo ? hi : 0, i);

    /// <summary>Whether a byte offset in <c>[0, length]</c> is on a scalar boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBoundary(ReadOnlySpan<byte> s, int i) =>
        (uint)i <= (uint)s.Length && (i == s.Length || !Utf8Ops.IsContinuation(s[i]));

    // The throws are out of line so that the steps stay small enough to inline
    // into the loops that call them, which is every string traversal. Not
    // NoInlining: the JIT has to look inside to learn that they never return,
    // and only then drops the bounds checks they already made.
    [DoesNotReturn]
    internal static void ThrowOutside(string op, int i, int hi) =>
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
