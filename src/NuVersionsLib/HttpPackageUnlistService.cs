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

using System.Net;
using NuVersionsLib.Internal;

namespace NuVersionsLib;

/// <summary>
/// Service for requesting package unlisting from a NuGet server over HTTP (the NuGet DELETE endpoint).
/// </summary>
/// <remarks>
/// <para>
/// The publish URL is resolved per call via the service index, so a single instance may serve
/// multiple sources. Every shared run rule (argument validation, package-existence check,
/// listed-state check, partition, bucket yields, fail-fast rate-limit loop) lives once in
/// <see cref="UnlistRun"/>.
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

#pragma warning disable MA0051 // Deliberately one linear transport flow; small pure classifiers live below.
    /// <summary>
    /// Unlists a single package version via the NuGet HTTP DELETE endpoint.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to unlist.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the unlist attempt for this version.</returns>
    /// <remarks>
    /// <para>
    /// Failures are classified here, in place: 401/403 report <see cref="PackageVersionFailureType.AuthFailed"/>,
    /// 5xx reports <see cref="PackageVersionFailureType.ServerError"/>, a caught transport fault without a
    /// status code reports <see cref="PackageVersionFailureType.Unavailable"/>, and any other non-success
    /// reports <see cref="PackageVersionFailureType.Unknown"/>. A 429 keeps its
    /// <see cref="PackageVersionStatus.RateLimited"/> status and additionally carries
    /// <see cref="PackageVersionFailureType.RateLimited"/>. An <see cref="HttpRequestException"/> that carries
    /// a status code is classified by that code as well.
    /// </para>
    /// <para>
    /// Every Failed and RateLimited outcome carries a Detail. Normally it is composed raw and verbatim
    /// from the response's status line (code plus reason phrase when present) and body text, untruncated
    /// at this boundary; if the body cannot be read, the status line alone is retained so classification
    /// and fail-fast behavior do not depend on a complete body. Caught faults carry the exception message.
    /// Any cut happens only at render time.
    /// </para>
    /// </remarks>
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

            try
            {
                client.DefaultRequestHeaders.Add(NugetApiKeyHeaderName, [apiKey]);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                // Invalid header values are local credential errors, not transport failures: no
                // request has reached the server, so do not report the server as unavailable.
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed,
                    PackageVersionFailureType.AuthFailed, exception.Message);
            }

            client.BaseAddress = new Uri(publishUrl.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);

            string relativeUrl = $"{packageId}/{version.ToNormalizedString()}";

            using HttpRequestMessage request = new(HttpMethod.Delete, relativeUrl);
            using HttpResponseMessage response =
                await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Unlisted);
            }

            if ((int)response.StatusCode == 404 && resolvedPublishUrl)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyUnlisted);
            }

            // Classify from the headers before reading the body. A server may send a complete
            // status line and then fail or truncate the body; that must not turn a received 429
            // into a generic failure and defeat the shared run's fail-fast rule.
            string statusLine = response.ReasonPhrase is { Length: > 0 } reasonPhrase
                ? $"HTTP {(int)response.StatusCode} {reasonPhrase}"
                : $"HTTP {(int)response.StatusCode}";

            PackageVersionStatus status = response.StatusCode == HttpStatusCode.TooManyRequests
                ? PackageVersionStatus.RateLimited
                : PackageVersionStatus.Failed;

            PackageVersionFailureType failureType = ClassifyStatusCode(response.StatusCode);

            string body;

            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (UnlistRun.ConvertsToFailed(exception, cancellationToken))
            {
                return new PackageVersionOutcome(packageId, version, status, failureType, statusLine);
            }

            string detail = body.Length > 0
                ? statusLine + Environment.NewLine + body
                : statusLine;

            return new PackageVersionOutcome(packageId, version, status, failureType, detail);
        }
        catch (Exception exception) when (UnlistRun.ConvertsToFailed(exception, cancellationToken))
        {
            return CreateExceptionOutcome(packageId, version, exception);
        }
    }
#pragma warning restore MA0051

    private static PackageVersionFailureType ClassifyStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            401 or 403 => PackageVersionFailureType.AuthFailed,
            >= 500 and < 600 => PackageVersionFailureType.ServerError,
            429 => PackageVersionFailureType.RateLimited,
            _ => PackageVersionFailureType.Unknown,
        };

    private static PackageVersionOutcome CreateExceptionOutcome(string packageId, NuGetVersion version,
        Exception exception)
    {
        HttpStatusCode? statusCode = (exception as HttpRequestException)?.StatusCode;

        PackageVersionStatus status = statusCode == HttpStatusCode.TooManyRequests
            ? PackageVersionStatus.RateLimited
            : PackageVersionStatus.Failed;

        PackageVersionFailureType failureType = statusCode is null
            ? PackageVersionFailureType.Unavailable
            : ClassifyStatusCode(statusCode.Value);

        return new PackageVersionOutcome(packageId, version, status, failureType, exception.Message);
    }
}
