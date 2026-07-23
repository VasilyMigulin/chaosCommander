using UnityEngine;

namespace AwesomeUI.Core.Panel
{
    /// <summary>
    /// Помечает string-поле идентификатора панели: в инспекторе рисуется дропдаун из каталога
    /// (категория UI/Panel) + кнопка «+», а пустое значение показывается как «(= ИмяКласса)».
    /// Рантайм-логика не меняется — SourcePanel.PanelId при пустом поле возвращает имя класса.
    /// </summary>
    public class PanelIdAttribute : PropertyAttribute { }
}
