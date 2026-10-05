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

using System.Collections;
using System.Text;

namespace BjoString;

/// <summary>The scalars of a string, decoded in place.</summary>
public struct RuneEnumerator : IEnumerator<Rune>, IEnumerable<Rune>
{
    private readonly byte[]? _bytes;
    private readonly int _end;
    private int _next;
    private Rune _current;

    internal RuneEnumerator(byte[]? bytes, int start, int end)
    {
        _bytes = bytes;
        _next = start;
        _end = end;
        _current = default;
    }

    public readonly Rune Current => _current;

    readonly object IEnumerator.Current => _current;

    public bool MoveNext()
    {
        if (_next >= _end) return false;
        _current = Utf8Ops.Decode(_bytes, _next, out int width);
        _next += width;
        return true;
    }

    public readonly RuneEnumerator GetEnumerator() => this;

    readonly IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => this;

    readonly IEnumerator IEnumerable.GetEnumerator() => this;

    void IEnumerator.Reset() => throw new NotSupportedException();

    readonly void IDisposable.Dispose() { }
}

/// <summary>The fields between separators, as slices of the string.</summary>
public struct SplitEnumerator : IEnumerator<Utf8String>, IEnumerable<Utf8String>
{
    private readonly byte[]? _bytes;
    private readonly Utf8String _separator;
    private readonly int _end;
    private int _next;
    private bool _done;
    private Utf8String _current;

    internal SplitEnumerator(Utf8String s, Utf8String separator)
    {
        _bytes = s.Bytes;
        _separator = separator;
        _next = s.Start;
        _end = s.End;
        _done = false;
        _current = default;
    }

    public readonly Utf8String Current => _current;

    readonly object IEnumerator.Current => _current;

    public bool MoveNext()
    {
        if (_done) return false;
        var sep = _separator.AsSpan();
        int k = sep.IsEmpty ? -1 : new ReadOnlySpan<byte>(_bytes, _next, _end - _next).IndexOf(sep);
        if (k < 0)
        {
            _current = new Utf8String(_bytes, _next, _end - _next);
            _done = true;
            return true;
        }
        _current = new Utf8String(_bytes, _next, k);
        _next += k + sep.Length;
        return true;
    }

    public readonly SplitEnumerator GetEnumerator() => this;

    readonly IEnumerator<Utf8String> IEnumerable<Utf8String>.GetEnumerator() => this;

    readonly IEnumerator IEnumerable.GetEnumerator() => this;

    void IEnumerator.Reset() => throw new NotSupportedException();

    readonly void IDisposable.Dispose() { }
}
