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
/// The CLI's truncation policy: one cut-at-cap helper behind the two stream-specific entry points,
/// plus the caps and the literal marker a cut appends.
/// </summary>
/// <remarks>
/// A value at or below its cap — and every value under <c>--verbose</c> — comes back unchanged, so
/// only a real cut ever grows the <c>...</c> marker. The two caps differ per stream (stderr's detail
/// is bounded tighter than stdout's free text) and each command picks its entry point, so a cap can
/// never be applied to the wrong stream.
/// </remarks>
internal static class TruncationHelpers
{
    /// <summary>The stderr cap: the failure-detail value's maximum length before the attribution prefix is added.</summary>
    internal const int StderrDetailCap = 2000;

    /// <summary>The stdout cap: the maximum length of the free text stdout bounds (show's deprecation message).</summary>
    internal const int StdoutMessageCap = 5000;

    /// <summary>The literal appended whenever — and only whenever — a cut happened.</summary>
    private const string TruncationMarker = "...";

    /// <summary>
    /// Bounds an stderr detail at <see cref="StderrDetailCap"/> characters, appending
    /// <see cref="TruncationMarker"/> when the value was cut. <c>verbose</c> lifts the cap
    /// entirely: the input is returned unchanged.
    /// </summary>
    /// <param name="value">The verbatim detail to bound.</param>
    /// <param name="verbose">Whether <c>--verbose</c> lifts the cap.</param>
    internal static string TruncateForStderr(string value, bool verbose) =>
        Truncate(value, StderrDetailCap, verbose);

    /// <summary>
    /// Bounds stdout free text at <see cref="StdoutMessageCap"/> characters, appending
    /// <see cref="TruncationMarker"/> when the value was cut. <c>verbose</c> lifts the cap
    /// entirely: the input is returned unchanged.
    /// </summary>
    /// <param name="value">The free text to bound.</param>
    /// <param name="verbose">Whether <c>--verbose</c> lifts the cap.</param>
    internal static string TruncateForStdout(string value, bool verbose) =>
        Truncate(value, StdoutMessageCap, verbose);

    /// <summary>
    /// The whole policy: unchanged when <paramref name="verbose"/> is set or the value already fits
    /// its cap, otherwise the value cut at <paramref name="cap"/> plus the marker.
    /// </summary>
    private static string Truncate(string value, int cap, bool verbose) =>
        verbose || value.Length <= cap
            ? value
            : string.Concat(value.AsSpan(0, cap), TruncationMarker);
}
