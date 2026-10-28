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
/// The show <c>deprecation</c> subtree's <c>alternatePackage</c> object: the recommended package id
/// plus the version range the server pairs with it, <c>range</c> explicit even when absent.
/// </summary>
/// <param name="Id">The alternate package identifier the server recommends.</param>
/// <param name="Range">The version range the server pairs with the id, or <see langword="null"/>.</param>
internal sealed record ShowAlternatePackageRecord(string Id, string? Range);
