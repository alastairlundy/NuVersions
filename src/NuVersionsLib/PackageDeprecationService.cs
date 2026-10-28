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
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
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

        PackageMetadataResource? metadataResource =
            await repoInfo.GetResourceAsync<PackageMetadataResource>(cancellationToken).ConfigureAwait(false);

        if (metadataResource is null)
            throw new InvalidOperationException(string.Format(Resources.Exceptions_NuGet_FindResources_FindPackageMetadata_Unavailable, nugetApiUrl));

        // Same fetch shape as the listing read: prerelease and unlisted versions both included, so
        // the version set the CLI already enumerates maps 1:1 onto the served metadata.
        IEnumerable<IPackageSearchMetadata> results =
            await metadataResource.GetMetadataAsync(packageId, includePrerelease: true, includeUnlisted: true,
                cacheContext, NullLogger.Instance, cancellationToken).ConfigureAwait(false);

        Dictionary<NuGetVersion, PackageDeprecationInfo?> metadataByVersion = new();

        foreach (IPackageSearchMetadata metadata in results)
        {
            PackageDeprecationMetadata? deprecationMetadata =
                await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false);

            metadataByVersion[metadata.Identity.Version] = MapDeprecationMetadata(deprecationMetadata);
        }

        Dictionary<NuGetVersion, PackageDeprecationInfo?> output = new(capacity: packageVersions.Count);

        foreach (NuGetVersion version in packageVersions.Distinct())
        {
            // A requested version the served metadata carries no deprecation object for is not
            // deprecated (D002) — the same verdict as a feed that never serves the fact at all,
            // which is what keeps the absent and no-fact cases indistinguishable.
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
    /// translation, no normalization (D002, D003). Advisory data is never read (ADR 0001).
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
