using Dalamud.Configuration;

namespace AethernetAssist;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool AutoOpen { get; set; } = true;
    public bool ShowUnlearned { get; set; } = true;
    public bool ShowDebug { get; set; } = false;
    public float OpenRange { get; set; } = 6.0f;
}