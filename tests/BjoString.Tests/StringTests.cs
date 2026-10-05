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

public static class StringTests
{
    [Test]
    public static void DefaultIsEmpty()
    {
        Utf8String d = default;
        Check.True(d.IsEmpty && d.ByteLength == 0 && d.Count() == 0, "empty");
        Check.Equal("", d.ToString(), "ToString");
        Check.True(d == U(""), "equals an empty array");
        Check.Equal(U("").GetHashCode(), d.GetHashCode(), "hash");
        Check.True(StringCursor.AtEnd(d, StringCursor.Start(d)), "start is end");
        Check.Equal(0, d.Split(U(",")).Length - 1, "split");
    }

    [Test]
    public static void ConversionsRoundTrip()
    {
        var rng = new Random(1);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 20);
            var u = U(s);
            Check.Equal(s, u.ToString(), "round trip");
            Check.SequenceEqual(Encoding.UTF8.GetBytes(s), u.AsSpan().ToArray(), "bytes");
            Check.True(Utf8String.FromUtf8(u.AsSpan()) == u, "FromUtf8");
        }
        Check.Equal("a\uFFFDb", U("a\uD800b").ToString(), "lone high surrogate");
        Check.Equal("\uFFFD", U("\uDC00").ToString(), "lone low surrogate");
        Check.Equal("😀", Utf8String.FromRune(new Rune(0x1F600)).ToString(), "FromRune");
    }

    [Test]
    public static void InvalidUtf8()
    {
        byte[][] bad = [[0x80], [0xC3], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xC0, 0xAF], [0xF5, 0x80, 0x80, 0x80], [0x61, 0xFF]];
        foreach (var b in bad)
        {
            Check.Throws<ArgumentException>(() => Utf8String.FromUtf8(b), $"FromUtf8 {Convert.ToHexString(b)}");
            Check.True(!Utf8String.TryFromUtf8(b, out _), "TryFromUtf8");
            Check.Equal(Encoding.UTF8.GetString(b), Utf8String.FromUtf8Lossy(b).ToString(), "lossy as Encoding.UTF8");
        }
        Check.True(Utf8String.TryFromUtf8([], out var e) && e.IsEmpty, "empty is valid");
        Check.Throws<ArgumentException>(
            () => Utf8String.Create(1, 0, (span, _) => span[0] = 0xFF), "Create validates");
        Check.Equal("hi", Utf8String.Create(2, 0, (span, _) => { span[0] = (byte)'h'; span[1] = (byte)'i'; }).ToString(), "Create");
    }

    [Test]
    public static void CountScalars()
    {
        var rng = new Random(2);
        for (int i = 0; i < Cases; i++)
        {
            // Long enough for the vector loops and their tails.
            string s = Text(rng, 120);
            Check.Equal(s.EnumerateRunes().Count(), U(s).Count(), $"count of {s}");
        }
    }

    [Test]
    public static void CursorsWalkBothWays()
    {
        var rng = new Random(3);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 20);
            var u = U(s);
            var forward = new List<Rune>();
            var c = StringCursor.Start(u);
            while (!StringCursor.AtEnd(u, c))
            {
                Check.Equal(StringCursor.Ref(u, c), StringCursor.RefNext(u, c).Item1, "ref");
                Check.Equal(StringCursor.Next(u, c), StringCursor.RefNext(u, c).Item2, "next");
                (Rune r, c) = StringCursor.RefNext(u, c);
                forward.Add(r);
            }
            Check.Equal(StringCursor.End(u), c, "ends at end");
            Check.SequenceEqual(s.EnumerateRunes(), forward, "forward");

            var backward = new List<Rune>();
            while (c > StringCursor.Start(u))
            {
                c = StringCursor.Prev(u, c);
                backward.Add(StringCursor.Ref(u, c));
            }
            backward.Reverse();
            Check.SequenceEqual(s.EnumerateRunes(), backward, "backward");
            Check.SequenceEqual(s.EnumerateRunes(), u.EnumerateRunes(), "EnumerateRunes");
        }
    }

    [Test]
    public static void CursorErrors()
    {
        var u = U("aé😀");
        var end = StringCursor.End(u);
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Ref(u, end), "ref at end");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Next(u, end), "next at end");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.RefNext(u, end), "ref+next at end");
        Check.Throws<ArgumentOutOfRangeException>(() => StringCursor.Prev(u, StringCursor.Start(u)), "prev at start");

        // A cursor from another string can land inside a scalar here.
        var other = U("abc");
        var inside = StringCursor.Next(other, StringCursor.Next(other, StringCursor.Start(other)));
        Check.Throws<ArgumentException>(() => StringCursor.Ref(u, inside), "ref inside a scalar");
        Check.Throws<ArgumentException>(() => StringCursor.Next(u, inside), "next inside a scalar");
        Check.Throws<ArgumentException>(() => StringCursor.Substring(u, StringCursor.Start(u), inside), "substring inside");
        Check.Equal(new StringCursor(1), StringCursor.Prev(u, inside), "prev resynchronizes");
        Check.Throws<ArgumentException>(() => StringCursor.Substring(u, end, StringCursor.Start(u)), "start after end");
    }

    [Test]
    public static void Substrings()
    {
        var rng = new Random(4);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 12);
            var u = U(s);
            var cursors = new List<StringCursor> { StringCursor.Start(u) };
            for (var c = StringCursor.Start(u); !StringCursor.AtEnd(u, c);)
            {
                c = StringCursor.Next(u, c);
                cursors.Add(c);
            }
            int a = rng.Next(cursors.Count);
            int b = rng.Next(a, cursors.Count);
            var runes = s.EnumerateRunes().ToList();
            string expected = Runes(runes.Skip(a).Take(b - a));
            var sub = StringCursor.Substring(u, cursors[a], cursors[b]);
            Check.Equal(expected, sub.ToString(), "substring");
            var slice = u.Slice(cursors[a], cursors[b]);
            Check.Equal(expected, slice.ToString(), "slice");
            Check.True(slice.Equals(sub) && sub.Equals(slice), "slice equals substring");
            Check.Equal(sub.GetHashCode(), slice.GetHashCode(), "slice hash");
            Check.Equal(b - a, slice.Count(), "slice count");
        }
        var whole = U("abc");
        Check.True(ReferenceEquals(whole.Bytes, whole.Substring(StringCursor.Start(whole), StringCursor.End(whole)).Bytes),
                   "whole substring is not copied");
    }

    [Test]
    public static void OrderIsScalarOrder()
    {
        var rng = new Random(5);
        for (int i = 0; i < Cases; i++)
        {
            string a = Text(rng, 4);
            string b = rng.Next(4) == 0 ? a : Text(rng, 4);
            var (ua, ub) = (U(a), U(b));
            Check.Equal(Math.Sign(CompareScalars(a, b)), Math.Sign(ua.CompareTo(ub)), $"compare {a} {b}");
            Check.Equal(a == b, ua == ub, "equality");
            if (a == b)
            {
                Check.Equal(ua.GetHashCode(), ub.GetHashCode(), "hash");
            }
            Check.Equal(ua < ub, CompareScalars(a, b) < 0, "<");
        }
        // Where UTF-16 ordinal order and scalar order part ways.
        Check.True(U("\uFFFD") < U("😀"), "BMP private use sorts before astral");
        Check.True(string.CompareOrdinal("\uFFFD", "😀") > 0, "unlike UTF-16 ordinal");
    }

    [Test]
    public static void Search()
    {
        var rng = new Random(6);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 16);
            string n = Text(rng, 2);
            var (us, un) = (U(s), U(n));
            Check.Equal(s.Contains(n, StringComparison.Ordinal), us.Contains(un), $"contains {s} / {n}");
            Check.Equal(s.StartsWith(n, StringComparison.Ordinal), us.StartsWith(un), "starts");
            Check.Equal(s.EndsWith(n, StringComparison.Ordinal), us.EndsWith(un), "ends");
            int k = s.IndexOf(n, StringComparison.Ordinal);
            Check.Equal(k < 0 ? null : new StringCursor(ByteOffset(s, k)), us.IndexOf(un), "index");
            int l = s.LastIndexOf(n, StringComparison.Ordinal);
            Check.Equal(l < 0 ? null : new StringCursor(ByteOffset(s, l)), us.LastIndexOf(un), $"last index {s} / {n}");
            if (n.Length > 0)
            {
                var r = n.EnumerateRunes().First();
                int ri = s.IndexOf(r.ToString(), StringComparison.Ordinal);
                Check.Equal(ri < 0 ? null : new StringCursor(ByteOffset(s, ri)), us.IndexOf(r), "index of rune");
                Check.Equal(ri >= 0, us.Contains(r), "contains rune");
            }
            int fromRunes = rng.Next(s.EnumerateRunes().Count() + 1);
            int fromUtf16 = s.EnumerateRunes().Take(fromRunes).Sum(r => r.Utf16SequenceLength);
            int k2 = s.IndexOf(n, fromUtf16, StringComparison.Ordinal);
            var from = new StringCursor(ByteOffset(s, fromUtf16));
            Check.Equal(k2 < 0 ? null : new StringCursor(ByteOffset(s, k2)), us.IndexOf(un, from), "index from");
        }
    }

    [Test]
    public static void SplitReplaceJoin()
    {
        var rng = new Random(7);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 16);
            string sep = Text(rng, 2);
            string rep = Text(rng, 2);
            var (us, usep, urep) = (U(s), U(sep), U(rep));
            var parts = s.Split(sep, StringSplitOptions.None);
            var uparts = us.Split(usep);
            Check.SequenceEqual(parts, uparts.Select(p => p.ToString()), $"split {s} / {sep}");
            Check.SequenceEqual(parts, us.EnumerateSplit(usep).Select(p => p.ToString()), "split enumerator");
            Check.Equal(s, Utf8String.Join(usep, uparts).ToString(), "join undoes split");
            Check.Equal(string.Join(sep, parts), Utf8String.Join(usep, (IEnumerable<Utf8String>)uparts).ToString(), "join enumerable");
            if (sep.Length > 0)
            {
                Check.Equal(s.Replace(sep, rep, StringComparison.Ordinal), us.Replace(usep, urep).ToString(), $"replace {s} / {sep} / {rep}");
            }
        }
        Check.Throws<ArgumentException>(() => U("abc").Replace(U(""), U("x")), "empty old");
        var unchanged = U("abc");
        Check.True(ReferenceEquals(unchanged.Bytes, unchanged.Replace(U("z"), U("y")).Bytes), "no hit, no copy");
        Check.Equal("bb", U("aaaa").Replace(U("aa"), U("b")).ToString(), "non-overlapping");
    }

    [Test]
    public static void Concat()
    {
        var rng = new Random(8);
        for (int i = 0; i < Cases; i++)
        {
            var parts = Enumerable.Range(0, rng.Next(6)).Select(_ => Text(rng, 3)).ToArray();
            var uparts = parts.Select(U).ToArray();
            Check.Equal(string.Concat(parts), Utf8String.Concat(uparts).ToString(), "concat");
            if (parts.Length >= 2)
            {
                Check.Equal(parts[0] + parts[1], Utf8String.Concat(uparts[0], uparts[1]).ToString(), "concat 2");
            }
            if (parts.Length >= 3)
            {
                Check.Equal(parts[0] + parts[1] + parts[2], Utf8String.Concat(uparts[0], uparts[1], uparts[2]).ToString(), "concat 3");
            }
        }
    }

    [Test]
    public static void CaseMapping()
    {
        var rng = new Random(9);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 16);
            var u = U(s);
            string upper = Runes(s.EnumerateRunes().Select(Rune.ToUpperInvariant));
            string lower = Runes(s.EnumerateRunes().Select(Rune.ToLowerInvariant));
            Check.Equal(upper, u.ToUpperInvariant().ToString(), $"upper {s}");
            Check.Equal(lower, u.ToLowerInvariant().ToString(), $"lower {s}");
            // What Bjolang does today.
            Check.Equal(s.ToUpperInvariant(), u.ToUpperInvariant().ToString(), $"as string.ToUpperInvariant {s}");
            Check.Equal(s.ToLowerInvariant(), u.ToLowerInvariant().ToString(), $"as string.ToLowerInvariant {s}");
        }
        var done = U("ABC É");
        Check.True(ReferenceEquals(done.Bytes, done.ToUpperInvariant().Bytes), "unchanged, not copied");
        Check.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                    U("abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz").ToUpperInvariant().ToString(),
                    "long ASCII");
    }

    [Test]
    public static void TrimPadReverse()
    {
        var rng = new Random(10);
        for (int i = 0; i < Cases; i++)
        {
            string s = Text(rng, 10);
            var u = U(s);
            Check.Equal(s.Trim(), u.Trim().ToString(), $"trim [{s}]");
            Check.Equal(s.TrimStart(), u.TrimStart().ToString(), "trim start");
            Check.Equal(s.TrimEnd(), u.TrimEnd().ToString(), "trim end");
            Check.Equal(s.Trim(), u.TrimSlice().ToString(), "trim slice");

            int count = s.EnumerateRunes().Count();
            int width = rng.Next(15);
            var pad = rng.Next(2) == 0 ? new Rune('.') : new Rune(0x1F600);
            string fill = string.Concat(Enumerable.Repeat(pad.ToString(), Math.Max(0, width - count)));
            Check.Equal(fill + s, u.PadLeft(width, pad).ToString(), "pad left");
            Check.Equal(s + fill, u.PadRight(width, pad).ToString(), "pad right");

            Check.Equal(Runes(s.EnumerateRunes().Reverse()), u.Reverse().ToString(), "reverse");
        }
    }

    [Test]
    public static void LiteralMatch()
    {
        Check.True(U("héllo").ContentEquals("héllo"u8), "literal");
        Check.True(!U("héllo").ContentEquals("hello"u8), "not literal");
    }

    [Test]
    public static void ByteAccess()
    {
        var u = U("aé");
        Check.Equal(3, u.ByteLength, "byte length");
        Check.Equal((byte)0xC3, u.ByteAt(1), "byte at");
        Check.True(!u.IsAscii() && U("abc").IsAscii(), "ascii");
        Check.Equal(3, u.AsMemory().Length, "memory");
    }
}
