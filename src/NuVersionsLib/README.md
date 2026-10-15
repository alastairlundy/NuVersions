# NuVersionsLib

`NuVersionsLib` is a .NET library that reads per-version facts about NuGet packages and unlists (delists) package versions from a NuGet server. It is the library powering the `nuversions` CLI.

It supports .NET 10 and newer.

## Features

- **Retrieve Package Versions**: enumerate all versions of a package (listed and unlisted), or only the pre-release subset.
- **Check Listing State**: resolve whether each version exists on the server and whether it is listed.
- **Unlist Versions**: request the unlisting of specific versions, or all pre-release versions, through HTTP (NuGet V3 delete endpoint) or the .NET SDK.
- **Support for NuGet V3**: built on top of `NuGet.Protocol` for reliable interaction with NuGet repositories.

## Installation

To use `NuVersionsLib` in your project, you can add it as a project reference or include the source files.

## Usage

### PackageVersionService

Used to query package versions and their listing state from a NuGet feed.

```csharp
using NuVersionsLib;
using NuGet.Versioning;

var versionService = new PackageVersionService();
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

### PackageDelistService

Used to unlist package versions. It requires an `IHttpClientFactory`, an `IPackageAvailabilityDetector`, an `IPackageVersionService`, and an `IPackageVersionDeleter` backend.

```csharp
using NuVersionsLib;
using Microsoft.Extensions.DependencyInjection;

// Setup Dependency Injection for IHttpClientFactory
var services = new ServiceCollection();
services.AddHttpClient();
services.AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>();
services.AddSingleton<IPackageVersionService, PackageVersionService>();
services.AddKeyedSingleton<IPackageVersionDeleter, HttpPackageVersionDeleter>("http");
services.AddKeyedSingleton<IPackageDelistService>("http",
    static (serviceProvider, _) =>
        new PackageDelistService(
            serviceProvider.GetRequiredService<IPackageVersionService>(),
            serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
            serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("http")));
var serviceProvider = services.BuildServiceProvider();

var delistService = serviceProvider.GetRequiredKeyedService<IPackageDelistService>("http");

string nugetApiUrl = "https://api.nuget.org/v3/index.json"; // V3 service index URL
string nugetApiKey = "your-api-key";
string packageId = "Your.Package.Id";

await foreach (PackageVersionOutcome outcome in delistService.RequestPackageDelistingAsync(
    nugetApiUrl,
    nugetApiKey,
    packageId,
    CancellationToken.None))
{
    Console.WriteLine($"Version: {outcome.Version.ToNormalizedString()}, Status: {outcome.Status}");
}
```

## License

This project is licensed under the GNU LGPL v3.0 or later. See the `COPYING` and `COPYING.LESSER` files for details.
