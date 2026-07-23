using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>Строка статистики: подпись слева + значение справа. Спавнится PlayerProfilePanel.</summary>
    public class StatRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _labelText;
        [SerializeField] private TextMeshProUGUI _valueText;

        public void SetData(string label, string value)
        {
            if (_labelText != null) _labelText.text = label;
            if (_valueText != null) _valueText.text = value;
        }
    }
}
