/*
    NuVersionsLib
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

using System.Text.Json.Serialization;

namespace NuVersionsLib.Helpers.Models;

internal class PackageSearchResult
{
    [JsonPropertyName("sourceName")]
    internal string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("packages")]
    internal PackageSearchResultResponse[] Packages { get; set; } = [];
}
