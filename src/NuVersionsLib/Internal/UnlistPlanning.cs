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

namespace NuVersionsLib.Internal;

/// <summary>
/// Turns requested package versions plus the server's listing metadata into a <see cref="UnlistPlan"/>.
/// </summary>
internal static class UnlistPlanning
{
    /// <summary>
    /// Partitions the requested versions by their current listing state.
    /// </summary>
    /// <remarks>
    /// A version that the server does not report is <em>not</em> treated as already unlisted. The two states are
    /// reported separately so that a mistyped or never-published version surfaces as a failure instead of a
    /// misleading success.
    /// </remarks>
    /// <param name="requestedVersions">The versions the caller asked to unlist. May contain duplicates.</param>
    /// <param name="listingInfoByVersion">Listing metadata keyed by version, as returned by
    /// <see cref="Abstractions.IPackageVersionService.CheckPackageVersionsListedAsync"/>.</param>
    internal static UnlistPlan Partition(IEnumerable<NuGetVersion> requestedVersions,
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfoByVersion)
    {
        ArgumentNullException.ThrowIfNull(requestedVersions);
        ArgumentNullException.ThrowIfNull(listingInfoByVersion);

        List<NuGetVersion> toUnlist = [];
        List<NuGetVersion> alreadyUnlisted = [];
        List<NuGetVersion> notOnServer = [];

        foreach (NuGetVersion version in requestedVersions.Distinct())
        {
            if (!listingInfoByVersion.TryGetValue(version, out PackageVersionListingInfo? listingInfo)
                || !listingInfo.PackageVersionExists)
            {
                notOnServer.Add(version);
            }
            else if (!listingInfo.IsListed)
            {
                alreadyUnlisted.Add(version);
            }
            else
            {
                toUnlist.Add(version);
            }
        }

        return new UnlistPlan(toUnlist, alreadyUnlisted, notOnServer);
    }
}
