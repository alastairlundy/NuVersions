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
using NuVersionsCli.Helpers;

namespace NuVersionsCli;

/// <summary>
/// The <c>show</c> subcommand: a pure read that prints every version of a package with whether
/// the server lists or has already unlisted it, including stable versions.
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
/// Status=&lt;kebab-case&gt;» and one three-field NDJSON object), with a show-specific closed
/// status vocabulary: <c>listed</c> and <c>unlisted</c>. Exit codes: 0 success, 1 feed-side
/// failure, 2 the package does not exist on the server, 130 Ctrl-C.
/// </para>
/// </remarks>
[CliCommand(Name = "show", Parent = typeof(RootCommand),
    Description = "Show whether each version of a NuGet package is listed or unlisted.")]
public class ShowCommand
{
    private readonly IConfiguration _configuration;
    private readonly IPackageVersionService _packageVersionService;
    private readonly string? _serverUrlGeneric;

    public ShowCommand(IConfiguration configuration,
        IPackageVersionService packageVersionService)
    {
        _configuration = configuration;
        _packageVersionService = packageVersionService;

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

            bool json = OutputModeHelpers.IsJson(options.OutputMode);

            foreach (PackageVersionListingInfo info in versions.OrderBy(static v => v.PackageVersion))
            {
                await Console.Out.WriteLineAsync(DescribeListing(options.PackageId, info, json)).ConfigureAwait(true);
            }

            return ExitCodeHelpers.ExitSuccess;
        }
        catch (OperationCanceledException)
        {
            return ExitCodeHelpers.ExitInterrupted;
        }
        catch (Exception exception) when (ExitCodeHelpers.TryFromException(exception, out int exitCode))
        {
            // Package-not-found-on-server is a usage error; a NuGet.Protocol failure or an
            // unavailable resource is a feed-side problem. Both report the message on stderr.
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return exitCode;
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
    /// One line of the show output in the same shape the unlist command emits: a normalised
    /// version plus a kebab-case status member, rendered as NDJSON under <c>--output json</c>.
    /// </summary>
    private static string DescribeListing(string packageId, PackageVersionListingInfo info, bool json)
    {
        string status = info.IsListed ? "listed" : "unlisted";

        if (json)
        {
            return NdjsonHelpers.ToPackageVersionLine(packageId, info.PackageVersion, status);
        }

        return $"Version={info.PackageVersion.ToNormalizedString()} Status={status}";
    }
}
