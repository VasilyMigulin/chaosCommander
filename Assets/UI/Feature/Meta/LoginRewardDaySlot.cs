using Game.Core.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Ячейка одного дня входных наград (7-дневный цикл). КОМПАКТНО: миниатюра + число, БЕЗ текста-сводки
    /// (ячейка маленькая). Иконка одна на всё — валюта (InterfaceConfig) ИЛИ предмет (InstanceData.Miniature),
    /// резолвит MetaIcon; отдельного booster-icon нет. День/бейджи — опционально.
    ///
    /// Префаб: _rewardIcon (Image, миниатюра), _amountText (число); опц. _claimedBadge (✓),
    /// _todayHighlight (рамка «сегодня»), _dayText («День N» — для минимальной ячейки оставь None).
    /// </summary>
    public class LoginRewardDaySlot : MonoBehaviour
    {
        [SerializeField] private Image _rewardIcon;
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private GameObject _claimedBadge;
        [SerializeField] private GameObject _todayHighlight;
        [Tooltip("Опц.: подпись «День N». Для минимальной ячейки оставь None.")]
        [SerializeField] private TextMeshProUGUI _dayText;

        public void SetData(DailyService.LoginDay day)
        {
            if (day == null) return;

            var icon = MetaIcon.Reward(day.RewardCode, day.RewardItemId);   // валюта или миниатюра предмета
            if (_rewardIcon != null) { _rewardIcon.sprite = icon; _rewardIcon.enabled = icon != null; }
            if (_amountText != null) _amountText.text = day.RewardAmount > 0 ? day.RewardAmount.ToString() : "";
            if (_claimedBadge != null) _claimedBadge.SetActive(day.Claimed);
            if (_todayHighlight != null) _todayHighlight.SetActive(day.Today);
            if (_dayText != null) _dayText.text = UIStrings.LoginDay(day.Day);
        }
    }
}
