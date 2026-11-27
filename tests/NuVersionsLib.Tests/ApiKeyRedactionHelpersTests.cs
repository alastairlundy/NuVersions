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
/// The exact-value redaction policy behind failure rendering: only the submitted key is secret, so
/// labels and header names survive unless they are literally the value the caller submitted.
/// </summary>
public class ApiKeyRedactionHelpersTests
{
    private const string ApiKey = "secret-123";

    [Test]
    public async Task RedactDetail_ReplacesEveryOccurrenceOfTheSubmittedValue()
    {
        string? redacted = ApiKeyRedactionHelpers.RedactDetail(
            $"Invalid API key: {ApiKey}. Retry with a new {ApiKey}.", ApiKey);

        await Assert.That(redacted).IsEqualTo(
            "Invalid API key: [redacted]. Retry with a new [redacted].");
    }

    [Test]
    public async Task RedactDetail_LeavesCredentialNamesAloneWhenTheyAreNotTheSubmittedValue()
    {
        string detail = "The X-NuGet-ApiKey header was rejected; check NUGET_API_KEY.";

        await Assert.That(ApiKeyRedactionHelpers.RedactDetail(detail, ApiKey)).IsEqualTo(detail);
    }

    [Test]
    public async Task RedactDetail_LeavesDetailWithoutTheSubmittedValueUnchanged()
    {
        string detail = "The server rejected the request.";

        await Assert.That(ApiKeyRedactionHelpers.RedactDetail(detail, ApiKey)).IsEqualTo(detail);
    }

    [Test]
    [Arguments(null, ApiKey)]
    [Arguments("", ApiKey)]
    [Arguments("Invalid API key: secret-123.", null)]
    [Arguments("Invalid API key: secret-123.", "")]
    public async Task RedactDetail_WithoutADetailOrKey_IsUnchanged(string? detail, string? apiKey)
    {
        await Assert.That(ApiKeyRedactionHelpers.RedactDetail(detail, apiKey)).IsEqualTo(detail);
    }

    [Test]
    public async Task RedactOutcome_ChangesOnlyTheDetail()
    {
        NuGetVersion version = NuGetVersion.Parse("1.0.0");
        PackageVersionOutcome outcome = new("Demo.Package", version, PackageVersionStatus.Failed,
            PackageVersionFailureType.AuthFailed, $"Invalid API key: {ApiKey}.");

        PackageVersionOutcome redacted =
            ApiKeyRedactionHelpers.RedactOutcome(outcome, ApiKey);

        await Assert.That(redacted.PackageId).IsEqualTo("Demo.Package");
        await Assert.That(redacted.Version).IsEqualTo(version);
        await Assert.That(redacted.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(redacted.FailureType).IsEqualTo(PackageVersionFailureType.AuthFailed);
        await Assert.That(redacted.Detail).IsEqualTo("Invalid API key: [redacted].");
    }
}
