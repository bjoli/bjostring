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
    // The offset must stay the only field: the steps make a cursor from an
    // offset with Unsafe.BitCast, which costs no inline (see Cursors).
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
        byte[]? a = Unsafe.As<byte[]>(d);
        int hi = 0;
        if (d is not null)
        {
            hi = Utf8String.EndOfNotNull(d);
            if (d.GetType() != typeof(byte[]))
            {
                var p = Unsafe.As<Utf8String.Part>(d);
                a = p.Bytes;
                if (i < p.Start) i = -1;
            }
        }
        return Cursors.Ref(a!, hi, i);
    }

    /// <summary>The cursor on the next scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Next(Utf8String s, StringCursor c)
    {
        var d = s.Data;
        int i = c.Offset;
        byte[]? a = Unsafe.As<byte[]>(d);
        int hi = 0;
        if (d is not null)
        {
            hi = Utf8String.EndOfNotNull(d);
            if (d.GetType() != typeof(byte[]))
            {
                var p = Unsafe.As<Utf8String.Part>(d);
                a = p.Bytes;
                if (i < p.Start) i = -1;
            }
        }
        return Unsafe.BitCast<int, StringCursor>(Cursors.Next(a!, hi, i));
    }

    /// <summary>The cursor on the previous scalar.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Prev(Utf8String s, StringCursor c)
    {
        var b = s.GetBounds();
        return Unsafe.BitCast<int, StringCursor>(Cursors.Prev(b.Bytes, b.Lo, b.Hi, c.Offset));
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
        byte[]? a = Unsafe.As<byte[]>(d);
        int hi = 0;
        if (d is not null)
        {
            hi = Utf8String.EndOfNotNull(d);
            if (d.GetType() != typeof(byte[]))
            {
                var p = Unsafe.As<Utf8String.Part>(d);
                a = p.Bytes;
                if (i < p.Start) i = -1;
            }
        }
        return Cursors.RefNext(a!, hi, i);
    }

    // The steps on the bounds of a string, taken once with StringBounds.Of.
    // They return and raise what the steps on the string return and raise. A
    // cursor below the slice is given as -1, so the range check rejects it
    // with the "outside the slice" message, as the steps on the string do.

    /// <summary><see cref="AtEnd(Utf8String, StringCursor)"/> on the bounds of the string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AtEnd(StringBounds b, StringCursor c) => c.Offset >= b.Hi;

    /// <summary><see cref="Ref(Utf8String, StringCursor)"/> on the bounds of the string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(StringBounds b, StringCursor c) =>
        Cursors.Ref(b.Bytes!, b.Hi, c.Offset >= b.Lo ? c.Offset : -1);

    /// <summary><see cref="Next(Utf8String, StringCursor)"/> on the bounds of the string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Next(StringBounds b, StringCursor c) =>
        Unsafe.BitCast<int, StringCursor>(Cursors.Next(b.Bytes!, b.Hi, c.Offset >= b.Lo ? c.Offset : -1));

    /// <summary><see cref="Prev(Utf8String, StringCursor)"/> on the bounds of the string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringCursor Prev(StringBounds b, StringCursor c) =>
        Unsafe.BitCast<int, StringCursor>(Cursors.Prev(b.Bytes, b.Lo, b.Hi, c.Offset));

    /// <summary><see cref="RefNext(Utf8String, StringCursor)"/> on the bounds of the string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(StringBounds b, StringCursor c) =>
        Cursors.RefNext(b.Bytes!, b.Hi, c.Offset >= b.Lo ? c.Offset : -1);

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
/// The array of a string and the string's bounds in it: what a cursor step
/// reads from the string.
/// </summary>
///
/// <remarks>
/// <list type="bullet">
/// <item>Purpose: a loop that steps over a string that does not change gets
/// the bounds once, before the loop, and gives them to the
/// <see cref="StringCursor"/> steps that take a <see cref="StringBounds"/>.
/// Each step then does not test again whether the string is a whole array or
/// a slice. Bjolang's compiler does this for its loops.</item>
/// <item>The fields are internal, so code outside BjoString and Bjolang's
/// runtime cannot get or write the array.</item>
/// <item>The bounds keep the array alive, as the string does.</item>
/// <item><c>default</c> is the bounds of the empty string.</item>
/// </list>
/// </remarks>
[NonUserCode]
public readonly struct StringBounds
{
    internal readonly byte[]? Bytes;
    internal readonly int Lo;
    internal readonly int Hi;

    internal StringBounds(byte[]? bytes, int lo, int hi)
    {
        Bytes = bytes;
        Lo = lo;
        Hi = hi;
    }

    /// <summary>The bounds of <paramref name="s"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBounds Of(Utf8String s) => s.GetBounds();
}

/// <summary>
/// Cursor steps over the bytes <c>[lo, hi)</c> of a string's array.
/// </summary>
///
/// <remarks>
/// <para>
/// The steps are inlined into every string loop. Each inline uses part of the
/// JIT's inline budget for the method that contains the loop, by the IL size
/// of the inlined method. When the budget is used up, the JIT keeps the
/// remaining helpers as calls, and the loop becomes several times slower. This
/// happens most when PGO inlines a method with a loop into a small caller. So
/// the steps are small, and call no helpers on the path that returns:
/// </para>
/// <list type="bullet">
/// <item>Bytes are read with <c>Unsafe.Add</c>, which the JIT expands in place.</item>
/// <item>A <see cref="Rune"/> and a <see cref="StringCursor"/> are made by <c>Unsafe.BitCast</c>, not by a constructor.</item>
/// <item>Each continuation byte is read once, by code that all widths share.</item>
/// <item><c>Ref</c> and <c>RefNext</c> have one exit, so the result is built once.</item>
/// </list>
/// <para>
/// Check a change here with the cursor loops of a Bjolang program under the
/// default JIT settings, and with <c>DOTNET_JitDisasm</c>. A loop that is
/// fast in a C# benchmark can still be over budget in Bjolang's code.
/// </para>
/// <para>
/// The reads are not range checked. The step checks the index against
/// <c>[lo, hi)</c> first, and a string's bounds are inside its array. The
/// continuation bytes of a multi-byte scalar are inside as well, because the
/// string is valid UTF-8 and ends on a boundary.
/// </para>
/// </remarks>
[NonUserCode]
internal static class Cursors
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Ref(byte[] s, int hi, int i)
    {
        if ((uint)i >= (uint)hi)
        {
            ThrowOutside("string-cursor-ref", i, hi);
        }
        uint b = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);
        if (b >= 0x80)
        {
            if (b < 0xC2)
            {
                ThrowNotBoundary("string-cursor-ref");
            }
            ref byte p = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);
            uint c1 = Unsafe.Add(ref p, 1) & 0x3Fu;
            if (b < 0xE0)
            {
                b = ((b & 0x1F) << 6) | c1;
            }
            else
            {
                uint c2 = Unsafe.Add(ref p, 2) & 0x3Fu;
                b = b < 0xF0
                    ? ((b & 0x0F) << 12) | (c1 << 6) | c2
                    : ((b & 0x07) << 18) | (c1 << 12) | (c2 << 6) | (Unsafe.Add(ref p, 3) & 0x3Fu);
            }
        }
        return Unsafe.BitCast<uint, Rune>(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(byte[] s, int hi, int i)
    {
        if ((uint)i >= (uint)hi)
        {
            ThrowOutside("string-cursor-next", i, hi);
        }
        uint b = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);
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
        ref byte p = ref MemoryMarshal.GetArrayDataReference(s!);
        i--;
        while (i > lo && (Unsafe.Add(ref p, (nint)(uint)i) & 0xC0) == 0x80)
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
        uint b = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);
        // `i` becomes the next offset on every path, so that the JIT can keep
        // it in the register of the caller's cursor.
        if (b < 0x80)
        {
            i++;
        }
        else
        {
            if (b < 0xC2)
            {
                ThrowNotBoundary("string-cursor-ref+next");
            }
            ref byte p = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s), (nint)(uint)i);
            uint c1 = Unsafe.Add(ref p, 1) & 0x3Fu;
            if (b < 0xE0)
            {
                b = ((b & 0x1F) << 6) | c1;
                i += 2;
            }
            else
            {
                uint c2 = Unsafe.Add(ref p, 2) & 0x3Fu;
                if (b < 0xF0)
                {
                    b = ((b & 0x0F) << 12) | (c1 << 6) | c2;
                    i += 3;
                }
                else
                {
                    b = ((b & 0x07) << 18) | (c1 << 12) | (c2 << 6) | (Unsafe.Add(ref p, 3) & 0x3Fu);
                    i += 4;
                }
            }
        }
        (Rune, StringCursor) result = default;
        result.Item1 = Unsafe.BitCast<uint, Rune>(b);
        result.Item2 = Unsafe.BitCast<int, StringCursor>(i);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Rune, StringCursor) RefNext(byte[]? s, int lo, int hi, int i) =>
        RefNext(s!, i >= lo ? hi : 0, i);

    /// <summary>Whether a byte offset in <c>[0, length]</c> is on a scalar boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBoundary(ReadOnlySpan<byte> s, int i) =>
        (uint)i <= (uint)s.Length && (i == s.Length || !Utf8Ops.IsContinuation(s[i]));

    // The throws are out of line so that the steps stay small enough to inline
    // into the loops that call them, which is every string traversal.
    // - The Throw* methods are not NoInlining: the JIT has to look inside to
    //   learn that they never return, and only then drops the bounds checks
    //   they already made. Their bodies are only a call and a throw, so this
    //   look uses almost none of the caller's inline budget.
    // - The exceptions are made by NoInlining methods, so that the message
    //   formatting is never copied into a caller.
    [DoesNotReturn]
    internal static void ThrowOutside(string op, int i, int hi) => throw Outside(op, i, hi);

    [DoesNotReturn]
    private static void ThrowBeforeStart(int i, int lo) => throw BeforeStart(i, lo);

    [DoesNotReturn]
    public static void ThrowNotBoundary(string op) => throw NotBoundary(op);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArgumentOutOfRangeException Outside(string op, int i, int hi) =>
        new("c", i >= hi
            ? $"{op}: the cursor is at the end of the string. Guard with (string-cursor-end? s c)."
            : $"{op}: the cursor is outside the slice.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArgumentOutOfRangeException BeforeStart(int i, int lo) =>
        new("c", i <= lo
            ? "string-cursor-prev: the cursor is at the start of the string."
            : "string-cursor-prev: the cursor is outside the slice.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArgumentException NotBoundary(string op) =>
        new($"{op}: the cursor is not on a character boundary of this string; was it made from another string?", "c");
}
