using NuVersionsLib.Detectors.Abstractions;

namespace NuVersionsLib.Detectors;

public class PackageAvailabilityDetector : IPackageAvailabilityDetector
{
    public async Task<bool> CheckPackageExistsAsync(string nugetApiUrl, string packageId, CancellationToken cancellationToken)
    {
        SourceRepository repository = Repository.Factory.GetCoreV3(nugetApiUrl);

        try
        {
            using SourceCacheContext cacheContext = new SourceCacheContext();

            FindPackageByIdResource? searchResource =
                await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

            if (searchResource is null)
                throw new InvalidOperationException($"The FindPackageById resource is unavailable for '{nugetApiUrl}'.");

            IEnumerable<NuGetVersion>? packageVersions = await searchResource.GetAllVersionsAsync(packageId,
                cacheContext,
                NullLogger.Instance, cancellationToken).ConfigureAwait(false);

            return packageVersions is not null && packageVersions.Any();
        }
        catch(ArgumentException)
        {
            return false;
        }
    }
}
