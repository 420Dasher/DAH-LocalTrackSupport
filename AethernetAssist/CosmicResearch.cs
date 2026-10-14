using System;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.2 RESEARCH1
    //
    // Read-only capture of:
    // - NPC BaseIDs
    // - Actual world XYZ positions
    // - Visible native dialogue menus
    // - Destination labels
    //
    // No teleport, interaction, movement or callbacks.

    private string BuildCosmicResearchReport()
    {
        var output = new StringBuilder();

        output.AppendLine(
            "=== AETHERNETASSIST DEV4.2 COSMIC RESEARCH ===");

        output.AppendLine(
            "UTC: " + DateTime.UtcNow.ToString("O"));

        output.AppendLine(
            "Territory: " + Client.TerritoryType);

        output.AppendLine(
            "Drivingway map reference: X=21.9 Y=13.2 Z=-1.4");

        var player = Objects.LocalPlayer;

        if (player == null)
        {
            output.AppendLine("Player unavailable.");
            return output.ToString();
        }

        output.AppendLine(
            "Player world XYZ: " +
            player.Position.X.ToString("F3") + ", " +
            player.Position.Y.ToString("F3") + ", " +
            player.Position.Z.ToString("F3"));

        output.AppendLine();
        output.AppendLine("=== NEARBY NON-PLAYER OBJECTS ===");

        // Avoid collecting player characters or names.
        // Scan only when a diagnostic export is requested.
        var objects = Objects
            .Where(obj =>
                obj != null &&
                obj.ObjectKind != ObjectKind.Pc &&
                Vector3.Distance(
                    player.Position, obj.Position) <= 45f)
            .OrderBy(obj =>
                Vector3.Distance(
                    player.Position, obj.Position))
            .Take(30)
            .ToArray();

        output.AppendLine(
            "Nearby candidates: " + objects.Length);

        foreach (var obj in objects)
        {
            float distance = Vector3.Distance(
                player.Position, obj.Position);

            output.AppendLine(
                "Name=" + obj.Name.ToString() +
                " | BaseID=" + obj.BaseId +
                " | Kind=" + obj.ObjectKind +
                " | Targetable=" + obj.IsTargetable +
                " | Distance=" + distance.ToString("F2") +
                " | WorldXYZ=(" +
                    obj.Position.X.ToString("F3") + ", " +
                    obj.Position.Y.ToString("F3") + ", " +
                    obj.Position.Z.ToString("F3") + ")");

            if (obj.Name.ToString().Contains(
                "Drivingway",
                StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine(
                    ">>> DRIVINGWAY CANDIDATE FOUND <<<");
            }

            if (obj.Name.ToString().Contains(
                "Cruisingway",
                StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine(
                    ">>> CRUISINGWAY CANDIDATE FOUND <<<");
            }
        }

        output.AppendLine();
        output.AppendLine("=== VISIBLE NATIVE ADDONS ===");

        foreach (var name in new[]
        {
            "Talk",
            "SelectString",
            "SelectIconString",
            "SelectYesno",
            "TelepotTown",
            "SelectList"
        })
        {
            var addon = GetAddon(name);

            output.AppendLine(
                name + ": " +
                (addon == null
                    ? "Not visible"
                    : "Visible | AtkValuesCount=" +
                      addon->AtkValuesCount));
        }

        output.AppendLine();
        output.AppendLine("=== SELECTSTRING OPTIONS ===");

        try
        {
            var addon =
                (AddonSelectString*)GetAddon("SelectString");

            if (addon == null)
            {
                output.AppendLine(
                    "SelectString is not currently open.");
            }
            else
            {
                var menu = addon->PopupMenu.PopupMenu;

                if (menu.EntryNames == null)
                {
                    output.AppendLine(
                        "Menu has no entry names.");
                }
                else
                {
                    int count = Math.Clamp(
                        menu.EntryCount, 0, 40);

                    output.AppendLine(
                        "Native entries: " + count);

                    for (int i = 0; i < count; i++)
                    {
                        var pointer =
                            menu.EntryNames[i].Value;

                        if (pointer == null)
                        {
                            output.AppendLine(
                                "[" + i + "] Null");
                            continue;
                        }

                        string name = Clean(
                            Marshal.PtrToStringUTF8(
                                (nint)pointer) ?? "");

                        output.AppendLine(
                            "[" + i + "] " + name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "SelectString read error: " + ex.Message);
        }

        output.AppendLine();
        output.AppendLine("=== RESEARCH STATUS ===");

        output.AppendLine(
            "Read-only: no movement or travel submitted.");

        output.AppendLine(
            "Next: identify Drivingway NPC and menu order.");

        return output.ToString();
    }
}