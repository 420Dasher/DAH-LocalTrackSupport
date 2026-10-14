using Dalamud.Configuration;

namespace AethernetAssist;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool AutoOpen { get; set; } = true;
    public bool ShowUnlearned { get; set; } = true;
    public bool ShowDebug { get; set; } = false;
    public float OpenRange { get; set; } = 11.1f;
}