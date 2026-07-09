using System.Collections;
using System.Collections.Generic;
using Game.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Тост «Получена карта!»: иконка + имя + количество. Слушает RewardCardsGrantedUIEvent
    /// (BattleState публикует при ПЕРВОЙ победе в PvE — карты-награды энкаунтера), показывает очередью
    /// с фейдами (по одной карте). Заголовок локализован (ui.reward.card).
    ///
    /// Префаб: объект на BattleCanvas (поверх боевого UI) с CanvasGroup (_group), внутри:
    /// _icon (Image, арт карты), _captionText (TMP, «Получена карта!»), _nameText (TMP, «Имя ×N»).
    /// Объект активен, видимость — альфой CanvasGroup.
    /// </summary>
    public class RewardToastView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _captionText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private float _showSeconds = 2.2f;
        [SerializeField] private float _fadeSeconds = 0.25f;

        private readonly Queue<RewardCardsGrantedUIEvent> _queue = new();
        private Coroutine _playing;

        private void Awake()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group != null) { _group.alpha = 0f; _group.blocksRaycasts = false; }
        }

        private void OnEnable()  => GameEventBus.Subscribe<RewardCardsGrantedUIEvent>(OnReward);
        private void OnDisable()
        {
            GameEventBus.Unsubscribe<RewardCardsGrantedUIEvent>(OnReward);
            if (_playing != null) { StopCoroutine(_playing); _playing = null; }
            _queue.Clear();
            if (_group != null) _group.alpha = 0f;
        }

        private void OnReward(RewardCardsGrantedUIEvent evt)
        {
            _queue.Enqueue(evt);
            if (_playing == null) _playing = StartCoroutine(PlayQueue());
        }

        private IEnumerator PlayQueue()
        {
            while (_queue.Count > 0)
            {
                var reward = _queue.Dequeue();

                if (_icon != null)
                {
                    _icon.sprite = reward.Visual.Icon;
                    _icon.enabled = reward.Visual.Icon != null;
                }
                if (_captionText != null)
                    _captionText.text = Game.Core.Shared.CardTextLocalization.GetText("ui.reward.card", "Получена карта!");
                if (_nameText != null)
                    _nameText.text = reward.Count > 1
                        ? $"{reward.Visual.CardName} ×{reward.Count}"
                        : reward.Visual.CardName;

                yield return Fade(0f, 1f);
                yield return new WaitForSeconds(_showSeconds);
                if (_queue.Count == 0)
                    yield return Fade(1f, 0f);
            }
            _playing = null;
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
