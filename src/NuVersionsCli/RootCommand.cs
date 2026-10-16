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

namespace NuVersionsCli;

/// <summary>
/// The root <c>nuversions</c> command. A small toolbox over per-version NuGet facts:
/// <c>show</c> reads the listing state of every version of a package, <c>unlist</c> delists
/// versions. Both share one exit-code shapes and one NDJSON schema.
/// </summary>
/// <remarks>
/// Invoked bare, the root prints the command hierarchy; the DotMake parser routes every
/// subcommand token to its child, so a genuine typo is a parser error used as the documented
/// command-line failure exit.
/// </remarks>
[CliCommand(Description = "Show and unlist NuGet package versions.",
    Children = [typeof(ShowCommand), typeof(UnlistCommand)])]
public class RootCommand
{
    public void Run(CliContext context)
    {
        if (!context.Result.HasArgs)
        {
            context.ShowHierarchy();
        }
        else
        {
            context.ShowValues();
        }
    }
}
