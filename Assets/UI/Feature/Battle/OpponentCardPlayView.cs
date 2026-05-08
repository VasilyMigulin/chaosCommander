using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Всплывающий показ карты, которую разыграл оппонент.
    /// Карточка плавно появляется, зависает, затем плавно исчезает.
    /// Вызывается через Show(cardName, icon).
    /// </summary>
    public class OpponentCardPlayView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private Image           _cardIcon;
        [SerializeField] private TextMeshProUGUI _cardNameText;

        [Header("Animation")]
        [SerializeField] private float _fadeInDuration  = 0.35f;
        [SerializeField] private float _holdDuration    = 1.8f;
        [SerializeField] private float _fadeOutDuration = 0.35f;
        [SerializeField] private float _riseDistance    = 40f;

        private Sequence _sequence;

        // ── Init ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Показать карту оппонента с анимацией появления, зависания и исчезания.</summary>
        public void Show(string cardName, Sprite icon)
        {
            _sequence?.Kill();

            if (_cardNameText != null) _cardNameText.text = cardName;
            if (_cardIcon     != null && icon != null) _cardIcon.sprite = icon;

            gameObject.SetActive(true);

            var rt = (RectTransform)transform;
            var startAnchoredY = rt.anchoredPosition.y - _riseDistance;
            var endAnchoredY   = rt.anchoredPosition.y;

            _canvasGroup.alpha = 0f;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, startAnchoredY);

            _sequence = DOTween.Sequence();

            // fade in + rise
            _sequence.Append(_canvasGroup.DOFade(1f, _fadeInDuration).SetEase(Ease.OutQuad));
            _sequence.Join(rt.DOAnchorPosY(endAnchoredY, _fadeInDuration).SetEase(Ease.OutQuad));

            // hold
            _sequence.AppendInterval(_holdDuration);

            // fade out
            _sequence.Append(_canvasGroup.DOFade(0f, _fadeOutDuration).SetEase(Ease.InQuad));
            _sequence.OnComplete(() => gameObject.SetActive(false));
        }

        private void OnDestroy()
        {
            _sequence?.Kill();
        }
    }
}
