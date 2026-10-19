# Glossary

## Package unlisting

Requesting that a NuGet source unlist chosen versions of a package.

## Unlist action

Requesting that a NuGet server stop listing a package version. The version
stays on the server and is hidden from new installations; NuGet Gallery
unlists and does not delete. Removing a version from the server entirely is
a different action that some other NuGet servers perform, and it is not
part of this project's contract. Transport commands may still say delete
(HTTP DELETE, `dotnet nuget delete`); those are wire-level names, not
contract words.

## Unlist planning

Deciding which versions need unlisting versus are already unlisted
(existence check, listed-state partition, skip logic). Sits in front of
each implementation's unlist mechanism.

## Unlist plan

The three buckets unlist planning partitions requested versions into:
*to unlist* (exists and is listed), *already unlisted* (exists but
unlisted), *not on server* (absent from the server's metadata).

## Unlist mechanism

The transport-level single-version unlist inside each public unlisting
implementation: the HTTP unlist flow in one, the `dotnet nuget delete`
process in the other. Selected per run by the CLI's `--backend` flag.
Has no contract of its own.

## Version outcome

The closed result vocabulary for one version's attempt: *unlisted*,
*already unlisted*, *not on server*, *failed*, *rate limited*, or *not
attempted* (stopped before dispatch). Shared by the unlisting
implementations, CLI output, and exit codes.

## Dry run

A run that resolves and prints the unlist plan without sending any unlist
request; exits non-zero when any requested version is not on the server.
