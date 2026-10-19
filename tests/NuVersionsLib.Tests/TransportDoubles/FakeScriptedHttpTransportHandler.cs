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

using System.Net;

namespace NuVersionsLib.Tests;

/// <summary>One request as observed by <see cref="FakeScriptedHttpTransportHandler"/>.</summary>
internal sealed record DispatchedRequest(string Method, string RequestUri);

/// <summary>
/// An <see cref="HttpMessageHandler"/> whose responses are scripted per request path: a test names
/// the path it expects the production code to call, scripts the status code (or thrown fault) that
/// path answers with, and then asserts on the outcomes the real mapping code produces. Every request
/// that reaches the handler is recorded so tests can prove exactly which dispatches left the
/// process; an unscripted path throws, which doubles as a tripwire for dispatches a test never
/// intended to allow.
/// </summary>
/// <remarks>
/// <para>
/// This double fakes dispatch at the transport edge:
/// it never produces a <see cref="Models.PackageVersionOutcome"/> itself. The status-code-to-outcome
/// mapping under test lives in the production services.
/// </para>
/// <para>
/// The service index is deliberately not served here. <see cref="PackageUnlistService"/> resolves it
/// through NuGet.Protocol's own internal HTTP stack — not through the injected client factory — so
/// <see cref="LoopbackServiceIndexServer"/> serves the real index document on loopback, and this
/// handler scripts the publish-endpoint responses that arrive through the factory. A 404-interpretation
/// test therefore always runs against a genuinely advertised publish endpoint (the
/// 404-only-when-publish-endpoint-was-advertised rule), never against a stubbed flag.
/// </para>
/// <para>
/// An unscripted path throws, but the production flow converts every non-cancellation fault into a
/// <see cref="PackageVersionStatus.Failed"/> outcome, so that throw never reaches the test. The
/// stray request is therefore also recorded in <see cref="UnscriptedRequests"/>, which is what
/// lets a test name the dispatch that should not have happened.
/// </para>
/// </remarks>
internal sealed class FakeScriptedHttpTransportHandler : HttpMessageHandler
{
    private readonly Dictionary<string, object> _responses = new(StringComparer.Ordinal);

    /// <summary>Every request that reached the handler, in arrival order.</summary>
    public List<DispatchedRequest> DispatchedRequests { get; } = [];

    /// <summary>
    /// The requests that reached the handler with no scripted response, in arrival order.
    /// </summary>
    /// <remarks>
    /// A test asserts this list is empty. The throw the handler raises for such a request is
    /// converted to a <see cref="PackageVersionStatus.Failed"/> outcome by the production flow, so
    /// this record — not the exception — is what makes the stray dispatch visible.
    /// </remarks>
    public List<DispatchedRequest> UnscriptedRequests { get; } = [];

    /// <summary>How many requests reached the handler.</summary>
    public int DispatchCount => DispatchedRequests.Count;

    /// <summary>Scripts <paramref name="statusCode"/> as the answer for requests to <paramref name="path"/>.</summary>
    public void ScriptStatusCode(string path, HttpStatusCode statusCode)
        => _responses[path] = statusCode;

    /// <summary>Scripts a thrown <paramref name="exception"/> for requests to <paramref name="path"/>.</summary>
    public void ScriptFault(string path, Exception exception)
        => _responses[path] = exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "/";

        DispatchedRequest dispatched = new(request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty);

        DispatchedRequests.Add(dispatched);

        if (!_responses.TryGetValue(path, out object? scripted))
        {
            UnscriptedRequests.Add(dispatched);

            throw new InvalidOperationException(
                $"No scripted response for {request.Method} {request.RequestUri}.");
        }

        if (scripted is Exception exception)
        {
            throw exception;
        }

        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)scripted));
    }
}
