using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// ЛИПКИЙ хинт выбора цели («Выберите цель»). Показывается на TargetSelectionHintEvent{Show=true} и висит,
    /// пока идёт выбор цели по доске; прячется при Show=false (выбор сделан/отменён). В отличие от TurnHintView
    /// (транзиентный) — не гаснет сам. GameObject держим активным (alpha=0 = скрыт), чтобы подписка жила;
    /// само-подписка в OnEnable/OnDisable — отдельной проводки в BattlePanel не нужно.
    /// </summary>
    public class TargetHintView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private TextMeshProUGUI _hintText;
        [SerializeField] private float           _fade = 0.2f;

        private Tween _tween;

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;   // скрыт, но активен (подписка живёт)
        }

        private void OnEnable()  => GameEventBus.Subscribe<TargetSelectionHintEvent>(OnHint);
        private void OnDisable() => GameEventBus.Unsubscribe<TargetSelectionHintEvent>(OnHint);

        private void OnHint(TargetSelectionHintEvent e)
        {
            if (e.Show) Show(e.Text);
            else        Hide();
        }

        private void Show(string text)
        {
            if (_hintText != null && !string.IsNullOrEmpty(text)) _hintText.text = text;
            _tween?.Kill();
            if (_canvasGroup != null) _tween = _canvasGroup.DOFade(1f, _fade);
        }

        private void Hide()
        {
            _tween?.Kill();
            if (_canvasGroup != null) _tween = _canvasGroup.DOFade(0f, _fade);
        }

        private void OnDestroy() => _tween?.Kill();
    }
}
