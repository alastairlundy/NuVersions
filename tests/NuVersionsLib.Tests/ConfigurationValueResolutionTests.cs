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
/// The CLI's configuration precedence chains: option, then <c>NUVERSIONS_*</c>, then
/// <c>NUGET_*</c>, then default — for the server URL and for the API key, each with its own
/// definition of what counts as set.
/// </summary>
public class ConfigurationValueResolutionTests
{
    [Test]
    public async Task ResolveServerUrl_PrefersOptionThenPrefixedThenGenericThenDefault()
    {
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl(
                "https://option.example", "https://prefixed.example", "https://generic.example"))
            .IsEqualTo("https://option.example");
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl(
                null, "https://prefixed.example", "https://generic.example"))
            .IsEqualTo("https://prefixed.example");
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl(null, null, "https://generic.example"))
            .IsEqualTo("https://generic.example");
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl(null, null, null))
            .IsEqualTo(ConfigurationValueHelpers.DefaultServerUrl);
    }

    [Test]
    public async Task ResolveServerUrl_TreatsWhitespaceAsUnset()
    {
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl("   ", "https://prefixed.example", null))
            .IsEqualTo("https://prefixed.example");
        await Assert.That(ConfigurationValueHelpers.ResolveServerUrl("  ", " \t ", " "))
            .IsEqualTo(ConfigurationValueHelpers.DefaultServerUrl);
    }

    [Test]
    public async Task ResolveApiKey_PrefersOptionThenPrefixedThenGenericThenUnset()
    {
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey("option-key", "prefixed-key", "generic-key"))
            .IsEqualTo("option-key");
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey(null, "prefixed-key", "generic-key"))
            .IsEqualTo("prefixed-key");
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey(null, null, "generic-key"))
            .IsEqualTo("generic-key");
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey(null, null, null)).IsNull();
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey("", "", "")).IsNull();
    }

    [Test]
    public async Task ResolveApiKey_CountsWhitespaceOnlyValuesAsSet()
    {
        // Actual behaviour: the API-key chain uses IsNullOrEmpty, not IsNullOrWhiteSpace, so a
        // whitespace-only value short-circuits the chain instead of falling through.
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey("   ", "prefixed-key", null)).IsEqualTo("   ");
        await Assert.That(ConfigurationValueHelpers.ResolveApiKey(null, "  ", "generic-key")).IsEqualTo("  ");
    }
}
