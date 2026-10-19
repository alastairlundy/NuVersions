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
/// The immutable result of deleting one package version, mirroring the machine-readable JSON
/// payload shape field-for-field.
/// </summary>
/// <param name="PackageId">The identifier of the package the version belongs to, exactly as supplied to the run.</param>
/// <param name="Version">The version the outcome describes, carried as its normalized string value in output.</param>
/// <param name="Status">The closed outcome status for this version.</param>
public sealed record PackageVersionOutcome(string PackageId, NuGetVersion Version, PackageVersionStatus Status);
