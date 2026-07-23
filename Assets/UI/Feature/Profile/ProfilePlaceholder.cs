using Game.Core.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Компактная плашка профиля в шапке HUD: иконка + имя + валюты (Gold/Gems/Обрывки).
    /// Самостоятельный синглтон — сам обновляет валюты (подписан на PlayerWallet.OnChanged) и по желанию
    /// подтягивает имя с сервера (ProfileService). Обновить извне: ProfilePlaceholder.Instance?.Refresh().
    ///
    /// Это «плашка-данные». Клик/переход на полный профиль — отдельным AwesomeButton на этом же
    /// объекте (_targetPanelId = ProfilePanel) — он даёт навигацию, анимацию и «просмотрено».
    ///
    /// Префаб: _avatarImage, _nameText, _goldText, _gemsText, _scrapsText (все опц.). Уровень в шапке убран —
    /// его место занял счётчик Обрывков (валюта чёрного рынка).
    /// </summary>
    public class ProfilePlaceholder : MonoBehaviour
    {
        public static ProfilePlaceholder Instance { get; private set; }

        [SerializeField] private Image _avatarImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _goldText;
        [SerializeField] private TextMeshProUGUI _gemsText;
        [Tooltip("Счётчик Обрывков (SC) в шапке — на месте бывшего уровня.")]
        [SerializeField] private TextMeshProUGUI _scrapsText;

        [Tooltip("Сам запросить имя/уровень с сервера при появлении.")]
        [SerializeField] private bool _autoFetch = true;

        bool _fetched;   // профиль успешно получен (не дёргаем повторно)

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            PlayerWallet.OnChanged += OnWalletChanged;
            RefreshWallet();
            if (_autoFetch) Fetch();   // без логина — грейсфул no-op (FunctionService гейтит)

            // Частая ошибка сборки: плашка есть, а TMP-поля валюты не назначены → золото/гемы «не обновляются».
            // Код при этом молчит (null-guard), поэтому предупредим явно.
            if (_goldText == null && _gemsText == null)
                Debug.LogWarning($"[ProfilePlaceholder] '{name}': _goldText и _gemsText не назначены в префабе — " +
                                 "валюта в шапке HUD не отображается. Привяжи TMP-объекты золота/гемов.", this);
        }

        void OnDisable()
        {
            PlayerWallet.OnChanged -= OnWalletChanged;
        }

        // Кошелёк меняется в т.ч. когда после логина BackendSession грузит инвентарь → повод
        // дозапросить профиль (один раз), т.к. на init (до логина) запрос был отклонён.
        void OnWalletChanged()
        {
            RefreshWallet();
            if (_autoFetch && !_fetched) Fetch();
        }

        // ── Публичное API ──────────────────────────────────────────────────────

        /// <summary>Обновить всё: валюты (локально) + запрос профиля с сервера.</summary>
        public void Refresh()
        {
            RefreshWallet();
            Fetch();
        }

        /// <summary>Запросить имя/уровень с сервера и заполнить.</summary>
        public void Fetch()
        {
            ProfileService.GetProfile(d => { SetProfile(d); _fetched = true; }, _ => { });
        }

        /// <summary>Заполнить шапку данными профиля (можно звать вручную, если уже есть данные).</summary>
        public void SetProfile(PlayerProfileData data)
        {
            if (data == null) return;
            if (_nameText != null) _nameText.text = string.IsNullOrEmpty(data.Name) ? "Командир" : data.Name;
        }

        /// <summary>Иконка профиля (equipped-аватар резолвится в спрайт снаружи).</summary>
        public void SetAvatar(Sprite sprite)
        {
            if (_avatarImage != null && sprite != null) _avatarImage.sprite = sprite;
        }

        // ── Валюты ─────────────────────────────────────────────────────────────

        void RefreshWallet()
        {
            if (_goldText != null) _goldText.text = PlayerWallet.Gold.ToString();
            if (_gemsText != null) _gemsText.text = PlayerWallet.Gems.ToString();
            if (_scrapsText != null) _scrapsText.text = PlayerWallet.Get(BackendConfig.ScrapsCode).ToString();
        }
    }
}
