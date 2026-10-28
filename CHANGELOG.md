# Changelog

## Unreleased

### Added

- `ApiWeld.Core`: normalize an OpenAPI description in one call, `OpenApiNormalizer.Normalize`.
  It runs format substitution, then collapses unions down to a single permissive shape, then
  folds identical referenced component schemas onto one canonical name and rewrites references
  to match. See the README's "Union collapse rules", "Deduplication rules", and "Order of the
  passes" sections for exactly what each stage does and why they run in that order.
- `ApiWeld.Cli`: an `apiweld normalize <description.json>` command. It writes the normalized
  result next to the input as `<name>.modified.json`; if the description is already normal, it
  writes nothing and reports that the document is unchanged — and warns, without deleting it, if
  a stale `<name>.modified.json` from an earlier run is still sitting beside it. Bad input (a
  missing file, a path that is not a `.json` file, malformed JSON, a description whose root is
  not an object, or a description whose shape the normalizer does not support) exits non-zero
  with a message describing the problem, as does a target file it cannot read or write.
- `ApiWeld.Http`: the runtime package generated clients reference. It builds and sends each
  request from a generated operation description, pins the versioned `Accept` media type and
  throws when a response names another version, reads declared error bodies into typed
  exceptions, walks paged operations (`GetAsync`, `EnumerateAsync`, `GetPagedAsync`) with a
  repeat guard and a per-walk request cap, reads mismatched JSON kinds tolerantly, trades an API
  key for a cached bearer token with one retry on 401, and registers a client with
  `AddApiWeldClient<TClient>`. See the README's "The runtime" section.
- `ApiWeld.Generator` and `apiweld generate <manifest>`: generate one typed C# client and one
  merged model set from any number of OpenAPI descriptions. All paths share one tree (base paths
  dropped, parameter segments merged by position); each operation's version is read from its
  success media type and chosen in code (`Api.Widgets[id].V2.GetAsync()`); identical shapes
  merge across files, named `{Root}{Version}{Path}{Request|Response}`; paged lists get
  `GetAsync`, `EnumerateAsync` and `GetPagedAsync`. Output is deterministic, and only files
  carrying the tool's own two-line header are ever replaced or deleted. See the README's "Generating a client".

### Fixed

- `ApiWeld.Http`: a lone JSON object where a list is declared reads as a one-item list instead
  of failing the response.
- `ApiWeld.Http`: dates and times read leniently, in ISO 8601 extended or basic form or as
  slash dates (`yyyy/M/d`, `M/d/yyyy`). A date takes the date part of a full date-time, a
  date-time with no offset (or a bare date) is read as UTC, a time keeps its clock time when it
  carries an offset, and a value that still does not parse reads as `null` instead of failing
  the response. See the README's "Tolerant JSON".
- `ApiWeld.Generator`: a string with format `time` maps to `TimeOnly` rather than `string`.
- `ApiWeld.Generator`: a part shared by several descriptions at the same path is named by that
  path (`MetadataResponse`) instead of after whichever description had the shortest name.
- `ApiWeld.Generator`: different shapes that want one name and that the verb cannot separate —
  two bodies of the same verb on different routes, or error bodies that differ between
  descriptions — are now told apart by their route or their description instead of failing
  generation; an error shape most descriptions share keeps the plain name.
- `ApiWeld.Generator`: an error body whose schema name already ends in a version
  (`errors_2_0_0`) is no longer named with the version twice (`Errors200V2`); the suffix is
  dropped when the media type carries the version, giving `ErrorsV2`.
- `ApiWeld.Http`: a whole number written as `3.0` or `1e2` now reads into an integer property,
  and a number that does not fit its property reads as `null` instead of failing the response.
- `ApiWeld.Http`: a header the request cannot carry (an invalid name, a content header, or a
  value containing a line break) and an operation template carrying a query or fragment are
  refused with `ArgumentException` when the request is built, instead of being dropped or
  mangled. A `byte[]` query or header value is sent as one base64 value, a list header value as
  comma-separated values, and a content header parameter such as `Content-Language` on the body.
- `ApiWeld.Http`: a token exchange that cannot reach its endpoint or times out throws
  `TokenExchangeException` with the cause as its inner exception, and the request retried after
  a 401 is disposed once it is answered.
- `ApiWeld.Http`: `AddApiWeldClient` decides whether to exchange tokens from the final options,
  so a token exchange configured or removed in code is honored; a second registration of the same
  client is ignored instead of adding every handler twice; a `BaseUrl` carrying a query or
  fragment is refused.
- `ApiWeld.Generator`: a success response declared as `2XX` is read as the success, instead of
  the operation silently returning nothing; a success declared only as `default` gets a warning;
  a path carrying a query or fragment is an error.
- `ApiWeld.Generator`: a model named like a generated navigation class or a type the generated
  code uses is reported as an error asking for a `names` entry, instead of producing code that
  does not compile.
- `ApiWeld.Generator`: the manifest refuses unknown keys, a `paging` or `names` that is not an
  object, and C# keywords as `client`, a `namespace` part or a `names` value.
- `ApiWeld.Generator`: common irregular plurals (`people`, `series`, `analyses`, `warehouses`, …)
  now become proper singular type names, and the manifest's new `singulars` map fixes any other
  word — including the item type of an inline array, which `names` cannot reach.
- `ApiWeld.Generator`: output writing reports a file it cannot read, write or delete as an error
  instead of throwing, removes folders that stale deletion empties, and accepts an output folder
  at a drive root.
- `ApiWeld.Cli`: `apiweld generate` reports a manifest it cannot read and exits 1, instead of
  stopping with an exception.
- `ApiWeld.Http` and `ApiWeld.Generator`: a paged list whose server sends no total-count header
  is walked to the end instead of stopping after page one. A page is full at the caller's limit,
  else the description's `default` for it, capped by a described `maximum`, else the first page's
  size. No limit is sent that the caller did not set, and the walk continues while pages come
  back full. A page longer than a limit the caller sent is the whole answer. Every `GET` that returns a list is now paged, whichever of offset and limit it declares,
  if either. See the README's "Page walking".
- `ApiWeld.Generator`: a `GET` that returns a list at an item path (one ending in a parameter,
  such as `/widgets/{id}`) and declares neither paging parameter is a single request again,
  instead of a walk that cost up to three requests for one record. A path that declares offset or
  limit is still paged. See the README's "Page walking".
- `ApiWeld.Http`: a page walk can no longer run forever. With no total, two pages in a row that
  fail without yielding a row end the walk. Every walk is capped by the new
  `ApiClientOptions.MaxPageRequests` (1,000 by default, 0 for no cap), narrowing included; a walk
  that hits the cap throws `ApiPagingException` with the rows it fetched and a last failure naming
  where it stopped. See the README's "Page walking" › "A page that fails".
