/*
    NuVersionsLib.FuzzTests
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

using System.Text.Json;
using NuGet.Versioning;
using NuVersionsCli.Helpers;
using NuVersionsLib.Models;
using TUnit.FsCheck;

namespace NuVersionsLib.FuzzTests;

/// <summary>
/// The failure-reason output contract as properties: the closed failure vocabulary renders its
/// documented kebab-case words through the exhaustive switch, the stream caps bound an
/// arbitrary-length detail by default and lift under <c>--verbose</c>, and the JSON
/// <c>failureMessage</c> carries every character at any length.
/// </summary>
/// <remarks>
/// These are the invariants users depend on rather than the examples a deterministic suite
/// spells out, so they run as properties in the suite CI executes on every push.
/// </remarks>
public class FailureContractProperties
{
    /// <summary>The stderr detail-value cap documented in README's Truncation section.</summary>
    private const int StderrCap = 2000;

    /// <summary>The stdout message cap documented in README's Truncation section.</summary>
    private const int StdoutCap = 5000;

    /// <summary>The literal a cut appends, and only a cut.</summary>
    private const string CutMarker = "...";

    [Test]
    [Arguments(PackageVersionFailureType.AuthFailed, "auth-failed")]
    [Arguments(PackageVersionFailureType.ServerError, "server-error")]
    [Arguments(PackageVersionFailureType.Unavailable, "unavailable")]
    [Arguments(PackageVersionFailureType.RateLimited, "rate-limited")]
    [Arguments(PackageVersionFailureType.Unknown, "unknown")]
    public async Task ToKebabCase_SpellsEveryFailureTypeMemberExactly(
        PackageVersionFailureType failureType, string expected)
    {
        await Assert.That(failureType.ToKebabCase()).IsEqualTo(expected);
    }

    [Test]
    public async Task ToKebabCase_HandlesEveryMemberAndRejectsUnknownValues()
    {
        PackageVersionFailureType[] members = Enum.GetValues<PackageVersionFailureType>();

        // The vocabulary is closed at five members; a sixth must not reach any output mode
        // until this suite documents its spelling above.
        await Assert.That(members.Length).IsEqualTo(5);

        foreach (PackageVersionFailureType member in members)
        {
            // The switch's catch-all arm throws, so reaching a rendered word proves the member
            // has an explicit arm rather than falling through to the default.
            string rendered = member.ToKebabCase();

            await Assert.That(rendered).IsNotEmpty();
            await Assert.That(rendered.All(character => character is (>= 'a' and <= 'z') or '-')).IsTrue();
            await Assert.That(rendered.StartsWith('-')).IsFalse();
            await Assert.That(rendered.EndsWith('-')).IsFalse();
        }

        // Values outside the vocabulary fail loudly instead of rendering an unknown word.
        bool rejectedUnknownValue = false;

        try
        {
            _ = ((PackageVersionFailureType)int.MaxValue).ToKebabCase();
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedUnknownValue = true;
        }

        await Assert.That(rejectedUnknownValue).IsTrue();
    }

    [Test, FsCheckProperty]
    public bool Truncate_DefaultBoundsDetailAtTheStreamCapsWithMarker(string seed, int length)
    {
        // Each cap's boundary lengths are checked on every run: whether a cut happens turns on
        // exactly at the cap, so the generated length alone must not decide coverage.
        string[] details =
        [
            ArbitraryDetail(seed, length),
            DetailOfLength(seed, StderrCap),
            DetailOfLength(seed, StderrCap + 1),
            DetailOfLength(seed, StdoutCap),
            DetailOfLength(seed, StdoutCap + 1),
        ];

        foreach (string detail in details)
        {
            if (!IsBoundedAtCap(TruncationHelpers.TruncateForStderr(detail, verbose: false), detail, StderrCap))
                return false;

            if (!IsBoundedAtCap(TruncationHelpers.TruncateForStdout(detail, verbose: false), detail, StdoutCap))
                return false;
        }

        return true;
    }

    [Test, FsCheckProperty]
    public bool Truncate_VerboseLiftsBothStreamCaps(string seed, int length)
    {
        // One detail at the arbitrary length and one forced past both caps: the lift only
        // matters where a cut would otherwise happen, so that branch is exercised every run.
        string[] details =
        [
            ArbitraryDetail(seed, length),
            DetailOfLength(seed, StdoutCap + 1 + (length & 0x3FF)),
        ];

        foreach (string detail in details)
        {
            if (TruncationHelpers.TruncateForStderr(detail, verbose: true) != detail)
                return false;

            if (TruncationHelpers.TruncateForStdout(detail, verbose: true) != detail)
                return false;
        }

        return true;
    }

    [Test, FsCheckProperty]
    public bool ToPackageVersionLine_FailureMessageNeverTruncated(string seed, int length)
    {
        // The same lengths that would be cut on stderr: the JSON field must still carry the
        // detail verbatim, at any length.
        string[] messages =
        [
            ArbitraryDetail(seed, length),
            DetailOfLength(seed, StdoutCap + 1 + (length & 0x3FF)),
        ];

        NuGetVersion version = NuGetVersion.Parse("1.0.0");

        foreach (string message in messages)
        {
            string line = NdjsonHelpers.ToPackageVersionLine("Any.Package", version,
                "failed", "auth-failed", message);

            using JsonDocument document = JsonDocument.Parse(line);

            if (document.RootElement.GetProperty("failureMessage").GetString() != message)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The default-mode cap contract for one stream: at or under the cap the detail comes back
    /// unchanged with no marker, past it the detail is cut at exactly the cap and the marker
    /// is appended.
    /// </summary>
    private static bool IsBoundedAtCap(string bounded, string detail, int cap) =>
        detail.Length <= cap
            ? bounded == detail
            : bounded == detail[..cap] + CutMarker;

    /// <summary>
    /// A detail at an arbitrary length: generated strings stay small, so the length is
    /// synthesized from the seed and the integer to reach past both caps.
    /// </summary>
    private static string ArbitraryDetail(string seed, int length)
    {
        int target = (seed.Length * 97 + (length & 0xFFFF)) % 10_001;

        return DetailOfLength(seed, target);
    }

    /// <summary>A detail of exactly <paramref name="length"/> characters, repeating the seed to reach it.</summary>
    private static string DetailOfLength(string seed, int length)
    {
        string source = seed.Length == 0 ? "x" : seed;
        int repeats = length / source.Length + 1;

        return string.Concat(Enumerable.Repeat(source, repeats))[..length];
    }
}
