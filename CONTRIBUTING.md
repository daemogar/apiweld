# Contributing

## Prerequisites

The .NET 10 SDK. The project targets `net10.0` throughout and uses central package management —
every dependency's version lives in `Directory.Packages.props`; a `PackageReference` never
carries its own `Version`.

## Build and test

```bash
dotnet restore ApiWeld.slnx
dotnet build   ApiWeld.slnx
dotnet test    ApiWeld.slnx
```

**Read the test count that is reported, not just the exit code.** A test project that builds
but whose runner adapter is missing or misconfigured discovers zero tests and still exits zero —
that looks exactly like a clean run. `ApiWeld.Tests` carries a permanent smoke test for exactly
this reason: if `dotnet test` ever reports fewer tests than expected, something about test
discovery broke, even though the command "succeeded."

## Style

- **Tabs** for indentation in C# files; see `.editorconfig` for every other file type.
- File-scoped namespaces, usings outside the namespace, system directives first, import groups
  separated by a blank line.
- Records are a reasonable default shape for data that has no behavior of its own.
- **Comments stay short.** A `<summary>` says what a member is and why it exists, briefly. When
  more explanation is warranted — a design rationale, a rejected alternative, a subtlety in how
  a rule behaves — it belongs in `README.md` (product-level) or here (contributor-level), with
  `<remarks>` reduced to a one-line pointer at it. Long prose does not belong inline in source.

## Format substitution is text replacement

Format substitution replaces the literal text `"format": "<name>"` — exactly one space after
the colon — anywhere it appears in the raw document text, including inside description strings,
before the document is ever parsed. This reproduces an existing implementation's behavior
exactly, which is why the normalizer's entry point for this rule accepts text rather than a
parsed tree. Moving this to a tree walk is a legitimate later change, once tests prove the old
and new behavior agree on the same inputs — until then, the literal-text match, spacing included,
is deliberate rather than an oversight to fix.

## No vendor or product references in tracked files

Nothing in this repository — code, comments, documentation, commit messages, file names, or
test names — names a specific product, company, system, or third-party code generator. Behavior
is described as a property of OpenAPI descriptions in general, and of code generators in
general: what a description expresses, and what a generator that reads it literally tends to
produce. This applies to new contributions exactly as it does to what is already here.

`docs/` and `.superpowers/` are both listed in `.gitignore` and are never committed; anything
that would need to name a specific system to explain itself belongs there, in a contributor's
own untracked notes, never in a tracked file.

## Fixtures are hand-written

Test fixtures under `ApiWeld.Tests/Fixtures/` are synthetic OpenAPI documents written by hand,
each one built to exercise a single rule. None is copied or derived from any real API's
description. A fixture should be the smallest document that makes its rule true or false —
not a trimmed-down copy of something that exists elsewhere.

## Pull requests

Keep a change and its explanation together: a behavior change that needs a longer rationale
gets that rationale in `README.md` or here, not only in the commit message. Update
`CHANGELOG.md`'s `## Unreleased` section for anything a consumer of either package would notice.
