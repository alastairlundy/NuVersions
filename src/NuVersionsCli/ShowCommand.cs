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
using System.Text.Json;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

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
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitInterrupted = 130;

    private readonly IConfiguration _configuration;
    private readonly IPackageVersionService _packageVersionService;
    private readonly string? _serverUrlGeneric;

    public ShowCommand(IConfiguration configuration,
        IPackageVersionService packageVersionService)
    {
        _configuration = configuration;
        _packageVersionService = packageVersionService;

        // Prefetched as a plain value so the command body holds no environment-variable lookups;
        // the precedence chain itself lives in UnlistOptions.ResolveServerUrl.
        _serverUrlGeneric = Environment.GetEnvironmentVariable("NUGET_SERVER_URL");
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
    [DefaultValue(UnlistOptions.DefaultOutputMode)]
    public string OutputMode { get; set; } = UnlistOptions.DefaultOutputMode;

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(PackageId))
        {
            await Console.Error.WriteLineAsync("Error: Package id is required.").ConfigureAwait(true);
            return ExitUsage;
        }

        if (!string.Equals(OutputMode, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(OutputMode, "json", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                "Error: Invalid output mode. Valid values are: text, JSON.").ConfigureAwait(true);
            return ExitUsage;
        }

        string serverUrl = UnlistOptions.ResolveServerUrl(ServerUrl,
            _configuration["NuGetServerUrl"], _serverUrlGeneric);

        using CancellationTokenSource cancellationSource = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            List<PackageVersionListingInfo> versions = [];

            await foreach (PackageVersionListingInfo info in _packageVersionService.EnumerateAllPackageVersionsAsync(
                               serverUrl, "show", PackageId, cancellationSource.Token).ConfigureAwait(true))
            {
                versions.Add(info);
            }

            bool json = string.Equals(OutputMode, "json", StringComparison.OrdinalIgnoreCase);

            foreach (PackageVersionListingInfo info in versions.OrderBy(static v => v.PackageVersion))
            {
                await Console.Out.WriteLineAsync(DescribeListing(PackageId, info, json)).ConfigureAwait(true);
            }

            return ExitSuccess;
        }
        catch (ArgumentException exception)
        {
            // Package-not-found-on-server is thrown by the composing services.
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return ExitUsage;
        }
        catch (NuGetProtocolException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return ExitFailure;
        }
        catch (InvalidOperationException exception)
        {
            // The library reports an unavailable NuGet.Protocol resource with this type.
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(true);
            return ExitFailure;
        }
        catch (OperationCanceledException)
        {
            return ExitInterrupted;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// One line of the show output in the same shape the unlist command emits: a normalized
    /// version plus a kebab-case status member, rendered as NDJSON under <c>--output json</c>.
    /// </summary>
    private static string DescribeListing(string packageId, PackageVersionListingInfo info, bool json)
    {
        if (json)
        {
            return ToJsonPropertyLine(packageId, info.PackageVersion,
                info.IsListed ? "listed" : "unlisted");
        }

        return $"Version={info.PackageVersion.ToNormalizedString()} " +
               $"Status={(info.IsListed ? "listed" : "unlisted")}";
    }

    /// <summary>
    /// The NDJSON line shared with the unlist command: exactly three fields in the fixed order
    /// package, version, status — no envelope, no array wrapper, no additional fields.
    /// </summary>
    private static string ToJsonPropertyLine(string packageId, NuGetVersion version, string status)
    {
        string package = JsonSerializer.Serialize(packageId);
        string normalizedVersion = JsonSerializer.Serialize(version.ToNormalizedString());
        string statusText = JsonSerializer.Serialize(status);

        return $"{{\"package\":{package},\"version\":{normalizedVersion},\"status\":{statusText}}}";
    }
}
