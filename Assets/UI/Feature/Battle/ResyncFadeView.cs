using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Полноэкранное затемнение на время self-heal ресинка (как в HS при восстановлении соединения):
    /// WorldResyncUIEvent{Show=true} — фейд в чёрное с надписью «Синхронизация…» (под ним WorldResyncSystem
    /// пересобирает мир — игрок не видит дёрганых телепортов вьюх), Show=false — плавное проявление
    /// (чуть медленнее, чтобы вьюхи борда успели заспавниться). Блокирует клики (CanvasGroup.blocksRaycasts).
    /// Подключение как у остальных вью боя: BattlePanel держит ссылку и зовёт OnInject/Unject.
    /// В префабе: объект на ВЕСЬ экран (чёрный Image) + CanvasGroup + TMP-текст.
    /// </summary>
    public class ResyncFadeView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private TextMeshProUGUI _label;

        [Header("Texts (фоллбэк; локализация ui.battle.resync)")]
        [SerializeField] private string _syncText = "Синхронизация…";

        [Header("Animation")]
        [SerializeField] private float _fadeIn  = 0.35f;
        [SerializeField] private float _fadeOut = 0.6f;   // медленнее: под проявление вьюхи уже стоят на местах

        private Tween _fade;

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        public void OnInject() => GameEventBus.Subscribe<WorldResyncUIEvent>(OnResync);
        public void Unject()  => GameEventBus.Unsubscribe<WorldResyncUIEvent>(OnResync);

        private void OnResync(WorldResyncUIEvent evt)
        {
            _fade?.Kill();

            if (evt.Show)
            {
                if (_label != null)
                    _label.text = Game.Core.Shared.CardTextLocalization.GetText("ui.battle.resync", _syncText);
                gameObject.SetActive(true);
                _canvasGroup.blocksRaycasts = true;
                _fade = _canvasGroup.DOFade(1f, _fadeIn).SetEase(Ease.OutQuad);
            }
            else
            {
                if (!gameObject.activeInHierarchy) return;
                _canvasGroup.blocksRaycasts = false;
                _fade = _canvasGroup.DOFade(0f, _fadeOut).SetEase(Ease.InQuad)
                                    .OnComplete(() => gameObject.SetActive(false));
            }
        }

        private void OnDestroy() => _fade?.Kill();
    }
}
