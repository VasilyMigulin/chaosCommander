using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Презентер «карта улетела в колоду» — НЕ часть CardLayout (это не про управление веером руки, а
    /// самостоятельная косметика): карта появляется на экране у источника эффекта (Старый колдун — на
    /// своей клетке; «Дать газу!» — у спелла нет борд-позиции, EntityWorldPosUtil сам провалится в
    /// аватар владельца, «летит от игрока») и улетает в offscreen-точку, символизирующую колоду — аналог
    /// того, как существо с поля летит В руку при баунсе/смерти командира (см. CardShuffledToDeckEvent/
    /// DeckShuffleUISystem — зеркало CardDrawnEvent/HandUISystem).
    ///
    /// Очередь + ОДИН переиспользуемый инстанс _cardPrefab (лениво инстанцируется при первом событии, живёт
    /// всё время жизни презентера, см. OnDestroy) — НЕ делит пул с рукой (CardLayout._handSlots). Несколько
    /// событий одним кадром (RepeatEffect «за каждое погибшее существо» — «Дать газу») не летят параллельно
    /// (слились бы в одну карту на глаз) — проигрываются строго по очереди, с паузой между (_queueInterval).
    ///
    /// Повесить на любой GameObject с RectTransform внутри боевого Canvas (BattlePanel или отдельный
    /// оверлей) — координаты локальные для ЭТОГО RectTransform, назначь _cardPrefab и при желании подправь
    /// _offscreenPoint (по умолчанию совпадает с CardLayout._dealInOffset — держи их синхронными вручную,
    /// если поменяешь одно из двух).
    /// </summary>
    public sealed class DeckShuffleFxPresenter : MonoBehaviour
    {
        [Tooltip("Прототип визуала карты (обычно SimpleCardView) — инстанцируется на каждое событие, уничтожается по завершении полёта.")]
        [SerializeField] private SimpleCardView _cardPrefab;

        [Tooltip("Куда улетает карта — дальше за границу экрана, чем у обычного добора руки " +
                 "(CardLayout._dealInOffset): там карта останавливается В КАДРЕ (садится в руку), здесь она " +
                 "должна полностью скрыться за краем, поэтому точка вынесена заметно дальше.")]
        [SerializeField] private Vector2 _offscreenPoint = new Vector2(900f, -550f);

        [Tooltip("Подъём к точке зависания (первый, вертикальный отрезок Г-траектории).")]
        [SerializeField] private float _riseDuration = 0.35f;

        [Tooltip("Зависание крупным планом — читаемая пауза, дать прочитать ЧТО замешивается. Тот же " +
                 "принцип, что у CardLayout._autoPlayHoldDuration (пауза форс-каста после добора).")]
        [SerializeField] private float _holdDuration = 0.6f;

        [Tooltip("Уход в offscreen-точку колоды со схлопыванием (второй отрезок Г-траектории).")]
        [SerializeField] private float _awayDuration = 0.45f;

        [Tooltip("Пауза между соседними замесами в очереди (RepeatEffect «за каждое погибшее существо» — " +
                 "«Дать газу» шлёт несколько событий разом) — иначе они наложились бы друг на друга и " +
                 "выглядели бы как одна карта. Тот же принцип, что у CardLayout._dealInterval.")]
        [SerializeField] private float _queueInterval = 0.18f;

        RectTransform _rectTransform;
        Camera _uiCamera;

        // ОЧЕРЕДЬ: несколько событий одним кадром (Дать газу — по одному на каждую смерть) не должны
        // стартовать параллельно — иначе N инстансов летят абсолютно синхронно по одной и той же дуге и
        // визуально сливаются в один. Играем строго по одному, с паузой между.
        readonly Queue<CardShuffledToDeckUIEvent> _pending = new Queue<CardShuffledToDeckUIEvent>();
        bool _isPlaying;

        // ОДИН переиспользуемый инстанс, а не Instantiate/Destroy на каждое событие: очередь выше и так
        // гарантирует, что параллельно летит максимум одна карта — второй инстанс никогда не понадобится.
        // Создаётся лениво при первом событии, живёт всё время жизни презентера (см. OnDestroy).
        SimpleCardView _view;

        void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            _uiCamera = canvas != null ? canvas.worldCamera : null;
        }

        void OnDestroy()
        {
            if (_view != null) Destroy(_view.gameObject);
        }

        void OnEnable()  => GameEventBus.Subscribe<CardShuffledToDeckUIEvent>(OnShuffled);
        void OnDisable()
        {
            GameEventBus.Unsubscribe<CardShuffledToDeckUIEvent>(OnShuffled);
            StopAllCoroutines();   // могло оборвать Fly() посреди полёта — прячем переиспользуемый инстанс
            if (_view != null) { _view.transform.DOKill(); _view.gameObject.SetActive(false); }
            _pending.Clear();
            _isPlaying = false;
        }

        void OnShuffled(CardShuffledToDeckUIEvent evt)
        {
            _pending.Enqueue(evt);
            if (!_isPlaying) StartCoroutine(RunQueue());
        }

        IEnumerator RunQueue()
        {
            _isPlaying = true;
            while (_pending.Count > 0)
            {
                var evt = _pending.Dequeue();
                yield return Fly(evt);
                if (_pending.Count > 0) yield return new WaitForSeconds(_queueInterval);
            }
            _isPlaying = false;
        }

        IEnumerator Fly(CardShuffledToDeckUIEvent evt)
        {
            if (_cardPrefab == null) yield break;   // не назначен в инспекторе — косметику пропускаем

            // Init() — обязателен: SourceSlot-фреймворк выставляет _rectTransform/_canvasGroup ТАМ, а не в
            // Unity Awake(). Без него PlayShuffleToDeckAnimation падал с NullReferenceException на первой
            // же строке (_rectTransform.DOKill()) — корутина умирала молча, карта оставалась висеть в
            // дефолтной позиции инстанса (баг: «карта тупо повисла» посреди поля). OnInject() НЕ зовём —
            // та подписка на CardAffordableChangedEvent/AbilityReady/etc. нужна интерактивным картам руки,
            // этот инстанс — немой показ, только Init() (transform-поля + ResetHighlight + SetActive(false)).
            if (_view == null)
            {
                _view = Instantiate(_cardPrefab, _rectTransform);
                _view.Init();
            }
            var view = _view;
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            view.SetCard(new PlayCardData
            {
                CardEntity = evt.CardEntity,
                CardName   = evt.CardName,
                Icon       = evt.Icon,
                CardType   = evt.Visual.CardType,
                Element    = evt.Visual.Element,
                Rarity     = evt.Visual.Rarity,
                Visual     = evt.Visual,
            });

            Vector3 from = ResolveFrom(evt.FromScreen);
            Vector3 to   = new Vector3(_offscreenPoint.x, _offscreenPoint.y, 0f);

            bool done = false;
            view.PlayShuffleToDeckAnimation(from, to, _riseDuration, _holdDuration, _awayDuration, () => done = true);

            float guard = _riseDuration + _holdDuration + _awayDuration + 2f;
            while (!done && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }

            if (view != null) view.gameObject.SetActive(false);
        }

        // Экранная точка источника → локальные координаты ЭТОГО RectTransform (тот же приём, что у
        // CardLayout.ResolveDealFrom). Источник не резолвился (эффект без карты-кастера) — появляемся
        // прямо в offscreen-точке: короткая вспышка на месте вместо перелёта, но честно — лучше так,
        // чем молча ничего не показать.
        Vector3 ResolveFrom(Vector2? fromScreen)
        {
            Vector3 fallback = new Vector3(_offscreenPoint.x, _offscreenPoint.y, 0f);
            if (fromScreen == null) return fallback;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rectTransform, fromScreen.Value, _uiCamera, out var local))
                return fallback;
            return new Vector3(local.x, local.y, 0f);
        }
    }
}
