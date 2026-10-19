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
    public const string DefaultOutputMode = "text";

    public string PackageId { get; init; } = string.Empty;

    public IReadOnlyList<string> Versions { get; init; } = [];

    public string? ApiKey { get; init; }

    public string ServerUrl { get; init; } = ConfigurationValueHelpers.DefaultServerUrl;

    public string Backend { get; init; } = DefaultBackend;

    public bool NonInteractive { get; init; }

    public bool IncludeZeroMajor { get; init; }

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
        bool includeZeroMajor,
        bool useStrictParsing,
        bool unlistAllVersions,
        bool dryRun,
        string? outputMode)
    {
        return new UnlistOptions
        {
            PackageId = packageId ?? string.Empty,
            Versions = versions?.ToArray() ?? [],
            ApiKey = ConfigurationValueHelpers.ResolveApiKey(apiKeyOption, apiKeyPrefixed, apiKeyGeneric),
            ServerUrl = ConfigurationValueHelpers.ResolveServerUrl(serverUrlOption, serverUrlPrefixed, serverUrlGeneric),
            Backend = string.IsNullOrWhiteSpace(backend) ? DefaultBackend : backend,
            NonInteractive = nonInteractive,
            IncludeZeroMajor = includeZeroMajor,
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
    public IReadOnlyList<UnlistOptionsValidationFailure> Validate()
    {
        List<UnlistOptionsValidationFailure> failures = [];

        if (string.IsNullOrWhiteSpace(PackageId))
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(PackageId),
                "Package id is required."));
        }

        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(ServerUrl),
                "Server URL must not be empty."));
        }

        if (!string.Equals(Backend, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Backend, "sdk", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(Backend),
                "Invalid backend value. Valid values are: http, sdk."));
        }

        if (!UnlistAllVersions && Versions.Count == 0)
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(Versions),
                "At least one version string is required unless --unlist-all is set."));
        }

        if (!DryRun && string.IsNullOrEmpty(ApiKey))
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(ApiKey),
                "API key is required for a real run (dry-run mode skips this requirement)."));
        }

        if (!string.Equals(OutputMode, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(OutputMode, "json", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new UnlistOptionsValidationFailure(
                nameof(OutputMode),
                "Invalid output mode. Valid values are: text, json."));
        }

        return failures;
    }
}
