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

namespace BjoString;

/// <summary>
/// Numbers to and from UTF-8 text in the invariant culture, with the same
/// styles as Bjolang's <c>BjoNum</c>, and without a UTF-16 string between.
/// </summary>
public static class Utf8Number
{
    public static Utf8String Format(int n) => Format<int>(n);

    public static Utf8String Format(long n) => Format<long>(n);

    public static Utf8String Format(byte n) => Format<byte>(n);

    /// <summary>The shortest text that reads back as the same double.</summary>
    public static Utf8String Format(double d) => Format<double>(d);

    private static Utf8String Format<T>(T value) where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[64];
        if (!value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("A number did not fit in 64 bytes.");
        }
        return new(buffer[..written].ToArray());
    }

    public static int ParseInt(ReadOnlySpan<byte> utf8) =>
        int.Parse(utf8, NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>A sign and digits only; overflows rather than saturating.</summary>
    public static long ParseLong(ReadOnlySpan<byte> utf8) =>
        long.Parse(utf8, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static double ParseDouble(ReadOnlySpan<byte> utf8) =>
        double.Parse(utf8, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static int ParseInt(Utf8String s) => ParseInt(s.AsSpan());

    public static long ParseLong(Utf8String s) => ParseLong(s.AsSpan());

    public static double ParseDouble(Utf8String s) => ParseDouble(s.AsSpan());
}
