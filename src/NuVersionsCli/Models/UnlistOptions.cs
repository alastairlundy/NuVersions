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
/// Resolved, validated CLI options. Pure logic only: no console access,
/// no service resolution, no <c>IConfiguration</c> or environment access.
/// </summary>
public sealed record UnlistOptions
{
    /// <summary>
    /// The default unlisting backend when none is supplied.
    /// </summary>
    public const string DefaultBackend = "http";

    /// <summary>
    /// The default output mode when none is supplied.
    /// </summary>
    public const string DefaultOutputMode = OutputModeHelpers.Text;

    public string PackageId { get; init; } = string.Empty;

    public IReadOnlyList<string> Versions { get; init; } = [];

    /// <summary>
    /// The version tokens that look like an unknown option. The argument tokeniser hands an
    /// unknown option to the greedy versions position, so it has to be rejected rather than
    /// dropped as a non-version string.
    /// </summary>
    public IReadOnlyList<string> UnknownOptions { get; init; } = [];

    public string? ApiKey { get; init; }

    public string ServerUrl { get; init; } = ConfigurationValueHelpers.DefaultServerUrl;

    public string Backend { get; init; } = DefaultBackend;

    public bool NonInteractive { get; init; }

    public bool UseStrictParsing { get; init; } = true;

    public bool UnlistAllVersions { get; init; }

    public bool DryRun { get; init; }

    public string OutputMode { get; init; } = DefaultOutputMode;

    /// <summary>
    /// Builds a fully-resolved <see cref="UnlistOptions"/> from bound values plus
    /// prefetched configuration strings. All inputs are plain values so the type
    /// stays pure (no configuration or environment access inside). The precedence
    /// chains themselves live in <see cref="ConfigurationValueHelpers"/>.
    /// </summary>
    /// <remarks>
    /// The version tokens are triaged here rather than in the command: a token that looks like an
    /// option is kept aside as an unknown option, and the rest are narrowed to the version
    /// strings. The narrowing applies only when the run was not asked to unlist every version,
    /// which is the only mode in which the supplied tokens are the versions themselves.
    /// </remarks>
    public static UnlistOptions Create(
        string? packageId,
        IEnumerable<string>? versions,
        string? apiKeyOption,
        string? apiKeyPrefixed,
        string? apiKeyGeneric,
        string? serverUrlOption,
        string? serverUrlPrefixed,
        string? serverUrlGeneric,
        string? backend,
        bool nonInteractive,
        bool useStrictParsing,
        bool unlistAllVersions,
        bool dryRun,
        string? outputMode)
    {
        string[] suppliedVersions = versions?.ToArray() ?? [];

        // A token that starts with a dash is an option the parser did not recognise, not a
        // version. It is never silently dropped as a non-version string.
        string[] unknownOptions =
        [
            .. suppliedVersions.Where(static version => version.StartsWith('-')),
        ];

        // Only version strings starting with a digit are considered; the rest are ignored.
        string[] requestedVersions = unlistAllVersions
            ? suppliedVersions
            :
            [
                .. suppliedVersions.Where(version => !string.IsNullOrWhiteSpace(version)
                                                      && char.IsDigit(version.Trim()[0])),
            ];

        return new UnlistOptions
        {
            PackageId = packageId ?? string.Empty,
            Versions = requestedVersions,
            UnknownOptions = unknownOptions,
            ApiKey = ConfigurationValueHelpers.ResolveApiKey(apiKeyOption, apiKeyPrefixed, apiKeyGeneric),
            ServerUrl = ConfigurationValueHelpers.ResolveServerUrl(serverUrlOption, serverUrlPrefixed, serverUrlGeneric),
            Backend = string.IsNullOrWhiteSpace(backend) ? DefaultBackend : backend,
            NonInteractive = nonInteractive,
            UseStrictParsing = useStrictParsing,
            UnlistAllVersions = unlistAllVersions,
            DryRun = dryRun,
            OutputMode = string.IsNullOrWhiteSpace(outputMode) ? DefaultOutputMode : outputMode,
        };
    }

    /// <summary>
    /// Validates the resolved options, returning one failure per violated rule.
    /// Dry-run mode skips the API-key requirement (delete requests are the only
    /// credential-bearing path); a real run requires it.
    /// </summary>
    public IReadOnlyList<OptionsValidationFailure> Validate()
    {
        List<OptionsValidationFailure> failures = [];

        // An unrecognized option is reported first: it is the most likely user mistake, and the
        // one the parser could not catch for us.
        foreach (string unknownOption in UnknownOptions)
        {
            failures.Add(new OptionsValidationFailure(
                OptionsValidationFailure.UnknownOptionField,
                string.Format(Resources.Errors_Input_UnrecognizedOption, unknownOption)));
        }

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

        if (!string.Equals(Backend, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Backend, "sdk", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new OptionsValidationFailure(
                nameof(Backend),
                "Invalid backend value. Valid values are: http, sdk."));
        }

        if (!UnlistAllVersions && Versions.Count == 0)
        {
            failures.Add(new OptionsValidationFailure(
                nameof(Versions),
                "At least one version string is required unless --unlist-all is set."));
        }

        if (!DryRun && string.IsNullOrEmpty(ApiKey))
        {
            failures.Add(new OptionsValidationFailure(
                nameof(ApiKey),
                "API key is required for a real run (dry-run mode skips this requirement)."));
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
