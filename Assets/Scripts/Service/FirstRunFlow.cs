using UnityEngine;

namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Флаги цикла ПЕРВОГО захода (PlayerPrefs): язык выбран → туториал пройден → ник задан («Давай
    /// знакомиться») → стартовые бустеры выданы. Роутинг: InitState (язык → туториал → логин), LoginPanel
    /// (панель ника после гостевого входа), MenuState (бустеры после первого логина).
    /// Лежит в Service — виден всем сборкам. Сброс всех флагов — для тестов (дев-кнопка/вручную).
    /// </summary>
    public static class FirstRunFlow
    {
        const string LanguageKey = "flow.language_done";
        const string TutorialKey = "flow.tutorial_done";
        const string NameKey     = "flow.name_chosen";
        const string StarterKey  = "flow.starter_granted";

        public static bool LanguageChosen
        {
            get => PlayerPrefs.GetInt(LanguageKey, 0) == 1;
            set { PlayerPrefs.SetInt(LanguageKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool TutorialDone
        {
            get => PlayerPrefs.GetInt(TutorialKey, 0) == 1;
            set { PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Ник (displayName) задан на панели «Давай знакомиться» — панель больше не показываем.</summary>
        public static bool NameChosen
        {
            get => PlayerPrefs.GetInt(NameKey, 0) == 1;
            set { PlayerPrefs.SetInt(NameKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool StarterGranted
        {
            get => PlayerPrefs.GetInt(StarterKey, 0) == 1;
            set { PlayerPrefs.SetInt(StarterKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Полный сброс цикла первого захода (для тестов).</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(LanguageKey);
            PlayerPrefs.DeleteKey(TutorialKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.DeleteKey(StarterKey);
            PlayerPrefs.Save();
        }
    }
}
