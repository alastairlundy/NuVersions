# TODO

Deferred work items. Each one records the motivation, the design sketch, the open
decisions, and what "done" would look like, so the item can be picked up later
without re-deriving the reasoning.

---

## Authenticated reads for private NuGet feeds

**Status:** Deferred, not started.
**Area:** `src/NuVersionsLib` read paths (`PackageVersionService`,
`PackageAvailabilityDetector`), `src/NuVersionsLib/Abstractions/IPackageVersionService.cs`,
`src/NuVersionsCli/ShowCommand.cs`, `src/NuVersionsCli/UnlistCommand.cs`.

### Motivation

The read paths never send a credential, and today they cannot: the repository is built
from a bare URL, so there is no channel for a key to reach the server.

```csharp
// src/NuVersionsLib/PackageVersionService.cs
private SourceRepository GetRepoInfo(string nugetApiUrl) => Repository.Factory.GetCoreV3(nugetApiUrl);
```

`GetCoreV3(string)` wraps the URL in a `PackageSource` that carries no `Credentials`, and
the `SourceCacheContext` used alongside it is the default, which is also credential-free.
NuGet.Protocol only authenticates read requests when the `PackageSource` carries a
`PackageSourceCredential`. `src/NuVersionsLib/Detectors/PackageAvailabilityDetector.cs:9`
makes the same call independently and would have to move in lockstep.

This is deliberate, not an oversight, and it is correct for nuget.org. See the
non-goals below. But it leaves **private feeds that require authentication for reads**
unsupported: Azure Artifacts, GitHub Packages, ProGet and Artifactory all sit behind
credentials on their metadata endpoints, and their rate limiters commonly key on the
authenticated identity rather than on the IP address.

### Non-goals

This is **not** a rate-limit optimisation for nuget.org, and it should not be sold as one.
The documented nuget.org limits make the position explicit
([Rate Limits, NuGet API](https://learn.microsoft.com/en-us/nuget/api/rate-limits)):

| API | Limit Type | Limit Value |
| --- | --- | --- |
| `GET /api/v1/Packages` | IP | 1000 / minute |
| `GET /api/v1/Search()` | IP | 3000 / minute |
| `GET /api/v2/Packages` | IP | 20000 / minute |
| `PUT /api/v2/package` (push) | API Key | 350 / hour |
| `DELETE /api/v2/package/{id}/{version}` (unlist) | API Key | 250 / hour |

Every read limit is keyed on the **IP address**. The API key is the limit key only for
push and unlist, and the unlist path already sends it
(`src/NuVersionsLib/PackageUnlistService.cs:161` sets `X-NuGet-ApiKey` on the DELETE).
The same page notes that nuget.org's V3 search APIs "are not rate limited currently".

Nor is it a change to the write path. The write path already carries the credential.

### Design sketch

Build the `PackageSource` with credentials rather than using the bare-string overload:

```csharp
PackageSource source = new(nugetApiUrl)
{
    Credentials = new PackageSourceCredential(
        nugetApiUrl, nugetApiKey, isPassword: true, validAuthenticationTypesText: "Basic")
};

SourceRepository repo = Repository.Factory.GetCoreV3(source);
```

`GetRepoInfo` in `PackageVersionService` is the only place the read path builds a source;
`PackageAvailabilityDetector` duplicates the same call and needs the same treatment.

Note the credential transport differs by feed type. `PackageSourceCredential` yields
Basic/NTLM. `X-NuGet-ApiKey` is a nuget.org `PackagePublish` mechanism that private feeds
do not expect, and in NuGet.Protocol the only API-key path is `PackageUpdateResource`,
which covers push and delete, not reads. The two mechanisms may have to coexist: the
HTTP unlist backend keeps `X-NuGet-ApiKey`, while a private-feed read would use Basic.

### Open decisions

1. **Credential model.** Reuse `--api-key` as the Basic password, introduce a separate
   `--username`/`--password` pair, or add a credential-provider lookup? `dotnet restore`'s
   `nuget.config` credential support is the obvious precedent to follow.
2. **Does `show` gain a credential option?** Today `show` deliberately sends no
   credential, documented in the `ShowCommand` remarks, and that behaviour is pinned by
   `tests/NuVersionsLib.Tests/CliShowCommandTests.cs` (`ShowSendsNoApiKey_AndNoDeleteRequest`).
   Private-feed support requires changing both the behaviour and the test.
3. **Fallback order.** Anonymous first and retry with credentials on 401, or always send
   credentials when one is configured? The first keeps nuget.org fast; the second avoids a
   round trip on private feeds.
4. **Dry run.** `RunDryRunAsync` currently passes the literal placeholder `"dry-run"` as
   the key because the read paths accept a non-empty string and never transmit it. With
   real credentials this placeholder has to go.
5. **Key optionality.** Every public method currently rejects an empty key with
   `ArgumentException.ThrowIfNullOrEmpty(nugetApiKey)`. Making the key optional for
   anonymous reads but required for authenticated ones changes the public
   `IPackageVersionService` interface. The assembly is not published on NuGet, so the break
   is contained to this repository, but it is still a breaking change to a public interface.

### Risks and constraints

- NuGet.Protocol's read authentication is challenge-response:
  `HttpSourceAuthenticationHandler` assigns `HttpClientHandler.Credentials` and does not
  enable `PreAuthenticate`, so the `Authorization` header is sent only after the server
  answers 401. A feed that requires auth on the *first* request will therefore take two
  round trips.
- `Repository.Factory.GetCoreV3` wires no credential service, so a 401 cannot trigger an
  interactive prompt. That is the right behaviour for a CLI, but it means an
  authentication failure shows up as a feed-side failure (exit `1`) rather than a usage
  error (exit `2`). Decide whether that deserves its own message.
- The existing contract that the credential never reaches stdout or stderr must survive
  this change.

### Acceptance criteria

- Reads succeed against a private feed that requires authentication, for both `show` and
  the dry run.
- The credential never appears in stdout or stderr.
- Behaviour against nuget.org is unchanged for anonymous use.

### References

- [Rate Limits, NuGet API](https://learn.microsoft.com/en-us/nuget/api/rate-limits):
  read limits are IP-keyed; the API key is the limit key only for push and unlist.
- [Overview of the NuGet Server API](https://learn.microsoft.com/en-us/nuget/api/overview):
  "For nuget.org, only the `PackagePublish` resource requires authentication via a special
  API key header."
- [Push and Delete, NuGet API](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource):
  `X-NuGet-ApiKey` is required for push and delete.

---

## Report the nuget.org unlist rate-limit ceiling

**Status:** Deferred, not started.
**Area:** `src/NuVersionsLib/PackageUnlistService.cs`, `src/NuVersionsLib/Internal/UnlistRun.cs`.

The unlist limit is 250 per hour per API key on nuget.org, and there is no client-side
knob for it. When a run hits it, the server answers 429 and the run already behaves
correctly: the offending version becomes `rate-limited` and every remaining version
becomes `not-attempted` (`PackageUnlistService.cs:180-182` and the fail-fast loop in
`UnlistRun.ExecuteAsync`).

The gap is only in what the user is told. Possible improvements:

- Read the server's `Retry-After` hint on the 429 and print it, so the run can say when it
  will be possible to continue.
- Offer a resume option for the `not-attempted` versions, so a long run does not have
  to be reconstructed by hand from the JSON output.

---

## Deprecation visibility in `show`; a deprecation command stays out of scope

**Status:** Deferred, not started.
**Area:** `src/NuVersionsLib/PackageVersionService.cs`, `src/NuVersionsCli/ShowCommand.cs`,
`src/NuVersionsLib/Internal/Localizations/Resources.resx`.

### Motivation

A maintainer unlisting a broken or vulnerable release also usually wants to deprecate the
version, so consumers get the *reason* (`legacy`, `critical-bugs`, `other`), an alternate
package, and a message via `dotnet list package --deprecated`. Deprecation cannot be set
programmatically: unlike unlisting it has no documented endpoint in the NuGet Server API,
NuGet.Protocol only reads deprecation metadata (verify: `PackageUpdateResource` in
NuGet/NuGet.Client contains push/delete logic only, and `PackageDeprecationMetadata` exists
only on the read side), and `dotnet nuget` has no
deprecate verb (the command list: push, delete, locals, sources, verify, trust, sign, why).
Deprecation is set only through the nuget.org website. A tool built on official APIs must
not scrape that web endpoint.

### Design sketch

Do the supported half: read. NuGet.Protocol exposes `PackageDeprecationMetadata` (reasons,
message, alternate package id/version) through `PackageSearchMetadata` and the registration
metadata. `show` could fetch the deprecation state of the package and render it per version:

- text mode: append a `Deprecation=<reason(s)>` column, or a suffix when deprecation exists so
  undeprecated lines stay unchanged;
- `--output json`: nullable `deprecation` object (`reasons`, `message`, `alternatePackage`)
  in the NDJSON schema. Adding an optional field is breaking for schema consumers, so it
  needs README contract-table and CHANGELOG treatment.

Third-party feeds: deprecation metadata is read from the registration protocol; a feed that
does not serve it renders as "no deprecation", indistinguishable from nuget.org's absent
case at the metadata level.

### Open decisions

1. **Does the text line grow or keep its width?** The `Version=... Status=...` line is
   pinned by tests; a wide-locked format (`-` for none) versus a conditional third field
   changes the CLI render contract.
2. **NDJSON schema stability.** `show` documents "exactly three fields, no additional
   fields", README-pinned. This feature would knowingly break that sentence; bump the major
   and document the new field, or gate behind a flag?
3. **Scope of the read.** Deprecation is per version; `show` already enumerates all versions,
   so the natural seam is the same loop. Verify whether `PackageSearchMetadata` (search
   resource) carries per-version deprecation, or whether the per-version registration catalog
   page is needed instead (more requests; respects nuget.org's IP-keyed read limits).

### Risks and constraints

- NuGet.Protocol's deprecation read is a Newtonsoft.Json reflection path
  (`PackageSearchMetadata`). Confirm it does not add trim-surface warnings beyond what
  NuGet.Protocol already contributes.
- The status vocabulary in the README contract tables must not gain a deprecation word from
  this feature: deprecation is metadata, not an outcome state.

### Acceptance criteria

- `show` reports existing deprecation reasons per version, for both text and JSON output.
- No write path is added; the CLI contract for `unlist` is untouched.
- README/GLOSSARY/exit-code tables updated in the same change, per AGENTS.md.

### References

- [Deprecating packages](https://learn.microsoft.com/en-us/nuget/nuget-org/deprecate-packages):
  the website-only writer and the nuget.org-only contract.
- [NuGet Client SDK](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk):
  the read-side source of `PackageDeprecationMetadata`.
- [dotnet nuget commands](https://learn.microsoft.com/en-us/nuget/reference/dotnet-commands):
  no deprecate verb as of .NET 10.
