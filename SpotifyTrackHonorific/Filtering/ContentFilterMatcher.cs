using SpotifyTrackHonorific.Spotify;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SpotifyTrackHonorific.Filtering;

internal sealed record ContentFilterMatch(string Entry, string Field, bool UsedFuzzyMatch, bool IsBuiltIn);

internal sealed record ContentFilterCensorResult(SpotifyTrackInfo Track, IReadOnlyList<string> Fields)
{
    public bool AnyMatch => Fields.Count > 0;
    public string FieldSummary => string.Join(", ", Fields);
}

internal sealed record BuiltInTriggerWord(string Id, string Term, string Category);

internal static class ContentFilterMatcher
{
    private enum FilterScope
    {
        Any,
        Artist,
        Track,
        Album,
    }

    private sealed record FilterEntry(string Original, string Term, FilterScope Scope, bool IsBuiltIn);

    private static readonly BuiltInTriggerWord[] BuiltInWords =
    {
        new("suicide", "suicide", "Self-harm / overdose"),
        new("suicidal", "suicidal", "Self-harm / overdose"),
        new("self-harm", "self harm", "Self-harm / overdose"),
        new("self-injury", "self injury", "Self-harm / overdose"),
        new("overdose", "overdose", "Self-harm / overdose"),

        new("sexual-assault", "sexual assault", "Abuse / sexual trauma"),
        new("rape", "rape", "Abuse / sexual trauma"),
        new("raped", "raped", "Abuse / sexual trauma"),
        new("raping", "raping", "Abuse / sexual trauma"),
        new("rapist", "rapist", "Abuse / sexual trauma"),
        new("sexual-abuse", "sexual abuse", "Abuse / sexual trauma"),
        new("physical-abuse", "physical abuse", "Abuse / sexual trauma"),
        new("emotional-abuse", "emotional abuse", "Abuse / sexual trauma"),
        new("domestic-abuse", "domestic abuse", "Abuse / sexual trauma"),
        new("domestic-violence", "domestic violence", "Abuse / sexual trauma"),
        new("child-abuse", "child abuse", "Abuse / sexual trauma"),
        new("child-sexual-abuse", "child sexual abuse", "Abuse / sexual trauma"),
        new("human-trafficking", "human trafficking", "Abuse / sexual trauma"),

        new("murder", "murder", "Severe violence"),
        new("homicide", "homicide", "Severe violence"),
        new("mass-shooting", "mass shooting", "Severe violence"),
        new("school-shooting", "school shooting", "Severe violence"),
        new("torture", "torture", "Severe violence"),
        new("mutilation", "mutilation", "Severe violence"),
        new("dismemberment", "dismemberment", "Severe violence"),
        new("decapitation", "decapitation", "Severe violence"),

        new("necrophilia", "necrophilia", "Other high-sensitivity terms"),
        new("pedophilia", "pedophilia", "Other high-sensitivity terms"),
        new("paedophilia", "paedophilia", "Other high-sensitivity terms"),
        new("incest", "incest", "Other high-sensitivity terms"),
        new("miscarriage", "miscarriage", "Other high-sensitivity terms"),
        new("stillbirth", "stillbirth", "Other high-sensitivity terms"),
    };

    public static IReadOnlyList<BuiltInTriggerWord> BuiltInTriggerWords => BuiltInWords;

    public static int ActiveBuiltInTriggerWordCount(string disabledEntries)
    {
        var disabled = ParseDisabledBuiltInEntries(disabledEntries);
        return BuiltInWords.Count(entry => !disabled.Contains(entry.Id));
    }

    public static bool IsBuiltInEntryEnabled(string id, string disabledEntries) =>
        !ParseDisabledBuiltInEntries(disabledEntries).Contains(id);

    public static string SetBuiltInEntryEnabled(string id, bool enabled, string disabledEntries)
    {
        var disabled = ParseDisabledBuiltInEntries(disabledEntries);
        if (enabled)
            disabled.Remove(id);
        else
            disabled.Add(id);

        // Keep serialization deterministic and limited to known IDs so renamed or
        // removed preset entries cannot leave permanent junk in a user's config.
        return string.Join("\n", BuiltInWords
            .Where(entry => disabled.Contains(entry.Id))
            .Select(entry => entry.Id));
    }

    public static ContentFilterMatch? MatchTrack(
        SpotifyTrackInfo track,
        string rawEntries,
        bool useBuiltInEntries,
        string disabledBuiltInEntries,
        bool smartMatching)
    {
        var entries = BuildEntries(rawEntries, useBuiltInEntries, disabledBuiltInEntries);
        foreach (var entry in entries)
        {
            switch (entry.Scope)
            {
                case FilterScope.Artist:
                    foreach (var artist in track.Artists)
                    {
                        if (MatchesEntry(entry, artist, smartMatching, out var fuzzy))
                            return new ContentFilterMatch(entry.Original, "artist", fuzzy, entry.IsBuiltIn);
                    }
                    break;

                case FilterScope.Track:
                    if (MatchesEntry(entry, track.Name, smartMatching, out var trackFuzzy))
                        return new ContentFilterMatch(entry.Original, "track", trackFuzzy, entry.IsBuiltIn);
                    break;

                case FilterScope.Album:
                    if (MatchesEntry(entry, track.Album, smartMatching, out var albumFuzzy))
                        return new ContentFilterMatch(entry.Original, "album", albumFuzzy, entry.IsBuiltIn);
                    break;

                default:
                    foreach (var artist in track.Artists)
                    {
                        if (MatchesEntry(entry, artist, smartMatching, out var artistFuzzy))
                            return new ContentFilterMatch(entry.Original, "artist", artistFuzzy, entry.IsBuiltIn);
                    }

                    if (MatchesEntry(entry, track.Name, smartMatching, out var nameFuzzy))
                        return new ContentFilterMatch(entry.Original, "track", nameFuzzy, entry.IsBuiltIn);

                    if (MatchesEntry(entry, track.Album, smartMatching, out var anyAlbumFuzzy))
                        return new ContentFilterMatch(entry.Original, "album", anyAlbumFuzzy, entry.IsBuiltIn);
                    break;
            }
        }

        return null;
    }

    public static ContentFilterCensorResult CensorTrack(
        SpotifyTrackInfo track,
        string rawEntries,
        bool useBuiltInEntries,
        string disabledBuiltInEntries,
        bool smartMatching,
        string replacement)
    {
        replacement = string.IsNullOrWhiteSpace(replacement)
            ? Configuration.DefaultContentFilterFallback
            : replacement.Trim();

        var entries = BuildEntries(rawEntries, useBuiltInEntries, disabledBuiltInEntries);
        if (entries.Count == 0)
            return new ContentFilterCensorResult(track, Array.Empty<string>());

        var fields = new List<string>();

        var artists = new string[track.Artists.Count];
        var artistChanged = false;
        for (var i = 0; i < track.Artists.Count; i++)
        {
            var artist = track.Artists[i];
            if (MatchesField(entries, FilterScope.Artist, artist, smartMatching))
            {
                artists[i] = replacement;
                artistChanged = true;
            }
            else
            {
                artists[i] = artist;
            }
        }

        if (artistChanged)
            fields.Add("artist");

        var name = track.Name;
        if (MatchesField(entries, FilterScope.Track, name, smartMatching))
        {
            name = replacement;
            fields.Add("track");
        }

        var album = track.Album;
        if (MatchesField(entries, FilterScope.Album, album, smartMatching))
        {
            album = replacement;
            fields.Add("album");
        }

        if (fields.Count == 0)
            return new ContentFilterCensorResult(track, Array.Empty<string>());

        return new ContentFilterCensorResult(
            track with
            {
                Name = name,
                Artists = artists,
                Album = album,
            },
            fields);
    }

    private static bool MatchesField(
        IReadOnlyList<FilterEntry> entries,
        FilterScope fieldScope,
        string value,
        bool smartMatching)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var entry in entries)
        {
            if (entry.Scope != FilterScope.Any && entry.Scope != fieldScope)
                continue;

            if (MatchesEntry(entry, value, smartMatching, out _))
                return true;
        }

        return false;
    }

    public static ContentFilterMatch? TestText(
        string rawEntries,
        bool useBuiltInEntries,
        string disabledBuiltInEntries,
        bool smartMatching,
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Treat the test text as every metadata field so scoped entries can be tested
        // without exposing extra UI controls just for the tester.
        var testTrack = new SpotifyTrackInfo(
            text,
            new[] { text },
            text,
            0,
            0,
            false,
            "content-filter-test");

        return MatchTrack(testTrack, rawEntries, useBuiltInEntries, disabledBuiltInEntries, smartMatching);
    }

    private static List<FilterEntry> BuildEntries(
        string rawEntries,
        bool useBuiltInEntries,
        string disabledBuiltInEntries)
    {
        var result = new List<FilterEntry>();

        // Custom rules are evaluated first so the test panel reports a user's explicit
        // scoped rule before a broader built-in term when both would match.
        result.AddRange(ParseEntries(rawEntries));

        if (!useBuiltInEntries)
            return result;

        var disabled = ParseDisabledBuiltInEntries(disabledBuiltInEntries);
        foreach (var builtIn in BuiltInWords)
        {
            if (!disabled.Contains(builtIn.Id))
                result.Add(new FilterEntry(builtIn.Term, builtIn.Term, FilterScope.Any, true));
        }

        return result;
    }

    private static HashSet<string> ParseDisabledBuiltInEntries(string rawEntries)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawEntries))
            return disabled;

        foreach (var rawLine in rawEntries.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var id = rawLine.Trim();
            if (id.Length > 0)
                disabled.Add(id);
        }

        return disabled;
    }

    private static IEnumerable<FilterEntry> ParseEntries(string rawEntries)
    {
        if (string.IsNullOrWhiteSpace(rawEntries))
            yield break;

        var lines = rawEntries.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var scope = FilterScope.Any;
            var term = line;
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var prefix = line[..colon].Trim();
                var candidate = line[(colon + 1)..].Trim();
                if (candidate.Length > 0)
                {
                    if (prefix.Equals("artist", StringComparison.OrdinalIgnoreCase))
                    {
                        scope = FilterScope.Artist;
                        term = candidate;
                    }
                    else if (prefix.Equals("track", StringComparison.OrdinalIgnoreCase))
                    {
                        scope = FilterScope.Track;
                        term = candidate;
                    }
                    else if (prefix.Equals("album", StringComparison.OrdinalIgnoreCase))
                    {
                        scope = FilterScope.Album;
                        term = candidate;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(term))
                yield return new FilterEntry(line, term, scope, false);
        }
    }

    private static bool MatchesEntry(FilterEntry entry, string valueText, bool smartMatching, out bool fuzzy)
    {
        // A very short built-in term such as "rape" should match the term itself but
        // not an unrelated containing word such as "grape". Custom entries retain
        // the original substring semantics because the user explicitly authored them.
        var normalizedPattern = Normalize(entry.Term, smartMatching);
        if (entry.IsBuiltIn && normalizedPattern.Length > 0 && normalizedPattern.Length <= 4)
        {
            fuzzy = false;
            foreach (var token in NormalizeTokens(valueText, smartMatching))
            {
                if (string.Equals(token, normalizedPattern, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        return Matches(entry.Term, valueText, smartMatching, out fuzzy);
    }

    private static bool Matches(string patternText, string valueText, bool smartMatching, out bool fuzzy)
    {
        fuzzy = false;
        if (string.IsNullOrWhiteSpace(patternText) || string.IsNullOrWhiteSpace(valueText))
            return false;

        var pattern = Normalize(patternText, smartMatching);
        var value = Normalize(valueText, smartMatching);
        if (pattern.Length == 0 || value.Length == 0)
            return false;

        // Exact/contained normalized matching handles case, whitespace and punctuation.
        // With Smart Matching enabled, common leetspeak substitutions are normalized too.
        if (value.Contains(pattern, StringComparison.Ordinal))
            return true;

        if (!smartMatching || pattern.Length < 7)
            return false;

        var allowedEdits = pattern.Length >= 10 ? 2 : 1;

        if (Math.Abs(pattern.Length - value.Length) <= allowedEdits &&
            IsWithinEditDistance(pattern, value, allowedEdits))
        {
            fuzzy = true;
            return true;
        }

        // For longer metadata strings, scan small windows so a typo inside a longer
        // artist/track/album value can still be detected without broad fuzzy matching.
        var minWindow = Math.Max(1, pattern.Length - allowedEdits);
        var maxWindow = Math.Min(value.Length, pattern.Length + allowedEdits);
        for (var windowLength = minWindow; windowLength <= maxWindow; windowLength++)
        {
            for (var start = 0; start + windowLength <= value.Length; start++)
            {
                if (IsWithinEditDistance(pattern, value.AsSpan(start, windowLength), allowedEdits))
                {
                    fuzzy = true;
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string input, bool smartMatching)
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var original in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
                continue;

            var c = NormalizeCharacter(original, smartMatching);
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IEnumerable<string> NormalizeTokens(string input, bool smartMatching)
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var original in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
                continue;

            var c = NormalizeCharacter(original, smartMatching);
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString().Normalize(NormalizationForm.FormC);
                builder.Clear();
            }
        }

        if (builder.Length > 0)
            yield return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static char NormalizeCharacter(char original, bool smartMatching)
    {
        var c = char.ToLowerInvariant(original);
        if (!smartMatching)
            return c;

        return c switch
        {
            '$' => 's',
            '@' => 'a',
            '4' => 'a',
            '0' => 'o',
            '1' => 'i',
            '!' => 'i',
            '3' => 'e',
            '5' => 's',
            '7' => 't',
            '8' => 'b',
            _ => c,
        };
    }

    private static bool IsWithinEditDistance(string left, string right, int maxDistance) =>
        IsWithinEditDistance(left, right.AsSpan(), maxDistance);

    private static bool IsWithinEditDistance(string left, ReadOnlySpan<char> right, int maxDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maxDistance)
            return false;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > maxDistance)
                return false;

            (previous, current) = (current, previous);
        }

        return previous[right.Length] <= maxDistance;
    }
}
