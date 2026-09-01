using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using DiscordActivityHonorific.Windows;
using DiscordActivityHonorific.Updaters;
using DiscordActivityHonorific.Configs;
using System;

namespace DiscordActivityHonorific;
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static IPluginLog PluginLog { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    private const string CommandName = "/discordactivityhonorific";
    private const string CommandHelpMessage =
        $"Available subcommands for {CommandName}: config, enable, disable, spotify-auth <client-id>, spotify-enable, spotify-disable, spotify-status";

    private Config Config { get; init; }

    private WindowSystem WindowSystem { get; init; }
    private ConfigWindow ConfigWindow { get; init; }
    private Updater Updater { get; init; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Config ?? new() { ActivityConfigs = ActivityConfig.GetDefaults() };
        #region Deprecated
        new ConfigMigrator(PluginInterface).MaybeMigrate(Config);
        #endregion
        Updater = new(ChatGui, Config, Framework, PluginInterface, PluginLog);
        ConfigWindow = new(Config, new(), PluginInterface, Updater);
        WindowSystem = new(nameof(DiscordActivityHonorific));
        WindowSystem.AddWindow(ConfigWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = CommandHelpMessage
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        Updater.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var split = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subcommand = split.Length > 0 ? split[0].ToLowerInvariant() : string.Empty;
        var argument = split.Length > 1 ? split[1].Trim() : string.Empty;

        switch (subcommand)
        {
            case "config":
                ToggleConfigUI();
                break;
            case "enable":
                Config.Enabled = true;
                SaveConfig();
                _ = Updater.Start();
                break;
            case "disable":
                Config.Enabled = false;
                SaveConfig();
                _ = Updater.Stop();
                break;
            case "spotify-auth":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    ChatGui.PrintError($"Usage: {CommandName} spotify-auth <Spotify Client ID>", "DAH-LocalSpotifySupport");
                    return;
                }
                _ = Updater.AuthenticateSpotify(argument);
                break;
            case "spotify-enable":
                Config.SpotifyLocalFallbackEnabled = true;
                SaveConfig();
                ChatGui.Print("Spotify local-file fallback enabled.");
                break;
            case "spotify-disable":
                Config.SpotifyLocalFallbackEnabled = false;
                SaveConfig();
                Updater.DisableSpotifyLocalFallback();
                ChatGui.Print("Spotify local-file fallback disabled.");
                break;
            case "spotify-status":
                ChatGui.Print(Updater.GetSpotifyStatus());
                break;
            default:
                ChatGui.Print(CommandHelpMessage);
                break;
        }
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();

    private void SaveConfig() => PluginInterface.SavePluginConfig(Config);
}
