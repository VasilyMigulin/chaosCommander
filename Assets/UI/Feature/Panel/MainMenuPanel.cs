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
        [SerializeField] private Button _storyBtn;      // Стори (PvE): открывает выбор уровня
        [SerializeField] private Button _deckBtn;
        [SerializeField] private Button _settingsBtn;
        [SerializeField] private Button _exitBtn;

        [Header("Meta")]
        [SerializeField] private Button _shopBtn;
        [SerializeField] private Button _marketBtn;
        [SerializeField] private Button _auctionBtn;
        [SerializeField] private Button _boostersBtn;

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
            // Инициализация Panel 
        }

        public override void OnInject()
        {
            base.OnInject();

            _matchmakingBtn.onClick.AddListener(() =>
            {
                _panelController.OpenPanel<FindOpponentPanel>();   // показать поиск соперника
                _state.StartMatchMaking();
            });
            // Стори (PvE) — отдельной кнопкой рядом с матчмейкингом (плоское меню, без подменю «Играть»).
            if (_storyBtn != null)
                _storyBtn.onClick.AddListener(() => _panelController.OpenPanel<StoryModePanel>());
            _deckBtn.onClick.AddListener(() => _panelController.OpenPanel<DeckBuilder.DeckBuilderPanel>());
            if (_settingsBtn != null)
                _settingsBtn.onClick.AddListener(() => _panelController.OpenPanel<SettingsPanel>());
            _exitBtn.onClick.AddListener(() => Application.Quit());

            // Мета-разделы (заглушки ComingSoonPanel, наполнятся фазами экономики).
            // Кнопки могут быть не назначены в префабе — провязываем только существующие.
            if (_shopBtn != null)
                _shopBtn.onClick.AddListener(() => _panelController.OpenPanel<ShopPanel>());
            if (_marketBtn != null)
                _marketBtn.onClick.AddListener(() => _panelController.OpenPanel<BlackMarketPanel>());
            if (_auctionBtn != null)
                _auctionBtn.onClick.AddListener(() => _panelController.OpenPanel<AuctionPanel>());
            if (_boostersBtn != null)
                _boostersBtn.onClick.AddListener(() => _panelController.OpenPanel<BoosterPanel>());
            // Вызывается после инъекции зависимостей
        }

        void ClearListeners()
        {
            _matchmakingBtn.onClick.RemoveAllListeners();
            if (_storyBtn != null) _storyBtn.onClick.RemoveAllListeners();
            if (_settingsBtn != null) _settingsBtn.onClick.RemoveAllListeners();
            _exitBtn.onClick.RemoveAllListeners();
            if (_shopBtn != null) _shopBtn.onClick.RemoveAllListeners();
            if (_marketBtn != null) _marketBtn.onClick.RemoveAllListeners();
            if (_auctionBtn != null) _auctionBtn.onClick.RemoveAllListeners();
            if (_boostersBtn != null) _boostersBtn.onClick.RemoveAllListeners();
        }

        public override void Unject()
        {
            ClearListeners();
            // Очистка инъекций
        }

        public override void OnDipose()
        {
            ClearListeners();
            base.OnDipose();
        }
    }
}
