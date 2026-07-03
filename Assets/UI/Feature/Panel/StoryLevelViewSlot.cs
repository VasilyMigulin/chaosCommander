using AwesomeUI.Core.Slot;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот уровня в StoryModePanel. Панель спавнит по слоту на каждый энкаунтер (Resources/Pve)
    /// и заполняет через SetData. Состояния:
    ///   • открыт  — кликабелен, по клику стартует бой;
    ///   • пройден — бейдж «✓» (_doneBadge), переигрывать можно;
    ///   • заблокирован — предыдущий уровень не пройден: _lockOverlay включён, кнопка не интерактивна.
    ///
    /// Префаб: Button на корне (IsButton=true в инспекторе слота), _titleText (TMP, «N. Название»),
    /// _doneBadge (галочка), _lockOverlay (замок/затемнение). Иконка уровня опциональна (_levelIcon).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class StoryLevelViewSlot : SourceSlot
    {
        [Header("Level")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private GameObject _doneBadge;      // «пройден» (✓)
        [SerializeField] private GameObject _lockOverlay;    // «заблокирован» (замок/затемнение)
        [SerializeField] private Image _levelIcon;           // опционально: арт уровня

        int _index;               // 0-based порядковый номер
        string _title = "";
        bool _done;
        bool _locked;
        System.Action _onStart;   // старт боя (панель передаёт замыкание с путём энкаунтера)

        public void SetData(int index, string title, bool done, bool locked, Sprite icon, System.Action onStart)
        {
            _index = index;
            _title = title;
            _done = done;
            _locked = locked;
            _onStart = onStart;

            if (_levelIcon != null)
            {
                _levelIcon.sprite = icon;
                _levelIcon.enabled = icon != null;
            }

            UpdateView();
        }

        public override void UpdateView()
        {
            if (_titleText != null)
                _titleText.text = $"{_index + 1}. {_title}";

            if (_doneBadge != null) _doneBadge.SetActive(_done && !_locked);
            if (_lockOverlay != null) _lockOverlay.SetActive(_locked);

            if (_btnClick != null) _btnClick.interactable = !_locked;
        }

        public override void OnClick()
        {
            if (_locked) return;   // страховка (кнопка и так не интерактивна)
            _onStart?.Invoke();
        }

        public override void OnUse() { }
        public override void Unject() { _onStart = null; }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
