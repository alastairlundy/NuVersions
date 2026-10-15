namespace NuVersionsLib.Detectors;

public interface IPackageAvailabilityDetector
{
    Task<bool> CheckPackageExistsAsync(string nugetApiUrl, string packageId, CancellationToken cancellationToken);
    
}