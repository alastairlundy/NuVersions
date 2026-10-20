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

namespace NuVersionsCli.Models;

/// <summary>
/// Describes a single options validation failure.
/// Any validation failure maps to the usage exit code.
/// </summary>
/// <param name="Field">The option field that failed validation.</param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record OptionsValidationFailure(string Field, string Message)
{
    /// <summary>
    /// The field name reported for a version token that looks like an unrecognized option. It
    /// stands for no option field of its own, so it gets its own name rather than borrowing one.
    /// </summary>
    public const string UnknownOptionField = "UnknownOption";
}
