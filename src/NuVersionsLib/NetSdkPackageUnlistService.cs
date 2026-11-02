/*
    NuVersionsLib
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.RegularExpressions;
using CliInvoke.Core;
using NuVersionsLib.Internal;

namespace NuVersionsLib;

/// <summary>
/// Unlists versions by running <c>dotnet nuget delete</c> for each request.
/// </summary>
/// <remarks>
/// <para>
/// The API key is passed to <c>dotnet nuget delete</c> through the child process's
/// <c>NUGET_API_KEY</c> environment variable, not the command line: the NuGet CLI reads that
/// variable when <c>--api-key</c> is omitted, and a command-line key would be visible in
/// process listings.
/// </para>
/// <para>
/// Every shared run rule (argument validation, package-existence check, listed-state check,
/// partition, bucket yields, fail-fast rate-limit loop) lives once in <see cref="UnlistRun"/>.
/// </para>
/// <para>
/// Detection confidence: <see cref="PackageVersionStatus.RateLimited"/> is best-effort here,
/// detected by non-zero exit plus a stderr rate-limit pattern, and
/// <see cref="PackageVersionStatus.AlreadyUnlisted"/> is best-effort here,
/// detected by process output indicating the version is not listed.
/// Server and SDK wording varies, so confidence is limited; runs that cannot be
/// confidently distinguished are classified as <see cref="PackageVersionStatus.Failed"/>.
/// </para>
/// </remarks>
public partial class NetSdkPackageUnlistService : IPackageUnlistService
{
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// Creates a service that sends unlist requests through <c>dotnet nuget delete</c>.
    /// </summary>
    /// <param name="packageVersionService">Service used to enumerate and check package versions.</param>
    /// <param name="packageAvailabilityDetector">Detector used to verify the package exists on the server.</param>
    /// <param name="processInvoker">Invoker used to run the <c>dotnet nuget delete</c> process for each unlist request.</param>
    public NetSdkPackageUnlistService(IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        IProcessInvoker processInvoker)
    {
        ArgumentNullException.ThrowIfNull(packageVersionService);
        ArgumentNullException.ThrowIfNull(packageAvailabilityDetector);
        ArgumentNullException.ThrowIfNull(processInvoker);

        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
        _processInvoker = processInvoker;
    }

    /// <summary>
    /// Asynchronously unlists all prerelease versions of a NuGet package based on the provided API credentials and package ID.
    /// Stable versions with Major == 0 count as prerelease and are unlisted alongside them.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint.</param>
    /// <param name="nugetApiKey">The API key for authentication with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package(s) to be unlisted.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageUnlistingAsync(
        string nugetApiUrl, string nugetApiKey,
        string packageId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<PackageVersionOutcome> results = UnlistRun.ExecuteAsync(nugetApiUrl, nugetApiKey,
            packageId, UnlistVersionAsync, _packageVersionService,
            _packageAvailabilityDetector, cancellationToken);

        await foreach (PackageVersionOutcome outcome in results.ConfigureAwait(false))
        {
            yield return outcome;
        }
    }

    /// <summary>
    /// Asynchronously unlists a list of versions of a NuGet package based on the provided API credentials and package ID.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint.</param>
    /// <param name="nugetApiKey">The API key for authentication with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package(s) to be unlisted.</param>
    /// <param name="versions">The package versions to unlist.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    /// <remarks>
    /// <para>
    /// Already-unlisted and not-on-server versions are reported from the plan's buckets; only
    /// <c>plan.ToUnlist</c> versions are dispatched, so every requested version yields exactly one outcome.
    /// </para>
    /// <para>
    /// Fail-fast: when a dispatch reports <see cref="PackageVersionStatus.RateLimited"/>, no further
    /// dispatches are made and every remaining to-unlist version is reported as
    /// <see cref="PackageVersionStatus.NotAttempted"/> rather than <see cref="PackageVersionStatus.Failed"/>.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageUnlistingAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, IList<NuGetVersion> versions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<PackageVersionOutcome> results = UnlistRun.ExecuteAsync(nugetApiUrl, nugetApiKey,
            packageId, versions, UnlistVersionAsync, _packageVersionService, _packageAvailabilityDetector,
            cancellationToken);

        await foreach (PackageVersionOutcome outcome in results.ConfigureAwait(false))
        {
            yield return outcome;
        }
    }

    /// <summary>
    /// Unlists a single package version via <c>dotnet nuget delete</c>.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to unlist.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the unlist attempt for this version.</returns>
    /// <remarks>
    /// Exit code <c>0</c> yields <see cref="PackageVersionStatus.Unlisted"/>.
    /// A non-zero exit whose output matches the rate-limit pattern yields
    /// <see cref="PackageVersionStatus.RateLimited"/> (best-effort).
    /// A non-zero exit whose output indicates the version is not listed yields
    /// <see cref="PackageVersionStatus.AlreadyUnlisted"/> (best-effort).
    /// All other failures yield <see cref="PackageVersionStatus.Failed"/>.
    /// </remarks>
    private async ValueTask<PackageVersionOutcome> UnlistVersionAsync(
        string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey, CancellationToken cancellationToken)
    {
        UnlistRun.ValidateUnlistArguments(nugetApiUrl, packageId, version, apiKey);

        ProcessConfiguration configuration = new()
        {
            TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
            OutputRedirection = true,
            ArgumentList =
            [
                "nuget", "delete",
                packageId.ToLowerInvariant(),
                version.ToNormalizedString(),
                "--source", nugetApiUrl,
                "--non-interactive",
            ],
            // dotnet nuget delete reads NUGET_API_KEY when --api-key is omitted, so the key
            // travels in the child's environment, not on the process-command-visible command line.
            // Ordinal: environment-variable names are case-sensitive on Unix.
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NUGET_API_KEY"] = apiKey,
            },
        };

        try
        {
            BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(configuration,
                new ProcessExitConfiguration(),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Unlisted);
            }

            string output = $"{result.StandardOutput} {result.StandardError}";

            if (LooksRateLimited(output))
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.RateLimited);
            }

            if (LooksAlreadyUnlisted(output))
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyUnlisted);
            }

            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
        catch (Exception exception) when (UnlistRun.ConvertsToFailed(exception, cancellationToken))
        {
            // Server-side faults become a Failed outcome; the caller-cancellation rule lives in
            // UnlistRun.ConvertsToFailed.
            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
    }

    private static bool LooksRateLimited(string output)
    {
        // "429" must stand alone: an unexpected error that merely echoes a version number such as
        // "2.429.0" must not be misreported as a rate limit. Dots are excluded so version numerals
        // and decimal times never match.
        if (MyRegex.IsMatch(output))
        {
            return true;
        }

        return output.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
               || output.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || output.Contains("rate-limit", StringComparison.OrdinalIgnoreCase)
               || output.Contains("ratelimit", StringComparison.OrdinalIgnoreCase)
               || output.Contains("retry later", StringComparison.OrdinalIgnoreCase)
               || output.Contains("throttl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksAlreadyUnlisted(string output)
    {
        return output.Contains("not listed", StringComparison.OrdinalIgnoreCase)
               || output.Contains("already unlisted", StringComparison.OrdinalIgnoreCase)
               || output.Contains("is not listed", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?<![0-9.])429(?![0-9.])", RegexOptions.IgnoreCase, 3000)]
    private static partial Regex MyRegex { get; }
}
