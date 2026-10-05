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

using System.Text;

namespace BjoString;

// Searching answers cursors. The operations that make new text return the
// string itself when nothing changes; the ones named for slices share the
// array instead of copying.
public readonly partial struct Utf8String
{
    public bool Contains(Utf8String needle) => AsSpan().IndexOf(needle.AsSpan()) >= 0;

    public bool Contains(Rune r) => Utf8Ops.IndexOf(AsSpan(), r) >= 0;

    public bool StartsWith(Utf8String prefix) => AsSpan().StartsWith(prefix.AsSpan());

    public bool EndsWith(Utf8String suffix) => AsSpan().EndsWith(suffix.AsSpan());

    /// <summary>The first occurrence, or null. An empty needle is found at the start.</summary>
    public StringCursor? IndexOf(Utf8String needle)
    {
        int i = AsSpan().IndexOf(needle.AsSpan());
        return i < 0 ? null : CursorAt(i);
    }

    /// <summary>The first occurrence at or after <paramref name="from"/>, or null.</summary>
    public StringCursor? IndexOf(Utf8String needle, StringCursor from)
    {
        if (from.Offset < _start || from.Offset > End)
        {
            throw new ArgumentOutOfRangeException(nameof(from), "The cursor is outside the string.");
        }
        int i = Buffer[from.Offset..End].IndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(from.Offset + i);
    }

    public StringCursor? IndexOf(Rune r)
    {
        int i = Utf8Ops.IndexOf(AsSpan(), r);
        return i < 0 ? null : CursorAt(i);
    }

    /// <summary>The last occurrence, or null. An empty needle is found at the end.</summary>
    public StringCursor? LastIndexOf(Utf8String needle)
    {
        int i = AsSpan().LastIndexOf(needle.AsSpan());
        return i < 0 ? null : CursorAt(i);
    }

    public StringCursor? LastIndexOf(Rune r)
    {
        int i = Utf8Ops.LastIndexOf(AsSpan(), r);
        return i < 0 ? null : CursorAt(i);
    }

    /// <summary>The text between two cursors, the second exclusive, sharing this string's array.</summary>
    public Utf8String Slice(StringCursor start, StringCursor end)
    {
        CheckSpan(start, end, "substring");
        return new(_bytes, start.Offset, end.Offset - start.Offset);
    }

    /// <summary>The text between two cursors, the second exclusive; the string itself if that is all of it.</summary>
    public Utf8String Substring(StringCursor start, StringCursor end) => Slice(start, end).CopyUnlessAll(this);

    /// <summary>A part of this string as its own string, unless the part is all of it.</summary>
    private Utf8String CopyUnlessAll(Utf8String whole) =>
        _start == whole._start && _length == whole._length ? whole : Copy();

    public static Utf8String Concat(Utf8String a, Utf8String b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var r = new byte[checked(a.ByteLength + b.ByteLength)];
        a.AsSpan().CopyTo(r);
        b.AsSpan().CopyTo(r.AsSpan(a.ByteLength));
        return new(r);
    }

    public static Utf8String Concat(Utf8String a, Utf8String b, Utf8String c)
    {
        if (a.IsEmpty) return Concat(b, c);
        if (c.IsEmpty) return Concat(a, b);
        var r = new byte[checked(a.ByteLength + b.ByteLength + c.ByteLength)];
        a.AsSpan().CopyTo(r);
        b.AsSpan().CopyTo(r.AsSpan(a.ByteLength));
        c.AsSpan().CopyTo(r.AsSpan(a.ByteLength + b.ByteLength));
        return new(r);
    }

    /// <summary>All the parts in one allocation, the exact size.</summary>
    public static Utf8String Concat(params ReadOnlySpan<Utf8String> parts)
    {
        int length = 0;
        int nonEmpty = 0;
        Utf8String last = default;
        foreach (var p in parts)
        {
            length = checked(length + p._length);
            if (p._length > 0)
            {
                nonEmpty++;
                last = p;
            }
        }
        if (nonEmpty == 0) return default;
        if (nonEmpty == 1) return last;
        var r = new byte[length];
        int at = 0;
        foreach (var p in parts)
        {
            p.AsSpan().CopyTo(r.AsSpan(at));
            at += p._length;
        }
        return new(r);
    }

    public static Utf8String Join(Utf8String separator, IEnumerable<Utf8String> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Join(separator, parts is Utf8String[] array ? array : parts.ToArray());
    }

    public static Utf8String Join(Utf8String separator, ReadOnlySpan<Utf8String> parts)
    {
        if (parts.Length == 0) return default;
        if (parts.Length == 1) return parts[0];
        var sep = separator.AsSpan();
        int length = checked(sep.Length * (parts.Length - 1));
        foreach (var p in parts)
        {
            length = checked(length + p._length);
        }
        if (length == 0) return default;
        var r = new byte[length];
        int at = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sep.CopyTo(r.AsSpan(at));
                at += sep.Length;
            }
            parts[i].AsSpan().CopyTo(r.AsSpan(at));
            at += parts[i]._length;
        }
        return new(r);
    }

    /// <summary>
    /// Every occurrence of <paramref name="old"/> replaced, left to right and
    /// not overlapping; the string itself when there is none.
    /// </summary>
    public Utf8String Replace(Utf8String old, Utf8String replacement)
    {
        if (old.IsEmpty)
        {
            throw new ArgumentException("string-replace: the string to replace is empty.", nameof(old));
        }
        var r = Utf8Ops.Replace(AsSpan(), old.AsSpan(), replacement.AsSpan());
        return r is null ? this : new(r);
    }

    /// <summary>
    /// The fields between separators, empty ones kept, each in an array of its
    /// own. An empty separator gives the whole string as the one field, as
    /// .NET's <c>Split</c> does.
    /// </summary>
    public Utf8String[] Split(Utf8String separator) => SplitCore(separator, share: false);

    /// <summary>The fields of <see cref="Split"/>, sharing this string's array.</summary>
    public Utf8String[] SplitSlices(Utf8String separator) => SplitCore(separator, share: true);

    private Utf8String[] SplitCore(Utf8String separator, bool share)
    {
        var sep = separator.AsSpan();
        if (sep.IsEmpty) return [this];
        var fields = new Utf8String[AsSpan().Count(sep) + 1];
        int n = 0;
        foreach (var field in EnumerateSplit(separator))
        {
            fields[n++] = share ? field : field.CopyUnlessAll(this);
        }
        return fields;
    }

    /// <summary>The fields of <see cref="Split"/> as slices, without allocating.</summary>
    public SplitEnumerator EnumerateSplit(Utf8String separator) => new(this, separator);

    /// <summary>Each scalar uppercased in the invariant culture; the string itself if none changes.</summary>
    public Utf8String ToUpperInvariant()
    {
        var r = Utf8Ops.MapCase(AsSpan(), upper: true);
        return r is null ? this : new(r);
    }

    /// <summary>Each scalar lowercased in the invariant culture; the string itself if none changes.</summary>
    public Utf8String ToLowerInvariant()
    {
        var r = Utf8Ops.MapCase(AsSpan(), upper: false);
        return r is null ? this : new(r);
    }

    /// <summary>White space (<see cref="Rune.IsWhiteSpace"/>) removed from both ends.</summary>
    public Utf8String Trim() => TrimSlice().CopyUnlessAll(this);

    public Utf8String TrimStart() => TrimStartSlice().CopyUnlessAll(this);

    public Utf8String TrimEnd() => TrimEndSlice().CopyUnlessAll(this);

    /// <summary>What <see cref="Trim"/> leaves, sharing this string's array.</summary>
    public Utf8String TrimSlice() => Trimmed(start: true, end: true);

    public Utf8String TrimStartSlice() => Trimmed(start: true, end: false);

    public Utf8String TrimEndSlice() => Trimmed(start: false, end: true);

    private Utf8String Trimmed(bool start, bool end)
    {
        var (lo, hi) = Utf8Ops.TrimBounds(AsSpan(), start, end);
        return new(_bytes, _start + lo, hi - lo);
    }

    /// <summary>Padded on the left with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadLeft(int width, Rune pad) => Pad(width, pad, left: true);

    /// <summary>Padded on the right with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadRight(int width, Rune pad) => Pad(width, pad, left: false);

    private Utf8String Pad(int width, Rune pad, bool left)
    {
        int missing = width - Count();
        if (missing <= 0) return this;
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
    public Utf8String Reverse() => _length <= 1 ? this : new(Utf8Ops.Reverse(AsSpan()));
}
