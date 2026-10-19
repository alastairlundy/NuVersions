/*
    NuVersionsLib
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using NuVersionsLib.Detectors.Abstractions;

namespace NuVersionsLib.Internal;

/// <summary>
/// The shared body of one unlist run: argument validation, the package-existence check, the listed-state
/// check, the <see cref="UnlistPlanning.Partition"/> bucket outcome yields, and the fail-fast rate-limit
/// loop with run state local to each invocation.
/// </summary>
/// <remarks>
/// Per-version dispatch is passed in as a delegate parameter of the exact shape
/// <c>Func&lt;string, string, NuGetVersion, string, CancellationToken, ValueTask&lt;PackageVersionOutcome&gt;&gt;</c>
/// (server URL, package id, version, API key, cancellation token); it never becomes an interface or a named
/// contract type. Each public implementation supplies its own transport flow for that parameter.
/// </remarks>
internal static class UnlistRun
{
    /// <summary>
    /// Runs the unlist flow for an explicit version list, dispatching each to-unlist version through
    /// <paramref name="dispatchUnlist"/>.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package to unlist versions for.</param>
    /// <param name="versions">The versions to unlist. May contain duplicates.</param>
    /// <param name="dispatchUnlist">The transport flow that unlists a single version and reports its outcome.</param>
    /// <param name="packageVersionService">Service used to check package versions' listed state.</param>
    /// <param name="packageAvailabilityDetector">Detector used to verify the package exists on the server.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    /// <remarks>
    /// <para>
    /// Already-unlisted and not-on-server versions are reported from the plan's buckets; only
    /// <c>plan.ToUnlist</c> versions are dispatched, so every requested version yields exactly one outcome.
    /// </para>
    /// <para>
    /// Fail-fast: when a dispatch reports <see cref="PackageVersionStatus.RateLimited"/>, no further
    /// dispatches are made and every remaining to-unlist version is reported as
    /// <see cref="PackageVersionStatus.NotAttempted"/> rather than <see cref="PackageVersionStatus.Failed"/>.
    /// </para>
    /// </remarks>
    internal static async IAsyncEnumerable<PackageVersionOutcome> ExecuteAsync(
        string nugetApiUrl,
        string nugetApiKey,
        string packageId,
        IList<NuGetVersion> versions,
        Func<string, string, NuGetVersion, string, CancellationToken, ValueTask<PackageVersionOutcome>> dispatchUnlist,
        IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(dispatchUnlist);
        ArgumentNullException.ThrowIfNull(packageVersionService);
        ArgumentNullException.ThrowIfNull(packageAvailabilityDetector);

        bool doesPackageExist = await packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken).ConfigureAwait(false);

        if (!doesPackageExist)
            throw new ArgumentException(string.Format(Resources.Exceptions_Package_NotFoundOnServer, packageId, nugetApiUrl), nameof(packageId));

        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo =
            await packageVersionService.CheckPackageVersionsListedAsync(nugetApiUrl, nugetApiKey, packageId,
                true, versions, cancellationToken).ConfigureAwait(false);

        UnlistPlan plan = UnlistPlanning.Partition(versions, listingInfo);

        foreach (NuGetVersion alreadyUnlistedVersion in plan.AlreadyUnlisted)
        {
            yield return new PackageVersionOutcome(packageId, alreadyUnlistedVersion,
                PackageVersionStatus.AlreadyUnlisted);
        }

        foreach (NuGetVersion missingVersion in plan.NotOnServer)
        {
            yield return new PackageVersionOutcome(packageId, missingVersion,
                PackageVersionStatus.NotOnServer);
        }

        if (plan.ToUnlist.Count == 0)
            yield break;

        bool rateLimitStop = false;

        foreach (NuGetVersion version in plan.ToUnlist)
        {
            if (rateLimitStop)
            {
                yield return new PackageVersionOutcome(packageId, version, PackageVersionStatus.NotAttempted);
                continue;
            }

            PackageVersionOutcome outcome = await dispatchUnlist(nugetApiUrl, packageId, version,
                nugetApiKey, cancellationToken).ConfigureAwait(false);

            yield return outcome;

            if (outcome.Status == PackageVersionStatus.RateLimited)
                rateLimitStop = true;
        }
    }

    /// <summary>
    /// Runs the unlist flow for every prerelease version of a package (optionally including stable
    /// zero-major versions), dispatching each to-unlist version through <paramref name="dispatchUnlist"/>.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package to unlist versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are also unlisted alongside prerelease versions.</param>
    /// <param name="dispatchUnlist">The transport flow that unlists a single version and reports its outcome.</param>
    /// <param name="packageVersionService">Service used to enumerate and check package versions.</param>
    /// <param name="packageAvailabilityDetector">Detector used to verify the package exists on the server.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    internal static async IAsyncEnumerable<PackageVersionOutcome> ExecuteAsync(
        string nugetApiUrl,
        string nugetApiKey,
        string packageId,
        bool includeZeroMajorVersions,
        Func<string, string, NuGetVersion, string, CancellationToken, ValueTask<PackageVersionOutcome>> dispatchUnlist,
        IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NuGetVersion[] versionsToUnlist = await packageVersionService.GetPrereleasePackageVersionsAsync
            (nugetApiUrl, nugetApiKey, packageId, includeZeroMajorVersions, cancellationToken).ConfigureAwait(false);

        IAsyncEnumerable<PackageVersionOutcome> results = ExecuteAsync(nugetApiUrl, nugetApiKey, packageId,
            versionsToUnlist, dispatchUnlist, packageVersionService, packageAvailabilityDetector, cancellationToken);

        await foreach (PackageVersionOutcome outcome in results.ConfigureAwait(false))
        {
            yield return outcome;
        }
    }

    /// <summary>
    /// Validates the per-dispatch argument block shared by every inlined unlist mechanism.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to unlist.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    internal static void ValidateUnlistArguments(string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
    }

    /// <summary>
    /// Decides whether a fault thrown by an inlined unlist mechanism converts to a
    /// <see cref="PackageVersionStatus.Failed"/> outcome.
    /// </summary>
    /// <param name="exception">The fault thrown by the mechanism.</param>
    /// <param name="callerCancellationToken">The token the caller passed for the run.</param>
    /// <returns><see langword="true"/> when the fault becomes a Failed outcome.</returns>
    /// <remarks>
    /// Server-side faults become a Failed outcome. A cancellation of the caller's token is deliberately not
    /// converted: it escapes so the command boundary can report the run as interrupted instead of as a wall
    /// of Failed outcomes. An <see cref="OperationCanceledException"/> that is not caused by the caller's
    /// token (for example the HttpClient timeout) stays a Failed outcome.
    /// </remarks>
    internal static bool ConvertsToFailed(Exception exception, CancellationToken callerCancellationToken)
    {
        return exception is not OperationCanceledException
               || !callerCancellationToken.IsCancellationRequested;
    }
}
