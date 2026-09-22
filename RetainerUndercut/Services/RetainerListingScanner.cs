using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using RetainerUndercut.Models;

namespace RetainerUndercut.Services;

public sealed class RetainerListingScanner : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(250);

    private readonly IFramework framework;
    private readonly IGameInventory gameInventory;
    private readonly IGameGui gameGui;
    private readonly IDataManager dataManager;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;

    private readonly List<ListingSnapshot> listings = [];
    private DateTime nextScanUtc;

    public RetainerListingScanner(
        IFramework framework,
        IGameInventory gameInventory,
        IGameGui gameGui,
        IDataManager dataManager,
        IPlayerState playerState,
        IPluginLog log)
    {
        this.framework = framework;
        this.gameInventory = gameInventory;
        this.gameGui = gameGui;
        this.dataManager = dataManager;
        this.playerState = playerState;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
    }

    public IReadOnlyList<ListingSnapshot> Listings => listings;

    public string Status { get; private set; } = "Waiting for character data.";

    public string Diagnostic { get; private set; } = "No scan has run yet.";

    public bool SellListVisible { get; private set; }

    public bool MarketContainerLoaded { get; private set; }

    public ulong ActiveRetainerId { get; private set; }

    public string ActiveRetainerName { get; private set; } = "None";

    public DateTime? LastSuccessfulScanUtc { get; private set; }

    public int MappedListings { get; private set; }

    public bool UiOrderMappingReady => listings.Count > 0 &&
        MappedListings == listings.Count &&
        listings.All(x => x.UnitPrice is >= 1 and <= 999_999_999UL);

    public void Dispose() => framework.Update -= OnFrameworkUpdate;

    public void ForceRefresh() => nextScanUtc = DateTime.MinValue;

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        if (now < nextScanUtc)
            return;

        nextScanUtc = now + ScanInterval;

        try
        {
            Scan(now);
        }
        catch (Exception ex)
        {
            Status = "Scan failed. Check /xllog.";
            Diagnostic = ex.Message;
            log.Error(ex, "Retainer Undercut scanner failed.");
        }
    }

    private unsafe void Scan(DateTime nowUtc)
    {
        listings.Clear();

        if (!playerState.IsLoaded || playerState.ContentId == 0)
        {
            ResetTransientState();
            Status = "Log in to a character first.";
            Diagnostic = "PlayerState is not loaded.";
            return;
        }

        var sellList = gameGui.GetAddonByName("RetainerSellList");
        SellListVisible = !sellList.IsNull && sellList.IsVisible;

        ActiveRetainerId = GetActiveRetainerId();
        ActiveRetainerName = ResolveRetainerName(ActiveRetainerId);

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
        {
            MarketContainerLoaded = false;
            Status = "InventoryManager is unavailable.";
            Diagnostic = "InventoryManager.Instance() returned null.";
            return;
        }

        var marketContainer = inventoryManager->GetInventoryContainer(InventoryType.RetainerMarket);
        MarketContainerLoaded = marketContainer is not null && marketContainer->IsLoaded;

        Diagnostic =
            $"sellList={(SellListVisible ? "visible" : "hidden")}; " +
            $"activeRetainer={(ActiveRetainerId == 0 ? "none" : ActiveRetainerName)}; " +
            $"marketContainer={(MarketContainerLoaded ? "loaded" : "waiting")}";

        if (!SellListVisible)
        {
            Status = ActiveRetainerId == 0
                ? "Open a summoning bell, select a retainer, then open 'Sell items in your inventory on the market'."
                : $"{ActiveRetainerName} is selected. Open the retainer sell list.";
            return;
        }

        if (ActiveRetainerId == 0)
        {
            Status = "Sell list is visible, but no active retainer ID was detected.";
            return;
        }

        if (!MarketContainerLoaded)
        {
            Status = $"{ActiveRetainerName}: waiting for RetainerMarket container.";
            return;
        }

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var visualOrder = ReadSellListVisualOrder();
        MappedListings = 0;

        foreach (var item in gameInventory.GetInventoryItems(GameInventoryType.RetainerMarket))
        {
            if (item.IsEmpty || item.BaseItemId == 0 || item.Quantity <= 0)
                continue;

            var slot = checked((short)item.InventorySlot);
            var unitPrice = inventoryManager->GetRetainerMarketPrice(slot);

            var itemName = itemSheet.TryGetRow(item.BaseItemId, out var itemRow)
                ? itemRow.Name.ToString()
                : $"Unknown item #{item.BaseItemId}";

            var uiRow = visualOrder.TryGetValue(slot, out var mappedRow) ? mappedRow : -1;
            if (uiRow >= 0)
                MappedListings++;

            listings.Add(new ListingSnapshot(
                uiRow,
                slot,
                item.BaseItemId,
                itemName,
                item.Quantity,
                item.IsHq,
                unitPrice));
        }

        listings.Sort(static (a, b) =>
        {
            var aRow = a.UiRow >= 0 ? a.UiRow : int.MaxValue;
            var bRow = b.UiRow >= 0 ? b.UiRow : int.MaxValue;
            var rowCompare = aRow.CompareTo(bRow);
            return rowCompare != 0 ? rowCompare : a.MarketSlot.CompareTo(b.MarketSlot);
        });

        var invalidPriceCount = listings.Count(x => x.UnitPrice is < 1 or > 999_999_999UL);
        Diagnostic += $"; uiOrder={MappedListings}/{listings.Count} mapped; invalidPrices={invalidPriceCount}";
        LastSuccessfulScanUtc = nowUtc;
        Status = invalidPriceCount > 0
            ? $"{ActiveRetainerName}: waiting for {invalidPriceCount} listing price(s) to become valid."
            : $"{ActiveRetainerName}: detected {listings.Count} active market listing(s).";
    }

    private void ResetTransientState()
    {
        SellListVisible = false;
        MarketContainerLoaded = false;
        ActiveRetainerId = 0;
        ActiveRetainerName = "None";
        listings.Clear();
        MappedListings = 0;
    }

    private unsafe Dictionary<int, int> ReadSellListVisualOrder()
    {
        var result = new Dictionary<int, int>();
        var addonRef = gameGui.GetAddonByName("RetainerSellList");
        if (addonRef.IsNull || !addonRef.IsVisible)
            return result;

        var addon = (AtkUnitBase*)addonRef.Address;
        if (addon is null || addon->AtkValues is null)
            return result;

        const int firstSlotValueIndex = 15;
        const int valuesPerRow = 13;
        const int maxRows = 20;

        for (var row = 0; row < maxRows; row++)
        {
            var atkIndex = firstSlotValueIndex + (row * valuesPerRow);
            if (atkIndex >= addon->AtkValuesCount)
                break;

            var value = addon->AtkValues[atkIndex];
            if ((int)value.Type == 0)
                continue;

            var marketSlot = value.Int;
            if (marketSlot < 0 || marketSlot >= 20)
                continue;

            result.TryAdd(marketSlot, row);
        }

        return result;
    }

    private static unsafe ulong GetActiveRetainerId()
    {
        var gameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (gameFramework is null || gameFramework->UIModule is null)
            return 0;

        var itemOrderModule = gameFramework->UIModule->GetItemOrderModule();
        return itemOrderModule is null ? 0 : itemOrderModule->ActiveRetainerId;
    }

    private static unsafe string ResolveRetainerName(ulong retainerId)
    {
        if (retainerId == 0)
            return "None";

        var manager = RetainerManager.Instance();
        if (manager is null)
            return $"Retainer {retainerId:X}";

        var count = manager->GetRetainerCount();
        for (uint index = 0; index < count; index++)
        {
            var retainer = manager->GetRetainerBySortedIndex(index);
            if (retainer is null || retainer->RetainerId != retainerId)
                continue;

            return string.IsNullOrWhiteSpace(retainer->NameString)
                ? $"Retainer {index + 1}"
                : retainer->NameString;
        }

        return $"Retainer {retainerId:X}";
    }
}
