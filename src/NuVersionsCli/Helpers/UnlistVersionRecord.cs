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
/// The unlist JSON line's five fields in their fixed order, serialized camelCase through
/// <see cref="NdjsonJsonContext"/> with no rename mappings: the failure pair is the explicit JSON
/// <see langword="null"/> on every outcome that carries no failure data, so every unlist line
/// carries the same key set.
/// </summary>
/// <param name="Package">The package the version belongs to.</param>
/// <param name="Version">The version the line describes, as its normalized string value.</param>
/// <param name="Status">The already-rendered status member.</param>
/// <param name="FailureType">The already-rendered failure type, or <see langword="null"/> when the outcome carries none.</param>
/// <param name="FailureMessage">The verbatim failure detail, never truncated, or <see langword="null"/> when the outcome carries none.</param>
internal sealed record UnlistVersionRecord(
    string Package,
    string Version,
    string Status,
    string? FailureType,
    string? FailureMessage);
