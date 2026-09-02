using SpotifyTrackHonorific.Spotify;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyTrackHonorific.Formatting;

internal static class TitleTemplateFormatter
{
    internal static readonly string[] SupportedVariables =
    {
        "{artist}",
        "{artists}",
        "{track}",
        "{album}",
        "{duration}",
        "{elapsed}",
        "{remaining}",
        "{is_local}",
        "{paused}",
        "{cycle:SECONDS|first|second|...}"
    };

    internal static string Expand(string? format, SpotifyTrackInfo track, bool paused, bool stripBracketedTrackParts = false)
    {
        if (string.IsNullOrWhiteSpace(format))
            format = Configuration.DefaultTitleFormat;

        var primaryArtist = track.Artists.Count > 0 ? track.Artists[0] : "Unknown Artist";
        var trackName = stripBracketedTrackParts ? StripBracketedParts(track.Name) : track.Name;
        if (string.IsNullOrWhiteSpace(trackName))
            trackName = track.Name;
        var allArtists = track.ArtistText;
        var album = string.IsNullOrWhiteSpace(track.Album) ? "Unknown Album" : track.Album;
        var durationMs = Math.Max(0, track.DurationMs);
        var elapsedMs = Math.Clamp(track.ProgressMs, 0, durationMs > 0 ? durationMs : int.MaxValue);
        var remainingMs = durationMs > 0 ? Math.Max(0, durationMs - elapsedMs) : 0;

        // Resolve cycle blocks first. Selected entries may then contain ordinary
        // template variables such as {track} or {artist}.
        var result = ExpandCycles(format, elapsedMs)
            .Replace("{artist}", primaryArtist, StringComparison.OrdinalIgnoreCase)
            .Replace("{artists}", allArtists, StringComparison.OrdinalIgnoreCase)
            .Replace("{track}", trackName, StringComparison.OrdinalIgnoreCase)
            .Replace("{album}", album, StringComparison.OrdinalIgnoreCase)
            .Replace("{duration}", FormatTime(durationMs), StringComparison.OrdinalIgnoreCase)
            .Replace("{elapsed}", FormatTime(elapsedMs), StringComparison.OrdinalIgnoreCase)
            .Replace("{remaining}", FormatTime(remainingMs), StringComparison.OrdinalIgnoreCase)
            .Replace("{is_local}", track.IsLocal ? "true" : "false", StringComparison.OrdinalIgnoreCase)
            .Replace("{paused}", paused ? "true" : "false", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(result)
            ? $"{primaryArtist} - {trackName}"
            : result;
    }

    internal static bool UsesProgressVariable(string? format) =>
        !string.IsNullOrWhiteSpace(format) &&
        (format.Contains("{elapsed}", StringComparison.OrdinalIgnoreCase) ||
         format.Contains("{remaining}", StringComparison.OrdinalIgnoreCase) ||
         format.Contains("{cycle:", StringComparison.OrdinalIgnoreCase));


    internal static string StripBracketedParts(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input?.Trim() ?? string.Empty;

        var result = input;

        // Repeat a few times so simple nested decorations are removed from the
        // inside out, e.g. "Song (Live [2026])".
        for (var pass = 0; pass < 4; pass++)
        {
            var previous = result;
            result = Regex.Replace(result, @"\s*\([^()]*\)\s*", " ");
            result = Regex.Replace(result, @"\s*\[[^\[\]]*\]\s*", " ");
            result = Regex.Replace(result, @"\s*\{[^{}]*\}\s*", " ");
            if (string.Equals(previous, result, StringComparison.Ordinal))
                break;
        }

        // Removing a trailing decoration from titles like "Song - (Remaster)"
        // should not leave an orphaned separator behind.
        result = Regex.Replace(result, @"\s{2,}", " ").Trim();
        result = Regex.Replace(result, @"\s*[-–—|:/]\s*$", string.Empty).Trim();
        return result;
    }

    private static string ExpandCycles(string input, int elapsedMs)
    {
        if (!input.Contains("{cycle:", StringComparison.OrdinalIgnoreCase))
            return input;

        var output = new StringBuilder(input.Length);
        var cursor = 0;

        while (cursor < input.Length)
        {
            var cycleStart = input.IndexOf("{cycle:", cursor, StringComparison.OrdinalIgnoreCase);
            if (cycleStart < 0)
            {
                output.Append(input, cursor, input.Length - cursor);
                break;
            }

            output.Append(input, cursor, cycleStart - cursor);

            if (!TryReadCycleBlock(input, cycleStart, out var cycleEnd, out var body) ||
                !TryEvaluateCycle(body, elapsedMs, out var replacement))
            {
                // Keep malformed text visible and advance one character so another
                // later cycle block can still be parsed.
                output.Append(input[cycleStart]);
                cursor = cycleStart + 1;
                continue;
            }

            output.Append(replacement);
            cursor = cycleEnd + 1;
        }

        return output.ToString();
    }

    private static bool TryReadCycleBlock(string input, int start, out int end, out string body)
    {
        end = -1;
        body = string.Empty;

        const string prefix = "{cycle:";
        if (start < 0 || start + prefix.Length > input.Length ||
            !input.Substring(start, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        // The outer cycle block may contain ordinary variables in braces. Count
        // braces so the first } from {track} does not terminate the cycle.
        var depth = 0;
        for (var i = start; i < input.Length; i++)
        {
            if (input[i] == '{')
            {
                depth++;
                continue;
            }

            if (input[i] != '}')
                continue;

            depth--;
            if (depth != 0)
                continue;

            end = i;
            var bodyStart = start + prefix.Length;
            body = input.Substring(bodyStart, end - bodyStart);
            return true;
        }

        return false;
    }

    private static bool TryEvaluateCycle(string body, int elapsedMs, out string replacement)
    {
        replacement = string.Empty;

        // Ordinary variables do not contain '|', so a direct split is both simpler
        // and more reliable for the supported cycle syntax. Nested cycle blocks are
        // intentionally not supported in this development version.
        var parts = body.Split('|', StringSplitOptions.None);
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0].Trim(), out var secondsPerEntry) || secondsPerEntry <= 0)
            return false;

        var entryCount = parts.Length - 1;
        if (entryCount <= 0)
            return false;

        var elapsedSeconds = Math.Max(0, elapsedMs) / 1000;
        var index = (elapsedSeconds / secondsPerEntry) % entryCount;
        replacement = parts[index + 1].Trim();
        return true;
    }

    private static string FormatTime(int milliseconds)
    {
        var totalSeconds = Math.Max(0, milliseconds) / 1000;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes}:{seconds:00}";
    }
}
