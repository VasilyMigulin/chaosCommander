using System.Collections;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Подсказка туториала. ДВА РЕЖИМА — у инфо-шага и шага-действия требования противоположные:
    ///
    ///   • ИНФО (NeedsContinue=true)  — игрок читает, играть всё равно запрещено → МОДАЛКА по центру
    ///     (крупный текст + кнопка «Далее»), перекрывает бой и ловит клики. Это нормально.
    ///   • ДЕЙСТВИЕ (NeedsContinue=false) — игрок должен видеть доску и тыкать → тонкий БАННЕР сверху,
    ///     клики проходят насквозь (blocksRaycasts=false), поле боя и кнопка «Завершить ход» открыты.
    ///
    /// Если _bannerRoot не назначен, оба режима идут через модалку (старое поведение) — но тогда плашка
    /// будет перекрывать бой и на шагах-действиях.
    ///
    /// Префаб: корень с CanvasGroup (_group, добавится сам) →
    ///   ModalRoot  (_modalRoot):  _modalText + _continueButton («Далее»)
    ///   BannerRoot (_bannerRoot): _bannerText  — узкая полоса вверху, с отступом под чёлку
    /// </summary>
    public class TutorialHintView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;

        [Header("Модалка (инфо-шаги: читаем и жмём «Далее»)")]
        [SerializeField] private GameObject _modalRoot;
        [SerializeField] private TextMeshProUGUI _modalText;
        [SerializeField] private UnityEngine.UI.Button _continueButton;

        [Header("Баннер (шаги-действия: узкая полоса, бой не перекрыт)")]
        [Tooltip("Не назначен → и действия пойдут через модалку (будет перекрывать бой).")]
        [SerializeField] private GameObject _bannerRoot;
        [SerializeField] private TextMeshProUGUI _bannerText;

        [SerializeField] private float _fadeSeconds = 0.2f;

        private Coroutine _swap;

        private void Awake()
        {
            // CanvasGroup обязателен: через него на шагах-ДЕЙСТВИЯХ клики проходят насквозь
            // (иначе плашка перекрывала бы кнопку «Завершить ход» и доску).
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinueClicked);
                _continueButton.gameObject.SetActive(false);
            }
            if (_modalRoot != null) _modalRoot.SetActive(false);
            if (_bannerRoot != null) _bannerRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveListener(OnContinueClicked);
        }

        // Кнопку прячем сразу: шаг уже «отвечен», иначе можно накликать несколько переходов подряд.
        private void OnContinueClicked()
        {
            if (_continueButton != null) _continueButton.gameObject.SetActive(false);
            GameEventBus.Publish(new TutorialContinueEvent());
        }

        // SubscribePersistent, не Subscribe: объект persistent (DontDestroyOnLoad), OnEnable отрабатывает
        // ОДИН раз за сессию — обычная подписка терялась бы навсегда после первого GameEventBus.Clear().
        private void OnEnable()  => GameEventBus.SubscribePersistent<TutorialHintUIEvent>(OnHint);
        private void OnDisable()
        {
            GameEventBus.UnsubscribePersistent<TutorialHintUIEvent>(OnHint);
            if (_swap != null) { StopCoroutine(_swap); _swap = null; }
        }

        private void OnHint(TutorialHintUIEvent evt)
        {
            string text = string.IsNullOrEmpty(evt.TextKey)
                ? (evt.FallbackText ?? "")
                : Game.Core.Shared.CardTextLocalization.GetText(evt.TextKey, evt.FallbackText ?? "");

            if (_continueButton != null) _continueButton.gameObject.SetActive(false);   // покажем после фейда
            if (_swap != null) StopCoroutine(_swap);
            _swap = StartCoroutine(SwapText(text, evt.NeedsContinue));
        }

        private IEnumerator SwapText(string text, bool needsContinue)
        {
            if (_group != null && _group.alpha > 0f)
                yield return Fade(_group.alpha, 0f);

            // Инфо → модалка; действие → баннер (если собран, иначе тоже модалка).
            bool useBanner = !needsContinue && _bannerRoot != null;

            if (_modalRoot != null)  _modalRoot.SetActive(!useBanner && !string.IsNullOrEmpty(text));
            if (_bannerRoot != null) _bannerRoot.SetActive(useBanner && !string.IsNullOrEmpty(text));

            if (useBanner) { if (_bannerText != null) _bannerText.text = text; }
            else           { if (_modalText  != null) _modalText.text  = text; }

            if (!string.IsNullOrEmpty(text))
                yield return Fade(0f, 1f);

            // «Далее» — только на инфо-шагах и только когда текст уже проявился.
            if (needsContinue && !string.IsNullOrEmpty(text) && _continueButton != null)
                _continueButton.gameObject.SetActive(true);

            // Модалка ловит клики (играть нельзя), баннер — пропускает насквозь.
            if (_group != null) _group.blocksRaycasts = needsContinue;

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
