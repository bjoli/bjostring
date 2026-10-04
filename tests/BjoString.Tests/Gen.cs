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

namespace BjoString.Tests;

/// <summary>Random valid text for differential tests against <c>System.String</c>.</summary>
internal static class Gen
{
    // A small alphabet so that searches hit, with what makes UTF-8 interesting:
    // widths 1 to 4, white space outside ASCII, scalars whose case mapping
    // changes width (ı, İ), astral case pairs (𐐨/𐐀), and U+E000..U+FFFF, where
    // scalar order and UTF-16 order disagree.
    private static readonly string[] Pieces =
    [
        "a", "b", "c", "A", "B", "x", " ", "\t", "\n", "ab", "é", "ß", "ı", "İ",
        "\u00A0", "\u0085", "\u2028", "\u3000", "ж", "Ж", "中", "\uE000", "\uFFFD",
        "😀", "𐐨", "𐐀",
    ];

    public static int Cases { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("BJOSTRING_FUZZ_CASES"), out int n) ? n : 5000;

    public static string Text(Random rng, int maxPieces)
    {
        var sb = new StringBuilder();
        int n = rng.Next(maxPieces + 1);
        for (int i = 0; i < n; i++)
        {
            sb.Append(Pieces[rng.Next(Pieces.Length)]);
        }
        return sb.ToString();
    }

    public static Utf8String U(string s) => Utf8String.FromUtf16(s);

    /// <summary>The byte offset of a UTF-16 index in the UTF-8 form of <paramref name="s"/>.</summary>
    public static int ByteOffset(string s, int utf16Index) => Encoding.UTF8.GetByteCount(s.AsSpan(0, utf16Index));

    /// <summary>Scalar order, the reference for UTF-8 byte order.</summary>
    public static int CompareScalars(string a, string b)
    {
        var x = a.EnumerateRunes().GetEnumerator();
        var y = b.EnumerateRunes().GetEnumerator();
        while (true)
        {
            bool hx = x.MoveNext();
            bool hy = y.MoveNext();
            if (!hx || !hy) return hx.CompareTo(hy);
            int c = x.Current.Value.CompareTo(y.Current.Value);
            if (c != 0) return c;
        }
    }

    public static string Runes(IEnumerable<Rune> runes) => string.Concat(runes.Select(r => r.ToString()));
}
