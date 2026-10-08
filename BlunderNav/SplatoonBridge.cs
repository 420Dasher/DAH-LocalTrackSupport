using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace BlunderNav;

public readonly record struct VisualLine(
    Vector3 A,
    Vector3 B,
    uint Color,
    float Thickness);

public readonly record struct VisualCircle(
    Vector3 Center,
    float Radius,
    uint Color,
    float Thickness);

public sealed class SplatoonBridge : IDisposable
{
    private const string Namespace = "BlunderNav.DEV4.VisualGuide";

    private readonly ICallGateSubscriber<bool> loadedIpc;
    private readonly IPluginLog log;

    private object? plugin;
    private Type? elementType;
    private MethodInfo? addMethod;
    private MethodInfo? removeMethod;

    public bool Available { get; private set; }
    public string Status { get; private set; } =
        "Waiting for Splatoon.";

    public SplatoonBridge(
        IDalamudPluginInterface pi,
        IPluginLog pluginLog)
    {
        log = pluginLog;

        loadedIpc = pi.GetIpcSubscriber<bool>(
            "Splatoon.IsLoaded");
    }

    private bool Connect()
    {
        Available = false;

        try
        {
            if (!loadedIpc.InvokeFunc())
            {
                plugin = null;
                Status = "Splatoon is not loaded.";
                return false;
            }

            foreach (var assembly in
                AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(
                    "Splatoon.Splatoon", false);

                if (type == null)
                    continue;

                var field = type.GetField(
                    "P",
                    BindingFlags.Public | BindingFlags.Static);

                var current = field?.GetValue(null);

                if (current == null)
                    continue;

                var loadedField = type.GetField("Loaded");

                if (loadedField?.GetValue(current) is not true)
                    continue;

                var element = assembly.GetType(
                    "Splatoon.Element", false);

                if (element == null)
                    continue;

                var add = type.GetMethod(
                    "AddDynamicElements");

                var remove = type.GetMethod(
                    "RemoveDynamicElements");

                if (add == null || remove == null)
                    continue;

                plugin = current;
                elementType = element;
                addMethod = add;
                removeMethod = remove;

                Available = true;
                Status = "Splatoon connected.";
                return true;
            }

            Status = "Splatoon integration unavailable.";
            return false;
        }
        catch (Exception ex)
        {
            Status = "Splatoon connection failed: " +
                ex.GetBaseException().Message;

            return false;
        }
    }

    private static void Field(
        object element,
        string name,
        object value)
    {
        var member = element.GetType().GetField(name);

        if (member == null)
            throw new MissingFieldException(name);

        member.SetValue(element, value);
    }

    private object CreateLine(VisualLine line)
    {
        var element = Activator.CreateInstance(
            elementType!, new object[] { 2 })!;

        Field(element, "Name", "BlunderNav route line");
        Field(element, "Enabled", true);

        // Splatoon stores coordinates as X, Z, Y.

        Field(element, "refX", line.A.X);
        Field(element, "refY", line.A.Z);
        Field(element, "refZ", line.A.Y);

        Field(element, "offX", line.B.X);
        Field(element, "offY", line.B.Z);
        Field(element, "offZ", line.B.Y);

        Field(element, "radius", 0f);
        Field(element, "color", line.Color);
        Field(element, "thicc", line.Thickness);

        return element;
    }

    private object CreateCircle(VisualCircle circle)
    {
        var element = Activator.CreateInstance(
            elementType!, new object[] { 0 })!;

        Field(element, "Name", "BlunderNav marker");
        Field(element, "Enabled", true);

        Field(element, "refX", circle.Center.X);
        Field(element, "refY", circle.Center.Z);
        Field(element, "refZ", circle.Center.Y);

        Field(element, "radius", circle.Radius);
        Field(element, "color", circle.Color);
        Field(element, "thicc", circle.Thickness);
        Field(element, "Filled", false);

        return element;
    }

    public void Publish(
        IReadOnlyList<VisualLine> lines,
        VisualCircle? next,
        VisualCircle? danger)
    {
        if (!Connect())
            return;

        try
        {
            var elements = new List<object>();

            foreach (var line in lines)
                elements.Add(CreateLine(line));

            if (next.HasValue)
                elements.Add(CreateCircle(next.Value));

            if (danger.HasValue)
                elements.Add(CreateCircle(danger.Value));

            removeMethod!.Invoke(
                plugin, new object[] { Namespace });

            if (elements.Count == 0)
                return;

            var array = Array.CreateInstance(
                elementType!, elements.Count);

            for (int i = 0; i < elements.Count; i++)
                array.SetValue(elements[i], i);

            // -2: Splatoon removes elements on zone change.
            // We also explicitly remove them when disabled.

            addMethod!.Invoke(plugin, new object[]
            {
                Namespace,
                array,
                new long[] { -2 }
            });

            Status = $"Displaying {elements.Count} elements.";
        }
        catch (Exception ex)
        {
            Status = "Splatoon render failed: " +
                ex.GetBaseException().Message;

            log.Warning(Status);
        }
    }

    public void Clear()
    {
        try
        {
            removeMethod?.Invoke(
                plugin, new object[] { Namespace });
        }
        catch
        {
            // Splatoon may have unloaded already.
        }
    }

    public void Dispose()
    {
        Clear();
        plugin = null;
        elementType = null;
        addMethod = null;
        removeMethod = null;
    }
}