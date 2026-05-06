using AwesomeUI.Core.Canvas;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Canvas боевого экрана.
    /// Открывает BattlePanel при активации.
    /// </summary>
    public class BattleCanvas : SourceCanvas
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
            OpenPanel<BattlePanel>();
        }
    }
}
