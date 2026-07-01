// Компилируется ТОЛЬКО когда установлен пакет com.unity.localization (см. define-constraint
// UNITY_LOCALIZATION_PRESENT в Game.Core.Localization.asmdef). Пока пакета/таблиц нет — игра
// работает на DefaultCardTextProvider (авторский RU), этот файл просто исключён из сборки.
#if UNITY_LOCALIZATION_PRESENT
using System.Collections.Generic;
using Game.Core.Shared;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Game.Core.Localization
{
    /// <summary>
    /// Бэкенд локализации карт поверх Unity Localization. Тексты лежат в String Table Collection
    /// "CardText", ключи — card.{expansionId}.{id}.name / .desc (см. CardTextLocalization).
    /// Возвращаем фоллбэк (авторский RU), если таблицы/ключа нет — поэтому до настройки таблиц
    /// всё показывается по-русски.
    /// </summary>
    public sealed class UnityLocalizationCardTextProvider : ICardTextProvider
    {
        public const string TableName = "CardText";

        public string Language
        {
            get
            {
                var locale = LocalizationSettings.SelectedLocale;
                return locale != null ? locale.Identifier.Code : "ru";
            }
        }

        public string GetText(string key, string fallback)
        {
            try
            {
                var db = LocalizationSettings.StringDatabase;
                var handle = db.GetTableEntryAsync(TableName, key);
                var result = handle.WaitForCompletion();   // синхронно (таблица грузится Addressables)
                var entry = result.Entry;
                if (entry == null) return fallback;
                var s = entry.GetLocalizedString();
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch
            {
                return fallback;   // таблицы ещё не настроены / ошибка загрузки → авторский текст
            }
        }
    }

    /// <summary>Переключение языка поверх Unity Localization (для меню настроек).</summary>
    public sealed class UnityLocaleBackend : ILocaleBackend
    {
        public IReadOnlyList<LocaleOption> Available
        {
            get
            {
                var list = new List<LocaleOption>();
                var locales = LocalizationSettings.AvailableLocales?.Locales;
                if (locales != null)
                    foreach (var l in locales)
                        list.Add(new LocaleOption { Code = l.Identifier.Code, DisplayName = FriendlyName(l.Identifier.Code, l.LocaleName) });
                return list;
            }
        }

        public string Current
        {
            get { var l = LocalizationSettings.SelectedLocale; return l != null ? l.Identifier.Code : "ru"; }
        }

        public void Apply(string code)
        {
            var locales = LocalizationSettings.AvailableLocales?.Locales;
            if (locales == null) return;
            foreach (var l in locales)
                if (l.Identifier.Code == code || l.Identifier.Code.StartsWith(code))
                { LocalizationSettings.SelectedLocale = l; return; }
        }

        // Красивые нативные названия для дропдауна (иначе показалось бы «Russian (Russia)»).
        static string FriendlyName(string code, string fallback)
        {
            if (code == null) return fallback;
            if (code.StartsWith("ru")) return "Русский";
            if (code.StartsWith("en")) return "English";
            return fallback;
        }
    }

    /// <summary>Подключает Unity-Localization-бэкенды на старте игры (заменяют passthrough).</summary>
    static class CardLocalizationBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            CardTextLocalization.Provider = new UnityLocalizationCardTextProvider();
            LocaleService.Backend = new UnityLocaleBackend();
            try
            {
                LocalizationSettings.SelectedLocaleChanged += _ => LocaleService.RaiseChanged();
                LocaleService.ApplySaved();
            }
            catch { /* локали ещё не готовы — применится при первом обращении */ }
        }
    }
}
#endif
