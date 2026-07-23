using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Drop-on модуль: пока объект активен — HUD (MenuHudPanel) скрыт. Повесь на корень попапа/оверлея,
    /// который должен перекрывать бары (осмотр карты в коллекции, полноэкранная награда и т.п.). Ноль кода.
    ///
    /// ЗАЩИТА ОТ INIT: на старте канвас сам активирует/деактивирует панели (OnEnable/OnDisable летят «вхолостую»).
    /// Пока UI не «прогрелся» (Armed=false), модуль HUD НЕ трогает — Armed взводит MenuHudPanel.OnInject
    /// (уже после init-churn). Плюс _hiding-guard: OnDisable «показывает» HUD только если мы его прятали.
    /// </summary>
    public class HidesHud : MonoBehaviour
    {
        /// <summary>UI готов — с этого момента HidesHud реагирует на активацию (взводит MenuHudPanel).</summary>
        public static bool Armed;

        bool _hiding;

        void OnEnable()  { if (Armed) Hide(); }
        void OnDisable() { Show(); }

        void Hide()
        {
            if (_hiding) return;
            _hiding = true;
            HudVisibility.Hide(this);
        }

        void Show()
        {
            if (!_hiding) return;
            _hiding = false;
            HudVisibility.Show(this);
        }
    }
}
