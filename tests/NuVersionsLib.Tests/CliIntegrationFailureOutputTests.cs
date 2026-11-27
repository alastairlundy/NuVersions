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
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;

namespace NuVersionsLib.Tests;

/// <summary>
/// The documented failure-output contract driven end-to-end: DotMake binds the real argument
/// vector (including <c>--verbose</c>), the production composition runs the real HTTP backend
/// against the in-process mock NuGet V3 server, and the captured streams hold exactly what the
/// process would print.
/// </summary>
/// <remarks>
/// The mock's scripted delete answers are what make the truncation contract reachable: a failure
/// detail longer than either cap rides in the response body, so the 2000-character stderr cap with
/// its marker, <c>--verbose</c> lifting that cap, and the never-truncated JSON
/// <c>failureMessage</c> are all asserted against genuinely long input. The exit codes asserted
/// here — failed 1, rate-limited 3 — are the unchanged mapping these runs must keep.
/// </remarks>
[NotInParallel] // Console.SetOut/SetError are process-global; never interleave captured runs.
public class CliIntegrationFailureOutputTests
{
    /// <summary>The stderr detail cap, pinned here as the documented contract value.</summary>
    private const int StderrDetailCap = 2000;

    [Test]
    public async Task FailedDelete_RendersFailureTypeOnItsTextLine_AndExitsOne()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("FailureText");
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeleteResponseStatus("1.0.0", 401);

        CommandRun run = await CliIntegrationHarness.RunAsync(RealRunArgs(server, packageId, [], "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=failed FailureType=auth-failed");
        // The verbatim detail rides on stderr, attributed to its package and version.
        await Assert.That(run.StdErr)
            .Contains($"Failure detail for package '{packageId}' version 1.0.0:");
    }

    [Test]
    public async Task RateLimitedDelete_RendersFailureTypeOnItsTextLine_AndExitsThree()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("RateLimitedText");
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeleteResponseStatus("1.0.0", 429);

        CommandRun run = await CliIntegrationHarness.RunAsync(RealRunArgs(server, packageId, [], "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitRateLimited);
        await Assert.That(run.StdOut)
            .Contains("Version=1.0.0 Status=rate-limited FailureType=rate-limited");
        // A rate-limited outcome is attributed on stderr exactly like a failed one.
        await Assert.That(run.StdErr)
            .Contains($"Failure detail for package '{packageId}' version 1.0.0:");
    }

    [Test]
    public async Task JsonLines_CarryFiveKeysWithNullsOnNonFailureLines_AndTheFullFailurePairOnFailures()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("FailureJson");
        server.AddPackage(packageId, ("1.0.0", true));
        string body = new('x', 6000);
        server.SetDeleteResponseStatus("1.0.0", 401, body);

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, ["--output", "json"], "1.0.0", "9.9.9"));

        string[] lines = Lines(run.StdOut);

        await Assert.That(lines.Length).IsEqualTo(2);
        // The absent version carries no failure data: both keys are the explicit JSON null.
        await Assert.That(lines[0]).IsEqualTo(
            $"{{\"package\":\"{packageId}\",\"version\":\"9.9.9\",\"status\":\"not-on-server\"," +
            "\"failureType\":null,\"failureMessage\":null}");

        using JsonDocument document = JsonDocument.Parse(lines[1]);
        JsonElement record = document.RootElement;
        string fieldOrder = string.Join(",",
            record.EnumerateObject().Select(static property => property.Name));

        await Assert.That(fieldOrder).IsEqualTo("package,version,status,failureType,failureMessage");
        await Assert.That(record.GetProperty("status").GetString()).IsEqualTo("failed");
        await Assert.That(record.GetProperty("failureType").GetString()).IsEqualTo("auth-failed");

        // The failure pair rides verbatim: this detail is longer than the 5000-character stdout
        // cap and carries no truncation marker, because failureMessage is never truncated.
        string failureMessage = record.GetProperty("failureMessage").GetString() ?? string.Empty;
        await Assert.That(failureMessage).IsEqualTo(ExpectedDetail(body));
        await Assert.That(failureMessage.Length).IsGreaterThan(5000);
        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
    }

    [Test]
    public async Task StderrDetailLine_IsCappedAtTwoThousandCharactersWithTheMarker_ByDefault()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("FailureCap");
        server.AddPackage(packageId, ("1.0.0", true));
        string body = new('x', 4000);
        server.SetDeleteResponseStatus("1.0.0", 401, body);

        CommandRun run = await CliIntegrationHarness.RunAsync(RealRunArgs(server, packageId, [], "1.0.0"));

        // Exactly the attribution prefix, then the first 2000 characters of the verbatim detail,
        // then the marker — the cut removes everything after the cap.
        string expected =
            $"Failure detail for package '{packageId}' version 1.0.0: " +
            $"{ExpectedDetail(body)[..StderrDetailCap]}...";

        await Assert.That(run.StdErr.TrimEnd()).IsEqualTo(expected);
        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
    }

    [Test]
    public async Task Verbose_LiftsTheStderrCapEntirely_WithStdoutFormatAndExitCodeUnchanged()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("FailureVerbose");
        server.AddPackage(packageId, ("1.0.0", true));
        string body = new('x', 4000);
        server.SetDeleteResponseStatus("1.0.0", 401, body);

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, ["--verbose"], "1.0.0"));

        string expected =
            $"Failure detail for package '{packageId}' version 1.0.0: {ExpectedDetail(body)}";

        // The whole detail rides, uncut: a truncation would drop the tail this equality reads.
        await Assert.That(run.StdErr.TrimEnd()).IsEqualTo(expected);
        // --verbose changes truncation only: the stdout line and the exit code are the same.
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=failed FailureType=auth-failed");
        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
    }

    /// <summary>
    /// The verbatim detail a scripted 401 produces: the HTTP status line the response carries,
    /// then the scripted body.
    /// </summary>
    private static string ExpectedDetail(string body)
        => $"HTTP 401 Unauthorized{Environment.NewLine}{body}";

    private static string NewPackageId(string scenario)
        => $"{scenario}.{Guid.NewGuid():N}";

    private static string[] RealRunArgs(MockNuGetV3Server server, string packageId,
        string[] options, params string[] versions)
        => ["--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            .. options,
            .. versions];

    /// <summary>Splits captured stdout into lines on either platform's terminator.</summary>
    private static string[] Lines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}
