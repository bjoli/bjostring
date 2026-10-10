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

using System.Globalization;
using System.Text;
using static BjoString.Tests.Gen;

namespace BjoString.Tests;

public static class BuilderTests
{
    [Test]
    public static void AppendsMatchStringBuilder()
    {
        var rng = new Random(20);
        for (int i = 0; i < Cases / 10; i++)
        {
            var expected = new StringBuilder();
            var b = new Utf8StringBuilder(1);
            for (int j = rng.Next(30); j > 0; j--)
            {
                string s = Text(rng, 3);
                switch (rng.Next(7))
                {
                    case 0:
                        expected.Append(s);
                        b.Append(U(s));
                        break;
                    case 1:
                        foreach (var r in s.EnumerateRunes())
                        {
                            expected.Append(r.ToString());
                            b.Append(r);
                        }
                        break;
                    case 2:
                        expected.Append(s);
                        b.Append(s);
                        break;
                    case 3:
                        int n = rng.Next(int.MinValue, int.MaxValue);
                        expected.Append(n.ToString(CultureInfo.InvariantCulture));
                        b.Append(n);
                        break;
                    case 4:
                        double d = rng.NextDouble() * Math.Pow(10, rng.Next(-30, 30));
                        expected.Append(d.ToString(CultureInfo.InvariantCulture));
                        b.Append(d);
                        break;
                    case 5:
                        // A scanner copying input byte by byte.
                        expected.Append(s);
                        foreach (byte x in Encoding.UTF8.GetBytes(s)) b.AppendByte(x);
                        break;
                    default:
                        expected.Append(s);
                        b.AppendUtf8(Encoding.UTF8.GetBytes(s));
                        break;
                }
            }
            Check.Equal(expected.ToString(), b.ToUtf8String().ToString(), "built");
            Check.Equal(Encoding.UTF8.GetByteCount(expected.ToString()), b.ByteLength, "byte length");
            Check.Equal(expected.ToString(), b.ToString(), "ToString");
            b.Clear();
            Check.True(b.ToUtf8String().IsEmpty, "cleared");
        }
    }

    // Past Utf8StringBuilder.ChunkSize the text is in several buffers. Every
    // append, every read and every result has to give what one buffer gave,
    // whichever buffer a piece of text landed in and wherever a boundary falls.
    [Test]
    public static void LongBuildsAcrossBuffers()
    {
        var rng = new Random(24);
        for (int i = 0; i < 12; i++)
        {
            var expected = new StringBuilder();
            var b = new Utf8StringBuilder(rng.Next(1, 40));
            int target = rng.Next(Utf8StringBuilder.ChunkSize / 2, Utf8StringBuilder.ChunkSize * 5);
            while (b.ByteLength < target)
            {
                string s = Text(rng, rng.Next(2) == 0 ? 5 : 3000);
                switch (rng.Next(7))
                {
                    case 0:
                        expected.Append(s);
                        b.Append(U(s));
                        break;
                    case 1:
                        foreach (var r in s.EnumerateRunes())
                        {
                            expected.Append(r.ToString());
                            b.Append(r);
                        }
                        break;
                    case 2:
                        expected.Append(s);
                        b.Append(s);
                        break;
                    case 3:
                        long n = rng.NextInt64(long.MinValue, long.MaxValue);
                        expected.Append(n.ToString(CultureInfo.InvariantCulture));
                        b.Append(n);
                        break;
                    case 4:
                        expected.Append(s);
                        foreach (byte x in Encoding.UTF8.GetBytes(s)) b.AppendByte(x);
                        break;
                    case 5:
                        expected.Append(s);
                        foreach (char unit in s) b.AppendUtf16Unit(unit);
                        break;
                    default:
                        expected.Append(s);
                        b.AppendUtf8(Encoding.UTF8.GetBytes(s));
                        break;
                }
            }
            string text = expected.ToString();
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            Check.Equal(utf8.Length, b.ByteLength, "byte length");
            for (int k = 0; k < 200; k++)
            {
                int at = rng.Next(utf8.Length);
                Check.Equal(utf8[at], b.ByteAt(at), $"byte at {at}");
            }
            Check.Equal(utf8[^1], b.ByteAt(utf8.Length - 1), "last byte");
            Check.Equal(utf8[0], b.ByteAt(0), "first byte");
            Check.Throws<ArgumentOutOfRangeException>(() => b.ByteAt(utf8.Length), "byte past the end");
            Check.Equal(text, b.ToUtf8String().ToString(), "built");
            Check.Equal(text, b.ToString(), "ToString");
            Check.True(b.AsSpan().SequenceEqual(utf8), "as one span");
            // Appending after the text was made one buffer again.
            b.Append(U("tail"));
            Check.Equal(text + "tail", b.ToUtf8String().ToString(), "appended after AsSpan");
            b.Clear();
            Check.Equal(0, b.ByteLength, "cleared");
            b.Append(U("again"));
            Check.Equal("again", b.ToUtf8String().ToString(), "reused after Clear");
        }
    }

    [Test]
    public static void BoundariesInsideScalars()
    {
        int size = Utf8StringBuilder.ChunkSize;
        // A raw two-byte scalar whose bytes fall in two buffers, then one left
        // half done there.
        var b = new Utf8StringBuilder();
        b.Append(new string('a', size - 1));
        b.AppendByte(0xC3).AppendByte(0xA9);
        Check.Equal(new string('a', size - 1) + "é", b.ToUtf8String().ToString(), "raw scalar across buffers");
        b.Clear();
        b.Append(new string('a', size - 1));
        b.AppendByte(0xC3).Append(U("x"));
        Check.Throws<InvalidOperationException>(() => b.ToUtf8String(), "raw half scalar across buffers");
        // A four-byte scalar and a surrogate pair at every offset near a boundary.
        for (int pad = size - 5; pad <= size + 1; pad++)
        {
            var s = new Utf8StringBuilder();
            s.Append(new string('b', pad));
            s.Append(new Rune(0x1F600));
            s.AppendUtf16Unit('\uD83D').AppendUtf16Unit('\uDE00');
            Check.Equal(new string('b', pad) + "😀😀", s.ToUtf8String().ToString(), $"scalars after {pad}");
        }
        // One append larger than a buffer, of each kind.
        string big = new string('c', size * 3 + 7) + "é";
        Check.Equal(big, new Utf8StringBuilder().Append(U("x")).Append(U(big)).ToUtf8String().ToString()[1..], "large text");
        Check.Equal(big, new Utf8StringBuilder().Append(new string('x', size)).Append(big).ToUtf8String().ToString()[size..], "large UTF-16");
    }

    [Test]
    public static void RawBytesAreCheckedAtTheEnd()
    {
        var b = new Utf8StringBuilder();
        b.Append(U("ok ")).AppendByte(0xC3);
        Check.Throws<InvalidOperationException>(() => b.ToUtf8String(), "half a scalar");
        b.AppendByte(0xA9);
        Check.Equal("ok é", b.ToUtf8String().ToString(), "completed");
        Check.Equal((byte)0xA9, b.ByteAt(4), "byte at");
        b.AppendByte(0xFF).Append(U("x"));
        Check.Throws<InvalidOperationException>(() => b.ToUtf8String(), "invalid byte, later checked text");
        b.Clear();
        b.AppendByte((byte)'a');
        Check.Equal("a", b.ToUtf8String().ToString(), "ascii raw");
        Check.Throws<ArgumentException>(() => b.AppendUtf8([0xC3]), "AppendUtf8 validates");
        Check.Throws<ArgumentOutOfRangeException>(() => b.ByteAt(1), "byte past the end");
    }

    [Test]
    public static void Utf16UnitsPairUp()
    {
        var rng = new Random(23);
        for (int i = 0; i < Cases / 10; i++)
        {
            string s = Text(rng, 20);
            var b = new Utf8StringBuilder();
            foreach (char unit in s) b.AppendUtf16Unit(unit);
            Check.Equal(s, b.ToUtf8String().ToString(), "units of valid text");
        }
        var lone = new Utf8StringBuilder().AppendUtf16Unit('a').AppendUtf16Unit('\uD83D');
        Check.Throws<InvalidOperationException>(() => lone.ToUtf8String(), "lone high");
        lone.AppendUtf16Unit('\uDE00');
        Check.Equal("a😀", lone.ToUtf8String().ToString(), "paired later");
        var low = new Utf8StringBuilder().AppendUtf16Unit('\uDE00');
        Check.Throws<InvalidOperationException>(() => low.ToUtf8String(), "lone low");
        var twoHighs = new Utf8StringBuilder().AppendUtf16Unit('\uD83D').AppendUtf16Unit('\uD83D').AppendUtf16Unit('\uDE00');
        Check.Throws<InvalidOperationException>(() => twoHighs.ToUtf8String(), "first high stays lone");
    }

    [Test]
    public static void NumbersAsBjoNum()
    {
        var rng = new Random(21);
        for (int i = 0; i < Cases; i++)
        {
            int n = rng.Next(int.MinValue, int.MaxValue);
            long l = rng.NextInt64(long.MinValue, long.MaxValue);
            byte y = (byte)rng.Next(256);
            double d = BitConverter.Int64BitsToDouble(rng.NextInt64());
            Check.Equal(n.ToString(CultureInfo.InvariantCulture), Utf8Number.Format(n).ToString(), "int");
            Check.Equal(l.ToString(CultureInfo.InvariantCulture), Utf8Number.Format(l).ToString(), "long");
            Check.Equal(y.ToString(CultureInfo.InvariantCulture), Utf8Number.Format(y).ToString(), "byte");
            Check.Equal(d.ToString(CultureInfo.InvariantCulture), Utf8Number.Format(d).ToString(), "double");
            Check.Equal(n, Utf8Number.ParseInt(Utf8Number.Format(n)), "parse int");
            Check.Equal(l, Utf8Number.ParseLong(Utf8Number.Format(l)), "parse long");
            if (!double.IsNaN(d))
            {
                Check.Equal(d, Utf8Number.ParseDouble(Utf8Number.Format(d)), "parse double");
            }
        }
        foreach (double d in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 1e300, 5e-324 })
        {
            Check.Equal(d.ToString(CultureInfo.InvariantCulture), Utf8Number.Format(d).ToString(), $"double {d}");
        }
        Check.Equal(-12, Utf8Number.ParseInt(U(" -12 ")), "int allows white space, as NumberStyles.Integer");
        Check.Throws<FormatException>(() => Utf8Number.ParseLong(U(" 1")), "long takes a sign and digits only");
        Check.Throws<OverflowException>(() => Utf8Number.ParseLong(U("99999999999999999999")), "long overflows");
        Check.Equal(1.5e3, Utf8Number.ParseDouble(U("1.5e3")), "double");
        var t = U("x=42;");
        Check.Equal(42, Utf8Number.ParseInt(t.Slice(new StringCursor(2), new StringCursor(4))), "from a slice");
        var b = new Utf8StringBuilder().Append(U("-7"));
        Check.Equal(-7L, Utf8Number.ParseLong(b.AsSpan()), "from a builder");
    }

    [Test]
    public static void WritesToTextWriter()
    {
        var rng = new Random(22);
        for (int i = 0; i < 200; i++)
        {
            // Long enough to cross the stack buffer with astral scalars in the way.
            string s = Text(rng, 400);
            var w = new StringWriter();
            Utf8Text.Write(w, U(s));
            Check.Equal(s, w.ToString(), "written");
        }
        var r = new StringReader("one\ntwo é\n");
        Check.Equal("one", Utf8Text.ReadLine(r)!.Value.ToString(), "line 1");
        Check.Equal("two é", Utf8Text.ReadLine(r)!.Value.ToString(), "line 2");
        Check.True(Utf8Text.ReadLine(r) is null, "end");
    }
}
