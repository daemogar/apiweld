# ApiWeld

ApiWeld generates typed C# HTTP clients from OpenAPI descriptions. It is a normalizer plus a
small command-line tool: given a description, it produces a smaller, more regular version of it
that a code generator — including a later ApiWeld package — can turn into a client whose shapes
are easy to map onto hand-written models.

## Why normalization exists

OpenAPI descriptions are written by many different tools and many different people, and the
same intent gets expressed in shapes that are needlessly hard to generate code from:

- **Optional values as unions.** A value that may simply be absent is sometimes written as a
  `oneOf` or `anyOf` against an empty, do-nothing branch, rather than a plain optional or
  nullable field. A generator that takes the union literally produces a wrapper type for a
  distinction that does not exist in the data.
- **Country- or variant-specific shapes as unions of objects.** The same field is sometimes
  described as a union of many near-identical object shapes — one per variant of some outside
  classification — rather than one shape with the fields every variant agrees on. Taken
  literally, this produces one generated class per variant, multiplying the number of types a
  caller has to know about for what is, in practice, one kind of thing.
- **Repeated component schemas.** The same shape is sometimes declared more than once under
  different names — once for a request body and once for a response, say — because whatever
  produced the description did not notice they were identical. Generating a distinct type for
  each copy multiplies types without adding any distinction a caller can use.

ApiWeld's normalizer addresses each of these before a single line of client code is generated:
it collapses a union down to one permissive shape, merges the properties and constraints every
surviving variant agrees on, and folds identical component schemas onto one canonical name,
rewriting every reference to point at it. The result is a description that describes the same
data, with far fewer, more mappable, generated types.

## Union collapse rules

Union collapse replaces every `oneOf`/`anyOf` with a single, more permissive schema:

1. Drop every *absent* branch — one with `maxProperties: 0`, an object declaring no
   properties, a string with `maxLength: 0`, or a bare nullable string carrying no `enum`
   and no `pattern`.
2. If nothing survives, keep all branches and treat the union as not emptiable.
3. Merge the survivors pairwise: every property either offers, only the `required` entries
   **both** carry, and no `enum`, `pattern` or `format` that they disagree on **or that only
   one of them carries**.
4. If any branch was dropped and the merged result is a string, drop its `format` and
   `pattern` too. A branch saying the value may arrive blank makes the survivor's constraints
   a possibility rather than a promise — this is the rule that keeps a timestamp typed as a
   nullable string rather than a date.
5. The property's own `title` and `description` override anything a variant carried, so a
   merged property documents itself rather than describing whichever variant happened to win.

## Deduplication rules

Deduplication folds every identical, referenced component schema onto one canonical name and
rewrites references to point at it:

1. Only *referenced* schemas participate. An orphan is never generated, so folding one in would
   rename a live type after a dead one.
2. The survivor is chosen by precedence — a response wins over a request, and the primary
   get-response over any other — because a response is the type callers actually hold.
3. Schemas that are merely similar are never merged. Equality is structural and exact.

References are rewritten to point at the survivor; a reference to a schema that does not exist is
left exactly as written.

## Order of the passes

The normalizer runs its stages in a fixed order:

1. **Format substitution**, on the raw document text, before anything is parsed — see
   `CONTRIBUTING.md`, "Format substitution is text replacement."
2. **Union collapse**.
3. **Deduplication**.

Collapse has to come before deduplication: two schemas that differ only in an absent branch are
not structurally identical until that branch is collapsed away. Deduplicating first would compare
them as written, find no match, and leave both in place.

## Packages

| Package | What it is |
| --- | --- |
| `ApiWeld.Core` | The normalizer itself: a library over the description's JSON tree. No dependencies beyond the shared framework. |
| `ApiWeld.Cli` | A command-line tool, installed as a .NET tool under the command `apiweld`, that reads a description from disk, normalizes it, and writes the result. |

Both are licensed AGPL-3.0-or-later; see [`LICENSE.txt`](LICENSE.txt).

## Installing and running the tool

```bash
dotnet tool install --global ApiWeld.Cli
apiweld normalize path/to/description.json
```

## Building from source

```bash
dotnet restore ApiWeld.slnx
dotnet build   ApiWeld.slnx
dotnet test    ApiWeld.slnx
```

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for prerequisites, the project's style conventions, and
how its tests are organized.
