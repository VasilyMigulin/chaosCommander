using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Ячейка достижения в ленте профиля: иконка + подпись. Лента показывает ПОСЛЕДНИЕ ПОЛУЧЕННЫЕ достижения —
    /// значит все они уже разблокированы, поэтому состояния «заблокировано» тут нет. Пока данные — заглушки
    /// (AchievementPlaceholders); реальная система достижений появится позже (в Game.Core.Progression), эта
    /// вьюшка не изменится — только источник данных.
    /// </summary>
    public class AchievementSlot : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _titleText;

        public void SetData(string title, Sprite icon)
        {
            if (_titleText != null) _titleText.text = title;
            if (_icon != null && icon != null) _icon.sprite = icon;
        }
    }
}
