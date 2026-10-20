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

namespace NuVersionsLib.Tests;

/// <summary>
/// The scope of <c>--unlist-all</c> driven end-to-end against the in-process mock NuGet V3 server:
/// label-carrying pre-release versions and stable <c>0.x</c> versions are unlisted, stable versions
/// with a major of 1 or more never enter the plan. This is the pre-release definition that replaced
/// the removed <c>--include-zero-major</c> flag.
/// </summary>
[NotInParallel] // Console.SetOut/SetError are process-global; never interleave captured runs.
public class CliIntegrationUnlistAllTests
{
    [Test]
    public async Task UnlistAll_IncludesStableZeroMajorAndPreRelease_ButNotStableOneAndAbove()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("UnlistAllScope");
        server.AddPackage(packageId, ("0.1.0", true), ("0.2.0", true), ("1.0.0", true),
            ("2.0.0-beta1", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "--unlist-all"));

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(run.StdOut).Contains("Version=0.1.0 Status=unlisted");
        await Assert.That(run.StdOut).Contains("Version=0.2.0 Status=unlisted");
        await Assert.That(run.StdOut).Contains("Version=2.0.0-beta1 Status=unlisted");
        // Stable 1.x is not pre-release under any reading of the rule; it never enters the plan.
        await Assert.That(run.StdOut.Contains("1.0.0")).IsFalse();
        await Assert.That(run.StdErr).IsEmpty();

        await Assert.That(server.DeleteRequests.Count).IsEqualTo(3);
        await Assert.That(server.DeleteRequests.Any(
            static request => request.Path.EndsWith("/1.0.0", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task UnlistAll_OnAPackageWithOnlyStableZeroMajorVersions_StillUnlistsThem()
    {
        // The regression this rule exists for: a package published only as 0.x used to match zero
        // candidates, so the run printed nothing and exited 0 as if it had done the work.
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("UnlistAllZeroMajorOnly");
        server.AddPackage(packageId, ("0.1.0", true), ("0.2.0", false));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "--unlist-all"));

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(run.StdOut).Contains("Version=0.1.0 Status=unlisted");
        await Assert.That(run.StdOut).Contains("Version=0.2.0 Status=already-unlisted");
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(1);
        await Assert.That(server.DeleteRequests[0].Path).IsEqualTo($"/publish/{packageId}/0.1.0");
    }

    private static string NewPackageId(string scenario)
        => $"{scenario}.{Guid.NewGuid():N}";

    private static string[] RealRunArgs(MockNuGetV3Server server, string packageId,
        params string[] options)
        => ["--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            .. options];
}
