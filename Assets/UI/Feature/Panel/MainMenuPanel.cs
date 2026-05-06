using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Attributes; 
using Game.Core.Events;
using Game.Core.Shared.Interface;
using UnityEngine;
using UnityEngine.UI;
using AwesomeUI.Interface;

namespace AwesomeUI.Feature
{
    public class MainMenuPanel : SourcePanel
    {
        [UIInject] private IMenuStateContext _state;
        [SerializeField] private Button _matchmakingBtn;
        [SerializeField] private Button _deckBtn;
        [SerializeField] private Button _exitBtn;

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
            // Инициализация Panel 
        }

        public override void OnInject()
        {
            base.OnInject();

            _matchmakingBtn.onClick.AddListener(() => _state.StartMatchMaking());
            _deckBtn.onClick.AddListener(() => _panelController.OpenPanel<DeckBuilder.DeckBuilderPanel>());
            _exitBtn.onClick.AddListener(() => Application.Quit()); 
            // Вызывается после инъекции зависимостей
        } 
        public override void Unject()
        {
            _matchmakingBtn.onClick.RemoveAllListeners();
            _exitBtn.onClick.RemoveAllListeners();
            // Очистка инъекций
        }
         
        public override void OnDipose()
        {
            _matchmakingBtn.onClick.RemoveAllListeners();
            _exitBtn.onClick.RemoveAllListeners();
            base.OnDipose();
        }
    }
}
