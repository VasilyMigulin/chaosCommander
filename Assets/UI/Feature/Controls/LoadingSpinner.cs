using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Бесконечный индикатор загрузки — крутит/заполняет каждый кадр на UNSCALED-времени (работает даже при
    /// Time.timeScale = 0, т.е. на паузе). Без DOTween/корутин. Вешается на объект спиннера внутри LoadingOverlay.
    ///
    /// Режимы:
    ///   Rotate       — вращение RectTransform (классический круговой спиннер: sprite-кольцо крутится).
    ///   FillLoop     — Image.fillAmount 0→1 по кругу (полоса «наполняется» и повторяет).
    ///   FillPingPong — Image.fillAmount 0→1→0 («дышит»).
    ///   Slider       — Slider.value 0→1→0 (то же для Slider-полосы).
    ///   Sweep        — бегущий сегмент: полоска ездит по дорожке слева-направо и повторяет (как
    ///                  «неопределённый прогресс» в Android/Material).
    ///
    /// ВАЖНО: для FillLoop/FillPingPong у Image должен быть **Image Type = Filled** (Radial360 для круга или
    /// Horizontal для полосы) — иначе fillAmount ни на что не влияет.
    /// </summary>
    public class LoadingSpinner : MonoBehaviour
    {
        public enum Mode { Rotate, FillLoop, FillPingPong, Slider, Sweep }

        [SerializeField] private Mode _mode = Mode.Rotate;
        [Tooltip("Скорость: оборотов/циклов/пробегов в секунду.")]
        [SerializeField] private float _speed = 1.2f;
        [Tooltip("Image для Rotate (крутим его RectTransform) или Fill* (меняем fillAmount). Пусто → берём с этого объекта.")]
        [SerializeField] private Image _image;
        [Tooltip("Slider для режима Slider.")]
        [SerializeField] private Slider _slider;
        [Tooltip("Sweep: RectTransform бегущей полоски (её родитель = дорожка с маской; полоска ездит внутри).")]
        [SerializeField] private RectTransform _sweepTarget;

        float _t;
        RectTransform _rt;

        void Awake()
        {
            if (_image == null) _image = GetComponent<Image>();
            _rt = _image != null ? _image.rectTransform : transform as RectTransform;
        }

        void OnEnable() => _t = 0f;   // при каждом показе стартуем «с нуля»

        void Update()
        {
            _t += Time.unscaledDeltaTime * _speed;
            switch (_mode)
            {
                case Mode.Rotate:
                    if (_rt != null) _rt.localRotation = Quaternion.Euler(0f, 0f, -_t * 360f);   // по часовой
                    break;
                case Mode.FillLoop:
                    if (_image != null) _image.fillAmount = Mathf.Repeat(_t, 1f);
                    break;
                case Mode.FillPingPong:
                    if (_image != null) _image.fillAmount = Mathf.PingPong(_t, 1f);
                    break;
                case Mode.Slider:
                    if (_slider != null) _slider.SetValueWithoutNotify(Mathf.PingPong(_t, 1f));
                    break;
                case Mode.Sweep:
                    Sweep();
                    break;
            }
        }

        // Полоска едет от «полностью за левым краем» до «полностью за правым» и повторяет. Родитель полоски —
        // дорожка с маской (RectMask2D), она обрезает полоску по своим границам. Якоря полоски — по центру.
        void Sweep()
        {
            if (_sweepTarget == null || _sweepTarget.parent is not RectTransform track) return;
            float span = track.rect.width + _sweepTarget.rect.width;   // путь: от края до края включая ширину полоски
            float phase = Mathf.Repeat(_t, 1f);                        // 0..1
            float x = -span * 0.5f + phase * span;
            var p = _sweepTarget.anchoredPosition;
            _sweepTarget.anchoredPosition = new Vector2(x, p.y);
        }
    }
}
