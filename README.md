# NuVersions

A CLI to show how a NuGet package's versions are listed on a NuGet server, and to unlist versions of the package.

[![NuGet latest version](https://img.shields.io/nuget/v/NuVersions.svg)][nuget]
[![NuGet downloads](https://img.shields.io/nuget/dt/NuVersions.svg)][nuget]
[![GitHub license](https://img.shields.io/github/license/alastairlundy/NuVersions.svg)][license]
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/alastairlundy/NuVersions/badge)][scorecard]

[nuget]: https://www.nuget.org/packages/NuVersions/
[license]: LICENSE
[scorecard]: https://api.scorecard.dev/projects/github.com/alastairlundy/NuVersions

## Features

- Show every version of a NuGet package together with whether each one is listed or unlisted (`show`).
- Unlist specific versions of a NuGet package by name, pre-release and stable versions alike (`unlist`).
- Unlist all pre-release versions of a package in one go (`unlist --unlist-all`).
- Distinguishes a version that is already unlisted from a version the server has never heard of, so a mistyped version string is reported as a failure rather than a silent success.
- Preview an unlist with `--dry-run`. No delete request is sent and no API key is required.
- Machine-readable stdout with `--output json`, plus a small documented exit-code contract for CI.
- Configure NuGet API Key and Server URL via environment variables.

> **Renamed from PrereleaseDelist:** the `prerelease-delist` tool is now `nuversions`, with
> today's unlist behaviour available as `nuversions unlist`. The environment variable prefix is
> now `NUVERSIONS_`, the old `NUGET_*` generic fallbacks are unchanged, and `show` is the first
> sibling command. See the `0.3.0` entry in the [changelog](CHANGELOG.md).

## Installation

NuVersions ships through two distribution channels.

### The `dotnet tool` package (primary)

The primary distribution is the `dotnet tool` package on NuGet:

```bash
dotnet tool install --global NuVersions
```

After installing, the `nuversions` command will be available on your PATH.

#### Prerequisites
- The [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), required by the `dotnet tool` channel.
- A .NET SDK on the host, but only for the `sdk` backend (`--backend sdk`), which drives SDK tooling that packaging cannot bundle. The standalone binaries below cannot remove this requirement.

This channel runs both backends (`http` and `sdk`) and serves musl/Alpine users, since no standalone `linux-musl-*` binary is published.

### Standalone binaries (GitHub Releases)

Self-contained, single-file binaries are attached to each GitHub Release for exactly six RIDs:

`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

- **No .NET installation is required.** These binaries run the HTTP backend out of the box.
- The `sdk` backend still requires a host .NET SDK installation, which packaging cannot change; use the `dotnet tool` channel with an SDK installed if you need `--backend sdk`.
- Each release ships SHA-256 checksums: one checksum file per artefact plus a combined `sha256.txt` listing every pair, verifiable with `sha256sum -c`.
- musl/Alpine users are served by the `dotnet tool` channel; no `linux-musl-*` RID is built.

### Build

```bash
dotnet build -c Release
```

## Usage

The `nuversions` command has subcommands, one per operation.

```text
nuversions [subcommand] [options]
```

| Subcommand | Reads or writes | What it does                                                                               |
|------------|-----------------|--------------------------------------------------------------------------------------------|
| `show`     | read-only       | Print every version of a package with whether it is listed or unlisted. No API key needed. |
| `unlist`   | write           | Unlist specified versions, or all pre-release versions, of a package.                      |

Running `nuversions` with no subcommand prints the command hierarchy.

### Shared options

| Name           | Type             | Required | Default                               | Description                                                                                                                                                               |
|----------------|------------------|----------|---------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `--package-id` | string           | yes      | —                                     | The ID of the package, e.g. `MyPackage`.                                                                                                                                  |
| `--server-url` | string           | no       | `https://api.nuget.org/v3/index.json` | The NuGet server's V3 service index URL. Useful for third-party NuGet servers. Falls back to the `NUVERSIONS_NuGetServerUrl` or `NUGET_SERVER_URL` environment variables. |
| `--output`     | `text` \| `json` | no       | `text`                                | Output format for stdout: human-readable text, or newline-delimited JSON (one object per version). stderr is unaffected in both modes.                                    |

### `show`

Prints every version the server knows for the package, including unlisted versions, with one status word per version:

```text
Version=1.0.0 Status=listed
Version=1.0.1 Status=unlisted
```

- `listed`: the server lists the version in search results.
- `unlisted`: the server knows the version but does not list it.

The output is sorted by version, ascending. `show` is a pure read: it sends no delete request
and needs no API key. It exits `0` when the package exists, `1` when a feed-side error occurs,
and `2` when the package does not exist on the server.

To unlist a version, use `unlist` with that version. The `show` statuses are deliberately
different from the `unlist` statuses: `show` describes the current state; `unlist` describes the
attempt it just made.

### `unlist`

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" "1.0.0-alpha.1" "1.0.0-alpha.2"
```

| Name                   | Type             | Required                            | Default                               | Description                                                                                                                                                                                                                                                                                                                                                                                                         |
|------------------------|------------------|-------------------------------------|---------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `versions` (argument)  | string[]         | Yes (unless `--unlist-all` is used) | —                                     | One or more version strings to unlist, passed positionally. There is no `--versions` option. Only version strings starting with a digit are considered; others are ignored, except a token starting with `-`, which is rejected as an unknown option (see [Exit codes](#exit-codes)). Pre-release and stable versions are both accepted; to unlist every pre-release version at once, see `--unlist-all`. |
| `--package-id`         | string           | Yes                                 | —                                     | The ID of the package to unlist versions of, e.g. `MyPackage`.                                                                                                                                                                                                                                                                                                                                                      |
| `--api-key`            | string           | Yes*                                | —                                     | The NuGet API key to authenticate with. If omitted, the CLI falls back to the `NUVERSIONS_NuGetApiKey` or `NUGET_API_KEY` environment variables.                                                                                                                                                                                                                                                                    |
| `--server-url`         | string           | No                                  | `https://api.nuget.org/v3/index.json` | The NuGet server's V3 service index URL. Useful for third-party NuGet servers. Falls back to the `NUVERSIONS_NuGetServerUrl` or `NUGET_SERVER_URL` environment variables.                                                                                                                                                                                                                                           |
| `--unlist-all`         | boolean          | No                                  | `false`                               | Unlist all pre-release versions of the package instead of the explicitly listed `versions`.                                                                                                                                                                                                                                                                                                                         |
| `--include-zero-major` | boolean          | No                                  | `false`                               | With `--unlist-all`, also include stable `0.x` (Major == 0) versions.                                                                                                                                                                                                                                                                                                                                                |
| `--use-strict-parsing` | boolean          | No                                  | `true`                                | When `true`, an invalid version string causes an error. When `false`, invalid version strings are silently skipped.                                                                                                                                                                                                                                                                                                 |
| `--backend`            | `http` \| `sdk`  | No                                  | `http`                                | Which unlisting backend to use: the V3 `http` API, or the .NET SDK's package deprecation/unlist support (`sdk`).                                                                                                                                                                                                                                                                                                    |
| `--non-interactive`    | boolean          | No                                  | `false`                               | Print one machine-friendly `Version=<version> Status=<kebab-case>` line per version on stdout and exit non-zero if any version failed. Useful in CI.                                                                                                                                                                                                                                                                |
| `--dry-run`            | boolean          | No                                  | `false`                               | Print the unlist plan without sending any delete request. Works without an API key. Exits only with `0`, `1`, or `2`. See [Dry runs](#dry-runs).                                                                                                                                                                                                                                                                   |
| `--output`             | `text` \| `json` | No                                  | `text`                                | Output format for stdout: human-readable text, or newline-delimited JSON (one object per version). stderr is unaffected in both modes.                                                                                                                                                                                                                                                                              |

\* Required either on the command line or via environment variable (see [Configuration](#configuration) below), except under `--dry-run`, where no API key is needed at all.

### Unlist version states

Every `unlist` run resolves each requested version to exactly one member of a closed status vocabulary. The same kebab-case word appears in `--non-interactive` lines, in `--output json` payloads, and in the exit-code mapping:

| State                                   | `--non-interactive` output | JSON `status`      | Exit code contribution |
|-----------------------------------------|----------------------------|--------------------|------------------------|
| Listed on the server (now unlisted)     | `Status=unlisted`          | `unlisted`         | 0                      |
| On the server but already unlisted      | `Status=already-unlisted`  | `already-unlisted` | 0                      |
| Not known to the server at all          | `Status=not-on-server`     | `not-on-server`    | 1                      |
| Unlist attempt failed                   | `Status=failed`            | `failed`           | 1                      |
| Server rate-limited the run             | `Status=rate-limited`      | `rate-limited`     | 3                      |
| Not attempted; the run stopped first   | `Status=not-attempted`     | `not-attempted`    | 4                      |

> **Status renames:** the unlist vocabulary was `delisted`/`already-delisted` under the
> `prerelease-delist` name; the same two states are `unlisted`/`already-unlisted` now, matching
> nuget.org's terminology. The all-pre-release option is now `--unlist-all` (was `--delist-all`).

A version the server has never heard of, a typo or a version that was never published, is
reported as a failure (`not-on-server`), not as "already unlisted". Versions that do need
unlisting are still processed, so one bad entry in a batch does not prevent the rest from being
unlisted.

### Dry runs

`--dry-run` resolves and prints the *unlist plan* without sending any delete request. Network reads still happen; nothing is written.

Each requested version lands in one of three buckets, printed as `Version=<version> Bucket=<bucket>`:

| Bucket             | Meaning                                                                         |
|--------------------|---------------------------------------------------------------------------------|
| `would-unlist`     | The version exists and is listed, so a real run would unlist it.                  |
| `already-unlisted` | The version exists but is unlisted.                                             |
| `not-on-server`    | The server has no record of the version (a typo, or a version never published). |

Exit is bucket-aware: `0` only when every requested version exists on the server, `1` when any version is `not-on-server`, and `2` when the package itself does not exist on the server. Dry runs exit only with `0`, `1`, or `2`, never `3` or `4`.

A dry run does not require an API key: delete requests are the only credential-bearing path, and a dry run sends none. With `--output json`, each plan line is a JSON object using the same closed status vocabulary: a `would-unlist` version renders as `not-attempted`, because the delete attempt is never made.

```bash
nuversions unlist --package-id "MyPackage" --dry-run "1.0.0-alpha.1" "1.0.0-alpha.2"
```

```text
Version=1.0.0-alpha.1 Bucket=would-unlist
Version=1.0.0-alpha.2 Bucket=already-unlisted
```

### JSON output

`--output json` switches stdout to newline-delimited JSON (NDJSON): one object per requested version, with exactly three fields in a fixed order. No envelope, no array wrapper, no additional fields:

```json
{"package":"MyPackage","version":"1.0.0-alpha.1","status":"unlisted"}
{"package":"MyPackage","version":"1.0.0-alpha.2","status":"not-on-server"}
```

- `package`: the package id exactly as supplied to the run.
- `version`: the normalised version string.
- `status`: a kebab-case member of the subcommand's status vocabulary in [Unlist version states](#unlist-version-states) or in [`show`](#show). `show` reports the listed/unlisted facts; `unlist` reports what its attempt did.

stderr is unchanged by this mode. Every error and diagnostic still prints there as human-readable text. In all modes, results, summaries, dry-run plans, and `show` listings print to stdout; errors and diagnostics print to stderr; the API key is never written to any stream. When both are set, `--output json` takes precedence over `--non-interactive` for stdout.

### Exit codes

The CLI exits only with these codes:

| Code  | Meaning                                                                                                                                                                                                                                                                                                                                                                   |
|-------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `0`   | Success: every requested version was unlisted or was already unlisted. For `show`, the package exists and every version's state was reported.                                                                                                                                                                                                                           |
| `1`   | At least one version failed, including a version the server does not have (`not-on-server`); also command-line errors the argument parser rejects (see below).                                                                                                                                                                                                            |
| `2`   | Validation failure detected by the CLI itself: an unknown option, an empty `--package-id`, an invalid `--backend` or `--output` value, no version strings without `--unlist-all`, a missing API key on a real run, a package that does not exist on the server, or an invalid version string under strict parsing. For `show`, the package does not exist on the server. |
| `3`   | The server rate-limited the run; the run stopped fail-fast and reported the results so far.                                                                                                                                                                                                                                                                               |
| `4`   | The run was cancelled; the remaining versions were not attempted.                                                                                                                                                                                                                                                                                                         |
| `130` | Interrupted with Ctrl-C.                                                                                                                                                                                                                                                                                                                                                  |

Command lines that the argument parser rejects outright, a missing required option such as `--package-id` or an option missing its value, never reach that validation: the parser writes its message to stderr and the process exits `1` without contacting the server. An unknown option is a different case: the parser hands it to the `versions` position, where the CLI detects it, writes `Error: Unrecognized option: '--typo'.` to stderr and exits `2`. A typo-ed option is never silently dropped as a non-version string.

When a run produces more than one of these codes, the most severe wins: `3` beats `4`, which beats `1`, which beats `0`. `unlist --dry-run` reuses `0`, `1`, and `2` only.

### Environment variables

The CLI enables DotMake command-line directives, so you can set environment variables inline for a single run using the DotMake `[env:...]` directive:

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" [env:NuGetServerUrl=https://my-server/api/v3/index.json]
```

### Configuration

Options can also be satisfied from environment variables instead of the command line. Precedence is: explicit `--api-key`/`--server-url` command-line options first, then `NUVERSIONS_`-prefixed environment variables, then generic `NUGET_*` environment variables, then built-in defaults.

| Option         | `NUVERSIONS_` env var       | Generic env var    |
|----------------|-----------------------------|--------------------|
| `--api-key`    | `NUVERSIONS_NuGetApiKey`    | `NUGET_API_KEY`    |
| `--server-url` | `NUVERSIONS_NuGetServerUrl` | `NUGET_SERVER_URL` |

```bash
# bash
export NUGET_API_KEY="myApiKey"
export NUGET_SERVER_URL="https://api.nuget.org/v3/index.json"
```

```powershell
# PowerShell
$env:NUGET_API_KEY = "myApiKey"
$env:NUGET_SERVER_URL = "https://api.nuget.org/v3/index.json"
```

The `--api-key` lookup order is therefore: `--api-key` option → `NUVERSIONS_NuGetApiKey` → `NUGET_API_KEY` → error (the CLI exits with a message if no key is found). If neither server-URL variable is set, the CLI falls back to `https://api.nuget.org/v3/index.json`.

### Examples

Show how every version of a package is listed, stable and pre-release, listed and unlisted:

```bash
nuversions show --package-id "MyPackage"
```

Unlist specific versions of a package (any pre-release or stable version):

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" "1.0.0-alpha.1" "1.0.0-alpha.2"
```

Unlist a broken or vulnerable stable release. Stable versions are unlisted the same way:

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" "1.4.2"
```

Unlist all pre-release versions of a package:

```bash
nuversions unlist --package-id "MyPackage" --unlist-all true --api-key "myApiKey"
```

Unlist all pre-release versions, including stable `0.x` versions:

```bash
nuversions unlist --package-id "MyPackage" --unlist-all true --include-zero-major true --api-key "myApiKey"
```

Unlist from a third-party NuGet server:

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" --server-url "https://my-server/api/v3/index.json" "2.0.0-beta.4"
```

Use the .NET SDK backend instead of the HTTP API:

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" --backend sdk "1.2.0-pre.1"
```

Run non-interactively (e.g. in CI), turning on lenient version parsing:

```bash
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" --non-interactive true --use-strict-parsing false "0.9.0-beta" "1.0.0"
```

Preview what an unlist would do, with no API key and no delete request:

```bash
nuversions unlist --package-id "MyPackage" --dry-run "1.0.0-alpha.1" "1.0.0-alpha.2"
```

Emit newline-delimited JSON on stdout for machine consumption:

```bash
nuversions show --package-id "MyPackage" --output json
nuversions unlist --package-id "MyPackage" --api-key "myApiKey" --output json "1.0.0-alpha.1"
```

## Unlisting and deprecation

Unlisting hides a version from search and from the nuget.org package details page. Existing consumers can still restore it, and unlisting tells them nothing by itself. Deprecation metadata is what surfaces warnings to `dotnet list package --deprecated` and IDE package managers. If you are unlisting a vulnerable or broken release, consider also deprecating the version (or a replacement) on nuget.org so consumers get the reason.

## Rate limits

NuGet.org's NuGet server implementation has an API rate limit for unlisting packages of [**250 package versions per hour**](https://learn.microsoft.com/en-gb/nuget/api/rate-limits) per API Key.

Third party NuGet servers may have their own rate limits. Please check your NuGet server's documentation for more information.

When the server rate-limits the run, this CLI **fails fast**: it stops sending further delete
requests, reports the results collected so far (versions not yet attempted are reported as
`not-attempted`), and exits with code `3`. It does not retry and does not wait for the rate-limit
window to reset. Rerun once the window has elapsed. Rate-limit detection is authoritative by HTTP
status code on the `http` backend and best-effort on the `sdk` backend.

## License

The CLI project is licensed under the GNU GPL v3.0 or later. See the [LICENSE](LICENSE) file for details.

The library powering the CLI's NuGet-related functionality is licensed under the GNU LGPL v3.0 or later.
