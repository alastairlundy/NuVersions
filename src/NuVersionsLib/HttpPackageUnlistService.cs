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

using NuVersionsLib.Internal;

namespace NuVersionsLib;

/// <summary>
/// Service for requesting package unlisting from a NuGet server over HTTP (the NuGet DELETE endpoint).
/// </summary>
/// <remarks>
/// <para>
/// Per-version unlist requests are dispatched through this class's own inlined HTTP flow; the publish URL
/// is resolved per call via the service index, so a single instance may serve multiple sources. Every
/// shared run rule (argument validation, package-existence check, listed-state check, partition, bucket
/// yields, fail-fast rate-limit loop) lives once in <see cref="UnlistRun"/>.
/// </para>
/// <para>
/// A 404 is only reported as <see cref="PackageVersionStatus.AlreadyUnlisted"/> when the server's
/// service index advertised the <c>PackagePublish/2.0.0</c> endpoint the request actually went to.
/// When that endpoint is absent and the request fell back to the raw server URL, a 404 cannot be
/// interpreted and stays a <see cref="PackageVersionStatus.Failed"/> outcome.
/// </para>
/// </remarks>
public class HttpPackageUnlistService : IPackageUnlistService
{
    private const string NugetApiKeyHeaderName = "X-NuGet-ApiKey";

    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;
    private readonly IHttpClientFactory _clientFactory;

    /// <summary>
    /// Creates a service that sends unlist requests through the NuGet HTTP publish endpoint.
    /// </summary>
    /// <param name="packageVersionService">Service used to enumerate and check package versions.</param>
    /// <param name="packageAvailabilityDetector">Detector used to verify the package exists on the server.</param>
    /// <param name="clientFactory">Factory used to create the <see cref="HttpClient"/> that sends each unlist request.</param>
    public HttpPackageUnlistService(IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        IHttpClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(packageVersionService);
        ArgumentNullException.ThrowIfNull(packageAvailabilityDetector);
        ArgumentNullException.ThrowIfNull(clientFactory);

        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Requests the unlisting of all prerelease versions of a NuGet package. Stable versions with
    /// Major == 0 count as prerelease and are unlisted alongside them.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the NuGet package to unlist versions for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageUnlistingAsync(
        string nugetApiUrl, string nugetApiKey, string packageId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<PackageVersionOutcome> results = UnlistRun.ExecuteAsync(nugetApiUrl, nugetApiKey,
            packageId, UnlistVersionAsync, _packageVersionService,
            _packageAvailabilityDetector, cancellationToken);

        await foreach (PackageVersionOutcome outcome in results.ConfigureAwait(false))
        {
            yield return outcome;
        }
    }

    /// <summary>
    /// Asynchronously requests the unlisting of specified NuGet package versions from a package registry.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the NuGet package to unlist versions for.</param>
    /// <param name="versions">The versions of the package to unlist.</param>
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
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageUnlistingAsync(string nugetApiUrl,
        string nugetApiKey, string packageId,
        IList<NuGetVersion> versions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<PackageVersionOutcome> results = UnlistRun.ExecuteAsync(nugetApiUrl, nugetApiKey,
            packageId, versions, UnlistVersionAsync, _packageVersionService, _packageAvailabilityDetector,
            cancellationToken);

        await foreach (PackageVersionOutcome outcome in results.ConfigureAwait(false))
        {
            yield return outcome;
        }
    }

    /// <summary>
    /// Unlists a single package version via the NuGet HTTP DELETE endpoint.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to unlist.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the unlist attempt for this version.</returns>
    private async ValueTask<PackageVersionOutcome> UnlistVersionAsync(
        string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey, CancellationToken cancellationToken)
    {
        UnlistRun.ValidateUnlistArguments(nugetApiUrl, packageId, version, apiKey);

        try
        {
            SourceRepository repoInfo = Repository.Factory.GetCoreV3(nugetApiUrl);

            ServiceIndexResourceV3? serviceIndex =
                await repoInfo.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken)
                    .ConfigureAwait(false);

            Uri? publishUrl = serviceIndex?.GetServiceEntryUri("PackagePublish/2.0.0");

            // A 404 only means "already unlisted" when the request actually went to the server's
            // declared publish endpoint. When the service index did not advertise one and the run
            // fell back to the raw server URL, a 404 says nothing about the version's state, so the
            // request stays a plain failure instead of a false success.
            bool resolvedPublishUrl = publishUrl is not null;

            publishUrl ??= new Uri(nugetApiUrl);

            HttpClient client = _clientFactory.CreateClient();

            client.DefaultRequestHeaders.Add(NugetApiKeyHeaderName, [apiKey]);
            client.BaseAddress = new Uri(publishUrl.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);

            string relativeUrl = $"{packageId}/{version.ToNormalizedString()}";

            using HttpResponseMessage response =
                await client.DeleteAsync(relativeUrl, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Unlisted);
            }

            if ((int)response.StatusCode == 404 && resolvedPublishUrl)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyUnlisted);
            }

            if ((int)response.StatusCode == 429)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.RateLimited);
            }

            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
        catch (Exception exception) when (UnlistRun.ConvertsToFailed(exception, cancellationToken))
        {
            // Server-side faults become a Failed outcome; the caller-cancellation rule lives in
            // UnlistRun.ConvertsToFailed.
            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
    }
}
