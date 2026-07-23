using AwesomeUI.Core.Canvas;

namespace AwesomeUI.Feature
{
    public class MainMenuCanvas : SourceCanvas
    {
        public override void Init()
        {
            base.Init();
            // Инициализация Canvas
        }

        public override void OnInject()
        {
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }

        public override void InvokeCanvas()
        {
            base.InvokeCanvas();

            // Домашний экран меню = GamePanel (выбор режима PvP/PvE). MainMenuPanel удалён: профиль,
            // настройки и выход живут в HUD (топ-бар), разделы — в нижней навигации.
            // Открываем здесь, т.к. CloseCanvas закрывает все панели — при возврате в меню нужен «дом».
            OpenPanel<GamePanel>();
        }
    }
}
