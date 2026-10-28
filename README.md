# ApiWeld

ApiWeld generates typed C# HTTP clients from OpenAPI descriptions. It is a normalizer, a client
generator, a small runtime the generated clients call, and a command-line tool that drives the
first two. The normalizer turns a description into a smaller, more regular version of itself; the
generator turns one or more descriptions into a single typed client whose shapes are easy to map
onto hand-written models.

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

Union collapse replaces every `oneOf`/`anyOf` with a single, more permissive schema. A type is
read the same way whether it is written as OpenAPI 3.0's `type` plus `nullable: true` or as a
3.1 `type` array such as `["string", "null"]`.

1. Drop every *absent* branch: one with `maxProperties: 0`, a `false` schema, a `null` type,
   an object declaring no properties, a string with `maxLength: 0`, or a bare nullable string
   carrying no `enum` and no `pattern`.
2. If nothing survives, keep all branches and treat the union as not emptiable.
3. A surviving `true` schema accepts anything, so the union becomes an empty schema.
4. Merge the survivors pairwise, by shape. Each variant that is a local `$ref` is replaced by
   the schema it names first; only a lone survivor stays a reference, so it keeps its name. A
   reference back to a schema already being merged is left as written, which stops a union
   that refers to itself.
   - A property either variant offers survives.
   - `required` keeps only what **both** variants require. A variant with no `required` list
     requires nothing, so the merge then requires nothing.
   - `type` survives when the variants agree on one real type. An integer and a number widen
     to a number, and `null` is kept when either allowed it. Two different real types drop
     `type`.
   - No `enum`, `pattern` or `format` survives that they disagree on or that only one of them
     carries.
5. Keywords written beside the union apply alongside it. Its own `properties` and `required`
   add to the merged ones. Every other keyword replaces the variants' value, so the property's
   own `title`, `description`, `nullable`, `readOnly` or `default` is what survives. A second
   union beside the first is collapsed the same way.
6. If any branch was dropped and the merged result is a string, drop its `format` and
   `pattern` too. A branch saying the value may arrive blank makes the survivor's constraints
   a possibility rather than a promise — this is the rule that keeps a timestamp typed as a
   nullable string rather than a date.

## Format substitution

Some descriptions spell standard formats in non-standard ways. Substitution rewrites the value
of every `format` keyword it recognizes: `guid` becomes `uuid`, and `email` becomes `string`.
It walks the parsed document, so spacing makes no difference. It skips values that are data
rather than schema: `example`, `examples`, `default`, `enum`, `const` and every `x-` extension.
A description string that happens to mention a format is left alone.

## Deduplication rules

Deduplication folds every identical, referenced component schema onto one canonical name and
rewrites references to point at it:

1. Only *referenced* schemas participate. An orphan is never generated, so folding one in would
   rename a live type after a dead one.
2. A request and a response are never folded together, however alike they look. A schema's
   direction is wherever it is reached from: under an operation's `requestBody`, under its
   `responses`, or both. That includes a schema nested inside another one and a schema reached
   through `components/requestBodies` or `components/responses`. Only schemas reached in the
   same directions can fold.
3. Within a group, the resource's own get-response survives if it is there. Otherwise the first
   schema in document order survives.
4. Schemas that are merely similar are never merged. Equality is structural and exact.

References are rewritten to point at the survivor; a reference to a schema that does not exist is
left exactly as written.

## Order of the passes

The normalizer runs its stages in a fixed order:

1. **Format substitution**, so that variants spelling one format two ways agree when merged.
2. **Union collapse**.
3. **Deduplication**.

Collapse has to come before deduplication: two schemas that differ only in an absent branch are
not structurally identical until that branch is collapsed away. Deduplicating first would compare
them as written, find no match, and leave both in place.

## Generating a client

`apiweld generate` turns one or more OpenAPI descriptions into a single typed C# client and one
set of models, written as `.cs` files you check in. Every description is normalized first (the
sections above), then all of them are merged into one path tree and one set of types. The
generated code calls the `ApiWeld.Http` runtime (see "The runtime").

### The manifest

`apiweld generate apiweld.json` reads a manifest that sits beside the code it generates:

```json
{
  "descriptions": ["resources/*.json"],
  "namespace": "Example.Integration",
  "client": "ExampleClient",
  "output": "Generated",
  "basePaths": ["/api", "/query"],
  "paging": { "offset": "offset", "limit": "limit", "totalHeader": "X-Total-Count" },
  "names": { "widget-parts": "Part" },
  "singulars": { "octopi": "octopus" }
}
```

- `descriptions` — required. File paths, or a wildcard in the file name within one folder
  (`resources/*.json`), relative to the manifest. A description is known by its file name, so
  two folders may not hold files with the same name.
- `namespace`, `client` — required. The namespace generated code is written in, and the name of
  the consumer's root client class, whose generated half the tool writes.
- `output` — where generated files go, relative to the manifest. Defaults to `Generated`.
- `basePaths` — leading path segments left out of navigation; see "Paths and versions".
- `paging` — the names that make an operation paged; see "Page walking". Defaults as shown.
- `names` — overrides for derived names; see "Names".
- `singulars` — plural words the built-in rules get wrong, each mapped to its singular, one word
  to one word; see "Names".

Unknown keys are refused — at the top level and inside `paging` — so a misspelled setting is
reported instead of ignored; keys starting with `$`, such as `$schema`, are allowed. `client`,
each part of `namespace`, and every `names` value must be a C# identifier that is not a keyword.

The manifest holds no version settings. Versions come from the descriptions and are chosen in
code, at every call site.

### Paths and versions

Every path from every description goes into one tree. Base paths listed in the manifest are
dropped first, and paths that meet once they are dropped merge into one node, so `/api/lookups`
and `/query/lookups` both become `Api.Lookups`. A path parameter is the same node whatever each
description calls it: `/widgets/{id}` and `/widgets/{widgetId}/parts` share one indexer, leading
to `Api.Widgets[id].Parts`. The indexer takes a `Guid` when every description declares a UUID
there, and a `string` otherwise (with a warning when they disagree). The same verb at the same
version on one merged path is an error, and so is a segment that mixes text and parameters, such
as `{id}.json` or `{from}-{to}`: every parameter must fill a whole segment. A path that carries a
query string or fragment, such as `/widgets?kind=a`, is an error too.

An operation's version is read from the media type of its success response — never from the
description's `info.version`, which is only reported when it disagrees. When a response
declares several media types, the most specific versioned JSON type wins over plain
`application/json`. The version member sits just before the verbs:
`Api.Widgets[id].V2.GetAsync()`. An operation whose success response carries no versioned media
type is `V0`; one with no response body at all takes the single version the rest of its
description uses, if there is exactly one. A request body and the error bodies keep their own
media types inside their operation, and never create members of their own.

A success declared as the range `2XX` is read like a numbered one; when both are present, the
numbered code wins. `default` is never read as the success, because it normally describes
errors: an operation whose only success is `default` is generated without a response body, with
a warning.

Several files for one resource at different versions generate side-by-side members. Removing a
file removes its member, so every call site that named it stops compiling — which is how a
version change is noticed, and then made, by hand.

`Accept` and `Content-Type` header parameters are absorbed into the operation; any other header
parameter becomes a property on the operation's query object.

### Names

A generated type is named `{Root}{Version}{Path}{Suffix}`:

- **Root** — the description's file name in PascalCase with its last word made singular:
  `academic-disciplines` becomes `AcademicDiscipline`. (`-ies` becomes `-y`; `-sses`, `-xes`,
  `-ches`, `-shes` and `-uses` drop `-es`; otherwise a trailing `-s` is dropped unless the word
  ends `-ss`, `-us` or `-is`. Common irregular plurals are known — `people`, `series`,
  `analyses`, `indices`, `warehouses` and others.)
- **Version** — the version member, such as `V12_6`.
- **Path** — the property path from the body, with array items singular: `addresses[].place`
  becomes `AddressPlace`. The same singular rules apply, and the manifest's `singulars` map comes
  first, so a word the rules get wrong can be fixed even for an array that has no schema name to
  override.
- **Suffix** — always `Response` or `Request`, so a name does not change when a description
  starts or stops sharing a shape between the two. Error bodies have no suffix; they are named
  after their schema and the error media type's version, such as `ErrorsV2`. A version already
  spelled at the end of the schema's name (`errors_2_0_0`, `errors_v2`) is dropped when the media
  type supplies one, so the version is not written twice.

Types merge when their shape and direction are identical, and a merged type takes its shortest
candidate name, ties broken in ordinal order. Within one description that is the whole rule. A
part merged within one description keeps the shortest name, even when it sits under two
differently named components (an `errors` and a `problems` body sharing a `source` object give
`ErrorsV2Source`).

Across descriptions, a shape merges only when every description reaches it at the same path,
and every shape nested inside it merges across descriptions too. It is then named by that shared
tail alone: `metadata` in every description becomes `MetadataResponse`, not one description's
`{Root}{Version}MetadataResponse`. Any other shape stays separate in each description, so a whole
body two descriptions share gives each its own type (`WidgetV2Response` and `GadgetV0Response`).
This keeps a description's type names fixed when another description is added, removed or
renamed. A shared name that depended on the shortest name across every file would change with no
edit near it. The nested-shape condition stops a merged type from holding a type that belongs to
just one of its descriptions. A small reference object reached at many paths keeps the parts
holding it in their own descriptions. Error bodies are the exception: they merge across
descriptions whenever their shapes match, as one error type usually serves every description.

The generator finds these shapes by building the model twice. The first pass merges every shape
everywhere, as the old rule did, and only answers which shapes qualify. The second pass builds
the model that is generated.

When different shapes want one name, the one a GET returns keeps it — or, when no GET returns
any of them, the one reached from strictly the most descriptions — and the others gain their
verb after the version (`ThingV1PostResponse`), with a warning. When the verb does not separate
them, a third step renames only the shapes the verb step renamed (every shape but a keeper,
if it renamed none), so a type that holds the name as its own plain name keeps it. If the
clash spans descriptions, each renamed shape is prefixed with its description's root
(`WidgetErrorsV2`); renamed shapes that still share a root, or every renamed shape when the
clash is within one description, then gain the route segments not shared by the shapes of the
original clash, after the verb (`ThingV1PostSearchRequest` for `POST /search/things` beside
`POST /things`; a path parameter counts as `Item`). Once a root prefix applies, only the
original shapes from that root are compared. A clash that survives all three is an error
naming the fix.

A model may not take a name the generated navigation already has — `{Path}Node`,
`{Path}{Version}Operations`, `{Path}{Version}{Verb}Query`, `{Client}Api` — nor the name of a
type the generated code uses, such as `ApiTransport` or `JsonElement`; that is an error asking
for a `names` entry.

`names` in the manifest overrides a base name. A description's file stem replaces its Root; a
component schema's name replaces the base of the type built from it, and the types nested under
it are named from the override.

Properties are PascalCase with their JSON names kept in `[JsonPropertyName]`; every property is
nullable, and `required` is recorded in the property's summary only. Every model is a `partial`
class with settable properties and an `AdditionalData` dictionary for fields the description
does not declare. String enums become extensible enums (see "Tolerant JSON").

### Output

Generated files are written beneath `output`: the client's generated half as `{Client}.g.cs`,
one file per path under `Paths/`, and one per type under `Models/`. Every file starts with the
same two lines:

```csharp
// <auto-generated/>
// Generated by apiweld generate. Do not edit; regenerate instead.
```

Every file is UTF-8 with a byte-order mark and LF line endings, and is identical for identical
input — so regenerating without changes leaves nothing to commit, and a file whose content has
not changed is not rewritten at all. Generated files left over from an earlier run are deleted,
but only files starting with both of those lines: the tool refuses to overwrite any other file,
including one another generator wrote, and never deletes one. It also refuses, writing nothing,
any output path that lies outside `output` or that differs from another only in case. A folder
that deleting stale files leaves empty is removed as well (the output folder itself never is). A
file the tool cannot read, write or delete — locked by another program, or read-only — is
reported as an error naming it instead of stopping the run with an exception.

The generated half of the client derives from `ApiClient` and declares no constructor; the
consumer's own half declares one (see "Registration"). Hand-written operations go in further
parts of the same partial class and reach the generated navigation through its `internal` `Api`
property — for example `Api.Widgets[id].V2.GetAsync(cancellationToken)`.

## The runtime

`ApiWeld.Http` is the package generated clients reference. Generated code describes each
operation as data and hands it to the runtime, which builds, sends and reads the request.

### Media-type versions

Many descriptions version an API through its media types rather than its URLs — a response is
declared as `application/vnd.example.v12.6.0+json` instead of plain `application/json`. The
runtime reads the version token from such a media type (`.v` followed by dot-separated digits,
immediately before `+` or the end) and normalizes it by dropping leading zeros and trailing zero
parts, so `v15`, `v15.0` and `v15.0.0` all read as `15`. A generated client names each version
member after that normalized form: `12.6` becomes `V12_6`, and an operation with no versioned
media type is `V0`.

### Tolerant JSON

Descriptions and servers do not always agree on a value's JSON kind: a field declared as a string
may arrive as a number, and a number may arrive as text. A strict reader either throws or, worse,
silently drops the value. The runtime's serializer options coerce instead:

- A JSON number or boolean read into a string yields its raw JSON text — `2026`, `1.50`, `true`.
- JSON text read into a number or boolean is parsed with the invariant culture; text that does
  not parse becomes `null`.
- A JSON number read into an integer is accepted when its value is whole, whatever its form —
  `3.0` and `1e2` read as `3` and `100`. A number that does not fit the property (a fraction or
  an out-of-range value for an integer, any number for a boolean) becomes `null` like unparseable
  text, so one odd value never fails a whole response.
- Dates and times are read leniently, and text that still does not parse becomes `null` the same
  way. A date may be ISO 8601 extended (`2026-10-27`) or basic (`20261027`), or use slashes as
  `yyyy/M/d` or month first as `M/d/yyyy`; a day-first slash date such as `27/10/2026` is
  rejected rather than guessed. A time may follow after `T` or a space, as `H:mm[:ss]` or
  `HHmm[ss]`, with a fraction of a second after a point or a comma and an optional offset (`Z`,
  `±HH`, `±HHmm` or `±HH:mm`). A `date` (`DateOnly`) takes the date part as written. A
  `date-time` (`DateTimeOffset`) with no offset is read as UTC, and a bare date as midnight UTC.
  A `time` (`TimeOnly`) is the time part alone, and keeps its clock time when it carries an
  offset. Fractions finer than a tick are dropped. Each is written back as ISO 8601 extended.
- A lone JSON object where a list is declared reads as a one-item list, at the top level of a
  response as well as in a property. Some servers answer a single object where their description
  promises an array. A scalar where a list is declared still fails, as before.
- Property names stay case-sensitive, so a field the description misnames is not quietly matched
  to the wrong property; it lands in the model's `AdditionalData`, where it can be seen.

String enums are generated as *extensible* enums — a small struct with one static member per
known value. A value the description did not list still deserializes, keeping its text, so a
server adding a new value never breaks an existing client.

### Version checking

Every generated operation asks for exactly one version: its `Accept` header is the versioned
success media type plus the operation's error media types, never a bare `application/json` that
would let the server pick. When a success response's `Content-Type` carries a *different*
version, the runtime throws `MediaTypeMismatchException` naming both, rather than reading a
payload the models were not generated for. A response that names no version is read as normal.
Setting `VersionMismatch` to `Warn` logs a warning instead of throwing.

A non-2xx response throws `ApiResponseException`. When the description declares a body for that
status, the exception is the generic `ApiResponseException<TError>` with the body read into
`Error`; either way the raw body, status, method, URL template and requested version are kept.
The body type is looked up the way OpenAPI defines precedence: the exact code (`404`) first,
then its range (`4XX` or `5XX`), then `default`.

### Page walking

Every `GET` that returns a list is paged, except one at an item path (a path ending in a
parameter, such as `/widgets/{id}`) that declares neither paging parameter: that is one record's
lookup, so its `GetAsync` is a single request. A paged `GetAsync` returns every row,
`EnumerateAsync` streams rows as pages arrive, and `GetPagedAsync` returns one `Page<T>` whose
`NextAsync` fetches the page after it. The offset and limit parameters are named by the
manifest's `paging` settings.

The walk sends a limit only when the caller sets one, through the query object or
`GetPagedAsync`, so a client's page size otherwise stays the server's business. A described
`maximum` for the limit caps what is sent. The size of a full page is the limit sent; or, when
none is, the `default` the description gives the limit parameter, capped by that `maximum` as
well; or, when there is neither, the size of the first page. There is no built-in fallback size:
a guessed size larger than the server's would end the walk after one short page. The walk starts
at offset zero and:

1. **takes a page longer than the limit it sent as the whole answer**: the server ignored the
   limit and does not page at all, whatever its total says;
2. otherwise **follows the total-count header** when the response sends one, requesting until
   the rows fetched reach the total, or a page comes back empty;
3. **without that header, follows the page size**. A full page is followed by one more request.
   A shorter page means the rows ran out, and the walk ends with no further request. When the
   rows are an exact multiple of the page size, the walk ends on an empty page instead of
   silently missing the last one. A page longer than an *assumed* size ignored nothing, so it
   becomes the page size from then on;
4. **throws** when the server is ignoring the offset. Otherwise every row of page one would come
   back again and again until the total was reached.

`Page<T>.HasMore` answers the same way.

When the first page sets the size, a result that fits in one page costs one extra request: the
empty page that confirms it. A described `default` that is wrong in the other direction (larger
than what the server really serves) still ends the walk after one short page, so a description
should only give a `default` the server honors.

How each operation is walked depends on which parameters its description declares:

- **Both:** as above.
- **Offset alone:** no limit can be sent, so the first page's size is taken as the server's page
  size, and the walk carries on while pages come back that long.
- **Limit alone:** there is no offset to move, so after the first page each request asks for every
  row up to the end of the next page (`limit` 4, then 6, for pages of 2), keeping only the rows
  past the ones already returned. The first request sends a limit only when the caller set one. Each request repeats the rows before it, which costs bandwidth
  but loses nothing.
- **Neither:** an item path is not paged at all (see above). Elsewhere, the walk sends the
  manifest's offset name anyway. A server that does not page
  answers every request with the same rows, so the walk ends after the first page, or, when that
  page happened to be full, after the repeat check below finds it repeated. That repeat does not
  throw here, because nothing promised the offset would be honored.

A page identical to the one before it is only a suspicion, because real rows can repeat. The
walk then requests the page one row later. A server that honors the offset answers that
differently, unless every row in the run is identical. It throws only when the shifted page
matches as well, and the operation declares an offset.

There is no request cap, so a large result set is never cut short. The total, or a page that is
not full, always ends the walk. `limit` stays on the query object and acts as the page size; the
offset is owned by the walk and does not appear there.

#### A page that fails

A page that comes back as an error status, or with a body that will not read, is usually one bad
record rather than a bad range — and losing the other records of the walk with it is a poor
trade. So the walk narrows the failure instead of ending on it. It asks for each half of the
failed page by itself, with an explicit `limit`, and keeps halving the side that fails:

- one bad record is found in about 2 × log2(page size) extra requests, and every other record of
  the page is returned;
- a page that fails on **both** halves is reported as a range, not a record, and neither half is
  split further — a failure that wide is not one record's fault;
- the walk then carries on with the page after it, so one bad page does not end the walk.

With a total, the walk steps over a failed page whatever came before it, because the total
proves more rows exist. Without one, nothing does, and a server that refuses every offset past
its end looks just like a run of bad pages. So without a total, a second page in a row that
yielded no rows at all ends the walk. A page narrowed down to its bad records still yielded rows,
and does not count toward that run.

When the walk ends with records missing, it **throws** `ApiPagingException`, whose `Failures`
name each record or range and carry the failure behind it. `GetAsync` throws
`ApiPagingException<T>`, whose `Rows` are every row it did fetch — the rows are in the exception,
not lost. `EnumerateAsync` has already yielded those rows, so it throws the non-generic one once
the walk is over. A page fetched by hand with `GetPagedAsync` is never narrowed; its failure
throws as it is.

Narrowing needs a window to narrow, and that window is the size of the page before the failure.
Without a total, the whole window is narrowed, and the page after it says whether the rows went
on. An operation with no limit parameter cannot ask for half a window, so its failed page is
reported as a range at once, with no extra requests. A failure on the **first** page, before any
page has been seen, throws as it is, as does any failure that is not an error status or an
unreadable body (a timeout, a cancelled call, a version mismatch). `PageNarrowingRequests` is the extra requests one
failed page may spend, 32 by default and 0 to turn narrowing off; each halving costs two. A
server that answers a limited request with more rows than were asked for is one that ignores
`limit`, and its page is reported as a range.

### Token exchange

Some APIs take a long-lived key once and hand back a short-lived bearer token for every other
request. Configuring `TokenExchange` adds a handler that does this: it posts to `Endpoint` with
`Authorization: <Scheme> <ApiKey>` over a separate, unauthenticated `HttpClient` (so fetching a
token never needs a token), reads the token from the response — the whole trimmed body by
default, or one string property with `TokenFormat: JsonProperty` and `TokenProperty` — and sends
it as `Authorization: Bearer <token>`.

The token is cached. When it is a JWT, it is replaced `RefreshMargin` before its `exp` claim;
otherwise it is kept for `FallbackLifetime`. A 401 response invalidates the cached token and
retries the request exactly once with a fresh one, replaying the same body. A failed exchange
throws `TokenExchangeException` naming the endpoint and status — never the key —
and so does an exchange that cannot be completed at all (the endpoint unreachable, or the request
timing out), with the underlying failure as its inner exception; cancelling the call itself is
not wrapped. Concurrent requests share one exchange: while a token is being fetched, every other
request waits for it instead of starting its own.

### Registration

A generated client's root is a `partial class` deriving from `ApiClient`. The consumer's own part
declares the constructor — `ApiClient` has no parameterless one — and may take any further
dependencies it needs:

```csharp
public partial class ExampleClient(HttpClient http, ApiClientOptions<ExampleClient> options)
	: ApiClient(http, options);
```

Register it with its configuration section; the call returns the `IHttpClientBuilder`, so
consumer-specific handlers chain on after the token handler:

```csharp
services.AddApiWeldClient<ExampleClient>(configuration.GetSection("ExampleApi"))
	.AddHttpMessageHandler<SomeConsumerHandler>();
```

```json
"ExampleApi": {
  "BaseUrl": "https://api.example.test",
  "Timeout": "00:01:40",
  "PooledConnectionLifetime": "00:02:00",
  "VersionMismatch": "Throw",
  "TokenExchange": { "Endpoint": "/auth", "ApiKey": "…" }
}
```

`BaseUrl` is required and may not carry a query or fragment; a trailing slash is added when
missing so operation paths resolve beneath it. `Timeout` defaults to 100 seconds,
`PooledConnectionLifetime` to two minutes and `VersionMismatch` to `Throw`. `TokenExchange` is off
unless it is configured — in the section, or in code with
`services.Configure<ApiClientOptions<ExampleClient>>(…)` — and then needs `Endpoint` and `ApiKey`;
see "Token exchange" for the rest of its settings. Whether a client exchanges tokens is decided
once, when its HTTP pipeline is first built. Options are validated on first use (and at start-up
when the host runs start-up validation).

`AddApiWeldClient` registers a client type once. A second call for the same type — easy to make
by accident through shared setup code or a test fixture — is ignored, section included, and
returns a builder for the same client, so handlers chained on it still apply.

## Packages

| Package | What it is |
| --- | --- |
| `ApiWeld.Core` | The normalizer itself: a library over the description's JSON tree. No dependencies beyond the shared framework. |
| `ApiWeld.Generator` | The client generator: reads a manifest's descriptions and produces the files of one typed client. Depends on `ApiWeld.Core` and `ApiWeld.Http`. |
| `ApiWeld.Http` | The runtime generated clients reference; see "The runtime". |
| `ApiWeld.Cli` | A command-line tool, installed as a .NET tool under the command `apiweld`, with `normalize` and `generate` commands. |

All four are licensed AGPL-3.0-or-later; see [`LICENSE.txt`](LICENSE.txt).

## Installing and running the tool

Only prerelease versions have shipped so far, so `--prerelease` is required until the first
official release:

```bash
dotnet tool install --global ApiWeld.Cli --prerelease
apiweld normalize path/to/description.json
apiweld generate path/to/apiweld.json
```

## Building from source

```bash
dotnet restore ApiWeld.slnx
dotnet build   ApiWeld.slnx
dotnet test    ApiWeld.slnx
```

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for prerequisites, the project's style conventions, and
how its tests are organized.
