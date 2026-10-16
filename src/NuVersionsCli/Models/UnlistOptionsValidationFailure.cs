/*
    NuVersions - Delist (unlist) NuGet package versions from a NuGet server
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

namespace NuVersionsCli.Models;

/// <summary>
/// Describes a single options validation failure.
/// Any validation failure maps to the usage exit code.
/// </summary>
/// <param name="Field">The option field that failed validation.</param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record UnlistOptionsValidationFailure(string Field, string Message);
