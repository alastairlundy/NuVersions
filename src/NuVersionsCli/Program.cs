/*
    NuVersions - Delist (unlist) NuGet package versions from a NuGet server
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

using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NuVersionsLib.Detectors.Abstractions;

Cli.Ext.ConfigureServices(services =>
{
    services.AddHttpClient()
        .AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>()
        .AddSingleton<IPackageVersionService, PackageVersionService>()
        // Per-version delete backends keyed to the exact vocabulary the --backend option accepts.
        .AddKeyedSingleton<IPackageVersionDeleter, HttpPackageVersionDeleter>("http")
        .AddKeyedSingleton<IPackageVersionDeleter, SdkPackageVersionDeleter>("sdk")
        // Each composing service receives its keyed backend; both in-box backends detect 429
        // themselves, so the fail-fast state machine lives inside the services.
        .AddKeyedSingleton<IPackageUnlistService>("http", static (serviceProvider, _) =>
            new PackageUnlistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("http")))
        .AddKeyedSingleton<IPackageUnlistService>("sdk", static (serviceProvider, _) =>
            new NetSdkPackageUnlistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("sdk")))
        .AddCliInvoke(ServiceLifetime.Singleton);

    ConfigurationBuilder configurationBuilder = new();
    configurationBuilder.AddEnvironmentVariables(prefix: "NUVERSIONS_");
    IConfiguration configuration = configurationBuilder.Build();

    services.AddSingleton(configuration);
});

return await Cli.RunAsync<RootCommand>(args, new CliSettings
{
    EnableDefaultExceptionHandler = true,
    EnableSuggestDirective = true,
    EnableEnvironmentVariablesDirective = true
});
