using System.Collections;
using System.Collections.Generic;
using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Панель настроек: язык интерфейса (LocaleService) + гостевой ID для входа на сайте.
    /// Открывается из MainMenuPanel: _panelController.OpenPanel&lt;SettingsPanel&gt;().
    /// Новые настройки добавляются сюда новыми секциями ([Header]) + полями.
    ///
    /// ПРЕФАБ (секция «ID для входа на сайте», все поля опциональны):
    ///   SiteIdRoot
    ///     ├─ HintText   (TMP_Text, LocalizedText: ui.settings.site_id.hint)
    ///     ├─ CopyIdBtn  (Button)   ← _copyDeviceIdBtn (лейбл: ui.settings.site_id.button)
    ///     ├─ IdText     (TMP_Text) ← _deviceIdText     (пустой; ID появляется по клику)
    ///     └─ CopiedText (TMP_Text) ← _copyFeedbackText (пустой; «скопировано» на пару секунд)
    /// </summary>
    public class SettingsPanel : SourcePanel
    {
        [Header("Language")]
        [SerializeField] private TMP_Dropdown _languageDropdown;

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
