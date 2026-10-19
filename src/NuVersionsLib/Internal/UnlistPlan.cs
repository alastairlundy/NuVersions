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

namespace NuVersionsLib.Internal;

/// <summary>
/// The requested versions of a package, bucketed by the listing state they were found in on the server.
/// </summary>
/// <param name="ToUnlist">Versions that exist, are listed, and therefore need an unlist request.</param>
/// <param name="AlreadyUnlisted">Versions that exist on the server but are already unlisted.</param>
/// <param name="NotOnServer">Versions the server does not know about at all.</param>
internal sealed record UnlistPlan(
    IReadOnlyList<NuGetVersion> ToUnlist,
    IReadOnlyList<NuGetVersion> AlreadyUnlisted,
    IReadOnlyList<NuGetVersion> NotOnServer);
