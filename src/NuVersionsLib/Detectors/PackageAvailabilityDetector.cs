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
                throw new InvalidOperationException(string.Format(Resources.Exceptions_NuGet_FindResources_FindByPackageId_Unavailable, nugetApiUrl));

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
