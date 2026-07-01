using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Core.Card
{
    /// <summary>
    /// При смене языка (LocaleService.Changed) перестраивает локализованный текст у всех активных
    /// карточных вьюшек. Статическая подписка ставится один раз на старте — отдельный объект в сцене
    /// не нужен. Срабатывает редко (только при переключении языка), поэтому FindObjectsByType ок.
    /// </summary>
    static class CardLocalizationRefresher
    {
        static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_installed) return;   // защита от двойной подписки (Enter Play Mode без domain reload)
            _installed = true;
            LocaleService.Changed += RefreshAll;
        }

        static void RefreshAll()
        {
            var views = Object.FindObjectsByType<CardBaseView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var v in views)
                if (v != null) v.RefreshLocalization();
        }
    }
}
