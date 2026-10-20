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

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using NuVersionsCli.Models;

namespace NuVersionsLib.Tests;

/// <summary>
/// <see cref="ShowOptions"/> construction and validation: the documented defaults applied when
/// nothing is supplied, and every validation rule shown with both its failing and its passing shape.
/// </summary>
public class ShowOptionsTests
{
    [Test]
    public async Task Create_WhenEverythingUnset_AppliesTheDocumentedDefaults()
    {
        ShowOptions options = ShowOptions.Create(
            packageId: null, serverUrlOption: null, serverUrlPrefixed: null,
            serverUrlGeneric: null, outputMode: null);

        await Assert.That(options.PackageId).IsEqualTo(string.Empty);
        await Assert.That(options.ServerUrl).IsEqualTo(ConfigurationValueHelpers.DefaultServerUrl);
        await Assert.That(options.OutputMode).IsEqualTo(ShowOptions.DefaultOutputMode);
    }

    [Test]
    public async Task Create_PassesSuppliedValuesThroughUnchanged()
    {
        ShowOptions options = ShowOptions.Create(
            packageId: "Demo.Package", serverUrlOption: "https://option.example",
            serverUrlPrefixed: "https://prefixed.example", serverUrlGeneric: "https://generic.example",
            outputMode: "json");

        await Assert.That(options.PackageId).IsEqualTo("Demo.Package");
        await Assert.That(options.ServerUrl).IsEqualTo("https://option.example");
        await Assert.That(options.OutputMode).IsEqualTo("json");
    }

    [Test]
    public async Task Create_WhenServerUrlOrOutputModeIsWhitespace_AppliesTheDefaults()
    {
        ShowOptions options = ShowOptions.Create(
            packageId: "Demo.Package", serverUrlOption: "  ", serverUrlPrefixed: null,
            serverUrlGeneric: null, outputMode: "\t");

        await Assert.That(options.ServerUrl).IsEqualTo(ConfigurationValueHelpers.DefaultServerUrl);
        await Assert.That(options.OutputMode).IsEqualTo(ShowOptions.DefaultOutputMode);
    }

    [Test]
    public async Task Validate_WithEveryOptionValid_HasNoFailures()
    {
        await Assert.That(Valid().Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_MissingPackageId_FailsWithThePackageIdFieldOnly()
    {
        ShowOptions options = Valid() with { PackageId = "   " };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(ShowOptions.PackageId));
    }

    [Test]
    public async Task Validate_WhitespaceServerUrl_FailsWithTheServerUrlFieldOnly()
    {
        ShowOptions options = Valid() with { ServerUrl = " " };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(ShowOptions.ServerUrl));
    }

    [Test]
    public async Task Validate_InvalidOutputMode_FailsWhileTextAndJsonPass()
    {
        ShowOptions invalid = Valid() with { OutputMode = "xml" };

        IReadOnlyList<OptionsValidationFailure> failures = invalid.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(ShowOptions.OutputMode));

        await Assert.That((Valid() with { OutputMode = "text" }).Validate()).IsEmpty();
        await Assert.That((Valid() with { OutputMode = "JSON" }).Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_ReportsEveryViolatedRuleAtOnceInDeclarationOrder()
    {
        ShowOptions options = new()
        {
            PackageId = " ",
            ServerUrl = " ",
            OutputMode = "xml",
        };

        IReadOnlyList<OptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(3);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(ShowOptions.PackageId));
        await Assert.That(failures[1].Field).IsEqualTo(nameof(ShowOptions.ServerUrl));
        await Assert.That(failures[2].Field).IsEqualTo(nameof(ShowOptions.OutputMode));
    }

    /// <summary>An otherwise-valid configuration; each test overrides one rule at a time.</summary>
    private static ShowOptions Valid() => new()
    {
        PackageId = "Demo.Package",
        ServerUrl = "https://example.test/v3/index.json",
        OutputMode = "text",
    };
}
