using System.Collections;
using System.Collections.Generic;
using Game.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// «Говорящая голова» стори-мода: портрет персонажа + локализованная реплика. Слушает
    /// StoryLineUIEvent (публикует BattleState из PveEncounterConfig.Intro/Victory/DefeatLines),
    /// показывает ОЧЕРЕДЬЮ: фейд-ин → держим Duration → фейд-аут → следующая.
    /// Локализация: TextKey/SpeakerKey через CardTextLocalization (фолбэк — сырой текст из конфига).
    ///
    /// Префаб: висит на BattleCanvas (НАД боевым UI, но ПОД поп-апом результата — реплики победы
    /// видны до/вместе с ним по вкусу), корень с CanvasGroup (_group), внутри: _portrait (Image),
    /// _speakerText (TMP, опц.), _lineText (TMP). Объект активен, видимость — альфой CanvasGroup.
    /// </summary>
    public class StoryDialogueView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _portrait;
        [SerializeField] private TextMeshProUGUI _speakerText;
        [SerializeField] private TextMeshProUGUI _lineText;
        [SerializeField] private float _fadeSeconds = 0.25f;

        private readonly Queue<StoryLineUIEvent> _queue = new();
        private Coroutine _playing;

        private void Awake()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group != null) { _group.alpha = 0f; _group.blocksRaycasts = false; }
        }

        private void OnEnable()  => GameEventBus.Subscribe<StoryLineUIEvent>(OnLine);
        private void OnDisable()
        {
            GameEventBus.Unsubscribe<StoryLineUIEvent>(OnLine);
            if (_playing != null) { StopCoroutine(_playing); _playing = null; }
            _queue.Clear();
            if (_group != null) _group.alpha = 0f;
        }

        private void OnLine(StoryLineUIEvent evt)
        {
            _queue.Enqueue(evt);
            if (_playing == null) _playing = StartCoroutine(PlayQueue());
        }

        private IEnumerator PlayQueue()
        {
            while (_queue.Count > 0)
            {
                var line = _queue.Dequeue();

                if (_portrait != null)
                {
                    _portrait.sprite = line.Portrait;
                    _portrait.enabled = line.Portrait != null;
                }

                if (_speakerText != null)
                {
                    string speaker = string.IsNullOrEmpty(line.SpeakerKey)
                        ? ""
                        : Game.Core.Shared.CardTextLocalization.GetText(line.SpeakerKey, "");
                    _speakerText.text = speaker;
                    _speakerText.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
                }

                if (_lineText != null)
                {
                    _lineText.text = string.IsNullOrEmpty(line.TextKey)
                        ? (line.FallbackText ?? "")
                        : Game.Core.Shared.CardTextLocalization.GetText(line.TextKey, line.FallbackText ?? "");
                }

                yield return Fade(0f, 1f);
                yield return new WaitForSeconds(Mathf.Max(0.5f, line.Duration));
                // Следующая реплика уже в очереди → без фейд-аута между репликами (смена «в кадре»).
                if (_queue.Count == 0)
                    yield return Fade(1f, 0f);
            }
            _playing = null;
        }

        private IEnumerator Fade(float from, float to)
        {
            if (_group == null) yield break;
            if (Mathf.Approximately(_group.alpha, to)) yield break;
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
