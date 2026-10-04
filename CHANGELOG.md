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
