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
    private static (StringSlice Slice, int Base) Middle(string pre, string mid, string post)
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
                var whole = U(mid);
                Check.Equal(At(0, mid, mid.LastIndexOf(r.ToString(), StringComparison.Ordinal)), whole.LastIndexOf(r), "string last index of rune");
            }
            int fromRunes = rng.Next(mid.EnumerateRunes().Count() + 1);
            int fromUtf16 = mid.EnumerateRunes().Take(fromRunes).Sum(x => x.Utf16SequenceLength);
            var from = At(b, mid, fromUtf16)!.Value;
            Check.Equal(At(b, mid, mid.IndexOf(n, fromUtf16, StringComparison.Ordinal)), sl.IndexOf(un, from), "index from");
        }
        var (s, _) = Middle("ab", "cd", "ef");
        Check.Throws<ArgumentOutOfRangeException>(() => s.IndexOf(U("x"), StringCursor.Start(s.Source)), "from before the slice");
    }

    [Test]
    public static void NewTextMatchesString()
    {
        var rng = new Random(31);
        for (int i = 0; i < Cases; i++)
        {
            string pre = Text(rng, 3), mid = Text(rng, 10), post = Text(rng, 3);
            string sep = Text(rng, 2), rep = Text(rng, 2);
            var (sl, _) = Middle(pre, mid, post);
            string what = $"[{pre}|{mid}|{post}]";
            Check.Equal(mid, sl.ToString(), $"text {what}");
            Check.Equal(mid.EnumerateRunes().Count(), sl.Count(), "count");
            Check.SequenceEqual(mid.Split(sep, StringSplitOptions.None), sl.Split(U(sep)).Select(f => f.ToString()), $"split {what} {sep}");
            if (sep.Length > 0)
            {
                Check.Equal(mid.Replace(sep, rep, StringComparison.Ordinal), sl.Replace(U(sep), U(rep)).ToString(), $"replace {what}");
            }
            Check.Equal(mid.ToUpperInvariant(), sl.ToUpperInvariant().ToString(), "upper");
            Check.Equal(mid.ToLowerInvariant(), sl.ToLowerInvariant().ToString(), "lower");
            Check.Equal(mid.Trim(), sl.Trim().ToString(), "trim");
            Check.Equal(Runes(mid.EnumerateRunes().Reverse()), sl.Reverse().ToString(), "reverse");
            int width = rng.Next(14);
            string fill = new('.', Math.Max(0, width - mid.EnumerateRunes().Count()));
            Check.Equal(fill + mid, sl.PadLeft(width, new Rune('.')).ToString(), "pad left");
            Check.Equal(mid + fill, sl.PadRight(width, new Rune('.')).ToString(), "pad right");
            Check.SequenceEqual(mid.EnumerateRunes(), sl.EnumerateRunes(), "runes");

            var walked = new List<Rune>();
            for (var c = sl.Start; !sl.AtEnd(c);)
            {
                (Rune r, c) = sl.RefNext(c);
                walked.Add(r);
            }
            Check.SequenceEqual(mid.EnumerateRunes(), walked, "walk");
            Check.Equal(mid, sl.Substring(sl.Start, sl.End).ToString(), "substring");
        }
    }

    [Test]
    public static void UnchangedWholeSliceIsTheSource()
    {
        var u = U("ABC");
        var all = u.AsSlice();
        Check.True(ReferenceEquals(u.Bytes, all.Replace(U("x"), U("y")).Bytes), "replace");
        Check.True(ReferenceEquals(u.Bytes, all.ToUpperInvariant().Bytes), "upper");
        Check.True(ReferenceEquals(u.Bytes, all.PadLeft(2, new Rune(' ')).Bytes), "pad");
        Check.True(ReferenceEquals(u.Bytes, all.Split(U(",")).Single().Bytes), "split");
        Check.True(ReferenceEquals(u.Bytes, Utf8String.Concat(U(""), all, U("")).Bytes), "concat of one");
    }

    [Test]
    public static void ConcatAndJoinSlices()
    {
        var rng = new Random(32);
        for (int i = 0; i < Cases / 5; i++)
        {
            var mids = Enumerable.Range(0, rng.Next(5)).Select(_ => Text(rng, 3)).ToArray();
            var slices = mids.Select(m => Middle(Text(rng, 2), m, Text(rng, 2)).Slice).ToArray();
            Check.Equal(string.Concat(mids), Utf8String.Concat(slices).ToString(), "concat slices");
            Check.Equal(string.Join(",", mids), Utf8String.Join(U(","), slices).ToString(), "join slices");
            Check.Equal(string.Join(",", mids), Utf8String.Join(U(","), (IEnumerable<StringSlice>)slices).ToString(), "join enumerable");
        }
        var t = U("a, b, c");
        Check.Equal("a;b;c", Utf8String.Join(U(";"), t.EnumerateSplit(U(", "))).ToString(), "join split fields");
        Check.Equal("xa, b, cy", Utf8String.Concat(U("x"), t.AsSlice(), U("y").AsSlice()).ToString(), "mixed concat");
    }

    [Test]
    public static void BoxedEqualityAcrossTypes()
    {
        var (sl, _) = Middle("pre", "ord", "post");
        object slice = sl, str = U("ord"), other = U("orc");
        Check.True(slice.Equals(str) && str.Equals(slice), "both ways");
        Check.Equal(str.GetHashCode(), slice.GetHashCode(), "hash");
        Check.True(!slice.Equals(other) && !other.Equals(slice), "different text");
        Check.True(!str.Equals("ord"), "not a .NET string");
    }
}
