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
/// The closed per-version outcome vocabulary crossing the delete seam.
/// </summary>
/// <remarks>
/// A plain enum on purpose: exhaustive switch handling must be a compiler feature, so new members
/// break compilation at every emission and exit-code mapping site until they are handled.
/// </remarks>
public enum PackageVersionStatus
{
    /// <summary>The version was deleted (unlisted) by this call.</summary>
    Unlisted,

    /// <summary>The version was already unlisted before this call.</summary>
    AlreadyUnlisted,

    /// <summary>The server does not have this version.</summary>
    NotOnServer,

    /// <summary>The server rate-limited the request.</summary>
    RateLimited,

    /// <summary>The delete attempt failed.</summary>
    Failed,

    /// <summary>The delete attempt was never made.</summary>
    NotAttempted,
}
