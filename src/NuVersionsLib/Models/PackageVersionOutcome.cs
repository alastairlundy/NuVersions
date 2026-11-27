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
/// <param name="FailureType">The failure type carrying the reason for a failed attempt, or <see langword="null"/> for every status that carries no failure reason.</param>
/// <param name="Detail">The verbatim failure detail for a failed attempt, or <see langword="null"/> for every status that carries no failure reason.</param>
/// <remarks>
/// <see cref="PackageVersionStatus.Failed"/> and <see cref="PackageVersionStatus.RateLimited"/> outcomes always
/// carry both <paramref name="FailureType"/> and <paramref name="Detail"/>; every other status carries nulls for
/// both. <paramref name="Detail"/> is raw verbatim content, uncut at this boundary - HTTP backends put the response
/// status line plus body text there and SDK backends put the child process's combined standard output and error
/// there; when no response body or process output exists, the detail is the status line, exit-code fallback, or
/// exception message. Truncation happens only at render time, never here. The new members default to null so
/// existing three-argument construction sites keep compiling unchanged.
/// </remarks>
public sealed record PackageVersionOutcome(
    string PackageId,
    NuGetVersion Version,
    PackageVersionStatus Status,
    PackageVersionFailureType? FailureType = null,
    string? Detail = null);
