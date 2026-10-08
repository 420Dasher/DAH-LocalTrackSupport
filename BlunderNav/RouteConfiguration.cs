using System.Collections.Generic;
using Dalamud.Configuration;

namespace BlunderNav;

public sealed class RouteConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int SelectedSlot { get; set; } = 0;
    public bool AutoRecord { get; set; } = false;
    public bool AutoAdvance { get; set; } = false;
    public bool SmoothCorners { get; set; } = false;
    public float CornerRadius { get; set; } = 0.65f;
    public float RecordSpacing { get; set; } = 3.0f;
    public List<RecordedRoute> Routes { get; set; } = new();
}

public sealed class RecordedRoute
{
    public string Name { get; set; } = "";
    public ushort Territory { get; set; } = 1165;
    public List<RoutePoint> Points { get; set; } = new();
}

public sealed class RoutePoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public bool Hold { get; set; }

    public RoutePoint() { }

    public RoutePoint(System.Numerics.Vector3 p)
    {
        X = p.X;
        Y = p.Y;
        Z = p.Z;
    }

    public System.Numerics.Vector3 Position =>
        new(X, Y, Z);
}