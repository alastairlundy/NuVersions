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

namespace NuVersionsCli.Helpers;

/// <summary>
/// The CLI's output modes: the closed vocabulary of formats a command can render, and the checks
/// that decide which one a resolved value names.
/// </summary>
/// <remarks>
/// Both commands accept the same two modes and default to <see cref="Text"/>. The values are the
/// wire spelling of the <c>--output</c> option, so they live here instead of being repeated as
/// literals in each command and each options type.
/// </remarks>
internal static class OutputModeHelpers
{
    /// <summary>The human-readable line-per-version format, and the default.</summary>
    internal const string Text = "text";

    /// <summary>The newline-delimited JSON format: one three-field object per version.</summary>
    internal const string Json = "json";

    /// <summary>
    /// Whether the resolved mode is the JSON one. The comparison is case-insensitive, so
    /// <c>--output JSON</c> selects the same format as <c>--output json</c>.
    /// </summary>
    internal static bool IsJson(string? outputMode) =>
        string.Equals(outputMode, Json, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the resolved mode names one of the two supported formats. Anything else is a
    /// validation failure.
    /// </summary>
    internal static bool IsValid(string? outputMode) =>
        IsJson(outputMode) || string.Equals(outputMode, Text, StringComparison.OrdinalIgnoreCase);
}
