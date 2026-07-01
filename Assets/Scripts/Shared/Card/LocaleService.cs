using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Shared
{
    /// <summary>Язык в списке выбора: код («ru»/«en») + отображаемое имя.</summary>
    public sealed class LocaleOption
    {
        public string Code;
        public string DisplayName;
    }

    /// <summary>
    /// Бэкенд переключения языка. По умолчанию — DefaultLocaleBackend (просто хранит код, без пакета
    /// локализации). При наличии Unity Localization подменяется на UnityLocaleBackend, который реально
    /// переключает активную локаль.
    /// </summary>
    public interface ILocaleBackend
    {
        IReadOnlyList<LocaleOption> Available { get; }
        string Current { get; }
        void Apply(string code);
    }

    /// <summary>
    /// Глобальный сервис языка для UI (настройки). Шлёт событие Changed при смене — на него реагируют
    /// панель настроек и (при необходимости) ре-рендер карт. Сохраняет выбор в PlayerPrefs.
    /// Зеркалит шаблон CardTextLocalization.Provider — UI не зависит от пакета локализации.
    /// </summary>
    public static class LocaleService
    {
        const string PrefKey = "settings.language";

        public static ILocaleBackend Backend = new DefaultLocaleBackend();
        public static event Action Changed;

        public static IReadOnlyList<LocaleOption> Available => Backend.Available;
        public static string Current => Backend.Current;

        public static void SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            Backend.Apply(code);
            PlayerPrefs.SetString(PrefKey, code);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Применить сохранённый язык (зовётся из bootstrap на старте).</summary>
        public static void ApplySaved()
        {
            string saved = PlayerPrefs.GetString(PrefKey, null);
            if (!string.IsNullOrEmpty(saved) && saved != Backend.Current)
            {
                Backend.Apply(saved);
                Changed?.Invoke();
            }
        }

        /// <summary>Сообщить подписчикам о внешней смене языка (напр. из Unity Localization).</summary>
        public static void RaiseChanged() => Changed?.Invoke();
    }

    /// <summary>Применяет сохранённый язык на старте (для случая БЕЗ пакета локализации; при наличии
    /// пакета CardLocalizationBootstrap всё равно применит сохранённый поверх Unity Localization).</summary>
    static class LocaleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => LocaleService.ApplySaved();
    }

    /// <summary>Фоллбэк-бэкенд без пакета: хранит код в памяти; язык читает DefaultCardTextProvider.</summary>
    public sealed class DefaultLocaleBackend : ILocaleBackend
    {
        string _current = "ru";

        public IReadOnlyList<LocaleOption> Available { get; } = new[]
        {
            new LocaleOption { Code = "ru", DisplayName = "Русский" },
            new LocaleOption { Code = "en", DisplayName = "English" },
        };

        public string Current => _current;
        public void Apply(string code) => _current = code;
    }
}
