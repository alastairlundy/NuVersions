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

namespace NuVersionsLib.Tests.TransportDoubles;

/// <summary>
/// An <see cref="IHttpClientFactory"/> double handing every client back over one shared
/// <see cref="FakeScriptedHttpTransportHandler"/>, so all HTTP dispatch a test drives lands on the
/// same scripted responses and the same request log.
/// </summary>
/// <remarks>
/// Each <see cref="CreateClient"/> call returns a fresh client, matching the real factory's contract:
/// the production services configure per-client defaults (base address, API-key header, timeout) that
/// would otherwise bleed between dispatches on a reused instance.
/// </remarks>
internal sealed class FakeHttpClientFactory(FakeScriptedHttpTransportHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
