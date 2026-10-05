# BjoString

UTF-8 strings for C#, made to be Bjolang's `string`: an immutable string
type that slices without copying, cursors, a builder, number formatting and parsing, and a
bridge to .NET's UTF-16 text I/O. Strings are searched as bytes, which is
what the [BjoRx](https://github.com/bjoli/rx-for-bjolang) regex engine
matches.

Everything that has a vectorized routine in .NET goes to it (`IndexOf`,
`SequenceEqual`, `SequenceCompareTo`, `Count`, `Utf8.IsValid`,
`Ascii.ToUpper`, `Encoding.UTF8`, the `IUtf8SpanFormattable` number
formatters); the library adds only what .NET has no routine for.

## Who wrote this

**All of the code, tests and documentation in this repository were written
by an AI** (Anthropic's Claude, run through the ECA editor assistant). No
person claims authorship of any of it. In particular, the person who keeps
this repository did not write it and makes no claim to have written it.

## License

Mozilla Public License, version 2.0, with the linking exception in
`LICENSE` (the same terms as Bjolang's runtime).

## Design

- **`Utf8String`** is a `readonly struct` of one reference, as wide as a
  .NET `string`: the `byte[]` itself when the string is all of it, or a
  small object holding the array, a start and an end when it is a slice.
  A string that was built costs one allocation; a slice costs one more,
  of 32 bytes, and copies nothing. `default` is the empty string.
- **Slices are strings, made by name.** `Slice`, `TrimSlice`,
  `TrimStartSlice`, `TrimEndSlice`, `SplitSlices` and `EnumerateSplit` share
  the array of the string they are taken from; `Substring`, `Trim*` and
  `Split` copy, so that nothing keeps a large text alive without asking.
  `Copy` gives a slice an array of its own, and `IsCompact` says whether it
  has one.
- **Always valid UTF-8.** Every way in validates, replaces invalid input with
  U+FFFD (`FromUtf16` for unpaired surrogates, `FromUtf8Lossy`), or is built
  from valid parts. So a cursor steps by reading one lead byte, and a byte
  search for a valid needle can only match on scalar boundaries.
- **Scalars are `System.Text.Rune`.**
- **Order is byte order**, which for UTF-8 is scalar order. It differs from
  .NET's UTF-16 ordinal order only where an astral scalar meets
  U+E000..U+FFFF (`"\uFFFD" < "😀"` here, the other way round in UTF-16).
- **Equality and hashing are by content**, so a slice equals, hashes and
  orders as a string with the same text, and finds it as a dictionary key.
  The hash is Marvin, the randomized hash of .NET's `string`, over the
  bytes. `Utf8StringComparer` also lets a dictionary be probed with raw
  UTF-8 (`GetAlternateLookup<ReadOnlySpan<byte>>()`).
- **`StringCursor`** is a byte offset at a scalar boundary, opaque outside
  this assembly: its offset and constructor are `internal`, visible only to
  `BjolangRuntime` (which turns regex byte offsets into cursors) and the
  tests. A cursor from another string is caught when it lands inside a
  scalar, so `Substring` cannot cut one in two. The offset is into the
  array, so a slice has the cursors of the string it was taken from, and a
  position found in a slice is valid in that string.
- **`Utf8StringBuilder`**: appends of strings, scalars and numbers
  are valid by construction. `AppendByte` is the escape hatch for scanners
  that copy input byte by byte; those bytes are validated once, by
  `ToUtf8String`, which throws if they did not form whole scalars.
- Operations that change nothing return the string they were given
  (`Replace` without a hit, `ToUpperInvariant` of upper case text, a whole
  `Substring`, `Trim` without white space).

## For Bjolang

| Bjolang | BjoString |
|---|---|
| `string` | `Utf8String` |
| `char` | `Rune` (it lacks `IComparisonOperators`, which Bjolang's generic `<` asks for) |
| string literal | a hoisted `Utf8String.FromUtf8("..."u8)` |
| match on a string literal | `s.ContentEquals("..."u8)` |
| `string-length` | `ByteLength` (bytes, not UTF-16 units) |
| `string-count` | `Count()` (vectorized) |
| `string-cursor-*`, `substring` | `StringCursor.Start/End/AtEnd/Ref/Next/Prev/RefNext/Substring` |
| `string-search`, `string-search-backward` | `IndexOf`, `LastIndexOf` (cursors) |
| `string-append`, `str` | `Utf8String.Concat` (`params ReadOnlySpan`, no array) |
| `string-contains?`, `-starts-with?`, `-ends-with?` | `Contains`, `StartsWith`, `EndsWith` |
| `string-replace`, `string-split`, `string-join` | `Replace`, `Split` / `EnumerateSplit`, `Join` |
| `string-upcase`, `string-downcase` | `ToUpperInvariant`, `ToLowerInvariant` (per scalar, as today) |
| `string-trim*`, `string-pad-*`, `string-reverse` | `Trim*`, `PadLeft`/`PadRight`, `Reverse` |
| `substring/slice`, `string-trim*/slice`, `string-split/slice`, `string-copy` | `Slice`, `Trim*Slice`, `SplitSlices`, `Copy` |
| `stringbuilder-*` | `Utf8StringBuilder` (`add-code!` → `AppendByte`, `code-ref` → `ByteAt`, `length` → `ByteLength`) |
| `int->string`, `string->int`, `BjoNum.Parse*(StringBuilder)` | `Utf8Number.Format`, `Utf8Number.Parse*` (strings, spans, builders) |
| `write-string`, `display` | `Utf8Text.Write(TextWriter, ...)` while ports are UTF-16 |
| `(std rx)` | `new Input(s.AsMemory())`, match offsets → `s.CursorAt(offset)` |
| .NET calls taking `string` | `ToString()` / `FromUtf16` at the boundary |

Things that change for Bjolang programs: `string-length` and the `*-code-*`
procedures count bytes, so scanners that compare codes above 127 need a
look; order between astral scalars and U+E000..U+FFFF; `stringbuilder->string`
throws on invalid bytes from `add-code!` instead of keeping them as U+FFFD.

## Speed

`timeout 300 dotnet run -c Release --project bench/BjoString.Bench`, against
`System.String` doing the same thing, on 1 MB of English-like text with a
non-ASCII word every few lines (Ryzen 9 5900X; both columns per UTF-8 byte):

| case | ratio |
|---|---:|
| equals, hash (1 MB) | 1.8× |
| count scalars | 21× |
| split enumerator (slices) / split to strings | 8× / 1.3× |
| replace, upcase, index of | 1.1–1.5× |
| builder, appending scalars / words | 1.9× / 1.0× |
| compare, concat, trim (short words) | 0.85–1.1× |
| cursor walk, ASCII / mixed | 0.7–0.8× / 0.5× |
| cursor walk, a slice | 0.15–0.3× |
| dictionary lookup (short words) | 0.65× |

The cursor walk pays for decoding two- and three-byte sequences that UTF-16
reads as one unit, and for a type test per step: a string that is all of its
array takes the path the loop is laid out for, and a slice the other one. Dictionaries of .NET strings use a fast unrandomized hash until
they see collisions; `Utf8String` always uses Marvin.

## Building and testing

```sh
dotnet build
timeout 180 dotnet run --project tests/BjoString.Tests --no-build          # all tests
timeout 180 dotnet run --project tests/BjoString.Tests --no-build -- Split # filter by Class.Method
timeout 300 dotnet run -c Release --project bench/BjoString.Bench          # benchmarks against System.String
```

The tests compare each operation with `System.String` on random text with
scalars of every width; `BJOSTRING_FUZZ_CASES` sets the number of cases
(default 5000).
