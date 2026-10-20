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

using System.Text.Json;
using CliInvoke.Core;
using NuVersionsLib.Helpers;

namespace NuVersionsLib.Detectors;

public class NetSdkPackageAvailabilityDetector : IPackageAvailabilityDetector
{
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    ///
    /// </summary>
    /// <param name="processInvoker"></param>
    public NetSdkPackageAvailabilityDetector(IProcessInvoker processInvoker)
    {
        _processInvoker = processInvoker;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="nugetApiUrl"></param>
    /// <param name="packageId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> CheckPackageExistsAsync(string nugetApiUrl, string packageId,
        CancellationToken cancellationToken)
    {
        ProcessConfiguration configuration = new(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
        {
            ArgumentList = ["package", "search", packageId, "--source", nugetApiUrl, "--format", "json", "--prerelease", "--exact-match"],
            OutputRedirection = true,
        };

        try
        {
            BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(configuration,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            PackageSearchResults? sdkSearchResults =
                JsonSerializer.Deserialize<PackageSearchResults>(result.StandardOutput, NetSdkPackageSearchJsonContext.Default.Options);

            if (sdkSearchResults is null || sdkSearchResults.SearchResults.Packages.Length == 0)
                return false;

            return sdkSearchResults.SearchResults.Packages.Any(p
                => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
