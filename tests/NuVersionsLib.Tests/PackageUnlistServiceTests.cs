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

using System.Net;
using CliInvoke.Core;
using NuGet.Versioning;
using NuVersionsLib.Abstractions;
using NuVersionsLib.Models;

namespace NuVersionsLib.Tests;

/// <summary>
/// The composing services' fail-fast dispatch loop, bucket merging, and per-transport status
/// mapping: tests script responses at the transport edge — HTTP status codes behind a served
/// service index, SDK exit codes and output strings — and assert on the outcomes the real mapping
/// code produces. No test scripts an outcome directly.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="FakeScriptedHttpTransportHandler"/>/<see cref="FakeHttpClientFactory"/> and
/// <see cref="FakeScriptedProcessInvoker"/> doubles fake dispatch at the transport edge;
/// the <see cref="ScriptedVersionService"/>
/// and <see cref="FixedAvailabilityDetector"/> doubles are shared with the command-level tests.
/// </para>
/// <para>
/// HTTP dispatch additionally needs <see cref="LoopbackServiceIndexServer"/>: the service index is
/// resolved through NuGet.Protocol's own HTTP stack, so a real (but strictly local) listener serves
/// the index document, and the handler scripts the publish-endpoint responses. A 404 is only
/// interpreted as <see cref="PackageVersionStatus.AlreadyUnlisted"/> when that served index
/// advertised the publish endpoint the request went to.
/// </para>
/// </remarks>
public class PackageUnlistServiceTests
{
    private const string ServerUrl = "https://example.test/v3/index.json";

    private const string ApiKey = "unit-test-api-key";

    private const string PackageId = "Demo.Package";

    [Test]
    public async Task FailFast_RateLimitOnFirstDispatchMarksTheRestNotAttempted()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();

        NuGetVersion first = V("1.0.0");
        NuGetVersion second = V("1.0.1");
        NuGetVersion third = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [first] = Listed(first),
            [second] = Listed(second),
            [third] = Listed(third)
        };

        // The first dispatch answers 429; nothing is scripted for the later versions, so any
        // further dispatch would throw inside the handler and fail this test loudly.
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(first), (HttpStatusCode)429);
        PackageUnlistService service =
            CreateHttpService(listingInfo, packageExists: true, new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes =
            await CollectAsync(service, server.ServiceIndexUrl, [first, second, third]);

        await Assert.That(outcomes.Count).IsEqualTo(3);
        await Assert.That(outcomes[0].Version).IsEqualTo(first);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.RateLimited);
        await Assert.That(outcomes[1].Version).IsEqualTo(second);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.NotAttempted);
        await Assert.That(outcomes[2].Version).IsEqualTo(third);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.NotAttempted);
        // The rate limit is authoritative: exactly one delete request left the process.
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
        await Assert.That(handler.DispatchedRequests[0].RequestUri)
            .IsEqualTo($"{server.PublishUrl}{PackageId}/{first.ToNormalizedString()}");
    }

    [Test]
    public async Task BucketMerge_AlreadyUnlistedAndNotOnServer_YieldOutcomesWithoutAnyDispatch()
    {
        NuGetVersion firstUnlisted = V("1.0.1");
        NuGetVersion absent = V("2.0.0");
        NuGetVersion secondUnlisted = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [firstUnlisted] = Unlisted(firstUnlisted),
            [secondUnlisted] = Unlisted(secondUnlisted)
            // absent is deliberately missing from the lookup: not-on-server, not already-unlisted.
        };

        // No responses scripted: any dispatch would throw inside the handler and fail the test loudly.
        FakeScriptedHttpTransportHandler handler = new();
        PackageUnlistService service =
            CreateHttpService(listingInfo, packageExists: true, new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes =
            await CollectAsync(service, ServerUrl, [firstUnlisted, absent, secondUnlisted]);

        await Assert.That(outcomes.Count).IsEqualTo(3);
        await Assert.That(outcomes[0].Version).IsEqualTo(firstUnlisted);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.AlreadyUnlisted);
        await Assert.That(outcomes[1].Version).IsEqualTo(secondUnlisted);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.AlreadyUnlisted);
        await Assert.That(outcomes[2].Version).IsEqualTo(absent);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.NotOnServer);
        await Assert.That(handler.DispatchCount).IsEqualTo(0);
        await Assert.That(handler.DispatchedRequests).IsEmpty();
    }

    [Test]
    public async Task OneOutcomePerRequestedVersion_InAlreadyUnlistedThenNotOnServerThenDispatchOrder()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();

        NuGetVersion absent = V("2.0.0");
        NuGetVersion unlisted = V("1.0.1");
        NuGetVersion firstListed = V("1.0.0");
        NuGetVersion secondListed = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [unlisted] = Unlisted(unlisted),
            [firstListed] = Listed(firstListed),
            [secondListed] = Listed(secondListed)
            // absent is deliberately missing from the lookup.
        };

        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(firstListed), HttpStatusCode.OK);
        handler.ScriptStatusCode(PublishPath(secondListed), HttpStatusCode.OK);
        PackageUnlistService service =
            CreateHttpService(listingInfo, packageExists: true, new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes = await CollectAsync(service, server.ServiceIndexUrl,
            [absent, unlisted, firstListed, secondListed]);

        await Assert.That(outcomes.Count).IsEqualTo(4);
        await Assert.That(outcomes[0].Version).IsEqualTo(unlisted);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.AlreadyUnlisted);
        await Assert.That(outcomes[1].Version).IsEqualTo(absent);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.NotOnServer);
        await Assert.That(outcomes[2].Version).IsEqualTo(firstListed);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.Unlisted);
        await Assert.That(outcomes[3].Version).IsEqualTo(secondListed);
        await Assert.That(outcomes[3].Status).IsEqualTo(PackageVersionStatus.Unlisted);
        await Assert.That(handler.DispatchCount).IsEqualTo(2);
    }

    [Test]
    public async Task MissingPackage_ThrowsArgumentExceptionBeforeAnyOutcomeOrDispatch()
    {
        NuGetVersion version = V("1.0.0");
        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [version] = Listed(version)
        };

        FakeScriptedHttpTransportHandler handler = new();
        PackageUnlistService service =
            CreateHttpService(listingInfo, packageExists: false, new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes = [];
        ArgumentException? caught = null;

        try
        {
            await foreach (PackageVersionOutcome outcome in service.RequestPackageUnlistingAsync(
                ServerUrl, ApiKey, PackageId, [version], CancellationToken.None))
            {
                outcomes.Add(outcome);
            }
        }
        catch (ArgumentException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();

        ArgumentException failure = caught!;

        await Assert.That(failure.ParamName).IsEqualTo("packageId");
        await Assert.That(failure.Message).Contains(PackageId);
        await Assert.That(outcomes).IsEmpty();
        await Assert.That(handler.DispatchCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(200)]
    [Arguments(204)]
    [Arguments(201)]
    public async Task HttpStatusMapping_WithAnySuccessStatusCode_YieldsUnlisted(int statusCode)
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(V("1.0.0")), (HttpStatusCode)statusCode);

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Unlisted);
        await Assert.That(outcome.PackageId).IsEqualTo(PackageId);
        await Assert.That(outcome.Version.ToNormalizedString()).IsEqualTo("1.0.0");
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpStatusMapping_WithNotFoundOnAnAdvertisedPublishEndpoint_YieldsAlreadyUnlisted()
    {
        // The served service index advertises the PackagePublish/2.0.0 endpoint, so the delete goes
        // there; a 404 from the advertised endpoint is the one case that means "already unlisted".
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(V("1.0.0")), HttpStatusCode.NotFound);

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.AlreadyUnlisted);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
        // The delete went to the publish URL advertised by the loopback service index, not to the
        // service index itself.
        await Assert.That(handler.DispatchedRequests[0].RequestUri)
            .IsEqualTo($"{server.PublishUrl}{PackageId}/1.0.0");
        await Assert.That(handler.DispatchedRequests[0].Method).IsEqualTo("DELETE");
    }

    [Test]
    public async Task HttpStatusMapping_WithNotFoundOnTheFallbackRoute_YieldsFailed()
    {
        // Without a PackagePublish/2.0.0 entry in the served service index the delete goes to the
        // raw server URL; a 404 from that route says nothing about the version's listing state, so it
        // must not be reported as an already-unlisted success.
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start(includePublishEntry: false);
        FakeScriptedHttpTransportHandler handler = new();
        NuGetVersion version = V("1.0.0");
        handler.ScriptStatusCode($"/index.json/{PackageId}/{version.ToNormalizedString()}",
            HttpStatusCode.NotFound);

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchedRequests[0].RequestUri)
            .IsEqualTo($"{server.ServiceIndexUrl}/{PackageId}/1.0.0");
    }

    [Test]
    public async Task HttpStatusMapping_WithTooManyRequests_YieldsRateLimited()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(V("1.0.0")), (HttpStatusCode)429);

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.RateLimited);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpStatusMapping_WithInternalServerError_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptStatusCode(PublishPath(V("1.0.0")), HttpStatusCode.InternalServerError);

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpStatusMapping_WhenTheTransportThrows_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        handler.ScriptFault(PublishPath(V("1.0.0")),
            new HttpRequestException("scripted transport failure"));

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpStatusMapping_WhenTheCallerCancelsMidDispatch_LetsTheCancellationEscape()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();

        // The caller's token is cancelled before the dispatch: the resulting cancellation must
        // escape the service so the run can be reported as interrupted (130), not as Failed.
        await Assert.That(async () => await UnlistSingleHttpAsync(server, handler,
                new CancellationToken(canceled: true)))
            .Throws<OperationCanceledException>();
        await Assert.That(handler.DispatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task HttpStatusMapping_WhenTheHandlerThrowsACancellationWithoutTheCallerCancelling_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        FakeScriptedHttpTransportHandler handler = new();
        // A TaskCanceledException whose own token is not our caller's token (e.g. the HttpClient
        // timeout) must stay a Failed outcome, not an interruption.
        handler.ScriptFault(PublishPath(V("1.0.0")), new TaskCanceledException());

        PackageVersionOutcome outcome = await UnlistSingleHttpAsync(server, handler,
            new CancellationTokenSource().Token);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task SdkStatusMapping_WithExitCodeZero_YieldsUnlisted()
    {
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(0, "", ""));

        PackageVersionOutcome outcome = await UnlistSingleSdkAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Unlisted);
        await Assert.That(invoker.BufferedCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SdkStatusMapping_WithAStandalone429_YieldsRateLimited()
    {
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "",
            "One or more errors occurred. (Response status code does not indicate success: 429 (Too Many Requests).)"));

        PackageVersionOutcome outcome = await UnlistSingleSdkAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.RateLimited);
    }

    [Test]
    public async Task SdkStatusMapping_With429OnlyInsideAVersionNumeral_YieldsFailed()
    {
        // A "429" inside a version string is not a rate-limit signal: a version numeral in
        // unexpected-failure output must keep the outcome a plain failure.
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "", "Unexpected error for version 2.429.0 on the server."));

        PackageVersionOutcome outcome = await UnlistSingleSdkAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
    }

    [Test]
    public async Task SdkStatusMapping_WithNotListedOutput_YieldsAlreadyUnlisted()
    {
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "Package 'Foo 1.0.0' is not listed on the server.", ""));

        PackageVersionOutcome outcome = await UnlistSingleSdkAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.AlreadyUnlisted);
    }

    [Test]
    public async Task SdkStatusMapping_WithUnrecognizedNonZeroExit_YieldsFailed()
    {
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "", "Something went wrong on the server."));

        PackageVersionOutcome outcome = await UnlistSingleSdkAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
    }

    [Test]
    public async Task SdkStatusMapping_WhenTheProcessInvokerReportsCancellation_LetsTheCancellationEscape()
    {
        // CliInvoke throws OperationCanceledException when the caller's token cancels a run (verified
        // against CliInvoke 3.0.2, both mid-run and pre-cancelled). The service must let that
        // cancellation escape so the composing boundary reports the run as interrupted, not Failed.
        FakeScriptedProcessInvoker invoker = new();
        invoker.EnqueueException(new OperationCanceledException("simulated Ctrl-C during process run"));
        CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.That(async () => await UnlistSingleSdkAsync(invoker, cancelled.Token))
            .ThrowsExactly<OperationCanceledException>();
        await Assert.That(invoker.BufferedCallCount).IsEqualTo(1);
    }

    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    private static PackageVersionListingInfo Listed(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = true, PackageVersionExists = true
    };

    private static PackageVersionListingInfo Unlisted(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = false, PackageVersionExists = true
    };

    /// <summary>The absolute request path the HTTP flow's DELETE reaches for one version.</summary>
    private static string PublishPath(NuGetVersion version)
        => $"/publish/{PackageId}/{version.ToNormalizedString()}";

    private static PackageUnlistService CreateHttpService(
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo, bool packageExists,
        IHttpClientFactory clientFactory)
        => new(new ScriptedVersionService(listingInfo),
            new FixedAvailabilityDetector(packageExists), clientFactory);

    private static NetSdkPackageUnlistService CreateSdkService(
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo, bool packageExists,
        IProcessInvoker processInvoker)
        => new(new ScriptedVersionService(listingInfo),
            new FixedAvailabilityDetector(packageExists), processInvoker);

    private static async Task<List<PackageVersionOutcome>> CollectAsync(
        IPackageUnlistService service, string serverUrl, IList<NuGetVersion> versions,
        CancellationToken cancellationToken = default)
    {
        List<PackageVersionOutcome> outcomes = [];

        await foreach (PackageVersionOutcome outcome in service.RequestPackageUnlistingAsync(
            serverUrl, ApiKey, PackageId, versions, cancellationToken))
        {
            outcomes.Add(outcome);
        }

        return outcomes;
    }

    /// <summary>Unlists version 1.0.0 through the HTTP service against the loopback service index.</summary>
    private static async Task<PackageVersionOutcome> UnlistSingleHttpAsync(
        LoopbackServiceIndexServer server, FakeScriptedHttpTransportHandler handler,
        CancellationToken cancellationToken = default)
    {
        NuGetVersion version = V("1.0.0");
        PackageUnlistService service = CreateHttpService(
            new Dictionary<NuGetVersion, PackageVersionListingInfo> { [version] = Listed(version) },
            packageExists: true, new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes =
            await CollectAsync(service, server.ServiceIndexUrl, [version], cancellationToken);

        return outcomes.Single();
    }

    /// <summary>Unlists version 1.0.0 through the SDK service against scripted process results.</summary>
    private static async Task<PackageVersionOutcome> UnlistSingleSdkAsync(
        FakeScriptedProcessInvoker invoker, CancellationToken cancellationToken = default)
    {
        NuGetVersion version = V("1.0.0");
        NetSdkPackageUnlistService service = CreateSdkService(
            new Dictionary<NuGetVersion, PackageVersionListingInfo> { [version] = Listed(version) },
            packageExists: true, invoker);

        List<PackageVersionOutcome> outcomes =
            await CollectAsync(service, ServerUrl, [version], cancellationToken);

        return outcomes.Single();
    }

    private static BufferedProcessResult MakeResult(int exitCode, string standardOutput,
        string standardError)
        => new("dotnet", exitCode, processId: 1, standardOutput, standardError,
            DateTime.UtcNow, DateTime.UtcNow, canceled: false, signal: null);
}
