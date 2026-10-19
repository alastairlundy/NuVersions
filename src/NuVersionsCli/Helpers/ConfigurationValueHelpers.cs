/*
    NuVersions
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace NuVersionsCli.Helpers;

/// <summary>
/// The CLI's configuration precedence chains: the documented lookup order for the server URL and
/// for the API key, shared by every command that reads them.
/// </summary>
/// <remarks>
/// <para>
/// Each chain is the explicit option first, then the <c>NUVERSIONS_</c>-prefixed environment
/// variable, then the generic <c>NUGET_</c> one, then the built-in default. The two chains differ
/// in what counts as set: the server URL treats whitespace as unset, the API key does not. Both
/// match the README Configuration section.
/// </para>
/// <para>
/// The helpers are pure: they take plain values and return a plain value, so the environment and
/// <c>IConfiguration</c> are read by the commands, never from here. The names the commands read
/// them from are the constants below, so the chain and its sources stay in one place.
/// </para>
/// </remarks>
internal static class ConfigurationValueHelpers
{
    /// <summary>
    /// The configuration key the <c>NUVERSIONS_</c>-prefixed environment variables land under
    /// (the prefix is applied when the configuration is built).
    /// </summary>
    internal const string ApiKeyConfigurationKey = "NuGetApiKey";

    /// <summary>
    /// The configuration key the <c>NUVERSIONS_</c>-prefixed server URL environment variable lands
    /// under (the prefix is applied when the configuration is built).
    /// </summary>
    internal const string ServerUrlConfigurationKey = "NuGetServerUrl";

    /// <summary>
    /// The generic environment variable holding the API key, used when neither the option nor the
    /// prefixed variable supplies one.
    /// </summary>
    internal const string GenericApiKeyEnvironmentVariable = "NUGET_API_KEY";

    /// <summary>
    /// The generic environment variable holding the server URL, used when neither the option nor the
    /// prefixed variable supplies one.
    /// </summary>
    internal const string GenericServerUrlEnvironmentVariable = "NUGET_SERVER_URL";

    /// <summary>
    /// The built-in default when no server URL is supplied at any level.
    /// </summary>
    internal const string DefaultServerUrl = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Resolves the server URL through the documented four-step precedence chain:
    /// explicit <c>--server-url</c> option first, then the
    /// <c>NUVERSIONS_NuGetServerUrl</c> prefixed variable, then the generic
    /// <c>NUGET_SERVER_URL</c> variable, then the built-in default.
    /// Whitespace counts as unset.
    /// </summary>
    internal static string ResolveServerUrl(string? optionValue, string? prefixedValue, string? genericValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        if (!string.IsNullOrWhiteSpace(prefixedValue))
        {
            return prefixedValue;
        }

        if (!string.IsNullOrWhiteSpace(genericValue))
        {
            return genericValue;
        }

        return DefaultServerUrl;
    }

    /// <summary>
    /// Resolves the API key through the documented chain:
    /// explicit <c>--api-key</c> option first, then the
    /// <c>NUVERSIONS_NuGetApiKey</c> prefixed variable, then the generic
    /// <c>NUGET_API_KEY</c> variable, then unset (null — missing key is a
    /// validation failure unless dry-run mode skips it).
    /// Empty counts as unset.
    /// </summary>
    internal static string? ResolveApiKey(string? optionValue, string? prefixedValue, string? genericValue)
    {
        if (!string.IsNullOrEmpty(optionValue))
        {
            return optionValue;
        }

        if (!string.IsNullOrEmpty(prefixedValue))
        {
            return prefixedValue;
        }

        if (!string.IsNullOrEmpty(genericValue))
        {
            return genericValue;
        }

        return null;
    }
}
