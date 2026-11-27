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

using NuVersionsLib.Models;

namespace NuVersionsCli.Helpers;

/// <summary>
/// The CLI's submitted-credential redaction policy: an exact, value-based replacement, not a
/// secret-shaped-text scanner.
/// </summary>
/// <remarks>
/// <para>
/// A server may echo the submitted key back in a failure body. Only that submitted value is
/// secret; words such as <c>API key</c>, <c>NUGET_API_KEY</c>, or <c>X-NuGet-ApiKey</c> name a
/// credential and stay visible unless they are themselves the value the caller submitted. The
/// replacement is therefore an ordinal match on the resolved key, applied before truncation and
/// JSON escaping so both output streams see the same redacted detail.
/// </para>
/// <para>
/// <c>--verbose</c> lifts truncation caps only. It never lifts this redaction.
/// </para>
/// </remarks>
internal static class ApiKeyRedactionHelpers
{
    /// <summary>The fixed marker that replaces every occurrence of the submitted key.</summary>
    internal const string RedactedMarker = "[redacted]";

    /// <summary>
    /// Returns <paramref name="outcome"/> with every occurrence of the submitted key in its
    /// Detail replaced by <see cref="RedactedMarker"/>.
    /// </summary>
    /// <param name="outcome">The outcome to make safe to render.</param>
    /// <param name="apiKey">The submitted key, or <see langword="null"/> when there is none.</param>
    /// <returns>The outcome with its Detail redacted, or the unchanged outcome when nothing needs redacting.</returns>
    internal static PackageVersionOutcome RedactOutcome(PackageVersionOutcome outcome, string? apiKey)
    {
        if (outcome.Detail is not { Length: > 0 } detail || string.IsNullOrEmpty(apiKey))
            return outcome;

        return outcome with { Detail = RedactDetail(detail, apiKey) };
    }

    /// <summary>
    /// Replaces every occurrence of <paramref name="apiKey"/> in <paramref name="detail"/>
    /// with <see cref="RedactedMarker"/>.
    /// </summary>
    /// <param name="detail">The verbatim detail to make safe to render.</param>
    /// <param name="apiKey">The submitted key, or <see langword="null"/> when there is none.</param>
    /// <returns>The redacted detail, or the unchanged detail when nothing needs redacting.</returns>
    internal static string? RedactDetail(string? detail, string? apiKey)
    {
        if (string.IsNullOrEmpty(detail) || string.IsNullOrEmpty(apiKey))
            return detail;

        return detail.Replace(apiKey, RedactedMarker, StringComparison.Ordinal);
    }
}
