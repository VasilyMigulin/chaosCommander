using System;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// Промокоды: ввод кода → серверная выдача плюшек (валюта / бустеры / аватары / карты).
    ///
    /// Источников кода ДВА, но клиенту это не важно — сервер (handlers.RedeemPromo) разбирается сам:
    ///   • свои кампании — Title Data "promoConfig" (многоразовый код, лимит на аккаунт, окно дат);
    ///   • персональные коды бэкеров — нативные купоны PlayFab (уникальные, одноразовые, генерятся
    ///     пачкой в Game Manager → Economy → Catalogs → Coupons).
    ///
    /// Валидация, лимиты и сама выдача — ТОЛЬКО на сервере. Клиент лишь чистит ввод и применяет ответ:
    /// кошелёк (PlayerWallet) и карты (PlayerLibrary) — как после открытия бустера.
    /// </summary>
    public static class PromoService
    {
        [Serializable] class RedeemRequest { public string Code; }

        /// <summary>Ответ активации: результат + что выдано (+ ключ заголовка и тег тира, если сервер прислал).</summary>
        [Serializable]
        public class RedeemResponse : RewardResponse   // Success/Reason/Wallet/Reward
        {
            public string TitleKey;   // ключ локализации для заголовка попапа («Плюшки основателя»)
            public string Tag;        // тег игрока, выданный этим кодом (founder_gold и т.п.)
        }

        /// <summary>Максимальная длина кода — совпадает с серверным PROMO_MAX_LEN (длиннее сервер отсечёт).</summary>
        public const int MaxCodeLength = 64;

        /// <summary>
        /// Убрать из ввода то, что игрок дописал случайно (пробелы по краям, перевод строки из буфера).
        /// Регистр и дефисы НЕ трогаем: свои кампании сервер сверяет без учёта регистра, а купоны PlayFab
        /// строчные с дефисами — их надо передать как есть.
        /// </summary>
        public static string Normalize(string code)
            => string.IsNullOrEmpty(code) ? "" : code.Trim().Replace("\r", "").Replace("\n", "");

        /// <summary>Похоже ли на код вообще (для гейта кнопки) — пустой/слишком длинный на сервер не шлём.</summary>
        public static bool LooksValid(string code)
        {
            var c = Normalize(code);
            return c.Length > 0 && c.Length <= MaxCodeLength;
        }

        /// <summary>Активировать промокод. При успехе кошелёк и библиотека уже обновлены к моменту колбэка.</summary>
        public static void Redeem(string code, Action<RedeemResponse> onSuccess, Action<string> onError = null)
        {
            var clean = Normalize(code);
            if (!LooksValid(clean)) { onError?.Invoke("bad_request"); return; }

            FunctionService.Call<RedeemRequest, RedeemResponse>(
                BackendConfig.Fn.RedeemPromo, new RedeemRequest { Code = clean },
                r =>
                {
                    if (r != null && r.Success)
                    {
                        PlayerWallet.ApplyIfPresent(r.Wallet);
                        PlayerLibrary.AddGranted(r.Reward?.Cards, BackendSession.Config);   // карты → в коллекцию
                    }
                    onSuccess?.Invoke(r);
                }, onError);
        }
    }
}
