# ADR 0001: No vulnerability advisory reporting in `show`

- Status: accepted
- Date: 2026-10-27

## Context

`show` reports, per version, the listing state (`show` command: `Status=listed|unlisted`) and, from the deprecation-visibility work, the deprecation verdict and reason(s) (`Deprecation=legacy|critical-bugs|other|...`). The NuGet registration metadata serves advisory data (GitHub Security Advisory IDs, aliases, affected ranges) directly beside deprecation metadata, and NuGet.Protocol exposes both through the same `IPackageSearchMetadata`, so adding advisory reporting to `show` looks like the obvious next feature.

Three reasons it was deliberately not built:

1. **Session criterion.** The session goal was to make `show` answer *one question per version*: deprecated or not, and why. The maintainer running `unlist` for a vulnerable release already knows the advisories; that is usually the reason for the run. Advisory data does not change listing or deprecation verdicts, so it earns no space in `show`'s output.
2. **No closed vocabulary.** Deprecation reasons on nuget.org are exactly three words. Advisories are open-ended: unbounded count per version, GitHub Advisory IDs with aliases, ranges. Every README contract promise (closed status words, fixed NDJSON field order and count) holds only while `show` reports closed data.
3. **Text-line layout.** A version with several advisories does not fit the `Version=... Status=... Deprecation=...` line format.

## Decision

`show` does not report vulnerability advisories, in either text or JSON output. Advisory data stays outside the tool's commands. The user-facing boundary is also stated in the README out-of-scope section.

## Consequences

**Positive**

- `show`'s output contract stays closed data and stays tabulable in the README.
- The 1.0 contract stops growing before freeze.

**Negative**

- A maintainer wanting advisory facts alongside listing state needs a second lookup (for consumers, `dotnet list package --vulnerable` exists; publisher-side, the registration page).
- If the boundary ever moves, the output contract break is a major-version event.

**Mitigations**

- The boundary and its reasons are published (this ADR and the README), so a future reversal starts from the recorded trade-off instead of treating the gap as an oversight.

## Alternatives considered

- **Advisory reporting in both output modes** - rejected: open-ended data breaks the closed vocabulary and the text-line layout.
- **Advisory reporting in JSON only** - rejected: the NDJSON schema is a published fixed three-field contract; widening it for data that does not influence `unlist` decisions buys little, and the text-mode user still sees nothing.
- **A TODO.md deferred entry instead** - rejected: the TODO header frames entries as future work, which miscommunicates a rejection as a backlog item.

## References

- [NuGet Client SDK](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk): `IPackageSearchMetadata` carries both deprecation and vulnerability data; the read-side source rejected here.
- `dotnet list package --vulnerable`: the existing consumer-side advisory surface.