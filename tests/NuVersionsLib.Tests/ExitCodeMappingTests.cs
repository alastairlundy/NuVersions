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

using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuVersionsCli;

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
        await Assert.That(ExitCodeHelpers.ToExitCode(status)).IsEqualTo(expected);
    }

    [Test]
    public async Task ExitConstants_MatchTheDocumentedProcessCodes()
    {
        // The constants are read into a local first: asserting Assert.That(UnlistCommand.ExitSuccess)
        // directly is flagged by TUnitAssertions0005 (comparing a compile-time constant with a
        // constant), while the array below still pins each documented code to its literal value.
        int[] codes =
        [
            ExitCodeHelpers.ExitSuccess, ExitCodeHelpers.ExitFailure, ExitCodeHelpers.ExitUsage,
            ExitCodeHelpers.ExitRateLimited, ExitCodeHelpers.ExitCancelled, ExitCodeHelpers.ExitInterrupted
        ];

        await Assert.That(codes).IsEquivalentTo([0, 1, 2, 3, 4, 130]);
    }

    [Test]
    public async Task TryFromException_MapsAnArgumentFailureToTheUsageCode()
    {
        bool handled = ExitCodeHelpers.TryFromException(new ArgumentException("bad argument"), out int exitCode);

        await Assert.That(handled).IsTrue();
        await Assert.That(exitCode).IsEqualTo(ExitCodeHelpers.ExitUsage);
    }

    [Test]
    public async Task TryFromException_MapsEveryFeedSideFailureToTheFailureCode()
    {
        bool protocolHandled = ExitCodeHelpers.TryFromException(
            new TestProtocolException("the feed refused the request"), out int protocolCode);

        await Assert.That(protocolHandled).IsTrue();
        await Assert.That(protocolCode).IsEqualTo(ExitCodeHelpers.ExitFailure);

        bool resourceHandled = ExitCodeHelpers.TryFromException(
            new InvalidOperationException("the metadata resource is unavailable"), out int resourceCode);

        await Assert.That(resourceHandled).IsTrue();
        await Assert.That(resourceCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
    }

    [Test]
    public async Task TryFromException_LeavesCancellationAndUnknownFaultsUnclaimed()
    {
        // Cancellation maps to its own code and prints nothing, so the shared mapping never
        // claims it: the command keeps its dedicated catch for it.
        await Assert.That(ExitCodeHelpers.TryFromException(new OperationCanceledException(), out _)).IsFalse();

        // An unexpected fault stays unhandled: the CLI is meant to crash with its stack trace
        // rather than fold an unknown fault into a documented code.
        await Assert.That(ExitCodeHelpers.TryFromException(new InvalidCastException(), out _)).IsFalse();
    }

    [Test]
    public async Task DryRun_WithEveryVersionPresent_NeverExitsOutsideZeroOneTwo()
    {
        NuGetVersion version = NuGetVersion.Parse("1.0.0");
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "Demo.Package", ["1.0.0"], dryRun: true,
            new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = UnlistCommandHarness.ListedInfo(version)
            });

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitSuccess);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }

    [Test]
    public async Task DryRun_WithAVersionMissingFromServer_NeverExitsOutsideZeroOneTwo()
    {
        NuGetVersion version = NuGetVersion.Parse("1.0.0");
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "Demo.Package", versions: ["1.0.0", "9.9.9"], dryRun: true,
            new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = UnlistCommandHarness.ListedInfo(version)
            });

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitFailure);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }

    [Test]
    public async Task DryRun_WithInvalidOptions_NeverExitsOutsideZeroOneTwo()
    {
        UnlistCommand command = UnlistCommandHarness.CreateCommand(
            packageId: "   ", ["1.0.0"], dryRun: true);

        CommandRun run = await UnlistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(ExitCodeHelpers.ExitUsage);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
    }

    /// <summary>
    /// A concrete <see cref="NuGetProtocolException"/>, which is abstract upstream: the mapping
    /// matches on the base type, so any concrete subclass has to map the same way.
    /// </summary>
    private sealed class TestProtocolException : NuGetProtocolException
    {
        internal TestProtocolException(string message) : base(message)
        {
        }
    }
}
