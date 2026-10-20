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

using CliInvoke.Core;
using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NuVersionsLib.Detectors.Abstractions;

Cli.Ext.ConfigureServices(services =>
{
    services.AddHttpClient()
        .AddCliInvoke(ServiceLifetime.Singleton)
        .AddKeyedSingleton<IPackageAvailabilityDetector>("http",
            new HttpPackageAvailabilityDetector())
        .AddKeyedSingleton<IPackageAvailabilityDetector>("sdk", static (serviceProvider, _) =>
            new NetSdkPackageAvailabilityDetector(serviceProvider.GetRequiredService<IProcessInvoker>()))
        .AddSingleton<IPackageVersionService, PackageVersionService>()
        // Composing services keyed to the exact vocabulary the --backend option accepts; each
        // resolves its own transport collaborator (the HTTP client factory or the process
        // invoker), and both in-box backends detect 429 themselves, so the fail-fast state
        // machine lives inside the services.
        .AddKeyedSingleton<IPackageUnlistService>("http", static (serviceProvider, _) =>
            new HttpPackageUnlistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()))
        .AddKeyedSingleton<IPackageUnlistService>("sdk", static (serviceProvider, _) =>
            new NetSdkPackageUnlistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredService<IProcessInvoker>()));

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
}).ConfigureAwait(true);
