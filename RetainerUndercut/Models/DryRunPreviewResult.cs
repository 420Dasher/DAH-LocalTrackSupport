namespace RetainerUndercut.Models;

public readonly record struct DryRunPreviewResult(
    string RetainerName,
    string ItemName,
    uint ItemId,
    bool IsHq,
    ulong CurrentPrice,
    ulong? LowestCompetitorPrice,
    ulong? ProposedPrice,
    string Reason,
    bool WouldChange,
    bool MatchlistProtected,
    bool SafetyFloorProtected,
    bool MaxDropBlocked,
    bool OutlierBlocked,
    bool PriceRecovery,
    bool PriceRecoveryBlocked);
