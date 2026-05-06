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

            OpenPanel<MainMenuPanel>();
        }
    }
}
