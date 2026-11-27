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
/// The single CLI-side renderer for the closed <see cref="PackageVersionFailureType"/> vocabulary.
/// </summary>
/// <remarks>
/// Mirrors <see cref="PackageVersionStatusExtensions"/>: every member of the closed vocabulary is
/// spelled out explicitly, so no output mode can silently render an unknown failure type. C#
/// requires a catch-all arm on an enum switch expression (CS8524), so the unnamed arm throws
/// instead of rendering: a member added to the enum reaches it loudly and instructs the
/// maintainer to add its kebab-case spelling.
/// </remarks>
internal static class PackageVersionFailureTypeExtensions
{
    extension(PackageVersionFailureType failureType)
    {
        internal string ToKebabCase() => failureType switch
        {
            PackageVersionFailureType.AuthFailed => "auth-failed",
            PackageVersionFailureType.ServerError => "server-error",
            PackageVersionFailureType.Unavailable => "unavailable",
            PackageVersionFailureType.RateLimited => "rate-limited",
            PackageVersionFailureType.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(failureType), failureType,
                string.Format(Resources.Exceptions_FailureType_Unhandled, nameof(PackageVersionFailureType), nameof(ToKebabCase))),
        };
    }
}
