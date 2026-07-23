using AwesomeUI.Core.Card;
using DG.Tweening;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Карточка-инспектор ЛЮБОЙ карты (существо на столе, спелл/чара в истории розыгрышей — тип карты не
    /// важен, принимает готовый CardVisualData) — ПОЛНОЦЕННАЯ карта (наследник CardBaseView, как
    /// OpponentCardPlayView), появляется справа на время удержания пальца и исчезает по отпусканию.
    /// В отличие от OpponentCardPlayView — БЕЗ очереди и автослайда: просто Show/Hide. Вызывается либо через
    /// CardDetailUIEvent (существо на столе — держит CreatureView, резолвит CreatureInspectSystem), либо
    /// НАПРЯМУЮ из PlayHistoryDrawerView/PlayHistoryThumbView (миниатюра истории — CardVisualData уже на
    /// руках, поход в ECS не нужен). Имя файла (CreatureDetailPopupView.cs) исторически осталось от первой
    /// версии — НЕ переименовывал, чтобы не терять GUID/ссылки на компонент в уже собранных префабах;
    /// содержимое — этот класс, CardDetailPopupView.
    /// </summary>
    public class CardDetailPopupView : CardBaseView
    {
        [Header("Detail Popup — Fade")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private float _fadeDuration = 0.15f;

        private Tween _fadeTween;

        // ── SourceSlot: обязательные абстрактные методы (карта дисплейная, без взаимодействия) ──
        public override void Unject()     { }
        public override void OnUse()      { }
        public override void OnClick()    { }
        public override void UpdateView() { }

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        public void Show(in CardVisualData visual)
        {
            ApplyVisualData(visual);   // полный рендер карты (поля CardBaseView)

            gameObject.SetActive(true);
            _fadeTween?.Kill();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _fadeTween = _canvasGroup.DOFade(1f, _fadeDuration).SetEase(Ease.OutQuad);
            }
        }

        public void Hide()
        {
            _fadeTween?.Kill();
            if (_canvasGroup != null)
                _fadeTween = _canvasGroup.DOFade(0f, _fadeDuration).SetEase(Ease.InQuad)
                    .OnComplete(() => gameObject.SetActive(false));
            else
                gameObject.SetActive(false);
        }

        private void OnDestroy() => _fadeTween?.Kill();
    }
}
