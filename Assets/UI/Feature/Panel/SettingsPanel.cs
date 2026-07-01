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
    /// Панель настроек. Пока единственная настройка — язык интерфейса (через LocaleService).
    /// Открывается из MainMenuPanel: _panelController.OpenPanel&lt;SettingsPanel&gt;().
    /// Новые настройки добавляются сюда новыми секциями ([Header]) + полями.
    /// </summary>
    public class SettingsPanel : SourcePanel
    {
        [Header("Language")]
        [SerializeField] private TMP_Dropdown _languageDropdown;

        [Header("Navigation")]
        [SerializeField] private Button _backBtn;

        readonly List<string> _codes = new List<string>();

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);
        }

        public override void OnInject()
        {
            base.OnInject();

            BuildLanguageDropdown();

            // Возврат в меню: OpenPanel сам закроет SettingsPanel и откроет MainMenuPanel
            // (просто ClosePanel оставил бы пустой экран — остальные панели уже закрыты).
            if (_backBtn != null)
                _backBtn.onClick.AddListener(() => _panelController.OpenPanel<MainMenuPanel>());
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
