using Dalamud.Configuration;
using Dalamud.Plugin;

namespace RetainerUndercut;

public enum UndercutPricingMode
{
    FixedAmount = 0,
    Percentage = 1,
}

public enum PriceRecoveryGapMode
{
    FixedGil = 0,
    Percentage = 1,
}

public sealed class ItemPricingRule
{
    public uint ItemId { get; set; }
    public bool IsHq { get; set; }
    public string ItemName { get; set; } = string.Empty;

    public bool Ignore { get; set; }

    public bool OverridePricing { get; set; }
    public UndercutPricingMode PricingMode { get; set; } = UndercutPricingMode.FixedAmount;
    public int FixedUndercutAmount { get; set; } = 1;
    public int PercentageUndercut { get; set; } = 1;

    public bool OverrideMinimumPrice { get; set; }
    public int MinimumPriceGil { get; set; } = 1;

    public bool OverrideMaxDrop { get; set; }
    public int MaxDropPercent { get; set; } = 25;

}

public readonly record struct ResolvedItemPricingSettings(
    bool HasItemRule,
    bool Ignore,
    UndercutPricingMode PricingMode,
    int FixedUndercutAmount,
    int PercentageUndercut,
    bool MinimumPriceEnabled,
    int MinimumPriceGil,
    bool MaxDropProtectionEnabled,
    int MaxDropPercent,
    string RuleSummary)
{
    public string PricingModeLabel => PricingMode == UndercutPricingMode.FixedAmount
        ? $"Fixed -{FixedUndercutAmount:N0} gil"
        : $"Percentage -{PercentageUndercut}%";

    public ulong CalculateNormalTarget(ulong competitorPrice)
    {
        if (competitorPrice <= 1)
            return 1;

        if (PricingMode == UndercutPricingMode.FixedAmount)
        {
            var amount = (ulong)Math.Max(1, FixedUndercutAmount);
            return competitorPrice > amount ? competitorPrice - amount : 1;
        }

        var percent = (ulong)Math.Clamp(PercentageUndercut, 1, 99);
        var target = competitorPrice * (100UL - percent) / 100UL;
        return Math.Max(target, 1UL);
    }
}


public sealed class RunChangeRecord
{
    public string RetainerName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public bool IsHq { get; set; }
    public ulong PreviousPrice { get; set; }
    public ulong? ResultPrice { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RetainerRunHistory
{
    public string RetainerName { get; set; } = string.Empty;
    public List<RunChangeRecord> Changes { get; set; } = [];
}

public sealed class RunHistoryEntry
{
    public DateTimeOffset FinishedAtUtc { get; set; }
    public bool DryRun { get; set; }
    public string Scope { get; set; } = string.Empty;
    public long DurationMilliseconds { get; set; }
    public int Checked { get; set; }
    public int Changed { get; set; }
    public int WouldChange { get; set; }
    public int OutlierBlocked { get; set; }
    public int PriceRecovered { get; set; }
    public int PriceRecoveryBlocked { get; set; }
    public List<RetainerRunHistory> Retainers { get; set; } = [];
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;

    public UndercutPricingMode PricingMode { get; set; } = UndercutPricingMode.FixedAmount;
    public int FixedUndercutAmount { get; set; } = 1;
    public int PercentageUndercut { get; set; } = 1;

    public bool MinimumPriceEnabled { get; set; }
    public int MinimumPriceGil { get; set; } = 1;

    public bool MaxDropProtectionEnabled { get; set; }
    public int MaxDropPercent { get; set; } = 25;

    public bool OutlierProtectionEnabled { get; set; }
    public int OutlierGapPercent { get; set; } = 60;

    public bool PriceRecoveryEnabled { get; set; }
    public PriceRecoveryGapMode RecoveryGapMode { get; set; } = PriceRecoveryGapMode.FixedGil;
    public int PriceRecoveryMinimumGapGil { get; set; } = 1000;
    public int PriceRecoveryMinimumGapPercent { get; set; } = 5;
    public bool PriceRecoveryMaxRaiseEnabled { get; set; } = true;
    public int PriceRecoveryMaxRaisePercent { get; set; } = 50;

    public List<string> MatchlistedRetainerNames { get; set; } = [];
    public List<ulong> DisabledRetainerIds { get; set; } = [];
    public List<ItemPricingRule> ItemRules { get; set; } = [];
    public List<RunHistoryEntry> RunHistory { get; set; } = [];

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        Normalize();
    }

    public string PricingModeLabel => PricingMode == UndercutPricingMode.FixedAmount
        ? $"Fixed -{FixedUndercutAmount:N0} gil"
        : $"Percentage -{PercentageUndercut}%";

    public ulong CalculateNormalTarget(ulong competitorPrice)
    {
        return ResolveItemPricing(0, false).CalculateNormalTarget(competitorPrice);
    }

    public ItemPricingRule? FindItemRule(uint itemId, bool isHq)
        => ItemRules.FirstOrDefault(x => x.ItemId == itemId && x.IsHq == isHq);

    public ItemPricingRule GetOrCreateItemRule(uint itemId, bool isHq, string? itemName)
    {
        var existing = FindItemRule(itemId, isHq);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(itemName) && !string.Equals(existing.ItemName, itemName.Trim(), StringComparison.Ordinal))
            {
                existing.ItemName = itemName.Trim();
                Save();
            }
            return existing;
        }

        var rule = new ItemPricingRule
        {
            ItemId = itemId,
            IsHq = isHq,
            ItemName = string.IsNullOrWhiteSpace(itemName) ? $"Item {itemId}" : itemName.Trim(),
            PricingMode = PricingMode,
            FixedUndercutAmount = FixedUndercutAmount,
            PercentageUndercut = PercentageUndercut,
            MinimumPriceGil = MinimumPriceGil,
            MaxDropPercent = MaxDropPercent,
        };
        ItemRules.Add(rule);
        Save();
        return rule;
    }

    public bool RemoveItemRule(uint itemId, bool isHq)
    {
        var removed = ItemRules.RemoveAll(x => x.ItemId == itemId && x.IsHq == isHq) > 0;
        if (removed)
            Save();
        return removed;
    }

    public ResolvedItemPricingSettings ResolveItemPricing(uint itemId, bool isHq)
    {
        var rule = itemId == 0 ? null : FindItemRule(itemId, isHq);
        if (rule is null)
        {
            return new ResolvedItemPricingSettings(
                false,
                false,
                PricingMode,
                FixedUndercutAmount,
                PercentageUndercut,
                MinimumPriceEnabled,
                MinimumPriceGil,
                MaxDropProtectionEnabled,
                MaxDropPercent,
                "Global defaults");
        }

        var mode = rule.OverridePricing ? rule.PricingMode : PricingMode;
        var fixedAmount = rule.OverridePricing ? rule.FixedUndercutAmount : FixedUndercutAmount;
        var percentage = rule.OverridePricing ? rule.PercentageUndercut : PercentageUndercut;
        var floorEnabled = rule.OverrideMinimumPrice || MinimumPriceEnabled;
        var floorGil = rule.OverrideMinimumPrice ? rule.MinimumPriceGil : MinimumPriceGil;
        var maxDropEnabled = rule.OverrideMaxDrop || MaxDropProtectionEnabled;
        var maxDropPercent = rule.OverrideMaxDrop ? rule.MaxDropPercent : MaxDropPercent;

        var parts = new List<string>();
        if (rule.Ignore)
            parts.Add("IGNORE");
        if (rule.OverridePricing)
            parts.Add($"pricing: {(mode == UndercutPricingMode.FixedAmount ? $"-{fixedAmount:N0} gil" : $"-{percentage}%")}");
        if (rule.OverrideMinimumPrice)
            parts.Add($"floor: {floorGil:N0}");
        if (rule.OverrideMaxDrop)
            parts.Add($"max drop: {maxDropPercent}%");
        if (parts.Count == 0)
            parts.Add("rule exists; global defaults inherited");

        return new ResolvedItemPricingSettings(
            true,
            rule.Ignore,
            mode,
            fixedAmount,
            percentage,
            floorEnabled,
            floorGil,
            maxDropEnabled,
            maxDropPercent,
            string.Join(" | ", parts));
    }

    public bool IsRetainerMatchlisted(string? retainerName)
    {
        if (string.IsNullOrWhiteSpace(retainerName))
            return false;

        return MatchlistedRetainerNames.Any(x =>
            string.Equals(x, retainerName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public bool AddMatchlistedRetainer(string? retainerName)
    {
        var trimmed = retainerName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        if (IsRetainerMatchlisted(trimmed))
            return false;

        MatchlistedRetainerNames.Add(trimmed);
        MatchlistedRetainerNames.Sort(StringComparer.OrdinalIgnoreCase);
        Save();
        return true;
    }

    public bool RemoveMatchlistedRetainer(string retainerName)
    {
        var removed = MatchlistedRetainerNames.RemoveAll(x =>
            string.Equals(x, retainerName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
            Save();
        return removed;
    }

    public bool IsRetainerEnabled(ulong retainerId) => !DisabledRetainerIds.Contains(retainerId);

    public void SetRetainerEnabled(ulong retainerId, bool enabled)
    {
        if (enabled)
            DisabledRetainerIds.RemoveAll(x => x == retainerId);
        else if (!DisabledRetainerIds.Contains(retainerId))
            DisabledRetainerIds.Add(retainerId);

        Save();
    }

    public void SetRetainersEnabled(IEnumerable<ulong> retainerIds, bool enabled)
    {
        var ids = retainerIds.Where(x => x != 0).Distinct().ToArray();
        if (ids.Length == 0)
            return;

        if (enabled)
        {
            var idSet = ids.ToHashSet();
            DisabledRetainerIds.RemoveAll(idSet.Contains);
        }
        else
        {
            foreach (var id in ids)
            {
                if (!DisabledRetainerIds.Contains(id))
                    DisabledRetainerIds.Add(id);
            }
        }

        Save();
    }

    public void AddRunHistory(RunHistoryEntry entry)
    {
        RunHistory ??= [];
        RunHistory.Insert(0, entry);
        if (RunHistory.Count > 5)
            RunHistory.RemoveRange(5, RunHistory.Count - 5);
        Save();
    }

    public void Save()
    {
        Normalize();
        pluginInterface?.SavePluginConfig(this);
    }

    private void Normalize()
    {
        Version = Math.Max(Version, 6);
        FixedUndercutAmount = Math.Clamp(FixedUndercutAmount, 1, 999_999_999);
        PercentageUndercut = Math.Clamp(PercentageUndercut, 1, 99);
        MinimumPriceGil = Math.Clamp(MinimumPriceGil, 1, 999_999_999);
        MaxDropPercent = Math.Clamp(MaxDropPercent, 1, 99);
        OutlierGapPercent = Math.Clamp(OutlierGapPercent, 10, 95);
        PriceRecoveryMinimumGapGil = Math.Clamp(PriceRecoveryMinimumGapGil, 1, 999_999_999);
        PriceRecoveryMinimumGapPercent = Math.Clamp(PriceRecoveryMinimumGapPercent, 1, 99);
        PriceRecoveryMaxRaisePercent = Math.Clamp(PriceRecoveryMaxRaisePercent, 1, 999);

        MatchlistedRetainerNames ??= [];
        MatchlistedRetainerNames = MatchlistedRetainerNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DisabledRetainerIds ??= [];
        DisabledRetainerIds = DisabledRetainerIds
            .Where(x => x != 0)
            .Distinct()
            .ToList();

        ItemRules ??= [];
        ItemRules = ItemRules
            .Where(x => x is not null && x.ItemId != 0)
            .GroupBy(x => (x.ItemId, x.IsHq))
            .Select(x => x.First())
            .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ItemId)
            .ThenBy(x => x.IsHq)
            .ToList();

        foreach (var rule in ItemRules)
        {
            rule.ItemName = string.IsNullOrWhiteSpace(rule.ItemName) ? $"Item {rule.ItemId}" : rule.ItemName.Trim();
            rule.FixedUndercutAmount = Math.Clamp(rule.FixedUndercutAmount, 1, 999_999_999);
            rule.PercentageUndercut = Math.Clamp(rule.PercentageUndercut, 1, 99);
            rule.MinimumPriceGil = Math.Clamp(rule.MinimumPriceGil, 1, 999_999_999);
            rule.MaxDropPercent = Math.Clamp(rule.MaxDropPercent, 1, 99);
        }

        RunHistory ??= [];
        RunHistory = RunHistory
            .Where(x => x is not null)
            .OrderByDescending(x => x.FinishedAtUtc)
            .Take(5)
            .ToList();
        foreach (var run in RunHistory)
        {
            run.Scope = string.IsNullOrWhiteSpace(run.Scope) ? "Run" : run.Scope.Trim();
            run.Retainers ??= [];
            foreach (var retainer in run.Retainers)
            {
                retainer.RetainerName = string.IsNullOrWhiteSpace(retainer.RetainerName) ? "Unknown retainer" : retainer.RetainerName.Trim();
                retainer.Changes ??= [];
            }
        }
    }
}
