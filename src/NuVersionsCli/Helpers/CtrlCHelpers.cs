/*
    NuVersions
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

namespace NuVersionsCli.Helpers;

/// <summary>
/// The CLI's Ctrl-C handling: the handler that cancels a run's token, and the disposal that
/// unsubscribes it.
/// </summary>
/// <remarks>
/// Both commands wire the same handler around the same cancellation source, so the wiring lives
/// here instead of being copied into each command. Disposing the returned registration
/// unsubscribes the handler, which replaces the explicit teardown each command used to repeat.
/// </remarks>
internal static class CtrlCHelpers
{
    /// <summary>
    /// Subscribes a Ctrl-C handler that cancels <paramref name="cancellationSource"/> and
    /// suppresses the default termination. Dispose the returned value to unsubscribe.
    /// </summary>
    internal static IDisposable Register(CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(cancellationSource);

        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += handler;

        return new Registration(handler);
    }

    /// <summary>Unsubscribes the handler it holds, at most once.</summary>
    private sealed class Registration : IDisposable
    {
        private ConsoleCancelEventHandler? _handler;

        internal Registration(ConsoleCancelEventHandler handler) => _handler = handler;

        public void Dispose()
        {
            ConsoleCancelEventHandler? handler = _handler;

            if (handler is null)
            {
                return;
            }

            _handler = null;
            Console.CancelKeyPress -= handler;
        }
    }
}
