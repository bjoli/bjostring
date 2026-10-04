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

/// <summary>
/// What strings and slices share, so that concatenation and joining are
/// written once; a constrained generic, so the JIT compiles each without
/// boxing.
/// </summary>
internal interface IUtf8Text
{
    ReadOnlySpan<byte> AsSpan();

    Utf8String ToUtf8String();
}

// The operations that make new text are written on StringSlice, and a
// string forwards to them as the slice of all of itself: the slice hands
// back its source when nothing changes, so the string still gets itself.
public readonly partial struct Utf8String : IUtf8Text
{
    Utf8String IUtf8Text.ToUtf8String() => this;

    public bool Contains(StringSlice needle) => AsSpan().IndexOf(needle.AsSpan()) >= 0;

    public bool Contains(Rune r) => Utf8Ops.IndexOf(AsSpan(), r) >= 0;

    public bool StartsWith(StringSlice prefix) => AsSpan().StartsWith(prefix.AsSpan());

    public bool EndsWith(StringSlice suffix) => AsSpan().EndsWith(suffix.AsSpan());

    /// <summary>The first occurrence, or null. An empty needle is found at the start.</summary>
    public StringCursor? IndexOf(StringSlice needle)
    {
        int i = AsSpan().IndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(i);
    }

    /// <summary>The first occurrence at or after <paramref name="from"/>, or null.</summary>
    public StringCursor? IndexOf(StringSlice needle, StringCursor from) => AsSlice().IndexOf(needle, from);

    public StringCursor? IndexOf(Rune r)
    {
        int i = Utf8Ops.IndexOf(AsSpan(), r);
        return i < 0 ? null : new StringCursor(i);
    }

    /// <summary>The last occurrence, or null. An empty needle is found at the end.</summary>
    public StringCursor? LastIndexOf(StringSlice needle)
    {
        int i = AsSpan().LastIndexOf(needle.AsSpan());
        return i < 0 ? null : new StringCursor(i);
    }

    public StringCursor? LastIndexOf(Rune r)
    {
        int i = Utf8Ops.LastIndexOf(AsSpan(), r);
        return i < 0 ? null : new StringCursor(i);
    }

    /// <summary>The text between two cursors, the second exclusive; the string itself if that is all of it.</summary>
    public Utf8String Substring(StringCursor start, StringCursor end) => Slice(start, end).ToUtf8String();

    /// <summary>A view of the text between two cursors, without a copy.</summary>
    public StringSlice Slice(StringCursor start, StringCursor end)
    {
        var s = AsSpan();
        if (start.Offset > end.Offset)
        {
            throw new ArgumentException("substring/cursors: the start cursor is after the end cursor.", nameof(start));
        }
        if (!Cursors.IsBoundary(s, start.Offset) || !Cursors.IsBoundary(s, end.Offset))
        {
            Cursors.ThrowNotBoundary("substring/cursors");
        }
        return new StringSlice(this, start.Offset, end.Offset - start.Offset);
    }

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
        var r = new byte[checked(a.ByteLength + b.ByteLength + c.ByteLength)];
        if (r.Length == 0) return default;
        a.AsSpan().CopyTo(r);
        b.AsSpan().CopyTo(r.AsSpan(a.ByteLength));
        c.AsSpan().CopyTo(r.AsSpan(a.ByteLength + b.ByteLength));
        return new(r);
    }

    /// <summary>All the parts in one allocation, the exact size.</summary>
    public static Utf8String Concat(params ReadOnlySpan<Utf8String> parts) => ConcatCore(parts);

    /// <summary>All the parts in one allocation, the exact size; strings convert to slices.</summary>
    public static Utf8String Concat(params ReadOnlySpan<StringSlice> parts) => ConcatCore(parts);

    private static Utf8String ConcatCore<T>(ReadOnlySpan<T> parts) where T : IUtf8Text
    {
        int length = 0;
        int nonEmpty = 0;
        T last = default!;
        foreach (var p in parts)
        {
            int n = p.AsSpan().Length;
            length = checked(length + n);
            if (n > 0)
            {
                nonEmpty++;
                last = p;
            }
        }
        if (nonEmpty == 0) return default;
        if (nonEmpty == 1) return last.ToUtf8String();
        var r = new byte[length];
        int at = 0;
        foreach (var p in parts)
        {
            var span = p.AsSpan();
            span.CopyTo(r.AsSpan(at));
            at += span.Length;
        }
        return new(r);
    }

    public static Utf8String Join(StringSlice separator, ReadOnlySpan<Utf8String> parts) => JoinCore(separator, parts);

    public static Utf8String Join(StringSlice separator, ReadOnlySpan<StringSlice> parts) => JoinCore(separator, parts);

    public static Utf8String Join(StringSlice separator, IEnumerable<Utf8String> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Join(separator, parts is Utf8String[] array ? array : parts.ToArray());
    }

    /// <summary>Joins slices, such as the fields of <see cref="EnumerateSplit"/>.</summary>
    public static Utf8String Join(StringSlice separator, IEnumerable<StringSlice> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Join(separator, parts is StringSlice[] array ? array : parts.ToArray());
    }

    private static Utf8String JoinCore<T>(StringSlice separator, ReadOnlySpan<T> parts) where T : IUtf8Text
    {
        if (parts.Length == 0) return default;
        if (parts.Length == 1) return parts[0].ToUtf8String();
        var sep = separator.AsSpan();
        int length = checked(sep.Length * (parts.Length - 1));
        foreach (var p in parts)
        {
            length = checked(length + p.AsSpan().Length);
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
            var span = parts[i].AsSpan();
            span.CopyTo(r.AsSpan(at));
            at += span.Length;
        }
        return new(r);
    }

    /// <summary>
    /// Every occurrence of <paramref name="old"/> replaced, left to right and
    /// not overlapping; the string itself when there is none.
    /// </summary>
    public Utf8String Replace(StringSlice old, StringSlice replacement) => AsSlice().Replace(old, replacement);

    /// <summary>
    /// The fields between separators, empty ones kept. An empty separator
    /// gives the whole string as the one field, as .NET's <c>Split</c> does.
    /// </summary>
    public Utf8String[] Split(StringSlice separator) => AsSlice().Split(separator);

    /// <summary>The fields of <see cref="Split"/> as slices, without allocating.</summary>
    public SplitEnumerator EnumerateSplit(StringSlice separator) => AsSlice().EnumerateSplit(separator);

    /// <summary>Each scalar uppercased in the invariant culture; the string itself if none changes.</summary>
    public Utf8String ToUpperInvariant() => AsSlice().ToUpperInvariant();

    /// <summary>Each scalar lowercased in the invariant culture; the string itself if none changes.</summary>
    public Utf8String ToLowerInvariant() => AsSlice().ToLowerInvariant();

    /// <summary>White space (<see cref="Rune.IsWhiteSpace"/>) removed from both ends.</summary>
    public Utf8String Trim() => AsSlice().Trim().ToUtf8String();

    public Utf8String TrimStart() => AsSlice().TrimStart().ToUtf8String();

    public Utf8String TrimEnd() => AsSlice().TrimEnd().ToUtf8String();

    /// <summary>Padded on the left with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadLeft(int width, Rune pad) => AsSlice().PadLeft(width, pad);

    /// <summary>Padded on the right with <paramref name="pad"/> to <paramref name="width"/> scalars.</summary>
    public Utf8String PadRight(int width, Rune pad) => AsSlice().PadRight(width, pad);

    /// <summary>The scalars in reverse order.</summary>
    public Utf8String Reverse() => AsSlice().Reverse();
}
