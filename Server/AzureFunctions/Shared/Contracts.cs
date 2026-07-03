namespace ChaosCommander.Functions.Shared;

// JSON-контракты, ЗЕРКАЛЬНЫЕ клиентским (Assets/Scripts/DeckBuilder/Backend/*).
// Имена полей должны совпадать один-в-один — их сериализует/десериализует PlayFab между
// клиентом и функцией. Держать в синке при любых правках.

public class CurrencyAmount
{
    public string Code = "";
    public int Amount;
}

public class GrantedCard
{
    public string ItemId = "";
    public int Amount;
}

public class RewardBundle
{
    public List<CurrencyAmount> Currencies { get; set; } = new();
    public List<GrantedCard> Cards { get; set; } = new();
    public List<string> Boosters { get; set; } = new();
    public List<string> Avatars { get; set; } = new();
}

public class BackendResult
{
    public bool Success;
    public string? Reason;
    public List<CurrencyAmount>? Wallet;
}

public class RewardResponse : BackendResult
{
    public RewardBundle Reward { get; set; } = new();
}

// ── Запросы систем ─────────────────────────────────────────────────────────

public class ItemIdRequest       { public string ItemId = ""; }
public class BoosterOpenRequest  { public string BoosterItemId = ""; }
public class ClaimTaskRequest    { public string TaskId = ""; }
public class ReportProgressRequest { public string Type = ""; public int Amount = 1; }
public class ListingIdRequest    { public string ListingId = ""; }
public class ListCardRequest     { public string ItemId = ""; public int PriceAmount; public string PriceCode = "GD"; }

public class MigrateResult { public bool Migrated; public int CardsGranted; }
