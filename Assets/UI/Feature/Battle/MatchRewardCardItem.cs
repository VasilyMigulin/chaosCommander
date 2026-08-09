using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Ячейка карты-награды в MatchRewardsWindow (карты PvE-энкаунтера за первое прохождение):
    /// иконка карты + «×N». Префаб: Image (арт) + дочерний TMP (кол-во; скрыт при N=1) + опц. имя.
    /// </summary>
    public class MatchRewardCardItem : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _countText;
        [SerializeField] private TextMeshProUGUI _nameText;   // опционально

        public void Set(Game.Core.Shared.CardVisualData visual, int count)
        {
            if (_icon != null)
            {
                _icon.sprite = visual.Icon;
                _icon.enabled = visual.Icon != null;
            }
            if (_countText != null)
            {
                _countText.text = $"×{count}";
                _countText.gameObject.SetActive(count > 1);
            }
            if (_nameText != null) _nameText.text = visual.CardName;
        }
    }
}
