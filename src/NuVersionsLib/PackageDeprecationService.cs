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

namespace NuVersionsLib;

/// <summary>
/// Retrieves per-version deprecation metadata from a NuGet repository's registration catalog pages.
/// </summary>
public class PackageDeprecationService : IPackageDeprecationService
{
    /// <summary>
    /// Reports the deprecation metadata each of the specified versions has on the repository,
    /// fetched through NuGet.Protocol's metadata resource with unlisted versions included.
    /// </summary>
    /// <remarks>
    /// The fetch is the registration catalog pages under the standard <see cref="SourceCacheContext"/>
    /// — the same page batching NuGet.Protocol already performs, with no search endpoint (which
    /// would drop unlisted versions) and no self-implemented catalog-leaf walks. The version set
    /// read is exactly the set the caller passes, so it can be the enumeration the CLI already
    /// prints.
    /// </remarks>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">
    /// The API key for authentication against the NuGet API. Shape-compatibility only: this read
    /// never uses or transmits the key, exactly like the other read paths (see
    /// <see cref="IPackageVersionService"/>); making keys meaningful on reads is tracked in TODO.md.
    /// </param>
    /// <param name="packageId">The identifier of the package to check.</param>
    /// <param name="packageVersions">The specific versions of the package to read deprecation metadata for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A dictionary mapping each requested version to its deprecation metadata, or <see langword="null"/> when not deprecated.</returns>
    /// <exception cref="InvalidOperationException">The package metadata resource is unavailable for this source.</exception>
    public async Task<IDictionary<NuGetVersion, PackageDeprecationInfo?>> CheckPackageVersionsDeprecationAsync(
        string nugetApiUrl, string nugetApiKey, string packageId,
        IList<NuGetVersion> packageVersions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(packageVersions);

        SourceRepository repoInfo = Repository.Factory.GetCoreV3(nugetApiUrl);

        using SourceCacheContext cacheContext = new SourceCacheContext();

        try
        {
            Dictionary<NuGetVersion, PackageDeprecationInfo?> metadataByVersion =
                await ReadServedDeprecationMetadataAsync(repoInfo, nugetApiUrl, packageId, cacheContext,
                        cancellationToken)
                    .ConfigureAwait(false);

            return DeprecationVerdicts(packageVersions, metadataByVersion);
        }
        catch (HttpRequestException e)
        {
            // A bare HttpRequestException's message carries only the HTTP status, not what was being
            // read; this command-boundary context names the package and server so the CLI's one-line
            // diagnostic (the printed exception.Message) is diagnosable on its own. Throwing
            // FatalProtocolException (NuGetProtocolException's concrete subclass) keeps the existing
            // recognized-family mapping (exit 1) and the inner exception for debugging.
            throw new FatalProtocolException(
                string.Format(Resources.Exceptions_NuGet_PackageMetadata_DeprecationReadFailed,
                    packageId, nugetApiUrl, e.Message), e);
        }
    }

    /// <summary>
    /// Reads the server's served deprecation metadata for every version of <paramref name="packageId"/>,
    /// mapped onto <see cref="PackageDeprecationInfo"/>, through the package metadata resource of
    /// <paramref name="repoInfo"/>. Prerelease and unlisted versions are both requested, so the version
    /// set the CLI already enumerates maps 1:1 onto the served metadata.
    /// </summary>
    /// <remarks>
    /// The per-iteration cancellation check is the only cancellation observation this read has after
    /// its metadata fetch: <c>GetDeprecationMetadataAsync</c> has no token-taking overload, so without
    /// it Ctrl-C during the fetch loop is ignored until the run completes instead of surfacing the
    /// documented interrupted shape (exit 130).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The package metadata resource is unavailable for this source.</exception>
    private async Task<Dictionary<NuGetVersion, PackageDeprecationInfo?>> ReadServedDeprecationMetadataAsync(
        SourceRepository repoInfo, string nugetApiUrl, string packageId, SourceCacheContext cacheContext,
        CancellationToken cancellationToken)
    {
        PackageMetadataResource? metadataResource =
            await repoInfo.GetResourceAsync<PackageMetadataResource>(cancellationToken).ConfigureAwait(false);

        if (metadataResource is null)
            throw new InvalidOperationException(string.Format(Resources.Exceptions_NuGet_FindResources_FindPackageMetadata_Unavailable,
                nugetApiUrl));

        Dictionary<NuGetVersion, PackageDeprecationInfo?> metadataByVersion = new();

        foreach (IPackageSearchMetadata metadata in
                 await metadataResource.GetMetadataAsync(packageId, includePrerelease: true, includeUnlisted: true,
                     cacheContext, NullLogger.Instance, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            PackageDeprecationMetadata? deprecationMetadata =
                await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false);

            metadataByVersion[metadata.Identity.Version] = MapDeprecationMetadata(deprecationMetadata);
        }

        return metadataByVersion;
    }

    /// <summary>
    /// Reads one version's deprecation verdict from the served metadata: a version the server serves
    /// no deprecation object for is not deprecated, the same verdict as a feed that never serves the
    /// fact at all — which is what keeps the absent and no-fact cases indistinguishable beyond the
    /// enumerated metadata.
    /// </summary>
    private static Dictionary<NuGetVersion, PackageDeprecationInfo?> DeprecationVerdicts(
        IList<NuGetVersion> packageVersions,
        IReadOnlyDictionary<NuGetVersion, PackageDeprecationInfo?> metadataByVersion)
    {
        Dictionary<NuGetVersion, PackageDeprecationInfo?> output = new(capacity: packageVersions.Count);

        foreach (NuGetVersion version in packageVersions.Distinct())
        {
            output[version] = metadataByVersion.TryGetValue(version, out PackageDeprecationInfo? deprecationInfo)
                ? deprecationInfo
                : null;
        }

        return output;
    }

    /// <summary>
    /// Maps one version's served deprecation object onto its metadata record: any object the
    /// server serves is the deprecated verdict, even a reason-less one; an absent object is the
    /// not-deprecated verdict (<see langword="null"/>). Reasons pass through verbatim — no
    /// translation, no normalization. Advisory data is never read (ADR 0001).
    /// </summary>
    internal static PackageDeprecationInfo? MapDeprecationMetadata(PackageDeprecationMetadata? deprecationMetadata)
    {
        if (deprecationMetadata is null)
        {
            return null;
        }

        return new PackageDeprecationInfo
        {
            Reasons = [.. deprecationMetadata.Reasons ?? []],
            Message = deprecationMetadata.Message,
            AlternatePackageId = deprecationMetadata.AlternatePackage?.PackageId,
            AlternatePackageRange = deprecationMetadata.AlternatePackage?.Range?.OriginalString
        };
    }
}
