using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;

namespace SpotifyTrackHonorific.Honorific;

internal sealed class HonorificBridge
{
    // Honorific currently enforces a 32-character title limit.
    public const int MaxTitleLength = 32;

    private static readonly (char Open, char Close)[] WrapperPairs =
    {
        ('»', '«'),
        ('«', '»'),
        ('“', '”'),
        ('\"', '\"'),
        ('\'', '\''),
        ('[', ']'),
        ('(', ')'),
        ('【', '】'),
        ('「', '」')
    };

    private readonly ICallGateSubscriber<uint, string, object> setTitle;
    private readonly ICallGateSubscriber<uint, object> clearTitle;

    public HonorificBridge(IDalamudPluginInterface pluginInterface)
    {
        setTitle = pluginInterface.GetIpcSubscriber<uint, string, object>("Honorific.SetCharacterTitle");
        clearTitle = pluginInterface.GetIpcSubscriber<uint, object>("Honorific.ClearCharacterTitle");
    }

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Set(
        string title,
        bool isPrefix = false,
        Vector3? color = null,
        Vector3? glow = null,
        int? gradientColourSet = null,
        int? gradientAnimationStyle = null,
        Vector3? color3 = null)
    {
        var payload = JsonSerializer.Serialize(new HonorificTitlePayload
        {
            // Titles reaching this bridge should normally already be fitted by the
            // plugin. Keep a final basic guard here because Honorific rejects >32.
            Title = FitTitle(title),
            IsPrefix = isPrefix,
            Color = color is null ? null : RgbPayload.FromVector(color.Value),
            // Honorific's custom GradientColourSet = -1 form uses Color, Glow and
            // Color3 as its three colour slots.
            Glow = glow is null ? null : RgbPayload.FromVector(glow.Value),
            Color3 = color3 is null ? null : RgbPayload.FromVector(color3.Value),
            GradientColourSet = gradientColourSet,
            // Honorific's enum is intentionally serialized numerically so this plugin
            // does not need a compile-time reference to Honorific's assembly. The UI
            // discovers the installed enum names/values at runtime.
            GradientAnimationStyle = gradientAnimationStyle
        }, PayloadJsonOptions);

        setTitle.InvokeAction(0u, payload);
    }

    public void Clear() => clearTitle.InvokeAction(0u);

    public static string FitTitle(string title, bool smartFit = false)
    {
        title = title.Trim();
        if (title.Length <= MaxTitleLength)
            return title;

        if (smartFit && TryFitWrappedTitle(title, out var wrapped))
            return wrapped;

        return Ellipsize(title, MaxTitleLength, smartFit);
    }

    private static bool TryFitWrappedTitle(string title, out string fitted)
    {
        fitted = string.Empty;

        foreach (var (open, close) in WrapperPairs)
        {
            if (title.Length < 3 || title[0] != open || title[^1] != close)
                continue;

            var prefixEnd = 1;
            while (prefixEnd < title.Length - 1 && char.IsWhiteSpace(title[prefixEnd]))
                prefixEnd++;

            var suffixStart = title.Length - 1;
            while (suffixStart > prefixEnd && char.IsWhiteSpace(title[suffixStart - 1]))
                suffixStart--;

            var prefix = title[..prefixEnd];
            var suffix = title[suffixStart..];
            var content = title[prefixEnd..suffixStart].Trim();
            var available = MaxTitleLength - prefix.Length - suffix.Length;

            if (available <= 0)
                return false;

            fitted = prefix + Ellipsize(content, available, preferWordBoundary: true) + suffix;
            return fitted.Length <= MaxTitleLength;
        }

        return false;
    }

    private static string Ellipsize(string value, int maxLength, bool preferWordBoundary = false)
    {
        value = value.Trim();
        if (value.Length <= maxLength)
            return value;
        if (maxLength <= 0)
            return string.Empty;
        if (maxLength <= 3)
            return value[..maxLength];

        const string ellipsis = "...";
        var contentLength = maxLength - ellipsis.Length;
        var cutAt = contentLength;

        if (preferWordBoundary)
        {
            var candidate = value[..contentLength];
            var wordBreak = candidate.LastIndexOf(' ');
            // Avoid collapsing a long title to a tiny first word just to hit a
            // boundary. Fall back to the exact character limit in that case.
            if (wordBreak >= Math.Max(8, contentLength / 2))
                cutAt = wordBreak;
        }

        var truncated = value[..cutAt].TrimEnd();

        // When smart fitting, avoid leaving a dangling separator immediately
        // before the ellipsis (for example "We are the people -...").
        // Only apply this to text that is actually being truncated.
        if (preferWordBoundary)
        {
            var cleaned = TrimTrailingCutSymbols(truncated);
            if (!string.IsNullOrWhiteSpace(cleaned))
                truncated = cleaned;
        }

        return truncated + ellipsis;
    }

    private static string TrimTrailingCutSymbols(string value)
    {
        var end = value.Length;
        while (end > 0)
        {
            var c = value[end - 1];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                end--;
                continue;
            }

            break;
        }

        return value[..end].TrimEnd();
    }

    private sealed class HonorificTitlePayload
    {
        public string Title { get; set; } = string.Empty;
        public bool IsPrefix { get; set; }
        public RgbPayload? Color { get; set; }
        public RgbPayload? Glow { get; set; }
        public RgbPayload? Color3 { get; set; }
        public int? GradientColourSet { get; set; }
        public int? GradientAnimationStyle { get; set; }
    }

    // Newtonsoft.Json (used by Honorific) maps this X/Y/Z object directly onto
    // System.Numerics.Vector3 in Honorific's TitleData payload.
    private sealed class RgbPayload
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static RgbPayload FromVector(Vector3 value) => new()
        {
            X = Math.Clamp(value.X, 0f, 1f),
            Y = Math.Clamp(value.Y, 0f, 1f),
            Z = Math.Clamp(value.Z, 0f, 1f)
        };
    }
}
