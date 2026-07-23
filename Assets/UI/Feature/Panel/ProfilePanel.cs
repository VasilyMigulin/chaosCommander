using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;   // ProfileService, PlayerProfileData, PlayerWallet, BackendConfig
using Game.Core.Service;   // PlayerRating
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Профиль игрока. Шапка: иконка звания (за универсальность — пока заглушка) + аватар + ник (DisplayName
    /// с сервера) + MMR и звание (PlayerRating — «упоротые» титулы). Ниже две ленты (HorizontalLayoutGroup):
    /// ресурсы + текущий опыт, и последние 5 достижений (пока заглушки AchievementPlaceholders).
    /// Опционально — вертикальный список доп.статистики (StatRowView), если привязан в префабе.
    ///
    /// Данные результатов/имени — с сервера (ProfileService.GetProfile). MMR/звание/валюты — локальные
    /// (PlayerRating / PlayerWallet), чтобы совпадать с шапкой HUD и GamePanel и показываться сразу.
    /// Открывается по id "ProfilePanel" (клик по плашке ProfilePlaceholder). «Назад» — AwesomeButton _back.
    ///
    /// Префаб:
    ///   Шапка:      _rankIcon (Image), _avatarImage (Image), _nameText, _mmrText, _rankText.
    ///   Ресурсы:    _goldText, _gemsText, _scrapsText, _levelText, _xpFill  (в одном HorizontalLayoutGroup).
    ///   Достижения: _achievementsRoot (контейнер) + _achievementSlotPrefab (AchievementSlot), _achievementCount.
    ///   Доп.стата:  _statsRoot + _statRowPrefab (опц.).
    ///   State:      _loadingOverlay, _feedbackText.
    /// </summary>
    public class ProfilePanel : SourcePanel
    {
        [Header("Шапка — звание / имя / MMR")]
        [Tooltip("Иконка звания «за универсальность» — косметика/понты. Пока заглушка: спрайт из префаба.")]
        [SerializeField] private Image _rankIcon;
        [SerializeField] private Image _avatarImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _mmrText;
        [SerializeField] private TextMeshProUGUI _rankText;

        [Header("Ресурсы + опыт (HorizontalLayoutGroup)")]
        [SerializeField] private TextMeshProUGUI _goldText;
        [SerializeField] private TextMeshProUGUI _gemsText;
        [SerializeField] private TextMeshProUGUI _scrapsText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private Image _xpFill;

        [Header("Последние достижения (HorizontalLayoutGroup, заглушки)")]
        [SerializeField] private Transform _achievementsRoot;
        [SerializeField] private AchievementSlot _achievementSlotPrefab;
        [SerializeField] private int _achievementCount = 5;

        [Header("Доп. статистика (опц., вертикальный список)")]
        [SerializeField] private Transform _statsRoot;
        [SerializeField] private StatRowView _statRowPrefab;

        [Header("State")]
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private TextMeshProUGUI _feedbackText;

        readonly List<StatRowView> _rows = new List<StatRowView>();
        readonly List<AchievementSlot> _achievements = new List<AchievementSlot>();
        bool _subscribed;

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);

            // Локальные части рисуем СРАЗУ (не ждут сервер): MMR/звание/валюты/опыт/достижения-заглушки.
            RefreshLocal();
            BuildAchievements();
            if (!_subscribed) { PlayerWallet.OnChanged += RefreshLocal; _subscribed = true; }

            // Серверные результаты (имя, победы/поражения…) — асинхронно.
            FetchProfile();
        }

        // ── Локальные данные (MMR/звание/валюты/опыт) ─────────────────────────────

        void RefreshLocal()
        {
            if (_mmrText != null)  _mmrText.text  = $"MMR {PlayerRating.Mmr}";
            if (_rankText != null) _rankText.text = PlayerRating.RankName;

            if (_goldText != null)   _goldText.text   = PlayerWallet.Gold.ToString();
            if (_gemsText != null)   _gemsText.text   = PlayerWallet.Gems.ToString();
            if (_scrapsText != null) _scrapsText.text = PlayerWallet.Get(BackendConfig.ScrapsCode).ToString();
        }

        // ── Достижения (пока заглушки) ────────────────────────────────────────────

        void BuildAchievements()
        {
            ClearAchievements();
            if (_achievementsRoot == null || _achievementSlotPrefab == null) return;
            foreach (var a in AchievementPlaceholders.Recent(_achievementCount))
            {
                var slot = Instantiate(_achievementSlotPrefab, _achievementsRoot);
                slot.gameObject.SetActive(true);
                slot.SetData(a.Title, null, a.Earned);
                _achievements.Add(slot);
            }
        }

        // ── Серверный профиль (имя + результаты) ──────────────────────────────────

        void FetchProfile()
        {
            SetLoading(true);
            ProfileService.GetProfile(
                data =>
                {
                    SetLoading(false);
                    Populate(data);
                },
                err =>
                {
                    SetLoading(false);
                    ShowFeedback(UIStrings.BackendReason(err));
                });
        }

        void Populate(PlayerProfileData d)
        {
            if (d == null) return;

            if (_nameText != null)  _nameText.text  = string.IsNullOrEmpty(d.Name) ? "Командир" : d.Name;
            if (_levelText != null) _levelText.text = $"{UIStrings.LevelShort} {d.Level}";
            if (_xpFill != null)    _xpFill.fillAmount = Mathf.Clamp01(d.Xp01);

            // Доп. статистика — только если в префабе есть контейнер+префаб строки.
            if (_statsRoot == null || _statRowPrefab == null) return;
            ClearRows();
            AddRow(UIStrings.ProfileWins,         d.Wins.ToString());
            AddRow(UIStrings.ProfileLosses,       d.Losses.ToString());
            AddRow(UIStrings.ProfileWinRate,      $"{d.WinRatePercent}%");
            AddRow(UIStrings.ProfileGames,        d.GamesPlayed.ToString());
            AddRow(UIStrings.ProfileAchievements, $"{d.AchievementsEarned} / {d.AchievementsTotal}");
            AddRow(UIStrings.ProfileBoosters,     d.BoostersOpened.ToString());
            AddRow(UIStrings.ProfileCards,        d.CardsCollected.ToString());
        }

        void AddRow(string label, string value)
        {
            var row = Instantiate(_statRowPrefab, _statsRoot);
            row.gameObject.SetActive(true);
            row.SetData(label, value);
            _rows.Add(row);
        }

        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearRows()
        {
            foreach (var r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
        }

        void ClearAchievements()
        {
            foreach (var a in _achievements) if (a != null) Destroy(a.gameObject);
            _achievements.Clear();
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            PlayerWallet.OnChanged -= RefreshLocal;
            _subscribed = false;
        }

        public override void Unject()
        {
            Unsubscribe();
            ClearRows();
            ClearAchievements();
        }

        public override void OnDipose()
        {
            Unsubscribe();
            ClearRows();
            ClearAchievements();
            base.OnDipose();
        }
    }
}
