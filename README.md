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

Union collapse replaces every `oneOf`/`anyOf` with a single, more permissive schema. The
rules target OpenAPI 3.0 shapes — a bare nullable field written as `nullable: true` rather
than a 3.1-style `type` array; a 3.1 type array (`"type": ["string", "null"]`) inside a
`oneOf`/`anyOf` variant is not yet handled and is reported as an error rather than collapsed.

1. Drop every *absent* branch — one with `maxProperties: 0`, an object declaring no
   properties, a string with `maxLength: 0`, or a bare nullable string carrying no `enum`
   and no `pattern`.
2. If nothing survives, keep all branches and treat the union as not emptiable.
3. Merge the survivors pairwise: a property either variant offers survives; `required`
   intersects only when **both** variants carry a `required` list — a `required` list only
   one variant carries is kept whole, unchanged; and no `enum`, `pattern` or `format`
   survives that they disagree on or that only one of them carries.
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
  "names": { "errors_1_0_0": "Errors" }
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
as `{id}.json` or `{from}-{to}`: every parameter must fill a whole segment.

An operation's version is read from the media type of its success response — never from the
description's `info.version`, which is only reported when it disagrees. When a response
declares several media types, the most specific versioned JSON type wins over plain
`application/json`. The version member sits just before the verbs:
`Api.Widgets[id].V2.GetAsync()`. An operation whose success response carries no versioned media
type is `V0`; one with no response body at all takes the single version the rest of its
description uses, if there is exactly one. A request body and the error bodies keep their own
media types inside their operation, and never create members of their own.

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
  ends `-ss`, `-us` or `-is`.)
- **Version** — the version member, such as `V12_6`.
- **Path** — the property path from the body, with array items singular: `addresses[].place`
  becomes `AddressPlace`.
- **Suffix** — always `Response` or `Request`, so a name does not change when a description
  starts or stops sharing a shape between the two. Error bodies have no suffix; they are named
  after their schema and the error media type's version, such as `ErrorsV2`.

Types merge when their shape and direction are identical, across files as well as within one,
and a merged type takes its shortest candidate name, ties broken in ordinal order. When
different shapes want one name, the one a GET returns keeps it and the others gain their verb
after the version (`ThingV1PostResponse`), with a warning; a clash that survives that is an
error naming the fix.

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
any output path that lies outside `output` or that differs from another only in case.

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

### Page walking

An operation is paged when it returns a list and takes both an offset and a limit parameter
(named by the manifest's `paging` settings). Its `GetAsync` returns every row, `EnumerateAsync`
streams rows as pages arrive, and `GetPagedAsync` returns one `Page<T>` whose `NextAsync`
fetches the page after it. The walk starts at offset zero and:

1. **stops after the first page when the response has no total-count header** — the page is
   returned as a normal response, and `Page<T>.HasMore` is false;
2. otherwise keeps requesting until the rows fetched reach the total, or a page comes back empty;
3. **throws** when a page is identical to the one before it, because that means the server is
   ignoring the offset and the walk would never end.

There is no request cap: a large result set is never cut short, and the repeat guard is what
stops a runaway walk. `limit` stays on the query object and acts as the page size; the offset is
owned by the walk and does not appear there.

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
