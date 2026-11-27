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
/// The closed vocabulary naming why one unlist attempt failed.
/// </summary>
/// <remarks>
/// A plain enum on purpose, mirroring <see cref="PackageVersionStatus"/>: exhaustive switch handling
/// must be a compiler feature, so new members break compilation at every emission and rendering site
/// until they are handled. Causes this vocabulary cannot classify report <see cref="Unknown"/>.
/// </remarks>
public enum PackageVersionFailureType
{
    /// <summary>The server rejected the attempt for missing or invalid credentials (the auth cause family).</summary>
    AuthFailed,

    /// <summary>The server faulted while handling the request (the server-error cause family, such as a 5xx response).</summary>
    ServerError,

    /// <summary>The server could not be reached or could not serve the request (the unavailability cause family, such as connection, DNS, or timeout faults).</summary>
    Unavailable,

    /// <summary>The server throttled the attempt (the rate-limit cause family).</summary>
    RateLimited,

    /// <summary>The cause does not classify into any of the other families; unclassifiable causes report this member.</summary>
    Unknown,
}
