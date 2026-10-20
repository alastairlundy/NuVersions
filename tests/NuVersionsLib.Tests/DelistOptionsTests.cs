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

using NuVersionsCli.Models;

namespace NuVersionsLib.Tests;

/// <summary>
/// <see cref="UnlistOptions"/> construction and mode-aware validation: the documented defaults
/// applied when nothing is supplied — and every validation rule shown with both its failing and
/// its passing shape.
/// </summary>
public class DelistOptionsTests
{
    [Test]
    public async Task Create_WhenEverythingUnset_AppliesTheDocumentedDefaults()
    {
        UnlistOptions options = UnlistOptions.Create(
            packageId: null, versions: null, apiKeyOption: null, apiKeyPrefixed: null, apiKeyGeneric: null,
            serverUrlOption: null, serverUrlPrefixed: null, serverUrlGeneric: null,
            backend: null, nonInteractive: false, includeZeroMajor: false, useStrictParsing: false,
            unlistAllVersions: false, dryRun: false, outputMode: null);

        await Assert.That(options.PackageId).IsEqualTo(string.Empty);
        await Assert.That(options.Versions).IsEmpty();
        await Assert.That(options.ApiKey).IsNull();
        await Assert.That(options.ServerUrl).IsEqualTo(ConfigurationValueHelpers.DefaultServerUrl);
        await Assert.That(options.Backend).IsEqualTo(UnlistOptions.DefaultBackend);
        await Assert.That(options.OutputMode).IsEqualTo(UnlistOptions.DefaultOutputMode);
        await Assert.That(options.UseStrictParsing).IsFalse();
        await Assert.That(options.DryRun).IsFalse();
    }

    [Test]
    public async Task Create_WhenBackendOrOutputModeAreWhitespace_AppliesTheDefaults()
    {
        UnlistOptions options = UnlistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0"], apiKeyOption: null, apiKeyPrefixed: null,
            apiKeyGeneric: null, serverUrlOption: null, serverUrlPrefixed: null, serverUrlGeneric: null,
            backend: "  ", nonInteractive: false, includeZeroMajor: false, useStrictParsing: true,
            unlistAllVersions: false, dryRun: false, outputMode: "\t");

        await Assert.That(options.Backend).IsEqualTo(UnlistOptions.DefaultBackend);
        await Assert.That(options.OutputMode).IsEqualTo(UnlistOptions.DefaultOutputMode);
    }

    [Test]
    public async Task Create_PassesSuppliedValuesThroughUnchanged()
    {
        UnlistOptions options = UnlistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0", "1.0.1"], apiKeyOption: "option-key",
            apiKeyPrefixed: "prefixed-key", apiKeyGeneric: "generic-key",
            serverUrlOption: "https://option.example", serverUrlPrefixed: "https://prefixed.example",
            serverUrlGeneric: "https://generic.example", backend: "sdk", nonInteractive: true,
            includeZeroMajor: true, useStrictParsing: false, unlistAllVersions: true, dryRun: true,
            outputMode: "json");

        await Assert.That(options.PackageId).IsEqualTo("Demo.Package");
        await Assert.That(options.Versions).IsEquivalentTo(new[] { "1.0.0", "1.0.1" });
        await Assert.That(options.ApiKey).IsEqualTo("option-key");
        await Assert.That(options.ServerUrl).IsEqualTo("https://option.example");
        await Assert.That(options.Backend).IsEqualTo("sdk");
        await Assert.That(options.OutputMode).IsEqualTo("json");
        await Assert.That(options.NonInteractive).IsTrue();
        await Assert.That(options.IncludeZeroMajor).IsTrue();
        await Assert.That(options.UseStrictParsing).IsFalse();
        await Assert.That(options.UnlistAllVersions).IsTrue();
        await Assert.That(options.DryRun).IsTrue();
    }

    [Test]
    public async Task Validate_DryRunWithoutApiKey_HasNoFailures()
    {
        IReadOnlyList<OptionsValidationFailure> failures = Valid(dryRun: true, apiKey: null).Validate();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Validate_RealRunWithoutApiKey_FailsOnlyOnTheApiKeyRule()
    {
        IReadOnlyList<OptionsValidationFailure> failures = Valid(dryRun: false, apiKey: null).Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.ApiKey));
    }

    [Test]
    public async Task Validate_RealRunWithApiKey_HasNoFailures()
    {
        IReadOnlyList<OptionsValidationFailure> failures = Valid(dryRun: false, apiKey: "unit-test-api-key").Validate();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Validate_MissingPackageId_FailsWithThePackageIdFieldOnly()
    {
        UnlistOptions options = Valid() with { PackageId = "   " };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.PackageId));
    }

    [Test]
    public async Task Validate_WhitespaceServerUrl_FailsWithTheServerUrlFieldOnly()
    {
        UnlistOptions options = Valid() with { ServerUrl = " " };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.ServerUrl));
    }

    [Test]
    public async Task Validate_InvalidBackend_FailsWhileHttpAndSdkPass()
    {
        UnlistOptions invalid = Valid() with { Backend = "ftp" };
        IReadOnlyList<OptionsValidationFailure> failures = invalid.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.Backend));

        await Assert.That((Valid() with { Backend = "http" }).Validate()).IsEmpty();
        await Assert.That((Valid() with { Backend = "SDK" }).Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_NoVersionsFailsUnlessUnlistAllIsSet()
    {
        UnlistOptions noVersions = Valid() with { Versions = [] };
        IReadOnlyList<OptionsValidationFailure> failures = noVersions.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.Versions));

        await Assert.That((Valid() with { Versions = [], UnlistAllVersions = true }).Validate()).IsEmpty();
        await Assert.That(Valid().Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_InvalidOutputMode_FailsWhileTextAndJsonPass()
    {
        UnlistOptions invalid = Valid() with { OutputMode = "xml" };
        IReadOnlyList<OptionsValidationFailure> failures = invalid.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.OutputMode));

        await Assert.That((Valid() with { OutputMode = "text" }).Validate()).IsEmpty();
        await Assert.That((Valid() with { OutputMode = "JSON" }).Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_ReportsEveryViolatedRuleAtOnceInDeclarationOrder()
    {
        UnlistOptions options = new()
        {
            PackageId = " ",
            ServerUrl = " ",
            Backend = "bogus",
            Versions = [],
            ApiKey = null,
            OutputMode = "xml",
            DryRun = false,
            UnlistAllVersions = false,
        };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(6);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(UnlistOptions.PackageId));
        await Assert.That(failures[1].Field).IsEqualTo(nameof(UnlistOptions.ServerUrl));
        await Assert.That(failures[2].Field).IsEqualTo(nameof(UnlistOptions.Backend));
        await Assert.That(failures[3].Field).IsEqualTo(nameof(UnlistOptions.Versions));
        await Assert.That(failures[4].Field).IsEqualTo(nameof(UnlistOptions.ApiKey));
        await Assert.That(failures[5].Field).IsEqualTo(nameof(UnlistOptions.OutputMode));
    }

    [Test]
    public async Task Create_WithAnOptionLookingToken_KeepsItAsideAsAnUnknownOption()
    {
        // The argument tokeniser hands an unknown option to the greedy versions position, so the
        // triage has to keep it apart from the version strings rather than drop it.
        UnlistOptions options = UnlistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0", "--typo"], apiKeyOption: null,
            apiKeyPrefixed: null, apiKeyGeneric: null, serverUrlOption: null, serverUrlPrefixed: null,
            serverUrlGeneric: null, backend: null, nonInteractive: false, includeZeroMajor: false,
            useStrictParsing: false, unlistAllVersions: false, dryRun: false, outputMode: null);

        await Assert.That(options.UnknownOptions).IsEquivalentTo(new[] { "--typo" });
        await Assert.That(options.Versions).IsEquivalentTo(new[] { "1.0.0" });
    }

    [Test]
    public async Task Create_WithAVersionToken_ReportsNoUnknownOption()
    {
        UnlistOptions options = UnlistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0"], apiKeyOption: null,
            apiKeyPrefixed: null, apiKeyGeneric: null, serverUrlOption: null, serverUrlPrefixed: null,
            serverUrlGeneric: null, backend: null, nonInteractive: false, includeZeroMajor: false,
            useStrictParsing: false, unlistAllVersions: false, dryRun: false, outputMode: null);

        await Assert.That(options.UnknownOptions).IsEmpty();
    }

    [Test]
    public async Task Validate_WithAnUnknownOption_FailsWithTheUnknownOptionFieldOnly()
    {
        UnlistOptions options = Valid() with { UnknownOptions = ["--typo"] };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(OptionsValidationFailure.UnknownOptionField);
        await Assert.That(failures[0].Message).Contains("--typo");
    }

    /// <summary>An otherwise-valid, real-run configuration; each test overrides one rule at a time.</summary>
    private static UnlistOptions Valid(bool dryRun = false, string? apiKey = "unit-test-api-key") => new()
    {
        PackageId = "Demo.Package",
        Versions = ["1.0.0"],
        ApiKey = apiKey,
        ServerUrl = "https://example.test/v3/index.json",
        Backend = "http",
        OutputMode = "text",
        UseStrictParsing = true,
        DryRun = dryRun,
    };
}
