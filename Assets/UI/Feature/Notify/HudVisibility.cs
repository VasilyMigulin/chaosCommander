using System;
using System.Collections.Generic;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Управление видимостью персистентного HUD (MenuHudPanel). Любой фуллскрин-попап/оверлей,
    /// который должен перекрыть бары (осмотр карты, награда, диалог), просит спрятать HUD и вернуть
    /// его при закрытии. РЕФЕРЕНС-СЧЁТЧИК по владельцам: HUD виден, только когда НИКТО не просит скрыть
    /// (два попапа не «передерутся» — HUD вернётся, когда закроется последний).
    ///
    /// Проще всего — компонент HidesHud на объекте попапа (прячет пока активен). Или вручную:
    ///   HudVisibility.Hide(this) при открытии / HudVisibility.Show(this) при закрытии.
    /// </summary>
    public static class HudVisibility
    {
        static readonly HashSet<object> _hiders = new HashSet<object>();

        /// <summary>true = HUD виден (никто не прячет).</summary>
        public static bool Visible => _hiders.Count == 0;

        /// <summary>Событие смены видимости (true = показать, false = спрятать).</summary>
        public static event Action<bool> VisibleChanged;

        public static void Hide(object owner)
        {
            if (owner == null) return;
            if (_hiders.Add(owner) && _hiders.Count == 1) VisibleChanged?.Invoke(false);
        }

        public static void Show(object owner)
        {
            if (owner == null) return;
            if (_hiders.Remove(owner) && _hiders.Count == 0) VisibleChanged?.Invoke(true);
        }

        /// <summary>Сброс всех запросов (напр. при уходе из меню-сцены).</summary>
        public static void Clear()
        {
            if (_hiders.Count == 0) return;
            _hiders.Clear();
            VisibleChanged?.Invoke(true);
        }
    }
}
