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
  repeat guard instead of a request cap, reads mismatched JSON kinds tolerantly, trades an API
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

- `ApiWeld.Http`: a whole number written as `3.0` or `1e2` now reads into an integer property,
  and a number that does not fit its property reads as `null` instead of failing the response.
- `ApiWeld.Http`: a header the request cannot carry (an invalid name, a content header, or a
  value containing a line break) and an operation template carrying a query or fragment are
  refused with `ArgumentException` when the request is built, instead of being dropped or
  mangled. A `byte[]` query or header value is sent as one base64 value.
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
