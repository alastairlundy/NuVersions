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

using NuGet.Versioning;
using NuVersionsCli;
using NuVersionsLib.Models;
using TUnit.Core;

namespace NuVersionsLib.Tests;

/// <summary>
/// Exit-code contract: every member of the closed status vocabulary maps to its documented
/// process code, and a dry run can only ever exit 0, 1 or 2 — never 3 (rate-limited) or 4
/// (cancelled), because a dry run dispatches nothing that could be rate-limited or cancelled.
/// </summary>
[NotInParallel] // The dry-run proofs drive UnlistCommand.RunAsync, which redirects Console streams.
public class ExitCodeMappingTests
{
    [Test]
    [Arguments(PackageVersionStatus.Unlisted, 0)]
    [Arguments(PackageVersionStatus.AlreadyUnlisted, 0)]
    [Arguments(PackageVersionStatus.NotOnServer, 1)]
    [Arguments(PackageVersionStatus.Failed, 1)]
    [Arguments(PackageVersionStatus.RateLimited, 3)]
    [Arguments(PackageVersionStatus.NotAttempted, 4)]
    public async Task ToExitCode_MapsEveryStatusMemberToItsDocumentedCode(
        PackageVersionStatus status, int expected)
    {
        await Assert.That(UnlistCommand.ToExitCode(status)).IsEqualTo(expected);
    }

    [Test]
    public async Task ExitConstants_MatchTheDocumentedProcessCodes()
    {
        // The constants are read into a local first: asserting Assert.That(UnlistCommand.ExitSuccess)
        // directly is flagged by TUnitAssertions0005 (comparing a compile-time constant with a
        // constant), while the array below still pins each documented code to its literal value.
        int[] codes =
        [
            UnlistCommand.ExitSuccess, UnlistCommand.ExitFailure, UnlistCommand.ExitUsage,
            UnlistCommand.ExitRateLimited, UnlistCommand.ExitCancelled, UnlistCommand.ExitInterrupted
        ];

        await Assert.That(codes).IsEquivalentTo(new[] { 0, 1, 2, 3, 4, 130 });
    }

    [Test]
    public async Task DryRun_WithEveryVersionPresent_NeverExitsOutsideZeroOneTwo()
    {
        NuGetVersion version = NuGetVersion.Parse("1.0.0");
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "Demo.Package", versions: ["1.0.0"], dryRun: true,
            listingInfo: new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = UnlistCommandHarness.ListedInfo(version)
            });

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(UnlistCommand.ExitSuccess);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }

    [Test]
    public async Task DryRun_WithAVersionMissingFromServer_NeverExitsOutsideZeroOneTwo()
    {
        NuGetVersion version = NuGetVersion.Parse("1.0.0");
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "Demo.Package", versions: ["1.0.0", "9.9.9"], dryRun: true,
            listingInfo: new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = UnlistCommandHarness.ListedInfo(version)
            });

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(UnlistCommand.ExitFailure);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }

    [Test]
    public async Task DryRun_WithInvalidOptions_NeverExitsOutsideZeroOneTwo()
    {
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "   ", versions: ["1.0.0"], dryRun: true);

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(UnlistCommand.ExitUsage);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }
}
