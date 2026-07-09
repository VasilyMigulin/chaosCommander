using AwesomeUI.Core.Canvas;

namespace AwesomeUI.Feature.Login
{
    public class LoginCanvas : SourceCanvas
    {
        public override void Init()
        {
            base.Init();
        }

        public override void OnInject()
        {
            base.OnInject();
        }

        public override void InvokeCanvas()
        {
            base.InvokeCanvas();

            // Роутинг стартовой панели канваса — ЕДИНАЯ точка (раньше жёсткий OpenPanel<LoginPanel>
            // перебивал открытие панели языка): первый запуск → выбор языка, иначе — логин.
            if (!Game.Core.Service.FirstRunFlow.LanguageChosen)
                OpenPanel<AwesomeUI.Feature.LanguageSelectPanel>();
            else
                OpenPanel<LoginPanel>();
        }
    }
}
