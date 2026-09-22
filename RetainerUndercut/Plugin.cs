using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RetainerUndercut.Services;
using RetainerUndercut.Windows;

namespace RetainerUndercut;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/rundercut";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("RetainerUndercut");
    private readonly Configuration configuration;
    private readonly RetainerListingScanner scanner;
    private readonly SingleListingMarketCheck marketCheck;
    private readonly AutoRetainerUndercutRunner autoRunner;
    private readonly MultiRetainerUndercutRunner multiRunner;
    private readonly MainWindow mainWindow;
    private readonly RetainerQuickRunOverlay quickRunOverlay;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Initialize(PluginInterface);

        scanner = new RetainerListingScanner(
            Framework,
            GameInventory,
            GameGui,
            DataManager,
            PlayerState,
            Log);

        marketCheck = new SingleListingMarketCheck(
            Framework,
            GameGui,
            MarketBoard,
            configuration,
            Log);

        autoRunner = new AutoRetainerUndercutRunner(
            Framework,
            GameGui,
            scanner,
            marketCheck,
            configuration,
            Log);

        multiRunner = new MultiRetainerUndercutRunner(
            Framework,
            GameGui,
            scanner,
            marketCheck,
            autoRunner,
            configuration,
            Log);

        mainWindow = new MainWindow(scanner, marketCheck, autoRunner, multiRunner, configuration);
        quickRunOverlay = new RetainerQuickRunOverlay(GameGui, scanner, marketCheck, autoRunner, multiRunner, Log);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Retainer Undercut: preview/apply retainer repricing with HQ/NQ separation, item rules, Matchlist protection, Price Recovery, history, and safety validation.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += quickRunOverlay.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;

        Log.Information("Retainer Undercut v0.1.0 loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= quickRunOverlay.Draw;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;

        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        multiRunner.Dispose();
        autoRunner.Dispose();
        marketCheck.Dispose();
        scanner.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleMainUi();

    private void ToggleMainUi() => mainWindow.Toggle();
}
