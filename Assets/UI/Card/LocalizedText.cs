using Game.Core.Shared;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Core.Card
{
    /// <summary>
    /// Локализует одну TMP-надпись в UI. Исходный текст, набитый в префабе, становится RU-фоллбэком;
    /// перевод берётся из таблицы CardText по ключу (напр. ui.menu.play). Обновляется при смене языка
    /// (LocaleService.Changed). Если ключ пуст или перевода нет — показывает исходный текст.
    ///
    /// Использование: повесь компонент на объект с TextMeshPro, оставь русский текст как есть,
    /// задай Key, и добавь перевод под этим ключом в таблицу (или в card_text.csv → импорт).
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [Tooltip("Ключ в таблице CardText, напр. ui.menu.play. Пусто → показывается исходный текст префаба.")]
        [SerializeField] string _key;

        TMP_Text _text;
        string _fallback;
        bool _captured;

        void Awake() => Capture();

        void Capture()
        {
            if (_captured) return;
            _text = GetComponent<TMP_Text>();
            _fallback = _text != null ? _text.text : string.Empty;
            _captured = true;
        }

        void OnEnable()
        {
            Capture();
            Apply();
            LocaleService.Changed += Apply;
        }

        void OnDisable() => LocaleService.Changed -= Apply;

        void Apply()
        {
            if (_text == null) return;
            _text.text = string.IsNullOrEmpty(_key) ? _fallback : CardTextLocalization.GetText(_key, _fallback);
        }

        /// <summary>Сменить ключ из кода (если текст не статичный).</summary>
        public void SetKey(string key) { _key = key; Apply(); }
    }
}
