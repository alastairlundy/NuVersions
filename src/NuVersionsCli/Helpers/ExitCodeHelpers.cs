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

using NuGet.Protocol.Core.Types;

namespace NuVersionsCli.Helpers;

internal static class ExitCodeHelpers
{
    /// <summary>Every requested version was unlisted or was already unlisted.</summary>
    internal const int ExitSuccess = 0;

    /// <summary>At least one version failed, including a version the server does not have.</summary>
    internal const int ExitFailure = 1;

    /// <summary>Usage or validation failure: bad options, package not found, invalid version string.</summary>
    internal const int ExitUsage = 2;

    /// <summary>The server rate-limited the run and the composing service stopped fail-fast.</summary>
    internal const int ExitRateLimited = 3;

    /// <summary>Remaining dispatches were cancelled before they were attempted.</summary>
    internal const int ExitCancelled = 4;

    /// <summary>The run was interrupted (Ctrl-C).</summary>
    internal const int ExitInterrupted = 130;

    /// <summary>
    /// Maps a per-version outcome to its documented exit code. Every member of the closed vocabulary
    /// is handled explicitly: a member added to the enum reaches the throwing catch-all
    /// arm — C# forces that arm on an enum switch expression (CS8524) — and fails loudly here and in
    /// <c>ToKebabCase</c> until its exit code is decided.
    /// </summary>
    internal static int ToExitCode(PackageVersionStatus status) => status switch
    {
        PackageVersionStatus.Unlisted => ExitSuccess,
        PackageVersionStatus.AlreadyUnlisted => ExitSuccess,
        PackageVersionStatus.NotOnServer => ExitFailure,
        PackageVersionStatus.RateLimited => ExitRateLimited,
        PackageVersionStatus.Failed => ExitFailure,
        PackageVersionStatus.NotAttempted => ExitCancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status,
            string.Format(Resources.Exceptions_ExitCodes_UnhandledStatus, nameof(PackageVersionStatus), nameof(ToExitCode))),
    };

    /// <summary>
    /// Maps a command-boundary exception to its documented exit code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping is the CLI's failure contract, so it lives here rather than in a catch clause
    /// per command: an argument failure is a usage error, and a feed-side failure is a plain
    /// failure. Cancellation is deliberately absent — it maps to its own code and prints nothing.
    /// </para>
    /// <para>
    /// An exception this method does not recognise returns <see langword="false"/>, leaving the
    /// caller to let it propagate: an unexpected fault still crashes with its stack trace.
    /// </para>
    /// </remarks>
    /// <param name="exception">The exception that escaped the command body.</param>
    /// <param name="exitCode">The documented exit code, when the exception is recognised.</param>
    /// <returns><see langword="true"/> when the exception maps to a documented exit code.</returns>
    internal static bool TryFromException(Exception exception, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ArgumentException)
        {
            exitCode = ExitUsage;
            return true;
        }

        if (exception is NuGetProtocolException or HttpRequestException or InvalidOperationException)
        {
            // HttpRequestException is the HTTP transport's own failure (a 4xx/5xx body or a dead
            // connection); without this arm a feed-side failure would escape the command catch and
            // surface as an unhandled stack trace instead of the documented one-line diagnostic.
            exitCode = ExitFailure;
            return true;
        }

        // Unused when this method returns false: the caller lets the exception propagate.
        exitCode = default;
        return false;
    }

    /// <summary>
    /// Folds one outcome's code into the run's code. Rate-limited outranks cancelled, which outranks
    /// per-version failure: a rate-limit is the root cause of the NotAttempted outcomes behind it.
    /// </summary>
    internal static int MergeExitCode(int current, int candidate)
    {
        if (current == ExitRateLimited || candidate == ExitRateLimited)
        {
            return ExitRateLimited;
        }

        if (current == ExitCancelled || candidate == ExitCancelled)
        {
            return ExitCancelled;
        }

        if (current == ExitFailure || candidate == ExitFailure)
        {
            return ExitFailure;
        }

        return ExitSuccess;
    }
}
