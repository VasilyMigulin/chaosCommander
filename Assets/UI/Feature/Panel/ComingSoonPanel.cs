using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Базовая панель-заглушка для мета-разделов, которые ещё не реализованы
    /// (магазин, чёрный рынок, аукцион, открытие бустеров). Показывает заголовок
    /// (через LocalizedText в префабе) и плашку «Скоро», даёт кнопку «Назад» в меню.
    ///
    /// Когда раздел будет готов — наполняем конкретного наследника реальным UI,
    /// раскладку префаба и провязку из MainMenuPanel переписывать не придётся.
    /// </summary>
    public abstract class ComingSoonPanel : SourcePanel
    {
        [Header("Navigation")]
        [SerializeField] private Button _backBtn;

        public override void OnInject()
        {
            base.OnInject();

            // OpenPanel сам закроет этот раздел и откроет MainMenuPanel
            // (см. SettingsPanel — тот же паттерн возврата).
            if (_backBtn != null)
                _backBtn.onClick.AddListener(() => _panelController.OpenPanel<MainMenuPanel>());
        }

        public override void Unject()
        {
            if (_backBtn != null)
                _backBtn.onClick.RemoveAllListeners();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
