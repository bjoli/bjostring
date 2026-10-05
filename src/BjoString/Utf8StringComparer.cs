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

namespace BjoString;

/// <summary>
/// Ordinal equality and order of strings, with an alternate lookup so that a
/// <c>Dictionary</c> or <c>HashSet</c> keyed by strings can be probed with raw
/// UTF-8 bytes without making a string first
/// (<c>GetAlternateLookup&lt;ReadOnlySpan&lt;byte&gt;&gt;()</c>). A slice is a
/// string, so it needs none.
/// </summary>
public sealed class Utf8StringComparer :
    IEqualityComparer<Utf8String>, IComparer<Utf8String>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, Utf8String>
{
    public static Utf8StringComparer Ordinal { get; } = new();

    private Utf8StringComparer() { }

    public bool Equals(Utf8String x, Utf8String y) => x.Equals(y);

    public int GetHashCode(Utf8String obj) => obj.GetHashCode();

    public int Compare(Utf8String x, Utf8String y) => x.CompareTo(y);

    public bool Equals(ReadOnlySpan<byte> alternate, Utf8String other) => other.ContentEquals(alternate);

    public int GetHashCode(ReadOnlySpan<byte> alternate) => Utf8Ops.Hash(alternate);

    public Utf8String Create(ReadOnlySpan<byte> alternate) => Utf8String.FromUtf8(alternate);
}
