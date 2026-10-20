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
/// The single renderer for options validation failures, shared by every command that validates
/// options.
/// </summary>
/// <remarks>
/// A failure names the option field it belongs to. The fields whose wording is already fixed keep
/// their localized text; every other failure renders as <c>Error: {message}</c>, one line per
/// failure on stderr.
/// </remarks>
internal static class ValidationFailureHelpers
{
    /// <summary>
    /// Renders one validation failure as the single stderr line that reports it.
    /// </summary>
    internal static string Describe(OptionsValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Field switch
        {
            nameof(UnlistOptions.ApiKey) => Resources.Exceptions_Configuration_NugetApiKey,
            nameof(UnlistOptions.Versions) => Resources.Errors_Input_NoVersionStrings,
            _ => $"Error: {failure.Message}",
        };
    }
}
