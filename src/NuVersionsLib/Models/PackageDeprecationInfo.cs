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

namespace NuVersionsLib.Models;

/// <summary>
/// The deprecation metadata a NuGet server records for one package version: the three parts the
/// server serves, carried without translation or normalization.
/// </summary>
/// <remarks>
/// A version maps to this record exactly when the server serves it a deprecation object — the
/// verdict rule is presence of the object itself, so a version with no reasons still maps here
/// (decision ledger D002); a version the server serves no deprecation object for is not
/// deprecated and maps to no record at all. Advisory data is never read into this record
/// (ADR 0001).
/// </remarks>
public sealed record PackageDeprecationInfo
{
    /// <summary>
    /// The deprecation reason words exactly as the server served them (an empty list for a
    /// reason-less deprecation object). No translation layer: unknown feed words pass through
    /// unvalidated, and on nuget.org <c>legacy</c> means the version is no longer maintained.
    /// </summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>The server's custom deprecation message, or <see langword="null"/> when it serves none.</summary>
    public string? Message { get; init; }

    /// <summary>The alternate (replacement) package identifier the server recommends, or <see langword="null"/>.</summary>
    public string? AlternatePackageId { get; init; }

    /// <summary>
    /// The version range the server pairs with <see cref="AlternatePackageId"/>, in its original
    /// string form, or <see langword="null"/> when the server serves none.
    /// </summary>
    public string? AlternatePackageRange { get; init; }
}
