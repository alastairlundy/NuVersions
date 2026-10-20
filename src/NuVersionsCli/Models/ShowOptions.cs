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

using NuVersionsCli.Helpers;

namespace NuVersionsCli.Models;

/// <summary>
/// Resolved, validated options for the show command. Pure logic only: no console access,
/// no service resolution, no <c>IConfiguration</c> or environment access.
/// </summary>
/// <remarks>
/// The shape mirrors <see cref="UnlistOptions"/>: the command resolves the options, validates
/// them, and reports every violated rule at once. The precedence chains themselves live in
/// <see cref="ConfigurationValueHelpers"/>.
/// </remarks>
public sealed record ShowOptions
{
    /// <summary>
    /// The default output mode when none is supplied.
    /// </summary>
    public const string DefaultOutputMode = OutputModeHelpers.Text;

    public string PackageId { get; init; } = string.Empty;

    public string ServerUrl { get; init; } = ConfigurationValueHelpers.DefaultServerUrl;

    public string OutputMode { get; init; } = DefaultOutputMode;

    /// <summary>
    /// Builds a fully-resolved <see cref="ShowOptions"/> from bound values plus prefetched
    /// configuration strings. All inputs are plain values so the type stays pure (no configuration
    /// or environment access inside).
    /// </summary>
    public static ShowOptions Create(
        string? packageId,
        string? serverUrlOption,
        string? serverUrlPrefixed,
        string? serverUrlGeneric,
        string? outputMode)
    {
        return new ShowOptions
        {
            PackageId = packageId ?? string.Empty,
            ServerUrl = ConfigurationValueHelpers.ResolveServerUrl(serverUrlOption, serverUrlPrefixed, serverUrlGeneric),
            OutputMode = string.IsNullOrWhiteSpace(outputMode) ? DefaultOutputMode : outputMode,
        };
    }

    /// <summary>
    /// Validates the resolved options, returning one failure per violated rule.
    /// </summary>
    public IReadOnlyList<OptionsValidationFailure> Validate()
    {
        List<OptionsValidationFailure> failures = [];

        if (string.IsNullOrWhiteSpace(PackageId))
        {
            failures.Add(new OptionsValidationFailure(
                nameof(PackageId),
                Resources.Failures_PackageId_Required));
        }

        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            failures.Add(new OptionsValidationFailure(
                nameof(ServerUrl),
                "Server URL must not be empty."));
        }

        if (!OutputModeHelpers.IsValid(OutputMode))
        {
            failures.Add(new OptionsValidationFailure(
                nameof(OutputMode),
                Resources.Failures_OutputModes_InvalidOutputMode));
        }

        return failures;
    }
}
