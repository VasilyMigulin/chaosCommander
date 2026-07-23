using System;
using UnityEngine;

namespace AwesomeUI.Interface
{
    public interface IPanelController
    {
        T OpenPanel<T>(params Action[] callback) where T : IPanel;

        /// <summary>Открыть КОНКРЕТНЫЙ экземпляр панели (декларативная навигация из AwesomeButton).
        /// Ведёт журнал истории — предыдущая панель кладётся в стек для «назад».</summary>
        IPanel OpenPanel(IPanel panel, params Action[] callback);

        /// <summary>Открыть панель ПО ID (SourcePanel.PanelId) — префабы кнопки и панели связаны
        /// только строковым id, без прямой объект-ссылки. Null, если панели с таким id нет в сцене.</summary>
        IPanel OpenPanel(string panelId, params Action[] callback);

        /// <summary>Найти панель по id (без открытия). Null, если нет.</summary>
        IPanel GetPanel(string panelId);

        T ClosePanel<T>() where T : IPanel;

        /// <summary>Есть ли куда возвращаться (журнал истории не пуст).</summary>
        bool CanGoBack { get; }

        /// <summary>Вернуться на предыдущую панель (жест «назад» Android/iOS, кнопка «назад»).
        /// false — если история пуста (корневой экран).</summary>
        bool Back();

        /// <summary>Найти панель по типу среди уже собранных канвасом (см. SourceCanvas._panels), без
        /// открытия/закрытия. Null, если панели нет (не собрана/удалена в этой сцене) — SourcePanel
        /// использует это в GatePanelButton, чтобы автоматически гасить кнопки недоступных разделов.</summary>
        T GetPanel<T>() where T : class, IPanel;
        bool TryGetPanel<T>(out T panel) where T : class, IPanel;
    }
}
