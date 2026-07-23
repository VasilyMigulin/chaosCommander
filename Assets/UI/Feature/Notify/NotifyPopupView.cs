using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Презентер тостов. Размести на ВЕРХНЕМ оверлей-канвасе (большой sortingOrder), лучше — на
    /// персистентном UIHandler (DontDestroyOnLoad), чтобы тосты показывались поверх меню И боя.
    /// Регистрируется в NotifyService на OnEnable.
    ///
    /// Префаб: _container (Transform с VerticalLayoutGroup — стопка тостов), _toastPrefab (NotifyToastItem),
    /// цвета по типам.
    /// </summary>
    public class NotifyPopupView : MonoBehaviour
    {
        [SerializeField] private Transform _container;
        [SerializeField] private NotifyToastItem _toastPrefab;

        [Header("Colors")]
        [SerializeField] private Color _info    = new Color(0.20f, 0.22f, 0.28f, 0.96f);
        [SerializeField] private Color _success = new Color(0.18f, 0.55f, 0.35f, 0.96f);
        [SerializeField] private Color _reward  = new Color(0.72f, 0.53f, 0.17f, 0.96f);
        [SerializeField] private Color _warning = new Color(0.65f, 0.24f, 0.20f, 0.96f);

        void OnEnable()  => NotifyService.Register(this);
        void OnDisable() => NotifyService.Unregister(this);

        public void Enqueue(NotifyData data)
        {
            if (_container == null || _toastPrefab == null) return;
            var toast = Instantiate(_toastPrefab, _container);
            toast.gameObject.SetActive(true);
            toast.Play(data, ColorFor(data.Kind));
        }

        Color ColorFor(NotifyKind kind)
        {
            switch (kind)
            {
                case NotifyKind.Success: return _success;
                case NotifyKind.Reward:  return _reward;
                case NotifyKind.Warning: return _warning;
                default:                 return _info;
            }
        }
    }
}
