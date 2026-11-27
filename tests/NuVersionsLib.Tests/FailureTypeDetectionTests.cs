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
using NuVersionsLib.Tests.TransportDoubles;
using TUnit.Assertions.Enums;

namespace NuVersionsLib.Tests;

/// <summary>
/// The failure-type detection each backend runs inside its per-version delegate, scripted at the
/// transport edge: HTTP status classes behind the served service index, SDK output wording families
/// behind the scripted process invoker. No test scripts an outcome — the real mapping code in
/// <see cref="HttpPackageUnlistService"/> and <see cref="NetSdkPackageUnlistService"/> produces it.
/// </summary>
/// <remarks>
/// <para>
/// The concrete words the SDK mappers match are implementation detail; what is pinned here is the
/// type each scripted input must produce, and the symmetry rule: both backends must be able to
/// produce every member of the closed five-type vocabulary, so a mapper that drifts onto a set its
/// twin cannot reach fails the last test.
/// </para>
/// <para>
/// HTTP additionally needs <see cref="LoopbackServiceIndexServer"/> because the service index is
/// resolved through NuGet.Protocol's own HTTP stack; the scripted handler answers the delete
/// dispatch that follows.
/// </para>
/// </remarks>
public class FailureTypeDetectionTests
{
    private const string ServerUrl = "https://example.test/v3/index.json";

    private const string ApiKey = "unit-test-api-key";

    private const string PackageId = "Demo.Package";

    /// <summary>
    /// The HTTP script: each status class the mapper must recognise, with the status and failure
    /// type it has to produce. 429 rides along because the rate-limited member is part of the
    /// symmetric set the two backends both have to reach.
    /// </summary>
    private static readonly (int StatusCode, PackageVersionStatus Status, PackageVersionFailureType Type)[]
        HttpStatusScript =
        [
            (401, PackageVersionStatus.Failed, PackageVersionFailureType.AuthFailed),
            (403, PackageVersionStatus.Failed, PackageVersionFailureType.AuthFailed),
            (500, PackageVersionStatus.Failed, PackageVersionFailureType.ServerError),
            (503, PackageVersionStatus.Failed, PackageVersionFailureType.ServerError),
            (409, PackageVersionStatus.Failed, PackageVersionFailureType.Unknown),
            (429, PackageVersionStatus.RateLimited, PackageVersionFailureType.RateLimited),
        ];

    /// <summary>
    /// The SDK script: one representative output per wording family, labelled so a wrong mapping
    /// names the family that drifted.
    /// </summary>
    private static readonly (string Label, string Output, PackageVersionStatus Status, PackageVersionFailureType Type)[]
        SdkWordingScript =
        [
            ("auth", "The API key was rejected: unauthorized.", PackageVersionStatus.Failed,
                PackageVersionFailureType.AuthFailed),
            ("server", "The server returned a service error.", PackageVersionStatus.Failed,
                PackageVersionFailureType.ServerError),
            ("unavailable", "Unable to connect to the remote server: connection refused.",
                PackageVersionStatus.Failed, PackageVersionFailureType.Unavailable),
            ("rate-limit", "The server throttled the request; retry later.",
                PackageVersionStatus.RateLimited, PackageVersionFailureType.RateLimited),
            ("unknown", "Something went wrong on the server.", PackageVersionStatus.Failed,
                PackageVersionFailureType.Unknown),
        ];

    [Test]
    public async Task HttpStatusClasses_ProduceTheirScriptedStatusAndFailureType()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        List<string> actual = [];

        foreach ((int statusCode, _, _) in HttpStatusScript)
        {
            PackageVersionOutcome outcome = await RunHttpAsync(server, statusCode);

            // A failed or rate-limited outcome also carries the verbatim detail its rendering reads.
            await Assert.That(outcome.FailureType).IsNotNull();
            await Assert.That(outcome.Detail ?? string.Empty).IsNotEmpty();
            actual.Add($"{statusCode}=>{outcome.Status}/{outcome.FailureType}");
        }

        await Assert.That(actual).IsEquivalentTo(
            [.. HttpStatusScript.Select(entry => $"{entry.StatusCode}=>{entry.Status}/{entry.Type}")],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task HttpTransportFault_ProducesFailedUnavailableWithTheFaultMessageAsDetail()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();

        PackageVersionOutcome outcome = await RunHttpAsync(server,
            handler => handler.ScriptFault(PublishPath(V("1.0.0")),
                new HttpRequestException("scripted transport failure")));

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(outcome.FailureType).IsEqualTo(PackageVersionFailureType.Unavailable);
        await Assert.That(outcome.Detail ?? string.Empty).Contains("scripted transport failure");
    }

    [Test]
    public async Task SdkOutputWordingFamilies_ProduceTheirScriptedStatusAndFailureType()
    {
        List<string> actual = [];

        foreach ((string label, string output, _, _) in SdkWordingScript)
        {
            PackageVersionOutcome outcome = await RunSdkAsync(output);

            await Assert.That(outcome.FailureType).IsNotNull();
            await Assert.That(outcome.Detail ?? string.Empty).IsNotEmpty();
            actual.Add($"{label}=>{outcome.Status}/{outcome.FailureType}");
        }

        await Assert.That(actual).IsEquivalentTo(
            [.. SdkWordingScript.Select(entry => $"{entry.Label}=>{entry.Status}/{entry.Type}")],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task BothBackends_ProduceTheSameSymmetricFiveMemberSet()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();

        List<PackageVersionFailureType> http = [];

        foreach ((int statusCode, _, _) in HttpStatusScript)
        {
            http.Add((await RunHttpAsync(server, statusCode)).FailureType!.Value);
        }

        http.Add((await RunHttpAsync(server,
            handler => handler.ScriptFault(PublishPath(V("1.0.0")),
                new HttpRequestException("scripted transport failure")))).FailureType!.Value);

        List<PackageVersionFailureType> sdk = [];

        foreach ((_, string output, _, _) in SdkWordingScript)
        {
            sdk.Add((await RunSdkAsync(output)).FailureType!.Value);
        }

        PackageVersionFailureType[] symmetricSet = Enum.GetValues<PackageVersionFailureType>();
        PackageVersionFailureType[] httpDistinct = [.. http.Distinct()];
        PackageVersionFailureType[] sdkDistinct = [.. sdk.Distinct()];

        await Assert.That(httpDistinct).IsEquivalentTo(sdkDistinct);
        await Assert.That(httpDistinct).IsEquivalentTo(symmetricSet);
    }

    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    private static PackageVersionListingInfo Listed(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = true, PackageVersionExists = true
    };

    /// <summary>The absolute request path the HTTP flow's DELETE reaches for version 1.0.0.</summary>
    private static string PublishPath(NuGetVersion version)
        => $"/publish/{PackageId}/{version.ToNormalizedString()}";

    /// <summary>Dispatches version 1.0.0 through the real HTTP backend against the served index.</summary>
    /// <param name="server">The loopback server serving the service index the backend resolves.</param>
    /// <param name="script">Scripts the one delete response (or fault) the handler answers with.</param>
    private static async Task<PackageVersionOutcome> RunHttpAsync(
        LoopbackServiceIndexServer server, Action<FakeScriptedHttpTransportHandler> script)
    {
        NuGetVersion version = V("1.0.0");
        FakeScriptedHttpTransportHandler handler = new();
        script(handler);

        HttpPackageUnlistService service = new(
            new ScriptedVersionService(new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = Listed(version)
            }),
            new FixedAvailabilityDetector(packageExists: true),
            new FakeHttpClientFactory(handler));

        List<PackageVersionOutcome> outcomes = [];

        await foreach (PackageVersionOutcome outcome in service.RequestPackageUnlistingAsync(
                           server.ServiceIndexUrl, ApiKey, PackageId, [version], CancellationToken.None))
        {
            outcomes.Add(outcome);
        }

        // A stray dispatch would surface only as a wrong outcome, so name the request that actually
        // went out: exactly one scripted delete, and nothing else.
        await Assert.That(handler.UnscriptedRequests).IsEmpty();
        await Assert.That(handler.DispatchCount).IsEqualTo(1);

        return outcomes.Single();
    }

    /// <summary>Dispatches version 1.0.0 through the real HTTP backend with one scripted status.</summary>
    private static Task<PackageVersionOutcome> RunHttpAsync(LoopbackServiceIndexServer server, int statusCode)
        => RunHttpAsync(server, handler =>
            handler.ScriptStatusCode(PublishPath(V("1.0.0")), (HttpStatusCode)statusCode));

    /// <summary>Dispatches version 1.0.0 through the real SDK backend against a scripted result.</summary>
    private static async Task<PackageVersionOutcome> RunSdkAsync(string standardErrorOutput)
    {
        NuGetVersion version = V("1.0.0");
        FakeScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, string.Empty, standardErrorOutput));

        NetSdkPackageUnlistService service = new(
            new ScriptedVersionService(new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [version] = Listed(version)
            }),
            new FixedAvailabilityDetector(packageExists: true),
            invoker);

        List<PackageVersionOutcome> outcomes = [];

        await foreach (PackageVersionOutcome outcome in service.RequestPackageUnlistingAsync(
                           ServerUrl, ApiKey, PackageId, [version], CancellationToken.None))
        {
            outcomes.Add(outcome);
        }

        // The same tripwire as the HTTP double: a run that reached the invoker with no scripted
        // result would otherwise show up only as a wrong outcome.
        await Assert.That(invoker.UnscriptedRuns).IsEmpty();
        await Assert.That(invoker.BufferedCallCount).IsEqualTo(1);

        return outcomes.Single();
    }

    private static BufferedProcessResult MakeResult(int exitCode, string standardOutput,
        string standardError)
        => new("dotnet", exitCode, processId: 1, standardOutput, standardError,
            DateTime.UtcNow, DateTime.UtcNow, canceled: false, signal: null);
}
