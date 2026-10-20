# NuVersionsLib

`NuVersionsLib` is a .NET library that reads per-version facts about NuGet packages and unlists package versions on a NuGet server. It is the library powering the `nuversions` CLI.

It supports .NET 10 and newer.

## Features

- Retrieve package versions: enumerate all versions of a package (listed and unlisted), or only the pre-release subset.
- Check listing state: resolve whether each version exists on the server and whether it is listed.
- Unlist versions: request the unlisting of specific versions, or all pre-release versions, over HTTP (the NuGet V3 delete endpoint) or the .NET SDK.
- NuGet V3 support: built on `NuGet.Protocol`.

## Installation

To use `NuVersionsLib` in your project, you can add it as a project reference or include the source files.

## Usage

### PackageVersionService

Used to query package versions and their listing state from a NuGet feed.

```csharp
using NuVersionsLib;
using NuVersionsLib.Abstractions;
using NuVersionsLib.Detectors;
using NuVersionsLib.Models;

var versionService = new PackageVersionService(new PackageAvailabilityDetector());
string nugetApiUrl = "https://api.nuget.org/v3/index.json";
string packageId = "Your.Package.Id";

// Every version of the package, with whether each is listed or already unlisted.
await foreach (PackageVersionListingInfo info in versionService.EnumerateAllPackageVersionsAsync(
    nugetApiUrl,
    string.Empty, // API key not required for public GET requests
    packageId,
    CancellationToken.None))
{
    Console.WriteLine($"{info.PackageVersion.ToNormalizedString()} " +
        (info.IsListed ? "listed" : "unlisted"));
}
```

### PackageUnlistService

Used to unlist package versions. It requires an `IPackageVersionService`, an `IPackageAvailabilityDetector`, and an `IHttpClientFactory`.

```csharp
using NuVersionsLib;
using NuVersionsLib.Abstractions;
using NuVersionsLib.Detectors;
using NuVersionsLib.Detectors.Abstractions;
using NuVersionsLib.Models;
using NuGet.Versioning;
using Microsoft.Extensions.DependencyInjection;

// Setup Dependency Injection for IHttpClientFactory
var services = new ServiceCollection();
services.AddHttpClient();
services.AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>();
services.AddSingleton<IPackageVersionService, PackageVersionService>();
services.AddKeyedSingleton<IPackageUnlistService>("http",
    static (serviceProvider, _) =>
        new PackageUnlistService(
            serviceProvider.GetRequiredService<IPackageVersionService>(),
            serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>()));
var serviceProvider = services.BuildServiceProvider();

var unlistService = serviceProvider.GetRequiredKeyedService<IPackageUnlistService>("http");

string nugetApiUrl = "https://api.nuget.org/v3/index.json"; // V3 service index URL
string nugetApiKey = "your-api-key";
string packageId = "Your.Package.Id";

await foreach (PackageVersionOutcome outcome in unlistService.RequestPackageUnlistingAsync(
    nugetApiUrl,
    nugetApiKey,
    packageId,
    [NuGetVersion.Parse("1.0.0-alpha.1")],
    CancellationToken.None))
{
    Console.WriteLine($"Version: {outcome.Version.ToNormalizedString()}, Status: {outcome.Status}");
}
```

## License

This project is licensed under the GNU LGPL v3.0 or later. See the `COPYING` and `COPYING.LESSER` files for details.
