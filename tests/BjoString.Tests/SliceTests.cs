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
using static BjoString.Tests.Gen;

namespace BjoString.Tests;

public static class SliceTests
{
    /// <summary>
    /// The middle of <c>pre + mid + post</c> as a slice, so that text around
    /// it could be found or changed by an operation that overruns its bounds.
    /// </summary>
    private static (Utf8String Slice, int Base) Middle(string pre, string mid, string post)
    {
        var u = U(pre + mid + post);
        int start = ByteOffset(pre + mid, pre.Length);
        int end = ByteOffset(pre + mid, pre.Length + mid.Length);
        return (u.Slice(new StringCursor(start), new StringCursor(end)), start);
    }

    private static StringCursor? At(int sliceBase, string mid, int utf16Index) =>
        utf16Index < 0 ? null : new StringCursor(sliceBase + ByteOffset(mid, utf16Index));

    [Test]
    public static void SearchStaysInside()
    {
        var rng = new Random(30);
        for (int i = 0; i < Cases; i++)
        {
            string pre = Text(rng, 3), mid = Text(rng, 10), post = Text(rng, 3), n = Text(rng, 2);
            var (sl, b) = Middle(pre, mid, post);
            var un = U(n);
            Check.Equal(mid.Contains(n, StringComparison.Ordinal), sl.Contains(un), $"contains [{pre}|{mid}|{post}] {n}");
            Check.Equal(mid.StartsWith(n, StringComparison.Ordinal), sl.StartsWith(un), "starts");
            Check.Equal(mid.EndsWith(n, StringComparison.Ordinal), sl.EndsWith(un), "ends");
            Check.Equal(At(b, mid, mid.IndexOf(n, StringComparison.Ordinal)), sl.IndexOf(un), "index");
            Check.Equal(At(b, mid, mid.LastIndexOf(n, StringComparison.Ordinal)), sl.LastIndexOf(un), "last index");
            if (n.Length > 0)
            {
                var r = n.EnumerateRunes().First();
                Check.Equal(At(b, mid, mid.IndexOf(r.ToString(), StringComparison.Ordinal)), sl.IndexOf(r), "index of rune");
                Check.Equal(At(b, mid, mid.LastIndexOf(r.ToString(), StringComparison.Ordinal)), sl.LastIndexOf(r), "last index of rune");
            }
            int fromRunes = rng.Next(mid.EnumerateRunes().Count() + 1);
            int fromUtf16 = mid.EnumerateRunes().Take(fromRunes).Sum(x => x.Utf16SequenceLength);
            var from = At(b, mid, fromUtf16)!.Value;
            Check.Equal(At(b, mid, mid.IndexOf(n, fromUtf16, StringComparison.Ordinal)), sl.IndexOf(un, from), "index from");
        }
        var (s, _) = Middle("ab", "cd", "ef");
        Check.Throws<ArgumentOutOfRangeException>(() => s.IndexOf(U("x"), new StringCursor(0)), "from before the slice");
    }

    [Test]
    public static void OperationsMatchString()
    {
        var rng = new Random(31);
        for (int i = 0; i < Cases; i++)
        {
            string pre = Text(rng, 3), mid = Text(rng, 10), post = Text(rng, 3);
            string sep = Text(rng, 2), rep = Text(rng, 2);
            var (sl, _) = Middle(pre, mid, post);
            string what = $"[{pre}|{mid}|{post}]";
            Check.Equal(mid, sl.ToString(), $"text {what}");
            Check.Equal(Encoding.UTF8.GetByteCount(mid), sl.ByteLength, "byte length");
            Check.Equal(mid.EnumerateRunes().Count(), sl.Count(), "count");
            Check.SequenceEqual(mid.Split(sep, StringSplitOptions.None), sl.Split(U(sep)).Select(f => f.ToString()), $"split {what} {sep}");
            Check.SequenceEqual(mid.Split(sep, StringSplitOptions.None), sl.SplitSlices(U(sep)).Select(f => f.ToString()), "split slices");
            if (sep.Length > 0)
            {
                Check.Equal(mid.Replace(sep, rep, StringComparison.Ordinal), sl.Replace(U(sep), U(rep)).ToString(), $"replace {what}");
            }
            Check.Equal(mid.ToUpperInvariant(), sl.ToUpperInvariant().ToString(), "upper");
            Check.Equal(mid.ToLowerInvariant(), sl.ToLowerInvariant().ToString(), "lower");
            Check.Equal(mid.Trim(), sl.Trim().ToString(), "trim");
            Check.Equal(mid.Trim(), sl.TrimSlice().ToString(), "trim slice");
            Check.Equal(mid.TrimStart(), sl.TrimStartSlice().ToString(), "trim start slice");
            Check.Equal(mid.TrimEnd(), sl.TrimEndSlice().ToString(), "trim end slice");
            Check.Equal(Runes(mid.EnumerateRunes().Reverse()), sl.Reverse().ToString(), "reverse");
            int width = rng.Next(14);
            string fill = new('.', Math.Max(0, width - mid.EnumerateRunes().Count()));
            Check.Equal(fill + mid, sl.PadLeft(width, new Rune('.')).ToString(), "pad left");
            Check.Equal(mid + fill, sl.PadRight(width, new Rune('.')).ToString(), "pad right");
            Check.SequenceEqual(mid.EnumerateRunes(), sl.EnumerateRunes(), "runes");
            Check.SequenceEqual(Encoding.UTF8.GetBytes(mid), sl.AsMemory().ToArray(), "memory");

            var walked = new List<Rune>();
            for (var c = StringCursor.Start(sl); !StringCursor.AtEnd(sl, c);)
            {
                (Rune r, c) = StringCursor.RefNext(sl, c);
                walked.Add(r);
            }
            Check.SequenceEqual(mid.EnumerateRunes(), walked, "walk");
            var back = new List<Rune>();
            for (var c = StringCursor.End(sl); c > StringCursor.Start(sl);)
            {
                c = StringCursor.Prev(sl, c);
                back.Add(StringCursor.Ref(sl, c));
            }
            back.Reverse();
            Check.SequenceEqual(mid.EnumerateRunes(), back, "walk back");

            var copy = sl.Copy();
            Check.True(copy.IsCompact && copy == sl && copy.GetHashCode() == sl.GetHashCode(), "copy");
            Check.True(sl == U(mid) && U(mid) == sl, "equals a string with the text");
            Check.Equal(0, sl.CompareTo(U(mid)), "orders as it");
        }
    }

    [Test]
    public static void SharingIsExplicit()
    {
        var u = U("  ab,cé,😀  ");
        var t = u.TrimSlice();
        Check.Equal("ab,cé,😀", t.ToString(), "trimmed");
        Check.True(ReferenceEquals(u.Bytes, t.Bytes) && !t.IsCompact, "a slice shares");
        Check.True(!ReferenceEquals(u.Bytes, u.Trim().Bytes) && u.Trim().IsCompact, "trim copies");
        var cut = StringCursor.Next(u, StringCursor.Next(u, StringCursor.Start(u)));
        Check.True(ReferenceEquals(u.Bytes, u.Slice(cut, StringCursor.End(u)).Bytes), "slice shares");
        Check.True(u.Substring(cut, StringCursor.End(u)).IsCompact, "substring copies");
        Check.True(t.SplitSlices(U(",")).All(f => ReferenceEquals(u.Bytes, f.Bytes)), "split slices share");
        Check.True(t.Split(U(",")).All(f => f.IsCompact), "split copies");
        Check.True(t.EnumerateSplit(U(",")).All(f => ReferenceEquals(u.Bytes, f.Bytes)), "the enumerator shares");
        Check.True(u.IsCompact && ReferenceEquals(u.Bytes, u.Copy().Bytes), "a compact string copies to itself");
        Check.True(default(Utf8String).IsCompact && default(Utf8String).Copy().IsEmpty, "the empty string");
    }

    [Test]
    public static void UnchangedIsItself()
    {
        var (sl, _) = Middle("x", "ABC", "y");
        foreach (var (what, s) in new[] { ("compact", U("ABC")), ("slice", sl) })
        {
            Check.True(ReferenceEquals(s.Bytes, s.Replace(U("x"), U("y")).Bytes), $"replace, {what}");
            Check.True(ReferenceEquals(s.Bytes, s.ToUpperInvariant().Bytes), $"upper, {what}");
            Check.True(ReferenceEquals(s.Bytes, s.PadLeft(2, new Rune(' ')).Bytes), $"pad, {what}");
            Check.True(ReferenceEquals(s.Bytes, s.Trim().Bytes), $"trim, {what}");
            Check.True(ReferenceEquals(s.Bytes, s.Split(U(",")).Single().Bytes), $"split, {what}");
            Check.True(ReferenceEquals(s.Bytes, Utf8String.Concat(U(""), s, U("")).Bytes), $"concat of one, {what}");
            Check.True(ReferenceEquals(s.Bytes, s.Substring(StringCursor.Start(s), StringCursor.End(s)).Bytes), $"whole substring, {what}");
        }
    }

    [Test]
    public static void CursorsAreTheSource()
    {
        var u = U("  ab,cé,😀  ");
        var second = u.TrimSlice().SplitSlices(U(","))[1];
        var start = StringCursor.Start(second);
        Check.Equal(new Rune('c'), StringCursor.Ref(u, start), "a slice's cursor is the source's");
        Check.Equal(new Rune('é'), StringCursor.Ref(second, StringCursor.Next(second, start)), "ref");
        Check.True(StringCursor.AtEnd(second, StringCursor.Next(second, StringCursor.Next(second, start))), "end");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Ref(second, StringCursor.End(second)), "past the slice");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Prev(second, start), "before the slice");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Ref(second, StringCursor.Start(u)), "a source cursor outside it");
        Check.Equal(u.IndexOf(U("é")), second.IndexOf(new Rune('é')), "search answers source cursors");
        Check.Throws<ArgumentOutOfRangeException>(() => second.Slice(StringCursor.Start(u), start), "slicing outside it");
        Check.True(second.Slice(start, StringCursor.Next(second, start)).ContentEquals("c"u8), "sub-slice");
    }

    [Test]
    public static void ConcatAndJoin()
    {
        var rng = new Random(32);
        for (int i = 0; i < Cases / 5; i++)
        {
            var mids = Enumerable.Range(0, rng.Next(5)).Select(_ => Text(rng, 3)).ToArray();
            var slices = mids.Select(m => Middle(Text(rng, 2), m, Text(rng, 2)).Slice).ToArray();
            Check.Equal(string.Concat(mids), Utf8String.Concat(slices).ToString(), "concat slices");
            Check.Equal(string.Join(",", mids), Utf8String.Join(U(","), slices).ToString(), "join slices");
            Check.Equal(string.Join(",", mids), Utf8String.Join(U(","), (IEnumerable<Utf8String>)slices).ToString(), "join enumerable");
        }
        var t = U("a, b, c");
        Check.Equal("a;b;c", Utf8String.Join(U(";"), t.EnumerateSplit(U(", "))).ToString(), "join split fields");
    }

    [Test]
    public static void BoxedEquality()
    {
        var (sl, _) = Middle("pre", "ord", "post");
        object slice = sl, str = U("ord"), other = U("orc");
        Check.True(slice.Equals(str) && str.Equals(slice), "both ways");
        Check.Equal(str.GetHashCode(), slice.GetHashCode(), "hash");
        Check.True(!slice.Equals(other) && !other.Equals(slice), "different text");
        Check.True(!str.Equals("ord"), "not a .NET string");
    }

    [Test]
    public static void LookupBySlice()
    {
        var map = new Dictionary<Utf8String, int> { [U("key")] = 1, [U("ключ")] = 2 };
        var text = U("a key, a ключ");
        var key = text.Slice(text.IndexOf(U("key"))!.Value, text.IndexOf(U(","))!.Value);
        Check.True(map.TryGetValue(key, out int v) && v == 1, "a slice finds its text");
        var bySpan = new Dictionary<Utf8String, int>(map, Utf8StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<byte>>();
        Check.True(bySpan.TryGetValue("ключ"u8, out v) && v == 2, "by span");
        Check.True(!bySpan.ContainsKey("nope"u8), "missing");
    }
}
