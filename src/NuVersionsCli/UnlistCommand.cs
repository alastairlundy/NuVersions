/*
    NuVersions - Delist (unlist) NuGet package versions from a NuGet server
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
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using NuVersionsCli.Helpers;
using NuVersionsLib.Detectors.Abstractions;
// ReSharper disable ArgumentsStyleOther

// ReSharper disable RedundantDefaultMemberInitializer

namespace NuVersionsCli;

/// <summary>
/// The unlisting command: the CLI's single presentation and failure boundary.
/// </summary>
/// <remarks>
/// <para>
/// DotMake binding attributes stay on this class and feed <see cref="UnlistOptions"/>; the body consumes
/// the resolved options and never resolves precedence itself. No options object crosses the seam:
/// the service layer keeps its explicit parameters.
/// </para>
/// <para>
/// Results, summaries, and the dry-run plan print to stdout; every error and diagnostic prints to stderr,
/// and the API key is never written to any stream. The process exits only with the documented
/// codes 0, 1, 2, 3, 4, or 130; anything unexpected is left unhandled so it crashes with its stack
/// trace.
/// </para>
/// </remarks>
[CliCommand(Name = "unlist", Parent = typeof(RootCommand),
    Description = "Unlist specified or all pre-release versions of a NuGet package.")]
public class UnlistCommand
{
    private readonly IConfiguration _configuration;
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _apiKeyGeneric;
    private readonly string? _serverUrlGeneric;

    public UnlistCommand(IConfiguration configuration,
        IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector  packageAvailabilityDetector,
        IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
        _serviceProvider = serviceProvider;

        // Prefetched as plain values so the command body holds no environment-variable lookups; the
        // precedence chain itself lives in ConfigurationValueHelpers.
        _apiKeyGeneric = Environment.GetEnvironmentVariable(
            ConfigurationValueHelpers.GenericApiKeyEnvironmentVariable);
        _serverUrlGeneric = Environment.GetEnvironmentVariable(
            ConfigurationValueHelpers.GenericServerUrlEnvironmentVariable);
    }

    // Initialized by DotMake option/argument binding after construction.
    [CliOption(Name = "--package-id", Required = true,
        Arity = CliArgumentArity.ExactlyOne)]
    public string PackageId { get; set; } = string.Empty;

    [CliOption(Name = "--unlist-all")]
    // ReSharper disable once RedundantDefaultMemberInitializer
    public bool UnlistAllVersions { get; set; } = false;

    [CliOption(Name = "--use-strict-parsing")]
    public bool UseStrictParsing { get; set; } = true;

    /// <summary>
    /// The pre-release versions to unlist. Optional when <see cref="UnlistAllVersions"/> is set.
    /// </summary>
    [CliArgument(Name = "versions", Required = false)]
    public string[] Versions { get; set; } = [];

    [CliOption(Name = "--api-key", Required = false)]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [CliOption(Name = "--non-interactive", Required = false)]
    [DefaultValue(false)]
    public bool NonInteractive { get; set; } = false;

    [CliOption(Name = "--server-url", Required = false)]
    public string? ServerUrl { get; set; }

    /// <summary>
    /// The backend to use for unlisting: "http" (default) or "sdk".
    /// </summary>
    [CliOption(Name = "--backend", Required = false)]
    public string Backend { get; set; } = "http";

    /// <summary>
    /// Prints the unlist plan without sending any unlist request. No API key is required in
    /// this mode, and it exits only with 0, 1, or 2.
    /// </summary>
    [CliOption(Name = "--dry-run", Required = false)]
    [DefaultValue(false)]
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// The output format: "text" (default) or "json" for newline-delimited JSON on stdout, one object
    /// per requested version.
    /// </summary>
    [CliOption(Name = "--output", Required = false)]
    [DefaultValue(UnlistOptions.DefaultOutputMode)]
    public string OutputMode { get; set; } = UnlistOptions.DefaultOutputMode;

    public async Task<int> RunAsync()
    {
        UnlistOptions options = ResolveOptions();

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
            // Resolving the keyed service here proves the composition-root wiring before any
            // work starts, for dry runs and real runs alike.
            IPackageUnlistService unlistService = GetUnlistService(options.Backend);

            return options.DryRun
                ? await RunDryRunAsync(options, cancellationSource.Token).ConfigureAwait(true)
                : await RunUnlistAsync(options, unlistService, cancellationSource.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C cancels the token above; a cancelled run exits 130.
            return ExitCodeHelpers.ExitInterrupted;
        }
        catch (Exception exception) when (ExitCodeHelpers.TryFromException(exception, out int exitCode))
        {
            // Package-not-found-on-server (thrown by the composing services) and invalid version
            // strings under strict parsing are usage errors; a NuGet.Protocol failure or an
            // unavailable resource is a feed-side problem. Both report the message on stderr.
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return exitCode;
        }
    }

    /// <summary>
    /// Builds the resolved options from the bound values plus the prefetched configuration strings.
    /// Precedence resolution, version-token triage and mode-aware validation belong to
    /// <see cref="UnlistOptions"/>.
    /// </summary>
    private UnlistOptions ResolveOptions()
    {
        return UnlistOptions.Create(
            PackageId, Versions,
            ApiKey, _configuration[ConfigurationValueHelpers.ApiKeyConfigurationKey],
            _apiKeyGeneric, ServerUrl,
            _configuration[ConfigurationValueHelpers.ServerUrlConfigurationKey],
            _serverUrlGeneric, Backend, NonInteractive,
            UseStrictParsing, UnlistAllVersions,
            DryRun, OutputMode);
    }

    /// <summary>
    /// Resolves the composing service for the selected backend. Validation has already proven the
    /// backend is one of the two registered keys, so the keys here stay exactly "http" and "sdk".
    /// </summary>
    private IPackageUnlistService GetUnlistService(string backend)
    {
        string serviceKey = string.Equals(backend, "sdk", StringComparison.OrdinalIgnoreCase)
            ? "sdk" : UnlistOptions.DefaultBackend;

        return _serviceProvider.GetRequiredKeyedService<IPackageUnlistService>(serviceKey);
    }

    /// <summary>
    /// Sends the unlist requests through the composing service, renders every outcome, and returns the
    /// merged exit code.
    /// </summary>
    private static async Task<int> RunUnlistAsync(UnlistOptions options, IPackageUnlistService unlistService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.ApiKey))
            throw new ArgumentNullException(paramName: nameof(options));

        IAsyncEnumerable<PackageVersionOutcome> results;

        if (options.UnlistAllVersions)
        {
            results = unlistService.RequestPackageUnlistingAsync(options.ServerUrl, options.ApiKey,
                options.PackageId, cancellationToken);
        }
        else
        {
            IList<NuGetVersion> requestedVersions =
                ParseVersions([.. options.Versions], options.UseStrictParsing);

            results = unlistService.RequestPackageUnlistingAsync(options.ServerUrl, options.ApiKey,
                options.PackageId, requestedVersions, cancellationToken);
        }

        List<PackageVersionOutcome> outcomes = [];
        int exitCode = ExitCodeHelpers.ExitSuccess;

        await foreach (PackageVersionOutcome outcome in results.WithCancellation(cancellationToken).ConfigureAwait(true))
        {
            outcomes.Add(outcome);
            exitCode = ExitCodeHelpers.MergeExitCode(exitCode, ExitCodeHelpers.ToExitCode(outcome.Status));
        }

        await WriteResultsAsync(options, outcomes).ConfigureAwait(true);

        return exitCode;
    }

    /// <summary>
    /// Resolves and prints the unlist plan without sending any unlist request, then exits bucket-aware:
    /// 0 only when every requested version exists on the server, 1 when any is absent, 2 when the
    /// package itself does not exist. Dry runs never emit 3 or 4.
    /// </summary>
    private async Task<int> RunDryRunAsync(UnlistOptions options, CancellationToken cancellationToken)
    {
        // Network reads still happen. The read paths require a non-empty API key string but
        // never send it, so a keyless dry-run passes a placeholder; no key is ever written to a stream.
        string readApiKey = options.ApiKey ?? "dry-run";

        bool packageExists = await _packageAvailabilityDetector.CheckPackageExistsAsync(options.ServerUrl,
            options.PackageId, cancellationToken).ConfigureAwait(true);

        if (!packageExists)
        {
            await Console.Error.WriteLineAsync(
                    $"Package '{options.PackageId}' does not exist on NuGet server '{options.ServerUrl}'.")
                .ConfigureAwait(true);
            return ExitCodeHelpers.ExitUsage;
        }

        IList<NuGetVersion> requestedVersions = options.UnlistAllVersions
            ? await _packageVersionService.GetPrereleasePackageVersionsAsync(options.ServerUrl, readApiKey,
                options.PackageId, cancellationToken).ConfigureAwait(true)
            : ParseVersions([.. options.Versions], options.UseStrictParsing);

        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo =
            await _packageVersionService.CheckPackageVersionsListedAsync(options.ServerUrl, readApiKey,
                options.PackageId, includePreReleaseVersions: true, requestedVersions, cancellationToken).ConfigureAwait(true);

        List<DryRunPlanLine> plan = [];

        foreach (NuGetVersion version in requestedVersions.Distinct())
        {
            var bucket = GetDryRunBucket(listingInfo, version);

            plan.Add(new DryRunPlanLine(version, bucket));
        }

        if (IsJsonOutput(options))
        {
            foreach (DryRunPlanLine line in plan)
            {
                await Console.Out.WriteLineAsync(NdjsonHelpers.ToPackageVersionLine(options.PackageId, line.Version,
                    ToBucketStatus(line.Bucket).ToKebabCase())).ConfigureAwait(true);
            }
        }
        else
        {
            foreach (DryRunPlanLine line in plan)
            {
                await Console.Out.WriteLineAsync(
                    $"Version={line.Version.ToNormalizedString()} Bucket={DescribeBucket(line.Bucket)}").ConfigureAwait(true);
            }
        }

        return plan.Exists(line => line.Bucket == DryRunBucket.NotOnServer) ?
            ExitCodeHelpers.ExitFailure : ExitCodeHelpers.ExitSuccess;
    }

    private static DryRunBucket GetDryRunBucket(IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo, NuGetVersion version)
    {
        // The same partition rule the composing services apply, resolved from read-only listing
        // metadata: absent from the server, present but unlisted, or listed and to be unlisted.
        DryRunBucket bucket;

        if (!listingInfo.TryGetValue(version, out PackageVersionListingInfo? listing)
            || !listing.PackageVersionExists)
        {
            bucket = DryRunBucket.NotOnServer;
        }
        else
        {
            bucket = listing.IsListed ? DryRunBucket.WouldUnlist : DryRunBucket.AlreadyUnlisted;
        }

        return bucket;
    }

    /// <summary>
    /// Renders every outcome to stdout: JSON lines, bare non-interactive lines, or grouped human
    /// output.
    /// </summary>
    private static async Task WriteResultsAsync(UnlistOptions options,
        IReadOnlyList<PackageVersionOutcome> outcomes)
    {
        if (IsJsonOutput(options))
        {
            foreach (PackageVersionOutcome outcome in outcomes)
            {
                await Console.Out.WriteLineAsync(
                    NdjsonHelpers.ToPackageVersionLine(outcome.PackageId, outcome.Version, outcome.Status.ToKebabCase())).ConfigureAwait(true);
            }

            return;
        }

        if (options.NonInteractive)
        {
            foreach (PackageVersionOutcome outcome in outcomes)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome)).ConfigureAwait(true);
            }

            return;
        }

        List<PackageVersionOutcome> unlisted = [.. outcomes.Where(IsUnlistedOutcome)];
        List<PackageVersionOutcome> notUnlisted = [.. outcomes.Where(outcome => !IsUnlistedOutcome(outcome))];

        if (unlisted.Count > 0)
        {
            await Console.Out.WriteLineAsync($"Versions Unlisted for Package: {options.PackageId}").ConfigureAwait(true);

            foreach (PackageVersionOutcome outcome in unlisted)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome)).ConfigureAwait(true);
            }
        }

        if (notUnlisted.Count > 0)
        {
            await Console.Out.WriteLineAsync(
                $"The following versions of {options.PackageId} could not be unlisted:").ConfigureAwait(true);

            foreach (PackageVersionOutcome outcome in notUnlisted)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome)).ConfigureAwait(true);
            }
        }
    }

    private static bool IsUnlistedOutcome(PackageVersionOutcome outcome) =>
        outcome.Status is PackageVersionStatus.Unlisted or PackageVersionStatus.AlreadyUnlisted;

    /// <summary>
    /// The machine line shared by the human and non-interactive text formats.
    /// </summary>
    private static string DescribeOutcome(PackageVersionOutcome outcome) =>
        $"Version={outcome.Version.ToNormalizedString()} Status={outcome.Status.ToKebabCase()}";

    private static bool IsJsonOutput(UnlistOptions options) =>
        OutputModeHelpers.IsJson(options.OutputMode);

    private static string DescribeBucket(DryRunBucket bucket) => bucket switch
    {
        DryRunBucket.WouldUnlist => "would-unlist",
        DryRunBucket.AlreadyUnlisted => "already-unlisted",
        DryRunBucket.NotOnServer => "not-on-server",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket,
            string.Format(Resources.Exceptions_DryRunBuckets_Unhandled, nameof(DryRunBucket)))
    };

    /// <summary>
    /// The JSON rendering of a bucket uses only the closed status vocabulary: a version a dry run
    /// would unlist is an unlist attempt that was never made.
    /// </summary>
    private static PackageVersionStatus ToBucketStatus(DryRunBucket bucket) => bucket switch
    {
        DryRunBucket.WouldUnlist => PackageVersionStatus.NotAttempted,
        DryRunBucket.AlreadyUnlisted => PackageVersionStatus.AlreadyUnlisted,
        DryRunBucket.NotOnServer => PackageVersionStatus.NotOnServer,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket,
            string.Format(Resources.Exceptions_BucketStatus_Unhandled, nameof(DryRunBucket)))
    };

    internal static IList<NuGetVersion> ParseVersions(string[] versions, bool throwOnError)
    {
        List<NuGetVersion> output = new(capacity: versions.Length);

        foreach (string versionString in versions)
        {
            bool success = NuGetVersion.TryParse(versionString, out NuGetVersion? version);

            if (success && version is not null)
            {
                output.Add(version);
            }
            else
            {
                if(throwOnError)
                    throw new ArgumentException(string.Format(Resources.Exceptions_Versions_InvalidVersionString, versionString),
                        nameof(versions));
            }
        }

        return [.. output.Distinct()];
    }
}
