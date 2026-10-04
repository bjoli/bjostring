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
using System.Runtime.CompilerServices;
using System.Text;
using BjoString;

// Utf8String against System.String on the operations Bjolang uses most.
// Arguments filter by substring of the case name.

var filter = args;
var text = Corpus(1 << 20, seed: 1);
var u = Utf8String.FromUtf16(text);
var shortWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(10_000).Distinct().ToArray();
var shortUtf8 = shortWords.Select(Utf8String.FromUtf16).ToArray();
var copy = new string(text.AsSpan());
var uCopy = Utf8String.FromUtf8(u.AsSpan());

Console.WriteLine($"corpus: {text.Length} UTF-16 units, {u.ByteLength} UTF-8 bytes, {shortWords.Length} distinct words");
Console.WriteLine($"{"case",-34} {"System.String",14} {"Utf8String",14}  unit");

Run("equals, 1 MB, equal", "MB/s", u.ByteLength,
    () => text.Equals(copy) ? 1 : 0,
    () => u.Equals(uCopy) ? 1 : 0);
Run("hash, 1 MB", "MB/s", u.ByteLength,
    () => text.GetHashCode(),
    () => u.GetHashCode());
Run("hash, short words", "Mops/s", shortWords.Length,
    () => { int h = 0; foreach (var w in shortWords) h ^= w.GetHashCode(); return h; },
    () => { int h = 0; foreach (var w in shortUtf8) h ^= w.GetHashCode(); return h; });
var needle = "Zyzzogeton";
var uNeedle = Utf8String.FromUtf16(needle);
Run("index of a missing word", "MB/s", u.ByteLength,
    () => text.IndexOf(needle, StringComparison.Ordinal),
    () => u.IndexOf(uNeedle) is { } c ? 1 : 0);
Run("contains a char (late)", "MB/s", u.ByteLength,
    () => text.IndexOf('\u2603'),
    () => u.IndexOf(new Rune('\u2603')) is { } ? 1 : 0);
Run("count scalars", "MB/s", u.ByteLength,
    () => Utf16Count(text),
    () => u.Count());
Run("cursor walk (ref+next)", "MB/s", u.ByteLength,
    () => Utf16Walk(text),
    () => Utf8Walk(u));
var asciiText = new string(text.Select(ch => ch < 0x80 ? ch : 'x').ToArray());
var asciiUtf8 = Utf8String.FromUtf16(asciiText);
Run("cursor walk, ASCII only", "MB/s", asciiUtf8.ByteLength,
    () => Utf16Walk(asciiText),
    () => Utf8Walk(asciiUtf8));
// Cyrillic letters, so most scalars take two bytes.
var cyrText = new string(asciiText.Select(ch => ch is >= 'a' and <= 'z' ? (char)('а' + (ch - 'a')) : ch).ToArray());
var cyrUtf8 = Utf8String.FromUtf16(cyrText);
Run("cursor walk, Cyrillic", "MB/s", cyrUtf8.ByteLength,
    () => Utf16Walk(cyrText),
    () => Utf8Walk(cyrUtf8));
var nl = Utf8String.FromUtf16("\n");
Run("split on newline", "MB/s", u.ByteLength,
    () => text.Split("\n").Length,
    () => u.Split(nl).Length);
Run("split enumerator (no allocation)", "MB/s", u.ByteLength,
    () => text.Split("\n").Length,
    () => { int n = 0; foreach (var _ in u.EnumerateSplit(nl)) n++; return n; });
var the = Utf8String.FromUtf16("the");
var THE = Utf8String.FromUtf16("THE");
Run("replace the → THE", "MB/s", u.ByteLength,
    () => text.Replace("the", "THE", StringComparison.Ordinal).Length,
    () => u.Replace(the, THE).ByteLength);
Run("upcase", "MB/s", u.ByteLength,
    () => text.ToUpperInvariant().Length,
    () => u.ToUpperInvariant().ByteLength);
Run("reverse", "MB/s", u.ByteLength,
    () => Utf16Reverse(text).Length,
    () => u.Reverse().ByteLength);
Run("reverse, Cyrillic", "MB/s", cyrUtf8.ByteLength,
    () => Utf16Reverse(cyrText).Length,
    () => cyrUtf8.Reverse().ByteLength);
Run("compare, short words", "Mops/s", shortWords.Length,
    () => { int c = 0; for (int i = 1; i < shortWords.Length; i++) c += string.CompareOrdinal(shortWords[i - 1], shortWords[i]); return c; },
    () => { int c = 0; for (int i = 1; i < shortUtf8.Length; i++) c += shortUtf8[i - 1].CompareTo(shortUtf8[i]); return c; });
var dict16 = shortWords.ToDictionary(w => w, w => 1, StringComparer.Ordinal);
var dict8 = shortUtf8.ToDictionary(w => w, w => 1, Utf8StringComparer.Ordinal);
var dict16Default = shortWords.ToDictionary(w => w, w => 1);
var dict8Default = shortUtf8.ToDictionary(w => w, w => 1);
Run("dictionary lookup, comparer", "Mops/s", shortWords.Length,
    () => { int n = 0; foreach (var w in shortWords) n += dict16[w]; return n; },
    () => { int n = 0; foreach (var w in shortUtf8) n += dict8[w]; return n; });
// What Bjolang's Eq (EqualityComparer.Default) gets.
Run("dictionary lookup, default", "Mops/s", shortWords.Length,
    () => { int n = 0; foreach (var w in shortWords) n += dict16Default[w]; return n; },
    () => { int n = 0; foreach (var w in shortUtf8) n += dict8Default[w]; return n; });
var padded = shortWords.Select(w => "  " + w + " \t").ToArray();
var paddedUtf8 = padded.Select(Utf8String.FromUtf16).ToArray();
Run("trim, short words", "Mops/s", padded.Length,
    () => { int n = 0; foreach (var w in padded) n += w.Trim().Length; return n; },
    () => { int n = 0; foreach (var w in paddedUtf8) n += w.Trim().ByteLength; return n; });
Run("concat 3, short words", "Mops/s", shortWords.Length,
    () => { int n = 0; foreach (var w in shortWords) n += string.Concat(w, ", ", w).Length; return n; },
    () => { var sep = Utf8String.FromUtf16(", "); int n = 0; foreach (var w in shortUtf8) n += Utf8String.Concat(w, sep, w).ByteLength; return n; });
Run("builder, append words", "MB/s", u.ByteLength,
    () => { var b = new StringBuilder(); foreach (var w in shortWords) b.Append(w).Append(' '); return b.ToString().Length; },
    () => { var b = new Utf8StringBuilder(); foreach (var w in shortUtf8) b.Append(w).Append(new Rune(' ')); return b.ToUtf8String().ByteLength; },
    bytesOverride: shortUtf8.Sum(w => w.ByteLength + 1));
Run("builder, append scalars", "MB/s", u.ByteLength,
    () => { var b = new StringBuilder(); foreach (var r in text.EnumerateRunes()) b.Append(r.ToString()); return b.Length; },
    () => { var b = new Utf8StringBuilder(); foreach (var r in u.EnumerateRunes()) b.Append(r); return b.ByteLength; });
Run("from UTF-16 (vs Encoding.UTF8)", "MB/s", u.ByteLength,
    () => Encoding.UTF8.GetBytes(text).Length,
    () => Utf8String.FromUtf16(text).ByteLength);
Run("to UTF-16 (vs Encoding.UTF8)", "MB/s", u.ByteLength,
    () => Encoding.UTF8.GetString(u.AsSpan()).Length,
    () => u.ToString().Length);

void Run(string name, string unit, int size, Func<int> utf16, Func<int> utf8, int bytesOverride = 0)
{
    if (filter.Length > 0 && !filter.Any(f => name.Contains(f, StringComparison.Ordinal))) return;
    double a = Measure(utf16, bytesOverride > 0 ? bytesOverride : size, unit);
    double b = Measure(utf8, bytesOverride > 0 ? bytesOverride : size, unit);
    Console.WriteLine($"{name,-34} {a,14:F0} {b,14:F0}  {unit}  ({b / a:F2}×)");
}

static double Measure(Func<int> f, int size, string unit)
{
    int sink = 0;
    var warm = Stopwatch.StartNew();
    while (warm.ElapsedMilliseconds < 300) sink += f();
    long iters = 0;
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 700)
    {
        sink += f();
        iters++;
    }
    GC.KeepAlive(sink);
    double perSecond = iters / sw.Elapsed.TotalSeconds;
    return unit == "MB/s" ? perSecond * size / 1e6 : perSecond * size / 1e6;
}

// Bjolang's StringCursor.Count and RefNext today, over UTF-16.
static int Utf16Count(string s)
{
    int n = 0;
    for (int i = 0; i < s.Length; n++)
    {
        i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
    }
    return n;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static int Utf16Walk(string s)
{
    int sum = 0;
    for (int i = 0; i < s.Length;)
    {
        char unit = s[i];
        if (!char.IsSurrogate(unit))
        {
            sum += unit;
            i++;
            continue;
        }
        Rune.DecodeFromUtf16(s.AsSpan(i), out Rune r, out int consumed);
        sum += r.Value;
        i += consumed;
    }
    return sum;
}

// The same job over UTF-16: reverse the units, then put pairs back in order.
static string Utf16Reverse(string s)
{
    return string.Create(s.Length, s, static (d, s) =>
    {
        s.CopyTo(d);
        d.Reverse();
        for (int i = 0; i < d.Length - 1; i++)
        {
            if (char.IsLowSurrogate(d[i]) && char.IsHighSurrogate(d[i + 1]))
            {
                (d[i], d[i + 1]) = (d[i + 1], d[i]);
                i++;
            }
        }
    });
}

[MethodImpl(MethodImplOptions.NoInlining)]
static int Utf8Walk(Utf8String s)
{
    int sum = 0;
    for (var c = StringCursor.Start(s); !StringCursor.AtEnd(s, c);)
    {
        (Rune r, c) = StringCursor.RefNext(s, c);
        sum += r.Value;
    }
    return sum;
}

// English-like lines with a few non-ASCII words, the kind of text programs read.
static string Corpus(int units, int seed)
{
    var rng = new Random(seed);
    string[] words =
    [
        "the", "of", "and", "to", "in", "a", "is", "that", "for", "it", "as", "was", "with", "be", "by",
        "on", "not", "he", "this", "are", "or", "his", "from", "at", "which", "but", "have", "an", "had",
        "they", "you", "were", "their", "one", "all", "we", "can", "her", "has", "there", "been", "if",
        "Holmes", "Watson", "London", "detective", "evidence", "remarkable", "singular", "observation",
        "café", "naïve", "Zürich", "smörgåsbord", "Москва", "東京", "😀", "señor", "Ελλάδα",
    ];
    var sb = new StringBuilder(units + 64);
    while (sb.Length < units)
    {
        int n = rng.Next(4, 14);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(words[rng.Next(words.Length)]);
        }
        sb.Append(".\n");
    }
    sb.Append('\u2603');
    return sb.ToString();
}
