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

namespace NuVersionsLib.Tests;

/// <summary>
/// The deprecation read's verdict mapping and its metadata parts against the
/// <see cref="MockNuGetV3Server"/> deprecation fixtures: the server serves the
/// deprecation object on registration catalog entries, the production service fetches them
/// through NuGet.Protocol's metadata resource, and the assertions pin the mapping at that seam —
/// no test scripts a verdict directly.
/// </summary>
public class PackageDeprecationServiceTests
{
    private const string ApiKey = "unit-test-api-key";

    [Test]
    public async Task OneReasonFixture_MapsToDeprecatedWithThatVerbatimReason()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.OneReason);

        PackageDeprecationInfo? verdict = await FetchSingleAsync(server, packageId, V("1.0.0"));

        await Assert.That(verdict).IsNotNull();
        await Assert.That(verdict!.Reasons).IsEquivalentTo(new[] { "legacy" });
        await Assert.That(verdict.Message).IsNull();
        await Assert.That(verdict.AlternatePackageId).IsNull();
        await Assert.That(verdict.AlternatePackageRange).IsNull();
    }

    [Test]
    public async Task SeveralReasonsFixture_MapsEveryReasonInServerOrder()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.SeveralReasons);

        PackageDeprecationInfo? verdict = await FetchSingleAsync(server, packageId, V("1.0.0"));

        await Assert.That(verdict).IsNotNull();
        // Verbatim pass-through: every word, in the order the server served them.
        await Assert.That(verdict!.Reasons).IsEquivalentTo(new[] { "legacy", "critical-bugs", "other" });
    }

    [Test]
    public async Task ReasonlessObjectFixture_IsStillDeprecated()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.ReasonlessObject);

        PackageDeprecationInfo? verdict = await FetchSingleAsync(server, packageId, V("1.0.0"));

        // The verdict is the served object itself: reasons play no part in it.
        await Assert.That(verdict).IsNotNull();
        await Assert.That(verdict!.Reasons).IsEmpty();
    }

    [Test]
    public async Task MessageAndAlternatePackageFixture_CarriesEveryNullablePart()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeprecation(packageId, "1.0.0", MockDeprecation.MessageAndAlternatePackage);

        PackageDeprecationInfo? verdict = await FetchSingleAsync(server, packageId, V("1.0.0"));

        await Assert.That(verdict).IsNotNull();
        await Assert.That(verdict!.Message).IsEqualTo("Use Demo.Package.Next instead.");
        await Assert.That(verdict.AlternatePackageId).IsEqualTo("Demo.Package.Next");
        // The range passes through in its original string form, not a normalized rewrite.
        await Assert.That(verdict.AlternatePackageRange).IsEqualTo("[2.0.0, )");
    }

    [Test]
    public async Task VersionWithNoDeprecationObject_MapsToNotDeprecated()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        // Both versions served; only the unlisted one carries a deprecation object, so the read
        // must also see the unlisted version's metadata.
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));
        server.SetDeprecation(packageId, "1.0.1", MockDeprecation.OneReason);

        IDictionary<NuGetVersion, PackageDeprecationInfo?> verdicts =
            await FetchAsync(server, packageId, V("1.0.0"), V("1.0.1"));

        // Absent object = not deprecated; served object = deprecated, on the same feed.
        await Assert.That(verdicts[V("1.0.0")]).IsNull();
        await Assert.That(verdicts[V("1.0.1")]).IsNotNull();
    }

    [Test]
    public async Task FeedServingNoDeprecationFacts_MapsEveryVersionToNotDeprecated()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        // Versions but no deprecation fact anywhere: indistinguishable from nuget.org's absent case.
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false), ("2.0.0-beta.1", true));

        IDictionary<NuGetVersion, PackageDeprecationInfo?> verdicts =
            await FetchAsync(server, packageId, V("1.0.0"), V("1.0.1"), V("2.0.0-beta.1"));

        await Assert.That(verdicts.Count).IsEqualTo(3);
        await Assert.That(verdicts[V("1.0.0")]).IsNull();
        await Assert.That(verdicts[V("1.0.1")]).IsNull();
        await Assert.That(verdicts[V("2.0.0-beta.1")]).IsNull();
    }

    [Test]
    public async Task ResultCoversExactlyTheRequestedVersionSet()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId();
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", true), ("1.0.2", true));
        server.SetDeprecation(packageId, "1.0.2", MockDeprecation.OneReason);

        NuGetVersion first = V("1.0.0");
        NuGetVersion second = V("1.0.1");

        // The read reports on exactly the versions it is fed — the set show already prints — and
        // carries no verdict for a version outside it, even one the feed deprecates.
        IDictionary<NuGetVersion, PackageDeprecationInfo?> verdicts =
            await FetchAsync(server, packageId, first, second);

        await Assert.That(verdicts.Count).IsEqualTo(2);
        await Assert.That(verdicts.ContainsKey(first)).IsTrue();
        await Assert.That(verdicts.ContainsKey(second)).IsTrue();
    }

    private static string NewPackageId() => $"Deprecation.{Guid.NewGuid():N}";

    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    /// <summary>Runs the production deprecation read over <paramref name="versions"/> against the mock feed.</summary>
    private static async Task<IDictionary<NuGetVersion, PackageDeprecationInfo?>> FetchAsync(
        MockNuGetV3Server server, string packageId, params NuGetVersion[] versions)
    {
        PackageDeprecationService service = new();

        return await service.CheckPackageVersionsDeprecationAsync(
            server.ServiceIndexUrl, ApiKey, packageId, versions, CancellationToken.None);
    }

    private static async Task<PackageDeprecationInfo?> FetchSingleAsync(
        MockNuGetV3Server server, string packageId, NuGetVersion version)
    {
        IDictionary<NuGetVersion, PackageDeprecationInfo?> verdicts =
            await FetchAsync(server, packageId, version);

        return verdicts[version];
    }
}
