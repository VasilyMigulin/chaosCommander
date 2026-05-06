using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Attributes;
using AwesomeUI.Interface;

namespace AwesomeUI.Feature
{
    public class LobbyPanel : SourcePanel
    {
        // [UIInject] private SomeService _service;

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
            // Инициализация Panel
        }

        public override void OnInject()
        {
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }

        public override void Unject()
        {
            // Очистка инъекций
        }
    }
}
