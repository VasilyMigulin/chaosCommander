using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Ячейка достижения в ленте профиля: иконка + подпись + затемнение «не получено». Пока данные —
    /// заглушки (AchievementPlaceholders); реальная система достижений появится позже (в Game.Core.Progression),
    /// эта вьюшка не изменится — только источник данных.
    /// </summary>
    public class AchievementSlot : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _titleText;
        [Tooltip("Оверлей поверх иконки, показывается для НЕ полученного достижения (замок/затемнение).")]
        [SerializeField] private GameObject _lockedOverlay;

        public void SetData(string title, Sprite icon, bool earned)
        {
            if (_titleText != null) _titleText.text = title;
            if (_icon != null && icon != null) _icon.sprite = icon;
            if (_lockedOverlay != null) _lockedOverlay.SetActive(!earned);
        }
    }
}
