# ApiWeld — working notes for agents

Read [`README.md`](README.md) for what ApiWeld is and why normalization exists, and
[`CONTRIBUTING.md`](CONTRIBUTING.md) for build, test, and style. This file is the agent-facing
companion: conventions and rules that are not product documentation. Do not duplicate either
document here — point at it instead.

## The naming rule — absolute, and the easiest rule to break

No product, company, system, or third-party code generator name may appear in any tracked
file — not in code, comments, documentation, commit messages, file names, or test names.
Describe behavior as a property of OpenAPI descriptions and of code generators in general, never
as "replaces X" or "X produces a wrapper class per variant." When an explanation wants a
concrete example, reach for a synthetic one (a hypothetical `oneOf` with an empty branch) rather
than naming what actually produced the shape being discussed.

This rule applies to prose files exactly as much as to code — `README.md`, `CONTRIBUTING.md`,
and this file are exactly where it is easiest to slip and name something while explaining "why."

## `docs/` and `.superpowers/` are untracked, on purpose

Both are listed in `.gitignore`. `docs/` holds design and planning notes that may name real
systems freely — that is the only reason they can. `.superpowers/` is an execution workspace,
scratch by nature. Nothing under either path is ever committed, and nothing tracked should
assume either exists.

## Style, in brief

Tabs for indentation, file-scoped namespaces, short `<summary>` comments with any longer
explanation in `README.md` or `CONTRIBUTING.md` rather than in `<remarks>`. See
`CONTRIBUTING.md`'s Style section for the rest; this file does not repeat it.

## Central package management

`Directory.Packages.props` owns every dependency's version. A `PackageReference` in a `.csproj`
carries the package name only — never add a `Version` attribute there.

## Testing

`dotnet test` runs xUnit v3 through `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`.
**Read the reported test count, not the exit code** — a test project whose runner adapter is
missing or broken discovers zero tests and still exits zero, which looks identical to a clean
run. `ApiWeld.Tests/HarnessTests.cs` is a permanent smoke test that exists to catch exactly that
failure mode; it is not testing anything about ApiWeld itself, and it must stay.
