using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// ДОМАШНИЙ ЭКРАН меню = выбор режима (PvP/PvE). MainMenuPanel удалён как лишний: профиль,
    /// настройки и выход живут в HUD (топ-бар), разделы — в нижней навигации HUD.
    /// Открывается вкладкой «Играть» (AwesomeButton _targetPanelId=GamePanel) и как стартовая
    /// панель MainMenuCanvas.InvokeCanvas.
    ///
    /// Кнопки — AwesomeButton'ы:
    ///   • PvE «Кампания» → StoryModePanel — ДЕКЛАРАТИВНО (_targetPanel в префабе), кода нет;
    ///   • «Назад» — AwesomeButton с _back=true — ДЕКЛАРАТИВНО (журнал истории), кода нет;
    ///   • PvP «Рейтинговый бой» — action: открытие панели декларативно (_targetPanel=FindOpponentPanel),
    ///     а запуск матчмейкинга цепляем в код через onClick;
    ///   • «Тренировка» — action: бой 1-на-1 с ИИ на ФИКСИРОВАННОМ энкаунтере (одна колода NPC), как дев-PvE.
    ///     Игрок играет своей активной колодой (энкаунтер НЕ навязывает PlayerDeck).
    ///
    /// Ранг/MMR — заглушка до рейтинговой системы.
    /// </summary>
    public class GamePanel : SourcePanel
    {
        [UIInject] private IMenuStateContext _state;

        [Header("PvP (нужен код — запуск матчмейкинга)")]
        [SerializeField] private AwesomeButton _pvpBtn;   // в префабе _targetPanel = FindOpponentPanel

        [Header("Тренировка (PvE 1-на-1, фикс. колода NPC)")]
        [SerializeField] private AwesomeButton _trainingBtn;
        [Tooltip("Прямая ссылка на ассет энкаунтера тренировки (PveEncounterConfig). Фикс. соперник, без навязанной колоды игрока.")]
        [SerializeField] private Game.Core.Configs.PveEncounterConfig _trainingEncounter;

        [Header("Campaign (PvE)")]
        [Tooltip("Подпись на карточке кампании: «Распрекрасная принцесса, глава 3 из 6». Если назначен " +
                 "и _chapterText — сюда идёт только название, а глава отдельной строкой.")]
        [SerializeField] private TMPro.TextMeshProUGUI _campaignText;
        [Tooltip("Необязательно. Отдельная строка «глава 3 из 6», если хочется разнести на два текста.")]
        [SerializeField] private TMPro.TextMeshProUGUI _chapterText;

        [Header("Rating")]
        [Tooltip("Лига словами («Золото») — PlayerRating.RankName.")]
        [SerializeField] private TMPro.TextMeshProUGUI _rankText;
        [Tooltip("Число, по которому реально ищется соперник — PlayerRating.Mmr.")]
        [SerializeField] private TMPro.TextMeshProUGUI _mmrText;

        public override void OnInject()
        {
            base.OnInject();
            if (_pvpBtn != null) _pvpBtn.onClick.AddListener(StartMatchmaking);
            if (_trainingBtn != null) _trainingBtn.onClick.AddListener(StartTraining);
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            // Перечитываем при КАЖДОМ открытии: и MMR, и глава меняются после боя.
            RefreshRating();
            RefreshCampaign();
        }

        void StartMatchmaking() => _state?.StartMatchMaking();

        // Тренировка: PvE-бой на фиксированном энкаунтере (одна колода NPC). Передаём ассет ПРЯМОЙ ссылкой
        // (как StoryModePanel) — PveEncounterLocator берёт EncounterAsset приоритетнее пути. StartPveBattle
        // сам гасит активный матчмейкинг перед LoadScene и включает PveMode. Игрок играет своей активной
        // колодой (энкаунтер тренировки НЕ должен навязывать PlayerDeck).
        void StartTraining()
        {
            if (_trainingEncounter == null)
            {
                Debug.LogWarning("[GamePanel] Тренировка: не назначен _trainingEncounter (PveEncounterConfig).", this);
                return;
            }
            Game.Core.Service.PveMode.EncounterAsset = _trainingEncounter;
            Game.Core.Service.PveMode.EncounterId = _trainingEncounter.name;
            _state?.StartPveBattle(null);
        }

        /// <summary>
        /// «Распрекрасная принцесса, глава 3 из 6». Кампания и прогресс — из CampaignProgress
        /// (тот же источник, что у StoryModePanel, поэтому цифры не разъедутся).
        /// </summary>
        void RefreshCampaign()
        {
            if (_campaignText == null && _chapterText == null) return;

            var state = CampaignProgress.Load();
            if (!state.HasLevels)
            {
                if (_campaignText != null) _campaignText.text = UIStrings.Campaign;
                if (_chapterText != null) _chapterText.text = string.Empty;
                return;
            }

            string chapter = state.Completed
                ? UIStrings.CampaignDone
                : UIStrings.ChapterOf(state.CurrentChapter, state.Total);
            string name = state.Name;   // пусто, если кампании нет (фолбэк-скан энкаунтеров)

            if (_chapterText != null)
            {
                _chapterText.text = chapter;
                if (_campaignText != null)
                    _campaignText.text = string.IsNullOrEmpty(name) ? UIStrings.Campaign : name;
                return;
            }

            // Одна строка на всё (как в макете карточки).
            _campaignText.text = string.IsNullOrEmpty(name) ? chapter : $"{name}, {chapter}";
        }

        // MVP: MMR локальный (PlayerRating, PlayerPrefs) — им же MatchmakingService выбирает комнату.
        // Когда рейтинг поедет с сервера (PlayFab statistics), поменяется только источник в PlayerRating.
        void RefreshRating()
        {
            if (_rankText != null) _rankText.text = Game.Core.Service.PlayerRating.RankName;
            if (_mmrText != null)  _mmrText.text  = $"MMR {Game.Core.Service.PlayerRating.Mmr}";
        }

        public override void Unject()
        {
            if (_pvpBtn != null) _pvpBtn.onClick.RemoveListener(StartMatchmaking);
            if (_trainingBtn != null) _trainingBtn.onClick.RemoveListener(StartTraining);
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
