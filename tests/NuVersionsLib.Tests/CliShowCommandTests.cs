/*
    NuVersionsLib.Tests
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

namespace NuVersionsLib.Tests;

/// <summary>
/// End-to-end proofs for the <c>nuversions show</c> subcommand over the in-process mock V3
/// server: the closed listed/unlisted vocabulary, the read-only guarantee (no API key on any
/// request, no DELETE dispatched), and the exit-code shapes 0 and 2.
/// </summary>
[NotInParallel] // ShowCommand.RunAsync redirects the Console streams; runs must not interleave.
public class CliShowCommandTests
{
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
    public async Task ShowJsonOutput_UsesOneThreeFieldObjectPerLine()
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
        await Assert.That(run.StdOut).Contains($"{{\"package\":\"{packageId}\",\"version\":\"1.0.0\",\"status\":\"listed\"}}");
        await Assert.That(run.StdOut).Contains($"{{\"package\":\"{packageId}\",\"version\":\"1.0.1\",\"status\":\"unlisted\"}}");
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
}
