using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Один блок-подсказка механики рядом с картой в CardInspectPopup (как в ХС: «Провокация — существа
    /// обязаны атаковать это существо первым»). Чистый дисплей: заголовок + описание, включается/выключается
    /// попапом. Какие подсказки показывать для карты — решает KeywordHintsResolver.
    /// </summary>
    public class KeywordHintBlockView : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI _titleText;
        [SerializeField] TextMeshProUGUI _descriptionText;

        public void Setup(string title, string description)
        {
            if (_titleText != null) _titleText.text = title;
            if (_descriptionText != null) _descriptionText.text = description;
            gameObject.SetActive(true);
        }
    }
}
