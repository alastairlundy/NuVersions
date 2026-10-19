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
