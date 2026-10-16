using NuGet.Versioning;

namespace NuVersionsCli;

/// <summary>
/// One line of the dry-run plan: a requested version and the bucket it landed in.
/// </summary>
internal sealed record DryRunPlanLine(NuGetVersion Version, DryRunBucket Bucket);
