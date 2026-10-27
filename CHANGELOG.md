# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Changed

- `--unlist-all` now treats stable `0.x` (Major == 0) versions as pre-release: they are enumerated
  and unlisted alongside label-carrying pre-release versions, so a package published only as `0.x`
  is no longer skipped by a run that printed nothing and exited `0`.

### Removed

- `--include-zero-major`. Including stable `0.x` versions is now part of the pre-release definition
  itself (semver reserves the whole `0.x` range for initial development), so there is no flag left
  to opt in — or out — of it.

## 0.3.0 - 2026-10-20

### Added

- The `nuversions show` subcommand: a pure read that prints every version of a package, stable
  and pre-release, listed and unlisted, with one `Status=listed|unlisted` line per version.
  It sends no delete request and needs no API key. With `--output json`, same one-object-per-line
  NDJSON schema as `unlist`; a missing package exits `2`.

### Changed

- The project is renamed: the project name `PreReleaseDelist` becomes `NuVersions`, the NuGet id
  `PreReleaseDelist` becomes `NuVersions`, and the `prerelease-delist` command becomes
  `nuversions`. The single command is now a hierarchy: `nuversions unlist` (prior delist
  behavior) and `nuversions show`. The old package id will be deprecated on NuGet.org pointing
  at the new one.
- The unlist status vocabulary now matches nuget.org's own wording: the outcome formerly rendered
  `delisted`/`already-delisted` is `unlisted`/`already-unlisted`, and dry-run buckets render
  `would-unlist`. Everything about the run is otherwise identical: same statuses for
  `not-on-server`, `failed`, `rate-limited`, `not-attempted`, same exit codes, same NDJSON shape.
- The domain vocabulary is unlist end to end, per four mappings: Delist becomes Unlist,
  Delisting becomes Unlisting, Delisted becomes Unlisted, and Delete becomes Unlist at the
  contract level. The all-pre-release option is now `--unlist-all` (was `--delist-all`).
  Transport command names keep their wire spelling (HTTP DELETE, `dotnet nuget delete`).
  What each term means is defined by the Unlist action entry in
  [GLOSSARY.md](GLOSSARY.md).
- The environment-variable fallback prefix is `NUVERSIONS_` (was `PRERELEASEDELIST_`); the
  generic `NUGET_API_KEY`/`NUGET_SERVER_URL` fallbacks are unchanged.
- The library's outcome enum members follow the same rename (`PackageVersionStatus.Unlisted`,
  `PackageVersionStatus.AlreadyUnlisted`); the assembly is not published on NuGet, so no external
  consumer impact is expected.

### Removed

- The `prerelease-delist` command name and the `PreReleaseDelist` package id.

## 0.2.0 - 2026-10-15

### Added
- `--dry-run` prints the delist plan (would delist / already delisted / not on server) without
  sending a delete request. No API key required. Exit is bucket-aware: `0` only when every version
  exists, `1` when any is missing, `2` when the package does not exist.
- `--output json`: newline-delimited JSON on stdout, one object per version with exactly `package`,
  `version`, `status` in that order. stderr stays human-readable.
- A documented exit-code contract, pinned by tests: `0` success, `1` per-version failure or a
  command line the parser rejects, `2` a validation failure the CLI detects itself, `3`
  rate-limited, `4` cancelled, `130` Ctrl-C. The four `return -1` paths are gone.
- Six-RID release binaries for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`,
  `osx-arm64`, attached to GitHub Releases beside the `dotnet tool` package. Self-contained
  single-file, each with a SHA-256 file plus a combined `sha256.txt`. The RID binaries run the HTTP
  backend with no .NET installed.
- `DelistOptions`, which resolves option precedence (`--option` then `PRERELEASEDELIST_*` then
  `NUGET_*` then default) and validates per mode, so dry-run skips the API-key check.
- A test suite: unit tests over the pure logic, handler stubs for the HTTP backend and composing
  services, and an end-to-end suite driving the CLI against an in-process mock NuGet V3 server. It
  pins exit codes `0`, `1`, `2`, `3` and `130`, proves dry runs send no delete, and confirms the
  API key reaches the wire but neither stream.

### Changed
- Updated CliInvoke to 3.0.3 (3.0.0 is affected by
  [GHSA-h4rx-7jm8-fr3g](https://github.com/alastairlundy/CliInvoke/security/advisories/GHSA-h4rx-7jm8-fr3g)),
  but PreReleaseDelist never invokes a runner, so the vulnerable code path is unreachable.
- Rate limiting fails fast: dispatching stops, results so far are reported with the rest marked
  `not-attempted`, and the process exits `3`. No retry.
- Results, summaries and dry-run plans go to stdout; errors and diagnostics go to stderr. The API
  key never reaches either stream.
- Both backends share one per-version delete path behind a single seam, with
  `DelistPlanning.Partition` composed in front of it and cross-cutting behaviour installed as
  decorators.
- `--non-interactive` lines render `Version=<normalized> Status=<kebab-case>` from a closed
  vocabulary (`delisted`, `already-delisted`, `not-on-server`, `rate-limited`, `failed`,
  `not-attempted`). The old `Success`/`Failure` text and its free-text `Info=`/`Error=` fields are
  gone.
- The command is a presentation and failure boundary: option resolution lives in `DelistOptions`,
  known exceptions become one-line stderr messages, unknown ones crash with stacks.
- Shared build policy in `Directory.Build.props` and `.editorconfig`: C# 14, nullable, warnings as
  errors, style checks at build, NuGet audit. `GeneratePackageOnBuild` is replaced by an explicit
  `dotnet pack`, so a local `dotnet build` no longer drops a `.nupkg`.
- Packaging tags corrected, README packed once from the repo root, and README/CHANGELOG rewritten
  for the new flags, exit codes and both distribution channels.
- Publish gates pack and push on the test suite, and builds the RID assets from a green tree only.
- Dependabot points at `/src` where it finds the projects, covers `github-actions` too, groups NuGet
  minor/patch bumps, and runs weekly with a 14-day version cooldown. Security updates stay
  immediate.
- Dropped `EnhancedLinq`, which existed for one `Exclude` call; both services use
  `DelistPlanning.Partition`.

### Removed
- ADR 0001, the csproj comments citing it, and the link in this changelog.
- The `IsTrimmable` and `IsAoTCompatible` claims. Trimmed and Native AOT publishes build and then
  die at runtime, because `NuGet.Protocol` reads signature resources through Newtonsoft.Json
  reflection the trimmer deletes. The properties stay out until a publish-and-run against a real
  feed passes.

### Fixed
- `--delist-all` works without `versions`. The argument was marked required, so every documented
  invocation died with `Required argument missing for command`.
- Versions the server does not know about are failures, not successes. A missing metadata entry was
  indistinguishable from "present but unlisted", so a typo exited 0 with
  `Status=Success Info='Package already de-listed.'`. Both backends had this.
- Unknown options are rejected instead of swallowed. The tokenizer turned an unrecognized `--flag`
  into an argument token, the greedy `versions` position took it, and the digit filter dropped it,
  so `--dry-runn` fell through to the run it was meant to prevent. The CLI writes
  `Error: Unrecognized option: '--flag'.` to stderr and exits `2`. README examples that passed
  versions as `--versions`, which worked only because that token was ignored, use positional
  arguments.
- Ctrl-C reports `130` again. Neither backend converts a cancellation of the caller token into a
  `Failed` outcome. A `TaskCanceledException` from something else, like the HttpClient timeout,
  maps to `Failed`.
- The SDK backend matches `429` as a standalone token, so a version number like `2.429.0` in
  failure output no longer reads as a rate limit.
- On the `http` backend, a `404` counts as already-delisted only when the service index declared
  the `PackagePublish/2.0.0` endpoint the delete hit. On the fallback route a `404` stays a
  failure.
- `--include-zero-major` reaches `CheckPackageVersionsListedAsync`, which ignored it.
- `global.json` says `10.0.100`, not `10.0.0`. Feature band 0 never ships, and setup-dotnet
  rejects it when `rollForward` is set, which broke the dependency-submission workflow.

## 0.1.0 - 2026-09-18

Initial release.

### Added
- `prerelease-delist` .NET global tool for delisting pre-release versions of NuGet packages.
- Delist explicitly listed pre-release versions via the `versions` argument.
- `--delist-all` to delist all pre-release versions of a package, with `--include-zero-major` to also include `0.x` versions.
- `--api-key` / `--server-url` options with `PRERELEASEDELIST_`-prefixed and generic `NUGET_*` environment variable fallback, plus third-party NuGet server support.
- `--backend` selection (`http` V3 API or .NET SDK-backed delisting).
- `--non-interactive` machine-friendly output mode for CI usage.
- `--use-strict-parsing` control over invalid version string handling.
