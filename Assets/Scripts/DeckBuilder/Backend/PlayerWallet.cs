using System;
using System.Collections.Generic;

namespace Game.Core.Backend
{
    /// <summary>
    /// Рантайм-кэш валют игрока (Gold/Gems), ключ = код валюты ("GD"/"GM").
    /// Источник истины — сервер: балансы приходят либо из инвентаря при загрузке,
    /// либо в ответах функций (BackendResult.Wallet / RewardBundle.Currencies).
    /// UI подписывается на OnChanged.
    /// </summary>
    public static class PlayerWallet
    {
        static readonly Dictionary<string, int> _balances = new Dictionary<string, int>();

        /// <summary>Вызывается при любом изменении балансов.</summary>
        public static event Action OnChanged;

        public static int Gold => Get(BackendConfig.GoldCode);
        public static int Gems => Get(BackendConfig.GemsCode);

        public static int Get(string code)
            => code != null && _balances.TryGetValue(code, out var v) ? v : 0;

        /// <summary>Полностью заменить набор балансов (при загрузке/после операции).</summary>
        public static void SetAll(IEnumerable<CurrencyAmount> currencies)
        {
            _balances.Clear();
            if (currencies != null)
                foreach (var c in currencies)
                    if (!string.IsNullOrEmpty(c.Code))
                        _balances[c.Code] = c.Amount;
            OnChanged?.Invoke();
        }

        /// <summary>Точечно выставить баланс валюты.</summary>
        public static void Set(string code, int amount)
        {
            if (string.IsNullOrEmpty(code)) return;
            _balances[code] = amount;
            OnChanged?.Invoke();
        }

        /// <summary>Применить кошелёк из ответа функции, если он присутствует (иначе no-op).</summary>
        public static void ApplyIfPresent(List<CurrencyAmount> wallet)
        {
            if (wallet != null && wallet.Count > 0) SetAll(wallet);
        }

        public static void Clear()
        {
            _balances.Clear();
            OnChanged?.Invoke();
        }
    }
}
