using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SpotifyTrackHonorific.Honorific;

internal readonly record struct HonorificNamedValue(int Value, string Name);

internal sealed class HonorificGradientCatalogSnapshot
{
    internal HonorificGradientCatalogSnapshot(
        IReadOnlyList<HonorificNamedValue> presets,
        IReadOnlyList<HonorificNamedValue> animationStyles,
        bool honorificTypesFound)
    {
        Presets = presets;
        AnimationStyles = animationStyles;
        HonorificTypesFound = honorificTypesFound;
    }

    internal IReadOnlyList<HonorificNamedValue> Presets { get; }
    internal IReadOnlyList<HonorificNamedValue> AnimationStyles { get; }
    internal bool HonorificTypesFound { get; }
    internal bool PresetsAvailable => Presets.Count > 0;
}

/// <summary>
/// Reads Honorific's own gradient metadata at runtime without taking a compile-time
/// dependency on Honorific. This keeps the dropdown labels in sync with the user's
/// installed Honorific version instead of baking preset IDs/names into this plugin.
/// </summary>
internal static class HonorificGradientCatalog
{
    private static readonly object Sync = new();
    private static readonly HonorificNamedValue[] FallbackAnimationStyles =
    {
        new(0, "Static"),
        new(1, "Animated")
    };

    private static HonorificGradientCatalogSnapshot cached =
        new(Array.Empty<HonorificNamedValue>(), FallbackAnimationStyles, false);
    private static DateTime nextProbeUtc = DateTime.MinValue;

    internal static HonorificGradientCatalogSnapshot GetSnapshot()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow < nextProbeUtc)
                return cached;

            cached = Discover();
            // Once Honorific is found we do not need to reflect every frame. If it is
            // not loaded yet, retry periodically so loading/reloading Honorific fixes
            // the dropdown automatically without restarting FFXIV.
            nextProbeUtc = DateTime.UtcNow.AddSeconds(cached.HonorificTypesFound ? 30 : 3);
            return cached;
        }
    }

    internal static void ForceRefresh()
    {
        lock (Sync)
            nextProbeUtc = DateTime.MinValue;
    }

    private static HonorificGradientCatalogSnapshot Discover()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var gradientSystemType = assemblies
                .Select(a => a.GetType("Honorific.Gradient.GradientSystem", throwOnError: false))
                .FirstOrDefault(t => t != null);
            var animationStyleType = assemblies
                .Select(a => a.GetType("Honorific.Gradient.GradientAnimationStyle", throwOnError: false))
                .FirstOrDefault(t => t != null);

            var animationStyles = ReadAnimationStyles(animationStyleType);
            if (gradientSystemType == null)
                return new HonorificGradientCatalogSnapshot(
                    Array.Empty<HonorificNamedValue>(), animationStyles, false);

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var count = ReadStaticInt(gradientSystemType, "NumColourSets", flags);
            var getName = gradientSystemType.GetMethod(
                "GetColourSetName",
                flags,
                binder: null,
                types: new[] { typeof(int) },
                modifiers: null);

            if (count <= 0 || getName == null)
                return new HonorificGradientCatalogSnapshot(
                    Array.Empty<HonorificNamedValue>(), animationStyles, true);

            var presets = new List<HonorificNamedValue>(count);
            for (var i = 0; i < count; i++)
            {
                var name = getName.Invoke(null, new object[] { i })?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Preset {i + 1}";

                presets.Add(new HonorificNamedValue(i, name));
            }

            return new HonorificGradientCatalogSnapshot(presets, animationStyles, true);
        }
        catch
        {
            // Discovery is optional UI metadata. Never let reflection failure affect
            // Spotify polling/title rendering; the UI will show a retry hint instead.
            return new HonorificGradientCatalogSnapshot(
                Array.Empty<HonorificNamedValue>(), FallbackAnimationStyles, false);
        }
    }

    private static IReadOnlyList<HonorificNamedValue> ReadAnimationStyles(Type? animationStyleType)
    {
        if (animationStyleType == null || !animationStyleType.IsEnum)
            return FallbackAnimationStyles;

        try
        {
            var values = Enum.GetValues(animationStyleType);
            var result = new List<HonorificNamedValue>(values.Length);
            foreach (var value in values)
            {
                var numericValue = Convert.ToInt32(value);
                var name = Enum.GetName(animationStyleType, value) ?? $"Style {numericValue}";
                result.Add(new HonorificNamedValue(numericValue, SplitPascalCase(name)));
            }

            return result.Count > 0 ? result : FallbackAnimationStyles;
        }
        catch
        {
            return FallbackAnimationStyles;
        }
    }

    private static int ReadStaticInt(Type type, string memberName, BindingFlags flags)
    {
        try
        {
            var property = type.GetProperty(memberName, flags);
            if (property?.GetValue(null) is { } propertyValue)
                return Convert.ToInt32(propertyValue);

            var field = type.GetField(memberName, flags);
            if (field?.GetValue(null) is { } fieldValue)
                return Convert.ToInt32(fieldValue);
        }
        catch
        {
            // Fall through to zero.
        }

        return 0;
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length + 4) { value[0] };
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                chars.Add(' ');
            chars.Add(value[i]);
        }

        return new string(chars.ToArray());
    }
}
