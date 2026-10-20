/*
    NuVersions
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using NuGet.Versioning;

namespace NuVersionsCli.Helpers;

/// <summary>
/// The shared NDJSON line: exactly three fields in the fixed order package, version, status — no
/// envelope, no array wrapper, no additional fields.
/// </summary>
/// <remarks>
/// The unlist and show commands render the same per-version object, so the serializer lives here
/// instead of being copied into each command. The shape is part of the output contract.
/// </remarks>
internal static class NdjsonHelpers
{
    /// <summary>
    /// Renders one per-version NDJSON object.
    /// </summary>
    /// <param name="packageId">The package the version belongs to.</param>
    /// <param name="version">The version the line describes.</param>
    /// <param name="status">The already-rendered status member.</param>
    internal static string ToPackageVersionLine(string packageId, NuGetVersion version, string status)
    {
        string package = JsonSerializer.Serialize(packageId);
        string normalizedVersion = JsonSerializer.Serialize(version.ToNormalizedString());
        string statusText = JsonSerializer.Serialize(status);

        return $"{{\"package\":{package},\"version\":{normalizedVersion},\"status\":{statusText}}}";
    }
}
