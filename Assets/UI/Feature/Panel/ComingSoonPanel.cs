using AwesomeUI.Core.Panel;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Базовая панель для мета-разделов (магазин, чёрный рынок, аукцион, бустеры, журнал).
    /// Кнопка «Назад» теперь ДЕКЛАРАТИВНА — это AwesomeButton с _back=true в префабе
    /// (возврат по журналу истории через SourceCanvas.Back()), поэтому кода навигации здесь нет.
    /// Наследники наполняют панель реальным контентом.
    /// </summary>
    public abstract class ComingSoonPanel : SourcePanel
    {
        public override void Unject() { }
    }
}
