using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Блокирующее окно «Обновите приложение» (версия устарела). Показывается на входе, если
    /// VersionGateService.Check вернул Outdated. Полноэкранный оверлей БЕЗ закрытия — единственное
    /// действие «Обновить» ведёт в стор. Дальше в игру не пускаем.
    ///
    /// Префаб: _root (полноэкранный блокирующий оверлей), _messageText, _updateBtn.
    /// </summary>
    public class VersionGateView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _updateBtn;

        string _storeUrl;

        void Awake()
        {
            if (_updateBtn != null) _updateBtn.onClick.AddListener(OpenStore);
            if (_root != null) _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_updateBtn != null) _updateBtn.onClick.RemoveListener(OpenStore);
        }

        /// <summary>Показать блокирующий гейт. message опционален (иначе — текст из префаба).</summary>
        public void Show(string storeUrl, string message = null)
        {
            _storeUrl = storeUrl;
            if (_messageText != null && !string.IsNullOrEmpty(message)) _messageText.text = message;
            if (_root != null) _root.SetActive(true);
        }

        void OpenStore()
        {
            if (!string.IsNullOrEmpty(_storeUrl)) Application.OpenURL(_storeUrl);
        }
    }
}
