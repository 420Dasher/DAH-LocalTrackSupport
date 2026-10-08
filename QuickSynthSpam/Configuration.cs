using Dalamud.Configuration;

namespace QuickSynthSpam;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int TotalCount { get; set; } = 250;
    public bool OpenWithCraftingLog { get; set; } = true;
    public bool CraftNqOnly { get; set; } = false;
    public bool AutoFillMaxCraftable { get; set; } = true;
}