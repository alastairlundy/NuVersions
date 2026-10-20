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

This is deliberate, not an oversight, and it is correct for nuget.org — see the
non-goals below. But it leaves **private feeds that require authentication for reads
unsupported**: Azure Artifacts, GitHub Packages, ProGet and Artifactory all sit behind
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

`GetRepoInfo` in `PackageVersionService` is the single seam for the read path;
`PackageAvailabilityDetector` duplicates the same call and needs the same treatment.

Note the credential transport differs by feed family. `PackageSourceCredential` yields
Basic/NTLM. `X-NuGet-ApiKey` is a nuget.org `PackagePublish` mechanism that private feeds
do not expect, and in NuGet.Protocol the only API-key path is `PackageUpdateResource`,
which covers push and delete — not reads. The two mechanisms may have to coexist: the
HTTP unlist backend keeps `X-NuGet-ApiKey`, while a private-feed read would use Basic.

### Open decisions

1. **Credential model.** Reuse `--api-key` as the Basic password, introduce a separate
   `--username`/`--password` pair, or add a credential-provider lookup? `dotnet restore`'s
   `nuget.config` credential support is the obvious precedent to follow.
2. **Does `show` gain a credential option?** Today `show` deliberately sends no
   credential — documented in the `ShowCommand` remarks — and that behaviour is pinned by
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
   `IPackageVersionService` surface. The assembly is not published on NuGet, so the break
   is contained to this repository, but it is still a breaking change to a public interface.

### Risks and constraints

- NuGet.Protocol's read authentication is challenge-response:
  `HttpSourceAuthenticationHandler` assigns `HttpClientHandler.Credentials` and does not
  enable `PreAuthenticate`, so the `Authorization` header is sent only after the server
  answers 401. A feed that requires auth on the *first* request will therefore take two
  round trips.
- `Repository.Factory.GetCoreV3` wires no credential service, so a 401 cannot trigger an
  interactive prompt. That is the right behaviour for a CLI, but it means an
  authentication failure surfaces as a feed-side failure (exit `1`) rather than a usage
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

- Read and surface the server's `Retry-After` hint on the 429, so the run can say when it
  will be possible to continue.
- Offer a resume affordance for the `not-attempted` versions, so a long run does not have
  to be reconstructed by hand from the JSON output.
