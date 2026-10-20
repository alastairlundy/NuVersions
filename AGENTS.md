# Agent instructions

NuVersions (formerly prerelease-delist) is a .NET 10 CLI and library for showing how a NuGet package's versions are listed on a NuGet server and unlisting specific versions or all pre-release versions of it.

## Rename: repo ≠ product

- The GitHub repository/package/tool was renamed to **NuVersions**: package `NuVersions`,
  tool command `nuversions`, solution `src/NuVersions.slnx`. Former names
  (`PreReleaseDelist`, `prerelease-delist` command, `--delist-all`, `delisted` statuses,
  `PRERELEASEDELIST_` env prefix) are gone as of 0.3.0; the generic `NUGET_API_KEY` /
  `NUGET_SERVER_URL` fallbacks are unchanged. Domain vocabulary is **unlist** — see
  `GLOSSARY.md`.
- The version outcome/status vocabulary is a closed contract (`unlisted`, `already-unlisted`,
  `not-on-server`, `failed`, `rate-limited`, `not-attempted`; exit codes 0/1/2/3/4/130). Changing
  a status word or an exit code means updating README's contract tables, GLOSSARY.md, and the
  exit-code/status rendering tests together.
- The root README is the tool's user manual **and is packed into the NuGet package**
  (`PackageReadmeFile`); edits ship to nuget.org on the next release. Keep its option/exit-code
  tables in sync with the CLI.

## Layout

- `src/NuVersionsLib` (LGPL): all NuGet-server logic. Not published to NuGet.
  - `UnlistRun` (`Internal/`) holds the shared run body: validation, existence/listed checks,
    bucket partition, fail-fast rate-limit loop. The two transports supply only a per-version
    delegate: `PackageUnlistService` (HTTP DELETE flow) and `NetSdkPackageUnlistService`
    (runs `dotnet nuget delete` via CliInvoke), chosen by the CLI's `--backend http|sdk`.
- `src/NuVersionsCli` (GPL): DotMake CLI (`Program.cs` composition root, `RootCommand.cs`,
  `ShowCommand.cs`, `UnlistCommand.cs`), `<PackAsTool>`, `ToolCommandName nuversions`.
- User-facing strings live in resx localization files, not hardcoded: Lib
  `Internal/Localizations/Resources.resx`, CLI `Localizations/Resources.resx`, each with a
  `Resources.Designer.cs`.
- Versioning lives in `NuVersionsCli.csproj`: a release bumps `Version`, `PackageVersion`,
  `AssemblyVersion`, `FileVersion`, the inline `PackageReleaseNotes`, and CHANGELOG.md.

## Build & test

`dotnet test` is the test entry point. `global.json` opts the repository into SDK 10's
native Microsoft.Testing.Platform command mode (`"test": { "runner": "Microsoft.Testing.Platform" }`),
and the suite itself is TUnit (MTP-only, 25 fuzz properties). SDK 10 is pinned by the root
`global.json` *and* `src/global.json` (the latter without the test-runner setting).

Verified commands:

```bash
# simplest full run
dotnet test tests/NuVersionsLib.FuzzTests

# exact .github/workflows/tests.yml sequence (build the solution first)
dotnet build -c Release "src/NuVersions.slnx"
dotnet test tests/NuVersionsLib.FuzzTests --no-build -c Release

# exact .github/workflows/publish.yml gate (runs before restore/pack/push)
dotnet test "src/NuVersions.slnx" -c Release

# class-scoped run (TUnit treenode filter; native mode takes MTP options directly, no -- separator)
dotnet test tests/NuVersionsLib.FuzzTests --treenode-filter "/*/*/VersionFilterProperties/*"
```

`dotnet test --help` lists everything the wrapper consumes in this mode. Any other
argument is forwarded to the test application, which rejects options it has not
registered.

### Never pass `--nologo`

`dotnet test` has no `--nologo` option in native MTP mode. The flag is forwarded to
the test application, which rejects it (`Unknown option '--nologo'`) and exits 5 —
and the wrapper swallows that message, so the only visible symptom is the misleading:

```text
Zero tests ran
error: 1
```

If you see that, re-check your `dotnet test` arguments against `dotnet test --help`
before suspecting the tests themselves. The reference behaviour is running the built
module directly, which prints the real error:

```bash
dotnet tests/NuVersionsLib.FuzzTests/bin/Debug/net10.0/NuVersionsLib.FuzzTests.dll
```

### Test suites

Two TUnit projects, both referencing **the CLI project** (`InternalsVisibleTo` on both src
projects):

- `tests/NuVersionsLib.FuzzTests` — FsCheck properties. This is the only suite CI runs on
  push/PR (`.github/workflows/tests.yml`).
- `tests/NuVersionsLib.Tests` — unit + CLI integration tests; runs via the full-solution
  `dotnet test "src/NuVersions.slnx"` in the publish gate only.

### CLI integration tests mirror Program.cs wiring

`CliIntegrationHarness` drives the real CLI in-process but *re-declares* the DI registrations
in `src/NuVersionsCli/Program.cs`, because top-level `Program.cs` has a private `Main` the
tests cannot call. **If you change DI registrations or command wiring there, mirror the change
in `tests/NuVersionsLib.Tests/CliIntegrationHarness.cs`** — the tests exercise the real
production path except that one registration block. Integration tests hit an in-process
`MockNuGetV3Server`; no real network, and options pass `--server-url`/`--api-key` explicitly
because env fallbacks are read at construction.

## Build gotchas

- NuGet audit is on in `Directory.Build.props` (`NuGetAudit=true`, `NuGetAuditMode=all`):
  all-graph vulnerabilities surface as NU190x warnings during restore. Caveat: several
  comments (props files, `.editorconfig`, `publish.yml`) claim these "fail the build via
  TreatWarningsAsErrors", but no props/csproj actually sets `TreatWarningsAsErrors` or
  `EnforceCodeStyleInBuild` — verify before relying on audit/style warnings to fail a build.
- Central package management: versions only in the `Directory.Packages.props` files, never in
  csproj. The transitive pin `System.Security.Cryptography.Pkcs` 8.0.2 must stay in **both**
  `src/` and `tests/` `Directory.Packages.props` — the nearest props file wins per project,
  so removing one pin re-exposes the audited version at build of that tree only.
- The CPM + audit pin combo means a NuGet.Protocol bump should re-check that pin.
- NuGet.Protocol is **not trimming or Native AOT safe**: several of its code paths the CLI
  exercises (service index parsing, repository signature resources, plugin serialization) are
  marked `[RequiresUnreferencedCode]`, and it transitively pulls in Newtonsoft.Json, which
  itself produces IL2104/IL3053 warnings. NuVersions does not reference Newtonsoft.Json
  directly. A Native AOT publish of the CLI succeeds with ILC warnings from those
  assemblies; none come from NuVersions' own code, which is trim/AOT clean (the NDJSON
  serializer uses `NdjsonJsonContext` source generation). This only matters for a
  self-contained trimmed or native-AOT distribution — the tool ships as a framework-dependent
  dotnet tool, which does not trim. Revisit when NuGet's packages drop the Newtonsoft.Json
  dependency; do not try to fix these warnings in-repo.

## Conventions

- Every .cs file carries a GPL or LGPL license header matching its project: LGPL for
  `NuVersionsLib` and both test projects, GPL for the CLI — copy an existing header when
  adding files.
- Namespaces are file-scoped (`.editorconfig`, warning severity).
