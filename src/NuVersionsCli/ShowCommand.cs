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

using System.ComponentModel;
using NuGet.Versioning;
using NuVersionsCli.Helpers;

namespace NuVersionsCli;

/// <summary>
/// The <c>show</c> subcommand: a pure read that prints every version of a package with whether
/// the server lists or has already unlisted it, including stable versions, plus each version's
/// deprecation verdict.
/// </summary>
/// <remarks>
/// <para>
/// Show sends no credential: the composition's read paths accept a non-empty API key string but
/// never transmit one, exactly as the dry run does, so no <c>--api-key</c> option exists here.
/// Reads report per-version listing state from the registration metadata, so a server that does
/// not expose unlisted entries would show its unlisted versions as listed — nuget.org does expose
/// them.
/// </para>
/// <para>
/// The line shape mirrors <see cref="UnlistCommand"/> («Version=&lt;normalized&gt;
/// Status=&lt;kebab-case&gt;» with a show-specific closed status vocabulary: <c>listed</c> and
/// <c>unlisted</c>), extended only where deprecation exists: a deprecated version gains
/// a trailing «Deprecation=&lt;reasons&gt;» segment («Deprecation=unspecified» when the served
/// object carries no reasons) while every not-deprecated line stays byte-identical, and under
/// <c>--output json</c> the per-version object gains <c>deprecation</c> as its fourth field with
/// the explicit JSON <see langword="null"/> on not-deprecated lines. The read is two-pass — the
/// version enumeration, then deprecation metadata for exactly those versions — and both passes
/// collapse feed-side failures onto the same shape: one stderr diagnostic, exit 1, no version
/// lines. When a deprecated version carries a custom message, one localized hint line goes to
/// stderr alone (never stdout) telling the user to rerun with <c>--output json</c>. Exit codes:
/// 0 success, 1 feed-side failure, 2 the package does not exist on the server, 130 Ctrl-C.
/// </para>
/// </remarks>
[CliCommand(Name = "show", Parent = typeof(RootCommand),
    Description = "Show whether each version of a NuGet package is listed or unlisted.")]
public class ShowCommand
{
    private readonly IConfiguration _configuration;
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageDeprecationService _packageDeprecationService;
    private readonly string? _serverUrlGeneric;

    public ShowCommand(IConfiguration configuration,
        IPackageVersionService packageVersionService,
        IPackageDeprecationService packageDeprecationService)
    {
        _configuration = configuration;
        _packageVersionService = packageVersionService;
        _packageDeprecationService = packageDeprecationService;

        // Prefetched as a plain value so the command body holds no environment-variable lookups;
        // the precedence chain itself lives in ConfigurationValueHelpers.ResolveServerUrl.
        _serverUrlGeneric = Environment.GetEnvironmentVariable(
            ConfigurationValueHelpers.GenericServerUrlEnvironmentVariable);
    }

    [CliOption(Name = "--package-id", Required = true,
        Arity = CliArgumentArity.ExactlyOne)]
    public string PackageId { get; set; } = "";

    [CliOption(Name = "--server-url", Required = false)]
    public string? ServerUrl { get; set; }

    /// <summary>
    /// The output format: "text" (default) or "json" for newline-delimited JSON on stdout, one
    /// object per version.
    /// </summary>
    [CliOption(Name = "--output", Required = false)]
    [DefaultValue(ShowOptions.DefaultOutputMode)]
    public string OutputMode { get; set; } = ShowOptions.DefaultOutputMode;

    public async Task<int> RunAsync()
    {
        ShowOptions options = ResolveOptions();

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        if (failures.Count > 0)
        {
            foreach (OptionsValidationFailure failure in failures)
            {
                await Console.Error.WriteLineAsync(ValidationFailureHelpers.Describe(failure)).ConfigureAwait(true);
            }

            return ExitCodeHelpers.ExitUsage;
        }

        using CancellationTokenSource cancellationSource = new();
        using IDisposable ctrlC = CtrlCHelpers.Register(cancellationSource);

        try
        {
            List<PackageVersionListingInfo> versions = [];

            await foreach (PackageVersionListingInfo info in _packageVersionService.EnumerateAllPackageVersionsAsync(
                               options.ServerUrl, "show", options.PackageId, cancellationSource.Token).ConfigureAwait(true))
            {
                versions.Add(info);
            }

            IDictionary<NuGetVersion, PackageDeprecationInfo?> deprecationByVersion =
                await ReadDeprecationAsync(options, versions, cancellationSource.Token).ConfigureAwait(true);

            await WriteVersionsAsync(options, versions, deprecationByVersion).ConfigureAwait(true);

            return ExitCodeHelpers.ExitSuccess;
        }
        catch (OperationCanceledException)
        {
            return ExitCodeHelpers.ExitInterrupted;
        }
        catch (Exception exception) when (ExitCodeHelpers.TryFromException(exception, out int exitCode))
        {
            // Package-not-found-on-server is a usage error; a NuGet.Protocol failure, an HTTP
            // transport failure, or an unavailable resource is a feed-side problem. Both report
            // the message on stderr.
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return exitCode;
        }
    }

    /// <summary>
    /// The second pass of the two-pass read: deprecation metadata for exactly the versions the
    /// enumeration returned, fetched before any line renders so a feed-side failure collapses onto
    /// the same catch shape as the enumeration itself — one stderr diagnostic, exit 1, no version
    /// lines, no per-version degrade token and no new exit code.
    /// </summary>
    private async Task<IDictionary<NuGetVersion, PackageDeprecationInfo?>> ReadDeprecationAsync(
        ShowOptions options, List<PackageVersionListingInfo> versions, CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
        {
            return new Dictionary<NuGetVersion, PackageDeprecationInfo?>();
        }

        return await _packageDeprecationService.CheckPackageVersionsDeprecationAsync(
            options.ServerUrl, "show", options.PackageId,
            [.. versions.Select(static version => version.PackageVersion)],
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Writes every version line in ascending version order, then — text mode only — the single
    /// custom-message awareness hint on stderr: exactly one localized line when any deprecated
    /// version carries a custom message, never on stdout, never in JSON mode, where consumers
    /// already carry the full deprecation record.
    /// </summary>
    private static async Task WriteVersionsAsync(ShowOptions options,
        IReadOnlyList<PackageVersionListingInfo> versions,
        IDictionary<NuGetVersion, PackageDeprecationInfo?> deprecationByVersion)
    {
        bool json = OutputModeHelpers.IsJson(options.OutputMode);
        bool customMessageSeen = false;

        foreach (PackageVersionListingInfo info in versions.OrderBy(static v => v.PackageVersion))
        {
            deprecationByVersion.TryGetValue(info.PackageVersion, out PackageDeprecationInfo? deprecation);
            customMessageSeen |= !string.IsNullOrEmpty(deprecation?.Message);

            await Console.Out.WriteLineAsync(DescribeListing(options.PackageId, info, json, deprecation)).ConfigureAwait(true);
        }

        if (!json && customMessageSeen)
        {
            await Console.Error.WriteLineAsync(Resources.Hints_Show_FullDeprecationJson).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Builds the resolved options from the bound values plus the prefetched configuration strings.
    /// Precedence resolution and validation belong to <see cref="ShowOptions"/>.
    /// </summary>
    private ShowOptions ResolveOptions()
    {
        return ShowOptions.Create(
            PackageId, ServerUrl,
            _configuration[ConfigurationValueHelpers.ServerUrlConfigurationKey],
            _serverUrlGeneric, OutputMode);
    }

    /// <summary>
    /// One line of the show output in the same shape the unlist command emits, plus the
    /// deprecation verdict where it exists: a normalised version, a kebab-case status member, and
    /// for a deprecated version only a trailing «Deprecation=&lt;reasons&gt;» segment — rendered
    /// as NDJSON (fourth <c>deprecation</c> field, explicit <see langword="null"/> when absent)
    /// under <c>--output json</c>.
    /// </summary>
    private static string DescribeListing(string packageId, PackageVersionListingInfo info, bool json,
        PackageDeprecationInfo? deprecation)
    {
        string status = info.IsListed ? "listed" : "unlisted";

        if (json)
        {
            return NdjsonHelpers.ToPackageVersionLine(packageId, info.PackageVersion, status, deprecation);
        }

        string line = $"Version={info.PackageVersion.ToNormalizedString()} Status={status}";

        if (deprecation is null)
        {
            // Byte-identical to the pre-deprecation formatter: no segment, no variant.
            return line;
        }

        return $"{line} Deprecation={RenderReasons(deprecation)}";
    }

    /// <summary>
    /// The «Deprecation=» segment's value: the server's reason words verbatim and comma-joined in
    /// served order — no translation, no renaming — or the <c>unspecified</c> render token
    /// when a deprecated version's deprecation object carries no reasons at all.
    /// <c>unspecified</c> is a NuVersions render token, not a server word, and is never localized.
    /// </summary>
    private static string RenderReasons(PackageDeprecationInfo deprecation)
    {
        return deprecation.Reasons.Count == 0
            ? "unspecified"
            : string.Join(",", deprecation.Reasons);
    }
}
