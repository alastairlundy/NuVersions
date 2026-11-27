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
/// The shared NDJSON line: exactly five fields in the fixed order package, version, status,
/// failureType, failureMessage for <c>unlist</c>, and the same line's first three fields plus a
/// fourth <c>deprecation</c> field for <c>show</c> — no envelope, no array wrapper, no fields
/// beyond those.
/// </summary>
/// <remarks>
/// The unlist and show commands render the same per-version object, so the serializer lives here
/// instead of being copied into each command. The shape is part of the output contract: unlist's
/// two failure fields are the explicit JSON <see langword="null"/> whenever the outcome carries no
/// failure data, and show's fourth field is the explicit JSON <see langword="null"/> on every
/// not-deprecated line, so every line of either command carries the same key set.
/// </remarks>
internal static class NdjsonHelpers
{
    /// <summary>
    /// Renders one per-version NDJSON object with the five-field unlist shape: <c>failureType</c>
    /// and <c>failureMessage</c> fourth and fifth after <c>status</c>, serialized through
    /// <see cref="NdjsonJsonContext"/> with camelCase keys, and the explicit JSON
    /// <see langword="null"/> on both when the outcome carries no failure data.
    /// </summary>
    /// <remarks>
    /// <paramref name="failureMessage"/> rides verbatim and is never truncated, at any length.
    /// </remarks>
    /// <param name="packageId">The package the version belongs to.</param>
    /// <param name="version">The version the line describes.</param>
    /// <param name="status">The already-rendered status member.</param>
    /// <param name="failureType">The already-rendered failure type, or <see langword="null"/> when the outcome carries none.</param>
    /// <param name="failureMessage">The verbatim failure detail, or <see langword="null"/> when the outcome carries none.</param>
    internal static string ToPackageVersionLine(string packageId, NuGetVersion version, string status,
        string? failureType, string? failureMessage)
    {
        return JsonSerializer.Serialize(
            new UnlistVersionRecord(packageId, version.ToNormalizedString(), status, failureType, failureMessage),
            NdjsonJsonContext.Default.UnlistVersionRecord);
    }

    /// <summary>
    /// Renders one per-version NDJSON object with show's four-field shape: <c>deprecation</c>
    /// fourth after <c>status</c>, serialized through <see cref="NdjsonJsonContext"/> with
    /// camelCase keys, and the explicit JSON <see langword="null"/> when the version is not
    /// deprecated.
    /// </summary>
    /// <param name="packageId">The package the version belongs to.</param>
    /// <param name="version">The version the line describes.</param>
    /// <param name="status">The already-rendered status member.</param>
    /// <param name="deprecation">The version's deprecation verdict, or <see langword="null"/> when not deprecated.</param>
    internal static string ToPackageVersionLine(string packageId, NuGetVersion version, string status,
        PackageDeprecationInfo? deprecation)
    {
        string prefix = ToVersionFieldsJson(packageId, version, status);
        string deprecationJson = deprecation is null
            ? "null"
            : JsonSerializer.Serialize(ToDeprecationRecord(deprecation), NdjsonJsonContext.Default.ShowDeprecationRecord);

        return $"{prefix},\"deprecation\":{deprecationJson}}}";
    }

    /// <summary>
    /// The line's first three fields as an open JSON object fragment — no closing brace — for the
    /// show overload to append its fourth field to.
    /// </summary>
    private static string ToVersionFieldsJson(string packageId, NuGetVersion version, string status)
    {
        string package = JsonSerializer.Serialize(packageId, NdjsonJsonContext.Default.String);
        string normalizedVersion = JsonSerializer.Serialize(version.ToNormalizedString(), NdjsonJsonContext.Default.String);
        string statusText = JsonSerializer.Serialize(status, NdjsonJsonContext.Default.String);

        return $"{{\"package\":{package},\"version\":{normalizedVersion},\"status\":{statusText}";
    }

    /// <summary>
    /// Maps the Lib's deprecation metadata onto the serialization record: reasons verbatim, the
    /// message as served, and the alternate package collapsed into its <c>id</c>/<c>range</c>
    /// object — present exactly when the server named an alternate package.
    /// </summary>
    private static ShowDeprecationRecord ToDeprecationRecord(PackageDeprecationInfo deprecation)
    {
        return new ShowDeprecationRecord(
            [.. deprecation.Reasons],
            deprecation.Message,
            deprecation.AlternatePackageId is null
                ? null
                : new ShowAlternatePackageRecord(deprecation.AlternatePackageId, deprecation.AlternatePackageRange));
    }
}
