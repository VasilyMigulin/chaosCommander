using System;
using System.Collections.Generic;

namespace Game.Core.Backend
{
    /// <summary>
    /// Общие DTO бэкенд-слоя. Это JSON-контракт с Azure Functions — поля должны совпадать
    /// один-в-один с тем, что возвращают/принимают серверные функции.
    /// </summary>

    /// <summary>Одна выданная/начисленная валюта.</summary>
    [Serializable]
    public class CurrencyAmount
    {
        public string Code;   // "GD" / "GM"
        public int    Amount;
    }

    /// <summary>Одна выданная карта (по item id каталога).</summary>
    [Serializable]
    public class GrantedCard
    {
        public string ItemId;      // "{expansionId}_{cardId}"
        public int    Amount;

        public bool TryResolve(out string expansionId, out int cardId)
            => CardItemId.TryParse(ItemId, out expansionId, out cardId);
    }

    /// <summary>
    /// Унифицированный результат «что игрок получил» — возвращается всеми системами выдачи
    /// (login/daily/booster/purchase). Клиент обновляет кошелёк/библиотеку и играет reveal.
    /// </summary>
    [Serializable]
    public class RewardBundle
    {
        public List<CurrencyAmount> Currencies = new List<CurrencyAmount>();
        public List<GrantedCard>    Cards      = new List<GrantedCard>();
        public List<string>         Boosters   = new List<string>();  // item id бустеров
        public List<string>         Avatars    = new List<string>();  // item id аватаров

        public bool IsEmpty =>
            (Currencies == null || Currencies.Count == 0) &&
            (Cards      == null || Cards.Count == 0) &&
            (Boosters   == null || Boosters.Count == 0) &&
            (Avatars    == null || Avatars.Count == 0);
    }

    /// <summary>Базовый ответ функции: успех + опциональная причина отказа + свежий кошелёк.</summary>
    [Serializable]
    public class BackendResult
    {
        public bool   Success;
        public string Reason;                    // код/текст отказа при Success=false
        public List<CurrencyAmount> Wallet;      // актуальные балансы после операции (если сервер прислал)
    }

    /// <summary>Ответ операции выдачи (покупка/открытие/клейм): результат + что выдано.</summary>
    [Serializable]
    public class RewardResponse : BackendResult
    {
        public RewardBundle Reward = new RewardBundle();
    }
}
