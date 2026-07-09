using System.Collections;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Плашка подсказки туториала: показывает ТЕКУЩИЙ шаг (TutorialHintUIEvent от TutorialDirectorSystem),
    /// при смене шага — мягкий фейд между текстами. Локализация — CardTextLocalization (ключ + фолбэк).
    /// Живёт только на TutorialScene (в MP/PvE события не публикуются — можно оставить и на BattleCanvas,
    /// вью просто молчит).
    ///
    /// Префаб: объект на BattleCanvas с CanvasGroup (_group) + _hintText (TMP). Изначально скрыт (альфа 0).
    /// </summary>
    public class TutorialHintView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TextMeshProUGUI _hintText;
        [SerializeField] private float _fadeSeconds = 0.2f;

        private Coroutine _swap;

        private void Awake()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group != null) { _group.alpha = 0f; _group.blocksRaycasts = false; }
        }

        private void OnEnable()  => GameEventBus.Subscribe<TutorialHintUIEvent>(OnHint);
        private void OnDisable()
        {
            GameEventBus.Unsubscribe<TutorialHintUIEvent>(OnHint);
            if (_swap != null) { StopCoroutine(_swap); _swap = null; }
        }

        private void OnHint(TutorialHintUIEvent evt)
        {
            string text = string.IsNullOrEmpty(evt.TextKey)
                ? (evt.FallbackText ?? "")
                : Game.Core.Shared.CardTextLocalization.GetText(evt.TextKey, evt.FallbackText ?? "");

            if (_swap != null) StopCoroutine(_swap);
            _swap = StartCoroutine(SwapText(text));
        }

        private IEnumerator SwapText(string text)
        {
            if (_group != null && _group.alpha > 0f)
                yield return Fade(_group.alpha, 0f);

            if (_hintText != null) _hintText.text = text;

            if (!string.IsNullOrEmpty(text))
                yield return Fade(0f, 1f);
            _swap = null;
        }

        private IEnumerator Fade(float from, float to)
        {
            if (_group == null) yield break;
            float t = 0f;
            while (t < _fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, t / _fadeSeconds);
                yield return null;
            }
            _group.alpha = to;
        }
    }
}
