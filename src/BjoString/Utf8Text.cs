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
using System.Text.Unicode;

namespace BjoString;

/// <summary>
/// The bridge to UTF-16 text I/O (<see cref="TextWriter"/>), which Bjolang's
/// ports are today. A stream needs no bridge: write <c>AsSpan()</c>.
/// </summary>
public static class Utf8Text
{
    /// <summary>Writes the text through a small stack buffer, without making a .NET string.</summary>
    public static void Write(TextWriter writer, ReadOnlySpan<byte> utf8)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Span<char> chars = stackalloc char[256];
        while (!utf8.IsEmpty)
        {
            // Stops before a scalar that does not fit, so no surrogate pair is split.
            Utf8.ToUtf16(utf8, chars, out int read, out int written, replaceInvalidSequences: true);
            writer.Write(chars[..written]);
            utf8 = utf8[read..];
        }
    }

    public static void Write(TextWriter writer, Utf8String s) => Write(writer, s.AsSpan());

    public static void Write(TextWriter writer, StringSlice s) => Write(writer, s.AsSpan());

    /// <summary>The next line, or null at the end.</summary>
    public static Utf8String? ReadLine(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? line = reader.ReadLine();
        return line is null ? null : Utf8String.FromUtf16(line);
    }
}
