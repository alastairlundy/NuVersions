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
