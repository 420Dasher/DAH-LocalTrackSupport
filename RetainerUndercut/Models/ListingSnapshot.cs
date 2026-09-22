namespace RetainerUndercut.Models;

public readonly record struct ListingSnapshot(
    int UiRow,
    short MarketSlot,
    uint ItemId,
    string ItemName,
    int Quantity,
    bool IsHq,
    ulong UnitPrice)
{
    public ulong TotalPrice => UnitPrice * (ulong)Math.Max(Quantity, 0);

    public string UiRowLabel => UiRow >= 0 ? (UiRow + 1).ToString() : "?";
}
