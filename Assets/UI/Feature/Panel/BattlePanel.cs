using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.Shared.Interface;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Главная панель боя.
    /// Содержит CardLayout с рукой игрока.
    /// Общение с игровой логикой только через GameEventBus и IBattleUIContext.
    /// </summary>
    public class BattlePanel : SourcePanel
    {
        [UIInject] private IBattleUIContext _battleContext;

        private CardLayout _cardLayout; 

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);

            _cardLayout = GetComponentInChildren<CardLayout>(true);
        }

        public override void OnInject()
        {
            base.OnInject();
        }

        public override void Unject()
        {
            _cardLayout?.Dispose();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
