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
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Text.Unicode;

namespace BjoString;

/// <summary>
/// The algorithms behind <see cref="Utf8String"/> and <see cref="StringSlice"/>,
/// on spans of valid UTF-8 so that both types share them. Searching, comparing
/// and copying go to .NET's vectorized span routines; only what has no such
/// routine (counting scalars, decoding at a boundary, case and trim) is here.
/// </summary>
internal static class Utf8Ops
{
    /// <summary>The width of the sequence a lead byte starts.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Width(byte lead) =>
        lead < 0x80 ? 1 : BitOperations.LeadingZeroCount((uint)(byte)~lead) - 24;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsContinuation(byte b) => (b & 0xC0) == 0x80;

    /// <summary>
    /// Decodes the scalar at a boundary of valid UTF-8. No checks beyond the
    /// span's own, because validity was proven when the string was made.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rune Decode(ReadOnlySpan<byte> s, int i, out int width)
    {
        uint b0 = s[i];
        if (b0 < 0x80)
        {
            width = 1;
            return Unsafe.BitCast<uint, Rune>(b0);
        }
        (Rune r, width) = DecodeMulti(s, i);
        return r;
    }

    /// <summary>
    /// Decodes a sequence of two to four bytes. The pair comes back in one
    /// register; an <c>out</c> width would keep a caller's loop state on the stack.
    /// </summary>
    public static (Rune Rune, int Width) DecodeMulti(ReadOnlySpan<byte> s, int i)
    {
        uint b0 = s[i];
        int width = Width((byte)b0);
        // One check for the whole sequence, so the reads below need none; a
        // valid string at a boundary never fails it.
        if ((uint)(width - 2) > 2 || (uint)(i + width) > (uint)s.Length)
        {
            ThrowInvalid();
        }
        ref byte p = ref Unsafe.Add(ref MemoryMarshal.GetReference(s), i);
        uint b1 = Unsafe.Add(ref p, 1) & 0x3Fu;
        if (width == 2)
        {
            return (Unsafe.BitCast<uint, Rune>(((b0 & 0x1F) << 6) | b1), 2);
        }
        uint b2 = Unsafe.Add(ref p, 2) & 0x3Fu;
        if (width == 3)
        {
            return (Unsafe.BitCast<uint, Rune>(((b0 & 0x0F) << 12) | (b1 << 6) | b2), 3);
        }
        uint b3 = Unsafe.Add(ref p, 3) & 0x3Fu;
        return (Unsafe.BitCast<uint, Rune>(((b0 & 0x07) << 18) | (b1 << 12) | (b2 << 6) | b3), 4);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowInvalid() =>
        throw new InvalidOperationException("Decoding UTF-8 off a scalar boundary or past the end.");

    /// <summary>The start of the scalar that ends at <paramref name="end"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StartOfLast(ReadOnlySpan<byte> s, int end)
    {
        int i = end - 1;
        while (IsContinuation(s[i]))
        {
            i--;
        }
        return i;
    }

    /// <summary>The number of scalars: the bytes that are not continuation bytes.</summary>
    public static int CountScalars(ReadOnlySpan<byte> s)
    {
        ref byte r = ref MemoryMarshal.GetReference(s);
        int n = 0;
        int i = 0;
        // Continuation bytes are 0x80..0xBF, which as signed bytes is everything up to -65.
        if (Vector256.IsHardwareAccelerated && s.Length >= Vector256<byte>.Count)
        {
            var limit = Vector256.Create((sbyte)-65);
            for (; i <= s.Length - Vector256<byte>.Count; i += Vector256<byte>.Count)
            {
                var v = Vector256.LoadUnsafe(ref r, (nuint)i).AsSByte();
                n += BitOperations.PopCount(Vector256.GreaterThan(v, limit).ExtractMostSignificantBits());
            }
        }
        else if (Vector128.IsHardwareAccelerated && s.Length >= Vector128<byte>.Count)
        {
            var limit = Vector128.Create((sbyte)-65);
            for (; i <= s.Length - Vector128<byte>.Count; i += Vector128<byte>.Count)
            {
                var v = Vector128.LoadUnsafe(ref r, (nuint)i).AsSByte();
                n += BitOperations.PopCount(Vector128.GreaterThan(v, limit).ExtractMostSignificantBits());
            }
        }
        for (; i < s.Length; i++)
        {
            if ((sbyte)Unsafe.Add(ref r, i) > -65)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// A hash of the bytes alone, so that a slice and a string with the same
    /// text hash alike. Marvin (the randomized hash of <c>string</c>) reads them
    /// two at a time, and an odd last byte is mixed in after.
    /// </summary>
    public static int Hash(ReadOnlySpan<byte> s)
    {
        int h = string.GetHashCode(MemoryMarshal.Cast<byte, char>(s));
        return (s.Length & 1) == 0 ? h : HashCode.Combine(h, s[^1]);
    }

    /// <summary>Byte order, which for UTF-8 is scalar order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        // Most strings that differ do so in the first byte, which is cheaper
        // to check than to set up the vectorized compare.
        if (a.Length != 0 && b.Length != 0 && a[0] != b[0])
        {
            return a[0] - b[0];
        }
        return a.SequenceCompareTo(b);
    }

    /// <summary>The UTF-8 encoding of a scalar, for searching.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> Encode(Rune r, Span<byte> buffer) => buffer[..r.EncodeToUtf8(buffer)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOf(ReadOnlySpan<byte> s, Rune r)
    {
        if (r.Value < 0x80)
        {
            return s.IndexOf((byte)r.Value);
        }
        return s.IndexOf(Encode(r, stackalloc byte[4]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWhiteSpace(byte b) => b == (byte)' ' || (uint)(b - '\t') <= '\r' - '\t';

    /// <summary>The bytes left after trimming white space (as <see cref="Rune.IsWhiteSpace"/> says).</summary>
    public static (int Start, int End) TrimBounds(ReadOnlySpan<byte> s, bool start, bool end)
    {
        int lo = 0;
        int hi = s.Length;
        if (start)
        {
            while (lo < hi)
            {
                byte b = s[lo];
                if (b < 0x80)
                {
                    if (!IsAsciiWhiteSpace(b)) break;
                    lo++;
                }
                else
                {
                    Rune r = Decode(s, lo, out int w);
                    if (!Rune.IsWhiteSpace(r)) break;
                    lo += w;
                }
            }
        }
        if (end)
        {
            while (hi > lo)
            {
                byte b = s[hi - 1];
                if (b < 0x80)
                {
                    if (!IsAsciiWhiteSpace(b)) break;
                    hi--;
                }
                else
                {
                    int at = StartOfLast(s, hi);
                    if (!Rune.IsWhiteSpace(Decode(s, at, out _))) break;
                    hi = at;
                }
            }
        }
        return (lo, hi);
    }

    /// <summary>
    /// Upper- or lowercases every scalar in the invariant culture; null when
    /// nothing changes, so that the caller can return what it was given.
    /// </summary>
    public static byte[]? MapCase(ReadOnlySpan<byte> s, bool upper)
    {
        if (Ascii.IsValid(s))
        {
            byte lo = upper ? (byte)'a' : (byte)'A';
            if (s.IndexOfAnyInRange(lo, (byte)(lo + 25)) < 0) return null;
            var all = new byte[s.Length];
            MapAscii(s, all, upper);
            return all;
        }

        // A mapping can change the width (ı becomes I), so the result goes into
        // a buffer: runs of ASCII in bulk, the rest scalar by scalar.
        var buf = new ByteBuffer(stackalloc byte[256]);
        try
        {
            int i = 0;
            while (i < s.Length)
            {
                int run = s[i..].IndexOfAnyExceptInRange((byte)0, (byte)0x7F);
                if (run != 0)
                {
                    if (run < 0) run = s.Length - i;
                    MapAscii(s.Slice(i, run), buf.Reserve(run), upper);
                    buf.Advance(run);
                    i += run;
                    continue;
                }
                Rune r = Decode(s, i, out int w);
                buf.Append(upper ? Rune.ToUpperInvariant(r) : Rune.ToLowerInvariant(r));
                i += w;
            }
            return buf.Written.SequenceEqual(s) ? null : buf.ToArray();
        }
        finally
        {
            buf.Dispose();
        }
    }

    private static void MapAscii(ReadOnlySpan<byte> src, Span<byte> dst, bool upper)
    {
        if (upper)
        {
            Ascii.ToUpper(src, dst, out _);
        }
        else
        {
            Ascii.ToLower(src, dst, out _);
        }
    }

    /// <summary>The scalars in reverse order; the byte length does not change.</summary>
    public static byte[] Reverse(ReadOnlySpan<byte> s)
    {
        var r = new byte[s.Length];
        if (Ascii.IsValid(s))
        {
            s.CopyTo(r);
            r.AsSpan().Reverse();
            return r;
        }
        int i = 0;
        while (i < s.Length)
        {
            int w = Width(s[i]);
            s.Slice(i, w).CopyTo(r.AsSpan(s.Length - i - w));
            i += w;
        }
        return r;
    }

    /// <summary>
    /// Every occurrence of <paramref name="old"/> (not empty) replaced, sized
    /// exactly by counting first; null when there is none.
    /// </summary>
    public static byte[]? Replace(ReadOnlySpan<byte> s, ReadOnlySpan<byte> old, ReadOnlySpan<byte> replacement)
    {
        int n = s.Count(old);
        if (n == 0) return null;
        var r = new byte[checked(s.Length + n * (replacement.Length - old.Length))];
        int src = 0;
        int dst = 0;
        while (true)
        {
            int k = s[src..].IndexOf(old);
            if (k < 0) break;
            s.Slice(src, k).CopyTo(r.AsSpan(dst));
            dst += k;
            replacement.CopyTo(r.AsSpan(dst));
            dst += replacement.Length;
            src += k + old.Length;
        }
        s[src..].CopyTo(r.AsSpan(dst));
        return r;
    }

    /// <summary>
    /// The scalars of a .NET string as UTF-8. An unpaired surrogate becomes
    /// U+FFFD, as <see cref="Rune.DecodeFromUtf16"/> reads it.
    /// </summary>
    public static byte[] FromUtf16(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return [];
        var r = new byte[Encoding.UTF8.GetByteCount(s)];
        Encoding.UTF8.GetBytes(s, r);
        return r;
    }

    /// <summary>Invalid sequences replaced with U+FFFD, as <see cref="Encoding.UTF8"/> decodes them.</summary>
    public static byte[] Lossy(ReadOnlySpan<byte> s) =>
        Utf8.IsValid(s) ? s.ToArray() : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(s));
}

/// <summary>
/// A growable byte buffer that starts on the stack and moves to the array pool
/// past that, for results whose length is not known in advance.
/// </summary>
internal ref struct ByteBuffer
{
    private byte[]? _rented;
    private Span<byte> _span;
    private int _length;

    public ByteBuffer(Span<byte> initial)
    {
        _span = initial;
        _length = 0;
        _rented = null;
    }

    public readonly int Length => _length;

    public readonly ReadOnlySpan<byte> Written => _span[.._length];

    /// <summary>Room for <paramref name="n"/> more bytes; commit them with <see cref="Advance"/>.</summary>
    public Span<byte> Reserve(int n)
    {
        if (_span.Length - _length < n)
        {
            Grow(n);
        }
        return _span.Slice(_length, n);
    }

    public void Advance(int n) => _length += n;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Reserve(bytes.Length));
        _length += bytes.Length;
    }

    public void Append(Rune r)
    {
        if (r.Value < 0x80)
        {
            Reserve(1)[0] = (byte)r.Value;
            _length++;
            return;
        }
        _length += r.EncodeToUtf8(Reserve(4));
    }

    private void Grow(int n)
    {
        var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_span.Length * 2, checked(_length + n)));
        _span[.._length].CopyTo(bigger);
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
        }
        _rented = bigger;
        _span = bigger;
    }

    public readonly byte[] ToArray() => _span[.._length].ToArray();

    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
            _rented = null;
        }
    }
}
