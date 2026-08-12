using System.Collections;
using System.Collections.Generic;
using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.Backend;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Панель настроек: язык интерфейса (LocaleService), промокод и гостевой ID для входа на сайте.
    /// Открывается из MainMenuPanel: _panelController.OpenPanel&lt;SettingsPanel&gt;().
    /// Новые настройки добавляются сюда новыми секциями ([Header]) + полями.
    ///
    /// ПРЕФАБ (секция «ID для входа на сайте», все поля опциональны):
    ///   SiteIdRoot
    ///     ├─ HintText   (TMP_Text, LocalizedText: ui.settings.site_id.hint)
    ///     ├─ CopyIdBtn  (Button)   ← _copyDeviceIdBtn (лейбл: ui.settings.site_id.button)
    ///     ├─ IdText     (TMP_Text) ← _deviceIdText     (пустой; ID появляется по клику)
    ///     └─ CopiedText (TMP_Text) ← _copyFeedbackText (пустой; «скопировано» на пару секунд)
    ///
    /// ПРЕФАБ (секция «Промокод», все поля опциональны — без них секция просто не работает):
    ///   PromoRoot
    ///     ├─ HintText     (TMP_Text, LocalizedText: ui.promo.hint)
    ///     ├─ PromoInput   (TMP_InputField) ← _promoInput  (placeholder: ui.promo.placeholder)
    ///     ├─ ApplyBtn     (Button)         ← _promoApplyBtn (лейбл: ui.promo.apply)
    ///     └─ ResultText   (TMP_Text)       ← _promoFeedbackText (пустой; итог активации)
    ///   _promoRewardPopup — RewardPopupView со сцены (опц.; без него выданное покажется тостом).
    /// </summary>
    public class SettingsPanel : SourcePanel
    {
        [Header("Language")]
        [SerializeField] private TMP_Dropdown _languageDropdown;

        [Header("Promo code")]
        [Tooltip("Поле ввода промокода (крауд-плюшки, акции). Валидацию и выдачу делает сервер.")]
        [SerializeField] private TMP_InputField _promoInput;
        [SerializeField] private Button _promoApplyBtn;
        [Tooltip("Опц.: строка результата — «Промокод принят!» либо причина отказа.")]
        [SerializeField] private TMP_Text _promoFeedbackText;
        [Tooltip("Опц.: попап «вы получили». Не задан → выданное покажем тостом (NotifyService).")]
        [SerializeField] private RewardPopupView _promoRewardPopup;

        [Header("Site login ID")]
        [Tooltip("Кнопка «Скопировать ID» — кладёт гостевой CustomId устройства в буфер обмена.")]
        [SerializeField] private Button _copyDeviceIdBtn;
        [Tooltip("Опц.: сюда по клику выводится сам ID (чтобы можно было переписать вручную).")]
        [SerializeField] private TMP_Text _deviceIdText;
        [Tooltip("Опц.: строка «ID скопирован», гаснет сама.")]
        [SerializeField] private TMP_Text _copyFeedbackText;

        [Header("Navigation")]
        [SerializeField] private Button _backBtn;

        const float CopyFeedbackSeconds = 2.5f;
        Coroutine _copyFeedbackRoutine;

        bool _promoBusy;

        readonly List<string> _codes = new List<string>();

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
        }

        public override void OnInject()
        {
            base.OnInject();

            BuildLanguageDropdown();

            if (_copyDeviceIdBtn != null)
                _copyDeviceIdBtn.onClick.AddListener(OnCopyDeviceId);
            if (_deviceIdText != null) _deviceIdText.text = "";
            if (_copyFeedbackText != null) _copyFeedbackText.text = "";

            if (_promoInput != null)
            {
                _promoInput.onValueChanged.AddListener(OnPromoChanged);
                _promoInput.onSubmit.AddListener(OnPromoSubmit);   // Enter = «Активировать»
                _promoInput.characterLimit = PromoService.MaxCodeLength;
                _promoInput.text = "";
            }
            if (_promoApplyBtn != null) _promoApplyBtn.onClick.AddListener(OnPromoApply);
            if (_promoFeedbackText != null) _promoFeedbackText.text = "";
            RefreshPromoButton();

            // Возврат в меню: OpenPanel сам закроет SettingsPanel и откроет MainMenuPanel
            // (просто ClosePanel оставил бы пустой экран — остальные панели уже закрыты).
            if (_backBtn != null)
                _backBtn.onClick.AddListener(() => _panelController.Back());
        }

        // ── Гостевой ID для входа на сайте ───────────────────────────────────────
        // Гостевой аккаунт = CustomId устройства (см. LoginPanel). Тем же ID можно войти на
        // сайте игры (вкладка «ID устройства») и посмотреть коллекцию. ID показываем и копируем
        // только по явному клику: это фактически пароль от гостевого аккаунта.
        void OnCopyDeviceId()
        {
            string id = SystemInfo.deviceUniqueIdentifier;
            GUIUtility.systemCopyBuffer = id;
            if (_deviceIdText != null) _deviceIdText.text = id;

            if (_copyFeedbackText == null) return;
            _copyFeedbackText.text = CardTextLocalization.GetText(
                "ui.settings.site_id.copied", "ID скопирован в буфер обмена");
            if (_copyFeedbackRoutine != null) StopCoroutine(_copyFeedbackRoutine);
            _copyFeedbackRoutine = StartCoroutine(ClearCopyFeedback());
        }

        IEnumerator ClearCopyFeedback()
        {
            yield return new WaitForSeconds(CopyFeedbackSeconds);
            if (_copyFeedbackText != null) _copyFeedbackText.text = "";
            _copyFeedbackRoutine = null;
        }

        // ── Промокод ─────────────────────────────────────────────────────────────
        // Клиент НИЧЕГО не решает: шлёт строку, сервер (RedeemPromo) сам ищет её среди своих кампаний
        // (Title Data promoConfig) и среди нативных купонов PlayFab, проверяет лимиты и выдаёт. Здесь —
        // только гейт кнопки, защита от двойного тапа и показ результата.

        void OnPromoChanged(string _) => RefreshPromoButton();

        void OnPromoSubmit(string _) => OnPromoApply();

        void RefreshPromoButton()
        {
            if (_promoApplyBtn == null) return;
            _promoApplyBtn.interactable = !_promoBusy && _promoInput != null && PromoService.LooksValid(_promoInput.text);
        }

        void OnPromoApply()
        {
            if (_promoBusy || _promoInput == null) return;
            string code = _promoInput.text;
            if (!PromoService.LooksValid(code)) return;

            _promoBusy = true;
            RefreshPromoButton();
            ShowPromoFeedback("");

            PromoService.Redeem(code,
                onSuccess: resp =>
                {
                    _promoBusy = false;
                    if (resp != null && resp.Success)
                    {
                        _promoInput.text = "";                       // код сгорел — не даём тапнуть второй раз
                        ShowPromoFeedback(UIStrings.PromoAccepted);
                        ShowPromoReward(resp);
                    }
                    else ShowPromoFeedback(UIStrings.BackendReason(resp != null ? resp.Reason : "promo_unknown"));
                    RefreshPromoButton();
                },
                onError: err =>
                {
                    _promoBusy = false;
                    ShowPromoFeedback(UIStrings.BackendReason(err));
                    RefreshPromoButton();
                });
        }

        // Попап «вы получили» — если он подключён в префабе; иначе сводка тостом (тост виден
        // поверх любой панели, так что плюшка не потеряется даже без попапа).
        void ShowPromoReward(PromoService.RedeemResponse resp)
        {
            var reward = resp.Reward;
            if (reward == null || reward.IsEmpty) return;

            if (_promoRewardPopup != null)
            {
                _promoRewardPopup.Show(reward, UIStrings.PromoRewardTitle(resp.TitleKey));
                return;
            }

            string summary = UIStrings.RewardSummary(reward);
            if (!string.IsNullOrEmpty(summary)) NotifyService.Reward(summary);
        }

        void ShowPromoFeedback(string text)
        {
            if (_promoFeedbackText != null) _promoFeedbackText.text = text;
        }

        void BuildLanguageDropdown()
        {
            if (_languageDropdown == null) return;

            _codes.Clear();
            var options = new List<TMP_Dropdown.OptionData>();
            int currentIndex = 0, i = 0;

            foreach (var opt in LocaleService.Available)
            {
                _codes.Add(opt.Code);
                options.Add(new TMP_Dropdown.OptionData(opt.DisplayName));
                if (opt.Code == LocaleService.Current) currentIndex = i;
                i++;
            }

            _languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            _languageDropdown.ClearOptions();
            _languageDropdown.AddOptions(options);
            _languageDropdown.SetValueWithoutNotify(currentIndex);
            _languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        }

        void OnLanguageChanged(int index)
        {
            if (index < 0 || index >= _codes.Count) return;
            LocaleService.SetLanguage(_codes[index]);
        }

        public override void Unject()
        {
            if (_languageDropdown != null)
                _languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            if (_copyDeviceIdBtn != null)
                _copyDeviceIdBtn.onClick.RemoveListener(OnCopyDeviceId);
            if (_promoInput != null)
            {
                _promoInput.onValueChanged.RemoveListener(OnPromoChanged);
                _promoInput.onSubmit.RemoveListener(OnPromoSubmit);
            }
            if (_promoApplyBtn != null)
                _promoApplyBtn.onClick.RemoveListener(OnPromoApply);
            if (_backBtn != null)
                _backBtn.onClick.RemoveAllListeners();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
