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

            OpenPanel<LoginPanel>();
        }
    }
}
