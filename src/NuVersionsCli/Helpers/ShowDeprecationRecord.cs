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
/// The show JSON line's <c>deprecation</c> subtree: <c>reasons</c> (verbatim words), <c>message</c>,
/// and <c>alternatePackage</c>, serialized camelCase through <see cref="NdjsonJsonContext"/> with no
/// rename mappings (decision ledger T004).
/// </summary>
/// <param name="Reasons">The server's reason words verbatim, empty for a reason-less object.</param>
/// <param name="Message">The server's custom deprecation message, or <see langword="null"/>.</param>
/// <param name="AlternatePackage">The recommended replacement package, or <see langword="null"/>.</param>
internal sealed record ShowDeprecationRecord(string[] Reasons, string? Message, ShowAlternatePackageRecord? AlternatePackage);
