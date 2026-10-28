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

namespace NuVersionsLib.Abstractions;

/// <summary>
/// Provides methods for retrieving per-version deprecation metadata from a NuGet repository.
/// </summary>
public interface IPackageDeprecationService
{
    /// <summary>
    /// Reports the deprecation metadata the repository serves for each of the specified package
    /// versions, fetched from the registration catalog pages with unlisted versions included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verdict rule: a version maps to non-null metadata when the
    /// server serves it a deprecation object — reasons present or not, a reason-less object still
    /// counts as deprecated — and to <see langword="null"/> when the object is absent. A feed
    /// that serves versions but no deprecation fact therefore reads as not deprecated, exactly
    /// like nuget.org's absent case.
    /// </para>
    /// <para>
    /// Every requested version is present in the result, including duplicates collapsed to one
    /// entry, so the result covers exactly the version set the caller asks about.
    /// </para>
    /// </remarks>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to check.</param>
    /// <param name="packageVersions">The specific versions of the package to read deprecation metadata for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A dictionary mapping each requested version to its deprecation metadata, or <see langword="null"/> when not deprecated.</returns>
    Task<IDictionary<NuGetVersion, PackageDeprecationInfo?>> CheckPackageVersionsDeprecationAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, IList<NuGetVersion> packageVersions,
        CancellationToken cancellationToken);
}
