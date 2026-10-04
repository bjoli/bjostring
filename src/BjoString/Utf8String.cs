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
/// A struct around one array, so a string costs one allocation, as a .NET
/// <c>string</c> does. <c>default</c> is the empty string, which is why every
/// member reads the array through <see cref="AsSpan()"/> (a null array is an
/// empty span).
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
    private readonly byte[]? _bytes;

    /// <summary>Wraps bytes that are valid UTF-8 and that nothing else will write.</summary>
    internal Utf8String(byte[] bytes) => _bytes = bytes;

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

    /// <summary>The length in bytes: the storage length, not the number of scalars.</summary>
    // Through the span, as every other member reads the array, so that the JIT
    // can share the null check between them.
    public int ByteLength => AsSpan().Length;

    public bool IsEmpty => ByteLength == 0;

    public ReadOnlySpan<byte> AsSpan() => new(_bytes);

    /// <summary>The bytes between two cursors, the second exclusive.</summary>
    public ReadOnlySpan<byte> AsSpan(StringCursor start, StringCursor end) =>
        AsSpan()[start.Offset..end.Offset];

    /// <summary>The bytes as memory, which is what the regex engine searches.</summary>
    public ReadOnlyMemory<byte> AsMemory() => new(_bytes);

    /// <summary>The whole string as a slice.</summary>
    public StringSlice AsSlice() => new(this, 0, ByteLength);

    public static implicit operator StringSlice(Utf8String s) => s.AsSlice();

    /// <summary>The byte at a byte index: the escape hatch for scanners that work in bytes.</summary>
    public byte ByteAt(int index) => AsSpan()[index];

    /// <summary>The number of scalars, counted: O(n), vectorized.</summary>
    public int Count() => Utf8Ops.CountScalars(AsSpan());

    public bool IsAscii() => Ascii.IsValid(AsSpan());

    public RuneEnumerator EnumerateRunes() => new(_bytes, 0, ByteLength);

    internal byte[]? Bytes => _bytes;

    public bool Equals(Utf8String other) =>
        ReferenceEquals(_bytes, other._bytes) || AsSpan().SequenceEqual(other.AsSpan());

    public bool Equals(StringSlice other) => AsSpan().SequenceEqual(other.AsSpan());

    /// <summary>
    /// Whether the text is exactly these bytes. What a match on a string
    /// literal compiles to, with the literal as a <c>u8</c> constant, so that
    /// matching allocates nothing.
    /// </summary>
    public bool ContentEquals(ReadOnlySpan<byte> utf8) => AsSpan().SequenceEqual(utf8);

    /// <summary>
    /// Equal to a string or a slice with the same text, both ways round, so
    /// that values compared as <c>object</c> agree with the typed comparison.
    /// </summary>
    public override bool Equals(object? obj) => obj switch
    {
        Utf8String s => Equals(s),
        StringSlice s => Equals(s),
        _ => false,
    };

    /// <summary>The same as the hash of a <see cref="StringSlice"/> with the same text.</summary>
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
    public override string ToString() => _bytes is null ? "" : Encoding.UTF8.GetString(_bytes);
}
