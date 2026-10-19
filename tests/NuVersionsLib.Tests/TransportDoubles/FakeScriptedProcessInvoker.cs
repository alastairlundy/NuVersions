/*
    NuVersionsLib.Tests
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using CliInvoke.Core;

namespace NuVersionsLib.Tests.TransportDoubles;

/// <summary>
/// An <see cref="IProcessInvoker"/> double replaying a scripted queue of
/// <see cref="BufferedProcessResult"/>s — exit code plus stdout and stderr per call — and counting
/// every buffered run it was asked to execute. With an empty script it throws, which doubles as
/// proof that no dispatch happened at all.
/// </summary>
/// <remarks>
/// Like the HTTP double, this fakes dispatch at the transport edge:
/// it returns process results, never
/// outcomes, so the SDK output-classification code under test is the real
/// <see cref="NetSdkPackageUnlistService"/> mapping.
/// <para>
/// The production flow converts every non-cancellation fault into a
/// <see cref="PackageVersionStatus.Failed"/> outcome, so the throw an unscripted run raises never
/// reaches the test. Such a run is therefore also recorded in <see cref="UnscriptedRuns"/>, which is
/// what lets a test name the dispatch that should not have happened.
/// </para>
/// </remarks>
internal sealed class FakeScriptedProcessInvoker : IProcessInvoker
{
    private readonly Queue<BufferedProcessResult> _results = new();
    private readonly Queue<Exception> _exceptions = new();

    /// <summary>How many buffered runs were dispatched to this invoker.</summary>
    public int BufferedCallCount { get; private set; }

    /// <summary>
    /// The process runs that reached the invoker with no scripted result, in arrival order.
    /// </summary>
    /// <remarks>
    /// A test asserts this list is empty. The throw the invoker raises for such a run is converted
    /// to a <see cref="PackageVersionStatus.Failed"/> outcome by the production flow, so this record
    /// — not the exception — is what makes the stray dispatch visible.
    /// </remarks>
    public List<ProcessConfiguration> UnscriptedRuns { get; } = [];

    /// <summary>Scripts the next <see cref="BufferedProcessResult"/> a dispatch returns.</summary>
    public void Enqueue(BufferedProcessResult result) => _results.Enqueue(result);

    /// <summary>Scripts a thrown <paramref name="exception"/> for the next dispatch.</summary>
    public void EnqueueException(Exception exception) => _exceptions.Enqueue(exception);

    public Task<BufferedProcessResult> ExecuteBufferedAsync(ProcessConfiguration processConfiguration,
        ProcessExitConfiguration? processExitConfiguration, CancellationToken cancellationToken = default)
    {
        BufferedCallCount++;

        if (_exceptions.Count > 0)
        {
            throw _exceptions.Dequeue();
        }

        if (_results.Count == 0)
        {
            UnscriptedRuns.Add(processConfiguration);

            throw new InvalidOperationException(
                $"No scripted result left for '{processConfiguration.TargetFilePath}'.");
        }

        return Task.FromResult(_results.Dequeue());
    }

    public Task<ProcessResult> ExecuteAsync(ProcessConfiguration processConfiguration,
        ProcessExitConfiguration? processExitConfiguration, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeScriptedProcessInvoker only scripts buffered runs.");
}
