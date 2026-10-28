/*
    NuVersionsLib.Tests
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using NuVersionsCli;
using NuVersionsLib.Detectors;
using NuVersionsLib.Detectors.Abstractions;

namespace NuVersionsLib.Tests;

/// <summary>
/// End-to-end proofs for the <c>nuversions show</c> subcommand over the in-process mock V3
/// server: the closed listed/unlisted vocabulary, the read-only guarantee (no API key on any
/// request, no DELETE dispatched), the deprecation render contract in both output modes, the
/// stderr hint routing, and the exit-code shapes 0, 1 and 2.
/// </summary>
[NotInParallel] // ShowCommand.RunAsync redirects the Console streams; runs must not interleave.
public class CliShowCommandTests
{
    /// <summary>The deprecation subtree of a version deprecated with one verbatim reason.</summary>
    private const string OneReasonDeprecation =
        "{\"reasons\":[\"legacy\"],\"message\":null,\"alternatePackage\":null}";

    /// <summary>The full deprecation subtree: custom message plus alternate package id and range.</summary>
    private const string FullDeprecation =
        "{\"reasons\":[\"other\"],\"message\":\"Use Demo.Package.Next instead.\"," +
        "\"alternatePackage\":{\"id\":\"Demo.Package.Next\",\"range\":\"[2.0.0, )\"}}";

    private static string NewPackageId(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    [Test]
    public async Task ShowWithMixedListing_ReportsEveryVersion_InAscendingOrder()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowMixed");
        server.AddPackage(packageId, ("1.0.1", false), ("1.0.0", true), ("1.0.2-beta.1", true));

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        // Ascending, regardless of the order the feed reported them in.
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=listed");
        await Assert.That(run.StdOut).Contains("Version=1.0.1 Status=unlisted");
        await Assert.That(run.StdOut).Contains("Version=1.0.2-beta.1 Status=listed");
        await Assert.That(run.StdErr).IsEmpty();

        int listedIndex = run.StdOut.IndexOf("Version=1.0.0", StringComparison.Ordinal);
        int unlistedIndex = run.StdOut.IndexOf("Version=1.0.1", StringComparison.Ordinal);
        await Assert.That(listedIndex).IsLessThan(unlistedIndex);
    }

    [Test]
    public async Task ShowTextDeprecatedWithReasons_AppendsTheVerbatimCommaJoinedSegment_OnlyOnThatLine()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowTextReasons");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false), ("1.0.2", true));
        server.SetDeprecation(packageId, "1.0.1", MockDeprecation.SeveralReasons);

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        string[] lines = Lines(run.StdOut);

        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("Version=1.0.0 Status=listed");
        // Reasons verbatim and in served order, comma-joined, trailing segment on the deprecated
        // line only; the not-deprecated siblings keep the pre-deprecation shape.
        await Assert.That(lines[1]).IsEqualTo("Version=1.0.1 Status=unlisted Deprecation=legacy,critical-bugs,other");
        await Assert.That(lines[2]).IsEqualTo("Version=1.0.2 Status=listed");
        // No custom message in the fixtures, so no hint is due.
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task ShowTextReasonlessDeprecation_RendersTheUnspecifiedToken()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowTextUnspecified");
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.ReasonlessObject);

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        // A reason-less object is still deprecated; the value is the NuVersions render
        // token, not a server word, and the line carries no other change.
        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(run.StdOut).IsEqualTo($"Version=1.0.0 Status=listed Deprecation=unspecified{Environment.NewLine}");
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task ShowTextNotDeprecated_KeepsTheLineByteIdentical()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowTextPlain");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        // Golden assertion: the whole stdout is exactly the pre-deprecation line and nothing else.
        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(run.StdOut).IsEqualTo($"Version=1.0.0 Status=listed{Environment.NewLine}");
        await Assert.That(run.StdOut.Contains("Deprecation=")).IsFalse();
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task ShowTextCustomMessage_WritesExactlyOneHintToStderr_NeverStdout()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowTextHint");
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.MessageAndAlternatePackage);

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);

        // Exactly one hint line, on stderr alone, naming the JSON rerun.
        string[] errorLines = Lines(run.StdErr);
        await Assert.That(errorLines.Length).IsEqualTo(1);
        await Assert.That(errorLines[0].Contains("--output json")).IsTrue();
        await Assert.That(run.StdOut.Contains("--output json")).IsFalse();

        // The text line gains no hint-flavoured Deprecation= variant, and the custom message
        // itself never renders in text: only the verbatim reasons do.
        await Assert.That(run.StdOut).IsEqualTo($"Version=1.0.0 Status=listed Deprecation=other{Environment.NewLine}");
        await Assert.That(run.StdOut.Contains("Use Demo.Package.Next instead.")).IsFalse();
    }

    [Test]
    public async Task ShowJsonOutput_UsesOneFourFieldObjectPerLine()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowJson");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--output", "json");

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        // Not-deprecated lines carry the explicit fourth-field null, so the key set never varies.
        await Assert.That(run.StdOut).Contains(ExpectedJsonLine(packageId, "1.0.0", "listed", "null"));
        await Assert.That(run.StdOut).Contains(ExpectedJsonLine(packageId, "1.0.1", "unlisted", "null"));
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task ShowJsonOutput_CarriesTheFullDeprecationObject_WithExplicitNullParts()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowJsonDeprecation");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false), ("1.0.2", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.OneReason);
        server.SetDeprecation(packageId, "1.0.1", MockDeprecation.MessageAndAlternatePackage);

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--output", "json");

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        // The custom-message hint is text-mode only: a JSON consumer already carries the record.
        await Assert.That(run.StdErr).IsEmpty();

        string[] lines = Lines(run.StdOut);
        await Assert.That(lines.Length).IsEqualTo(3);
        // Deprecated with a reason and nothing else: message and alternatePackage explicit null.
        await Assert.That(lines[0]).IsEqualTo(ExpectedJsonLine(packageId, "1.0.0", "listed", OneReasonDeprecation));
        // Full object: verbatim reasons, custom message, alternate package id and range.
        await Assert.That(lines[1]).IsEqualTo(ExpectedJsonLine(packageId, "1.0.1", "unlisted", FullDeprecation));
        // Not deprecated: the explicit JSON null member, fourth field after status.
        await Assert.That(lines[2]).IsEqualTo(ExpectedJsonLine(packageId, "1.0.2", "listed", "null"));

        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            string fieldOrder = string.Join(",",
                document.RootElement.EnumerateObject().Select(static property => property.Name));

            await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
            await Assert.That(fieldOrder).IsEqualTo("package,version,status,deprecation");
        }
    }

    [Test]
    public async Task ShowWhenTheRegistrationReadFails_ExitsOne_WithOneStderrDiagnosticAndNoVersionLines()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowFeedError");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));
        // Registration pages are what the whole read is served from: the feed-side failure maps
        // onto show's existing error shape — one stderr diagnostic, exit 1, zero stdout lines,
        // no new exit code and no per-version degrade token.
        server.FailRegistration();

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(Lines(run.StdErr).Length).IsEqualTo(1);
    }

    [Test]
    public async Task DeprecationReadFailure_CollapsesOntoTheSameFeedErrorShape()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowDeprecationError");
        server.AddPackage(packageId, ("1.0.0", true));

        // The version enumeration succeeds against the mock and only the second pass — the
        // deprecation read — fails, pinning the shape: one stderr diagnostic, exit 1, and
        // no version line ever rendered (the second pass runs before any output).
        CommandRun run = await RunWithFailingDeprecationReadAsync(server, packageId);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(Lines(run.StdErr).Length).IsEqualTo(1);
        await Assert.That(run.StdErr).Contains("Simulated deprecation read failure.");
    }

    [Test]
    public async Task ShowSendsNoApiKey_AndNoDeleteRequest()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowReadOnly");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(server.DeleteRequests).IsEmpty();
        await Assert.That(server.Requests).IsNotEmpty();
        // The show path is credential-free: no request carries an API key header at all.
        await Assert.That(server.Requests.All(static request => request.ApiKey is null)).IsTrue();
    }

    [Test]
    public async Task ShowForAPackageTheServerDoesNotHave_ExitsTwo()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", "No.Such.Package.In.Show",
            "--server-url", server.ServiceIndexUrl);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitUsage);
        await Assert.That(run.StdErr).Contains("No.Such.Package.In.Show");
        await Assert.That(run.StdOut).IsEmpty();
    }

    [Test]
    public async Task ShowWithAnInvalidOutputMode_ExitsTwo()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ShowBadMode");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunSubcommandAsync(
            "show",
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--output", "xml");

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitUsage);
        await Assert.That(run.StdErr).Contains("Invalid output mode");
        await Assert.That(run.StdOut).IsEmpty();
    }

    /// <summary>The expected show JSON line: four fields in fixed order, deprecation fourth.</summary>
    private static string ExpectedJsonLine(string packageId, string version, string status,
        string deprecationJson)
        => $"{{\"package\":\"{packageId}\",\"version\":\"{version}\",\"status\":\"{status}\"," +
           $"\"deprecation\":{deprecationJson}}}";

    /// <summary>Splits captured stdout into lines on either platform's terminator.</summary>
    private static string[] Lines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Drives a real <see cref="ShowCommand"/> over the production version read against the mock
    /// feed, with one seam replaced: the deprecation second pass fails feed-side the way the
    /// failing mock cannot address on the wire (NuGet.Protocol's HTTP cache serves that pass the
    /// enumeration's stored registration response). stdout and stderr are captured exactly as the
    /// integration harness captures them.
    /// </summary>
    private static async Task<CommandRun> RunWithFailingDeprecationReadAsync(
        MockNuGetV3Server server, string packageId)
    {
        ServiceCollection services = new();
        services.AddHttpClient()
            .AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>()
            .AddSingleton<IPackageVersionService, PackageVersionService>();
        await using ServiceProvider provider = services.BuildServiceProvider();

        ShowCommand command = new(new ConfigurationBuilder().Build(),
            provider.GetRequiredService<IPackageVersionService>(),
            new FailingDeprecationService())
        {
            PackageId = packageId,
            ServerUrl = server.ServiceIndexUrl,
        };

        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        await using StringWriter stdout = new();
        await using StringWriter stderr = new();

        try
        {
            // Same capture discipline as the integration harness: this class is [NotInParallel],
            // so no captured run can interleave, and the finally block always restores the writers.
#pragma warning disable TUnit0055
            Console.SetOut(stdout);
            Console.SetError(stderr);
#pragma warning restore TUnit0055

            int exitCode = await command.RunAsync();

            return new CommandRun(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
            Console.SetError(originalError);
#pragma warning restore TUnit0055
        }
    }
}

/// <summary>
/// Deprecation read that fails feed-side: the second pass of show's two-pass read throws the way
/// an unavailable metadata resource does, so the command's catch shape is what gets asserted.
/// </summary>
internal sealed class FailingDeprecationService : IPackageDeprecationService
{
    public Task<IDictionary<NuGetVersion, PackageDeprecationInfo?>> CheckPackageVersionsDeprecationAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, IList<NuGetVersion> packageVersions,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("Simulated deprecation read failure.");
}
