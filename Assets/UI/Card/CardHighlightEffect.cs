using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Core.Card
{
    /// <summary>
    /// Внешняя подсветка карты через шейдер CardHighlight.
    ///
    /// Структура в иерархии:
    ///   [Card Root]
    ///     ├─ [HighlightImage]   ← этот компонент (Image, RectTransform расширен наружу)
    ///     └─ [CardBackground]   ← обычный фон карты
    ///
    /// RectTransform HighlightImage должен быть больше карты на величину обводки
    /// (например padding = 16px со всех сторон через Padding в инспекторе).
    /// Шейдер сам вычисляет долю "внутренней карты" и делает её прозрачной.
    ///
    /// Типы подсветки:
    ///   Selection    — карта удерживается игроком     (синий)
    ///   Affordable   — на карту хватает ресурсов      (зелёный)
    ///   AbilityReady — у карты активировался эффект   (оранжевый)
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class CardHighlightEffect : MonoBehaviour
    {
        public enum HighlightType { None, Selection, Affordable, AbilityReady }

        [Header("Highlight Colors")]
        [SerializeField] Color _selectionColor    = new Color(0.20f, 0.55f, 1.00f, 1f);
        [SerializeField] Color _affordableColor   = new Color(0.20f, 0.90f, 0.30f, 1f);
        [SerializeField] Color _abilityReadyColor = new Color(1.00f, 0.55f, 0.10f, 1f);

        [Header("Border Size (pixels expanded outside card)")]
        [SerializeField] float _paddingX = 16f;  // пикселей наружу по горизонтали
        [SerializeField] float _paddingY = 16f;  // пикселей наружу по вертикали

        [Header("Highlight Settings")]
        [SerializeField] [Range(0f, 0.15f)] float _rimSoftness      = 0.03f;
        [SerializeField] [Range(0f, 4f)]    float _glowIntensity    = 1.6f;
        [SerializeField] [Range(0f, 8f)]    float _pulseSpeed       = 2.0f;
        [SerializeField] [Range(0f, 1f)]    float _pulseDepth       = 0.45f;
        [SerializeField] [Range(0f, 4f)]    float _shimmerSpeed     = 0.8f;
        [SerializeField] [Range(0f, 1f)]    float _shimmerWidth     = 0.18f;
        [SerializeField] [Range(0f, 2f)]    float _shimmerIntensity = 0.75f;

        static readonly int ID_RimColor         = Shader.PropertyToID("_RimColor");
        static readonly int ID_BorderFractionX  = Shader.PropertyToID("_BorderFractionX");
        static readonly int ID_BorderFractionY  = Shader.PropertyToID("_BorderFractionY");
        static readonly int ID_RimSoftness      = Shader.PropertyToID("_RimSoftness");
        static readonly int ID_GlowIntensity    = Shader.PropertyToID("_GlowIntensity");
        static readonly int ID_PulseSpeed       = Shader.PropertyToID("_PulseSpeed");
        static readonly int ID_PulseDepth       = Shader.PropertyToID("_PulseDepth");
        static readonly int ID_ShimmerSpeed     = Shader.PropertyToID("_ShimmerSpeed");
        static readonly int ID_ShimmerWidth     = Shader.PropertyToID("_ShimmerWidth");
        static readonly int ID_ShimmerIntensity = Shader.PropertyToID("_ShimmerIntensity");

        Image         _image;
        Material      _material;
        RectTransform _rect;

        bool _isSelected;
        bool _isAffordable;
        bool _isAbilityReady;

        void Awake()
        {
            _image          = GetComponent<Image>();
            _rect           = GetComponent<RectTransform>();
            _material       = new Material(_image.material);
            _image.material = _material;
            _image.raycastTarget = false;

            if (!_material.HasProperty(ID_RimColor))
            {
                Debug.LogWarning($"[CardHighlightEffect] Material '{_material.name}' with Shader '{_material.shader.name}' on '{name}' is missing '_RimColor'. Expected shader 'Custom/Selection/Highlight'.", this);
                return;
            }

            ApplyBorderFractions();
            ApplySettings();
            SetRimAlpha(0f);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetState(HighlightType type, bool active)
        {
            switch (type)
            {
                case HighlightType.Selection:    _isSelected     = active; break;
                case HighlightType.Affordable:   _isAffordable   = active; break;
                case HighlightType.AbilityReady: _isAbilityReady = active; break;
            }
            Refresh();
        }

        public void ResetAll()
        {
            _isSelected     = false;
            _isAffordable   = false;
            _isAbilityReady = false;
            Refresh();
        }

        // ── Private ───────────────────────────────────────────────────────────

        // Приоритет: Selection > AbilityReady > Affordable
        void Refresh()
        {
            if (_material == null) return;

            if (!_material.HasProperty(ID_RimColor)) return;

            if (_isSelected)
            {
                _material.SetColor(ID_RimColor, _selectionColor);
                ApplySettings();
            }
            else if (_isAbilityReady)
            {
                _material.SetColor(ID_RimColor, _abilityReadyColor);
                ApplySettings();
            }
            else if (_isAffordable)
            {
                _material.SetColor(ID_RimColor, _affordableColor);
                ApplySettings();
            }
            else
            {
                SetRimAlpha(0f);
            }
        }

        // Вычисляет долю padding / totalSize, чтобы шейдер знал,
        // где заканчивается внешняя зона и начинается карта.
        void ApplyBorderFractions()
        {
            if (_material == null || _rect == null) return;
            Vector2 size = _rect.rect.size;
            if (size.x > 0f) _material.SetFloat(ID_BorderFractionX, _paddingX / size.x);
            if (size.y > 0f) _material.SetFloat(ID_BorderFractionY, _paddingY / size.y);
        }

        void ApplySettings()
        {
            if (_material == null) return;
            ApplyBorderFractions();
            _material.SetFloat(ID_RimSoftness,      _rimSoftness);
            _material.SetFloat(ID_GlowIntensity,    _glowIntensity);
            _material.SetFloat(ID_PulseSpeed,       _pulseSpeed);
            _material.SetFloat(ID_PulseDepth,       _pulseDepth);
            _material.SetFloat(ID_ShimmerSpeed,     _shimmerSpeed);
            _material.SetFloat(ID_ShimmerWidth,     _shimmerWidth);
            _material.SetFloat(ID_ShimmerIntensity, _shimmerIntensity);
        }

        void SetRimAlpha(float alpha)
        {
            if (_material == null) return;
            if (!_material.HasProperty(ID_RimColor)) return;
            Color c = _material.GetColor(ID_RimColor);
            c.a = alpha;
            _material.SetColor(ID_RimColor, c);
        }
    }
}
