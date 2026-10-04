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

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace BjoString;

/// <summary>
/// A view of part of a <see cref="Utf8String"/>, on scalar boundaries, without
/// a copy: what trimming, splitting and parsing hand out.
/// </summary>
///
/// <remarks>
/// Its cursors are those of the string it slices, so a position found in a
/// slice is a position in the string. It equals, hashes and orders by its
/// text, the same as a string with that text, so a slice can look up a
/// string key (see <see cref="Utf8StringComparer"/>). Holding a slice keeps
/// the whole string alive.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct StringSlice :
    IEquatable<StringSlice>, IComparable<StringSlice>,
    IComparisonOperators<StringSlice, StringSlice, bool>, IUtf8Text
{
    private readonly Utf8String _source;
    private readonly int _start;
    private readonly int _length;

    /// <summary>Bounds already checked to be in range and on boundaries.</summary>
    internal StringSlice(Utf8String source, int start, int length)
    {
        _source = source;
        _start = start;
        _length = length;
    }

    public Utf8String Source => _source;

    /// <summary>A cursor on the first scalar of the slice.</summary>
    public StringCursor Start => new(_start);

    /// <summary>The past-the-end cursor of the slice.</summary>
    public StringCursor End => new(_start + _length);

    public int ByteLength => _length;

    public bool IsEmpty => _length == 0;

    public ReadOnlySpan<byte> AsSpan() => _source.AsSpan().Slice(_start, _length);

    public ReadOnlyMemory<byte> AsMemory() => _source.AsMemory().Slice(_start, _length);

    public int Count() => Utf8Ops.CountScalars(AsSpan());

    public bool IsAscii() => Ascii.IsValid(AsSpan());

    public RuneEnumerator EnumerateRunes() => new(_source.Bytes, _start, _start + _length);

    /// <summary>The text as a string: the source itself when the slice is all of it.</summary>
    public Utf8String ToUtf8String()
    {
        if (_length == _source.ByteLength) return _source;
        if (_length == 0) return default;
        return new(AsSpan().ToArray());
    }

    public bool AtEnd(StringCursor c) => c.Offset >= _start + _length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rune Ref(StringCursor c) => Cursors.Ref(_source.AsSpan(), _start, _start + _length, c.Offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StringCursor Next(StringCursor c) => new(Cursors.Next(_source.AsSpan(), _start, _start + _length, c.Offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StringCursor Prev(StringCursor c) => new(Cursors.Prev(_source.AsSpan(), _start, _start + _length, c.Offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (Rune, StringCursor) RefNext(StringCursor c) =>
        Cursors.RefNext(_source.AsSpan(), _start, _start + _length, c.Offset);

    /// <summary>The part between two cursors of this slice.</summary>
    public StringSlice Slice(StringCursor start, StringCursor end)
    {
        if (start.Offset < _start || end.Offset > _start + _length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The cursors are outside the slice.");
        }
        return _source.Slice(start, end);
    }

    public bool Contains(StringSlice needle) => AsSpan().IndexOf(needle.AsSpan()) >= 0;

    public bool Contains(Rune r) => Utf8Ops.IndexOf(AsSpan(), r) >= 0;

    public bool StartsWith(StringSlice prefix) => AsSpan().StartsWith(prefix.AsSpan());

    public bool EndsWith(StringSlice suffix) => AsSpan().EndsWith(suffix.AsSpan());

    public StringCursor? IndexOf(StringSlice needle)
    {
        int i = AsSpan().IndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(_start + i);
    }

    public StringCursor? IndexOf(Rune r)
    {
        int i = Utf8Ops.IndexOf(AsSpan(), r);
        return i < 0 ? null : new StringCursor(_start + i);
    }

    /// <summary>The first occurrence at or after <paramref name="from"/>, a cursor of this slice, or null.</summary>
    public StringCursor? IndexOf(StringSlice needle, StringCursor from)
    {
        if (from.Offset < _start || from.Offset > _start + _length)
        {
            throw new ArgumentOutOfRangeException(nameof(from), "The cursor is outside the slice.");
        }
        int i = _source.AsSpan()[from.Offset..(_start + _length)].IndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(from.Offset + i);
    }

    public StringCursor? LastIndexOf(StringSlice needle)
    {
        int i = AsSpan().LastIndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(_start + i);
    }

    public StringCursor? LastIndexOf(Rune r)
    {
        int i = Utf8Ops.LastIndexOf(AsSpan(), r);
        return i < 0 ? null : new StringCursor(_start + i);
    }

    /// <summary>The text between two cursors of this slice, copied.</summary>
    public Utf8String Substring(StringCursor start, StringCursor end) => Slice(start, end).ToUtf8String();

    // The operations below make new text, and return the source itself when
    // the slice is all of it and nothing changes.

    /// <summary>Every occurrence of <paramref name="old"/> replaced, left to right and not overlapping.</summary>
    public Utf8String Replace(StringSlice old, StringSlice replacement)
    {
        if (old.IsEmpty)
        {
            throw new ArgumentException("string-replace: the string to replace is empty.", nameof(old));
        }
        var r = Utf8Ops.Replace(AsSpan(), old.AsSpan(), replacement.AsSpan());
        return r is null ? ToUtf8String() : new(r);
    }

    /// <summary>
    /// The fields between separators, empty ones kept. An empty separator
    /// gives the whole text as the one field, as .NET's <c>Split</c> does.
    /// </summary>
    public Utf8String[] Split(StringSlice separator)
    {
        var sep = separator.AsSpan();
        if (sep.IsEmpty) return [ToUtf8String()];
        var fields = new Utf8String[AsSpan().Count(sep) + 1];
        int n = 0;
        foreach (var field in EnumerateSplit(separator))
        {
            fields[n++] = field.ToUtf8String();
        }
        return fields;
    }

    /// <summary>Each scalar uppercased in the invariant culture.</summary>
    public Utf8String ToUpperInvariant()
    {
        var r = Utf8Ops.MapCase(AsSpan(), upper: true);
        return r is null ? ToUtf8String() : new(r);
    }

    /// <summary>Each scalar lowercased in the invariant culture.</summary>
    public Utf8String ToLowerInvariant()
    {
        var r = Utf8Ops.MapCase(AsSpan(), upper: false);
        return r is null ? ToUtf8String() : new(r);
    }

    /// <summary>Padded on the left with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadLeft(int width, Rune pad) => Pad(width, pad, left: true);

    /// <summary>Padded on the right with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadRight(int width, Rune pad) => Pad(width, pad, left: false);

    private Utf8String Pad(int width, Rune pad, bool left)
    {
        int missing = width - Count();
        if (missing <= 0) return ToUtf8String();
        var padBytes = Utf8Ops.Encode(pad, stackalloc byte[4]);
        var text = AsSpan();
        var r = new byte[checked(text.Length + missing * padBytes.Length)];
        var fill = left ? r.AsSpan(0, r.Length - text.Length) : r.AsSpan(text.Length);
        if (padBytes.Length == 1)
        {
            fill.Fill(padBytes[0]);
        }
        else
        {
            for (int i = 0; i < fill.Length; i += padBytes.Length)
            {
                padBytes.CopyTo(fill[i..]);
            }
        }
        text.CopyTo(left ? r.AsSpan(r.Length - text.Length) : r);
        return new(r);
    }

    /// <summary>The scalars in reverse order.</summary>
    public Utf8String Reverse() => _length <= 1 ? ToUtf8String() : new(Utf8Ops.Reverse(AsSpan()));

    public StringSlice Trim() => Trimmed(start: true, end: true);

    public StringSlice TrimStart() => Trimmed(start: true, end: false);

    public StringSlice TrimEnd() => Trimmed(start: false, end: true);

    private StringSlice Trimmed(bool start, bool end)
    {
        var (lo, hi) = Utf8Ops.TrimBounds(AsSpan(), start, end);
        return new(_source, _start + lo, hi - lo);
    }

    /// <summary>The fields between separators, as slices; see <see cref="Utf8String.Split"/>.</summary>
    public SplitEnumerator EnumerateSplit(StringSlice separator) => new(this, separator);

    public bool Equals(StringSlice other) => AsSpan().SequenceEqual(other.AsSpan());

    public bool Equals(Utf8String other) => AsSpan().SequenceEqual(other.AsSpan());

    public bool ContentEquals(ReadOnlySpan<byte> utf8) => AsSpan().SequenceEqual(utf8);

    /// <summary>Equal to a slice or a string with the same text, as <see cref="Utf8String.Equals(object?)"/> is.</summary>
    public override bool Equals(object? obj) => obj switch
    {
        StringSlice s => Equals(s),
        Utf8String u => Equals(u),
        _ => false,
    };

    public override int GetHashCode() => Utf8Ops.Hash(AsSpan());

    public int CompareTo(StringSlice other) => Utf8Ops.Compare(AsSpan(), other.AsSpan());

    public static bool operator ==(StringSlice a, StringSlice b) => a.Equals(b);
    public static bool operator !=(StringSlice a, StringSlice b) => !a.Equals(b);
    public static bool operator <(StringSlice a, StringSlice b) => a.CompareTo(b) < 0;
    public static bool operator >(StringSlice a, StringSlice b) => a.CompareTo(b) > 0;
    public static bool operator <=(StringSlice a, StringSlice b) => a.CompareTo(b) <= 0;
    public static bool operator >=(StringSlice a, StringSlice b) => a.CompareTo(b) >= 0;

    public override string ToString() => Encoding.UTF8.GetString(AsSpan());
}
