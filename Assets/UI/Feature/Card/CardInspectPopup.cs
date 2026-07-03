using System.Collections;
using System.Collections.Generic;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Оверлей предпросмотра карты: крупная карта + описание + список СВЯЗАННЫХ карт
    /// (что карта создаёт/замешивает/призывает) через VerticalLayoutGroup.
    /// Открывается удержанием карты (CardInspectBus.Requested), закрывается тапом по фону.
    ///
    /// Размещение в сцене: как последний (верхний) ребёнок панели/канваса колодостроителя, чтобы
    /// рисоваться поверх. Сам GameObject держим активным (видимость — через CanvasGroup), иначе
    /// подписка в OnEnable не сработает.
    ///
    /// Поля инспектора:
    ///   _mainCard          — StaticCardView крупной карты
    ///   _descriptionText   — (опц.) отдельное крупное описание
    ///   _relatedSection    — контейнер «заголовок + список» (прячется, если связанных нет)
    ///   _relatedContent    — Transform с VerticalLayoutGroup (сюда кладутся карточки)
    ///   _relatedCardPrefab — префаб StaticCardView для элемента списка
    ///   _backgroundButton  — полноэкранная кнопка-фон для закрытия
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class CardInspectPopup : MonoBehaviour
    {
        [Header("Main card")]
        [SerializeField] StaticCardView _mainCard;
        [SerializeField] TextMeshProUGUI _descriptionText;

        [Header("Related (VerticalLayoutGroup)")]
        [SerializeField] GameObject _relatedSection;
        [SerializeField] Transform _relatedContent;
        [SerializeField] StaticCardView _relatedCardPrefab;

        [Header("Close")]
        [SerializeField] Button _backgroundButton;

        [Header("Animation")]
        [SerializeField] float _fadeDuration = 0.15f;

        CanvasGroup _cg;
        Coroutine _fade;
        readonly List<StaticCardView> _related = new List<StaticCardView>();

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            SetVisible(false, instant: true);
            if (_backgroundButton != null) _backgroundButton.onClick.AddListener(Hide);
        }

        void OnEnable() => CardInspectBus.Requested += Show;

        void OnDisable()
        {
            CardInspectBus.Requested -= Show;
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
        }

        public void Show(CardModel source)
        {
            if (source == null) return;

            _mainCard?.SetModel(source);
            if (_descriptionText != null)
                _descriptionText.text = CardVisualDataFactory.From(source).Description;

            BuildRelated(source);
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        void BuildRelated(CardModel source)
        {
            foreach (var v in _related)
                if (v != null) Destroy(v.gameObject);
            _related.Clear();

            var cards = RelatedCardsResolver.Resolve(source);
            if (_relatedSection != null) _relatedSection.SetActive(cards.Count > 0);
            if (_relatedContent == null || _relatedCardPrefab == null) return;

            foreach (var model in cards)
            {
                var view = Instantiate(_relatedCardPrefab, _relatedContent);
                view.SetModel(model);
                _related.Add(view);
            }
        }

        void SetVisible(bool visible, bool instant = false)
        {
            _cg.blocksRaycasts = visible;
            _cg.interactable = visible;

            if (_fade != null) { StopCoroutine(_fade); _fade = null; }

            if (instant || !gameObject.activeInHierarchy)
            {
                _cg.alpha = visible ? 1f : 0f;
                return;
            }
            _fade = StartCoroutine(Fade(visible ? 1f : 0f));
        }

        IEnumerator Fade(float target)
        {
            float start = _cg.alpha;
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                _cg.alpha = Mathf.Lerp(start, target, t / _fadeDuration);
                yield return null;
            }
            _cg.alpha = target;
            _fade = null;
        }
    }
}
