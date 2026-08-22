using AwesomeUI.Core.Layout;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Layout руки игрока.
    /// — Первый слот всегда CommanderCardView (не уходит из руки).
    /// — Остальные SimpleCardView располагаются веером.
    /// — Click внутри Layout → expand (карты поднимаются и разворачиваются).
    /// — Click вне Layout → collapse (веер складывается, не мешает обзору).
    /// </summary>
    public class CardLayout : SourceLayout, IPointerClickHandler
    {
        [Header("Fan Settings")]
        [SerializeField] private float _fanAngleRange    = 30f;   // суммарный угол веера
        [SerializeField] private float _fanHeightCurve   = 40f;   // высота прогиба карт
        [SerializeField] private float _cardSpacing      = 90f;   // расстояние между картами
        [SerializeField] private float _animDuration     = 0.25f;

        [Header("Collapse")]
        [SerializeField] private float _collapsedCardY  = -80f;  // карты частично уходят вниз когда рука свёрнута
        [SerializeField] private float _expandedCardY   = 0f;   // базовый Y карт в развёрнутом состоянии

        [Header("Deal Animation")]
        [SerializeField] private float _dealInterval     = 0.18f; // задержка между картами
        [Tooltip("Локальный offset стартовой точки добора (откуда карта прилетает в руку). Обычно — направление колоды.")]
        [SerializeField] private Vector2 _dealInOffset   = new Vector2(500f, -300f);
        [Tooltip("Длительность прилёта ОДИНОЧНОЙ карты: дуга из-за края + зависание крупным планом + посадка в веер.")]
        [SerializeField] private float _dealInDuration      = 0.85f;
        [Tooltip("То же для раздачи ПАЧКОЙ (стартовая рука, каскад доборов) — быстрый плоский залёт без укрупнения.")]
        [SerializeField] private float _dealInBatchDuration = 0.45f;

        [Header("Discard Animation")]
        [SerializeField] private float _discardDuration  = 1.2f;
        [Tooltip("Сколько держать авто-разыгранную с добора карту (Вонючее облако) в руке перед сбросом-анимацией.")]
        [SerializeField] private float _autoPlayHoldDuration = 0.6f;

        [Header("Card Slots")]
        [SerializeField] private CommanderCardView _commanderSlot;
        [SerializeField] private List<SimpleCardView> _handSlots;

        private bool _isExpanded = false;
        private PlayCardView _selectedCard;
        private RectTransform _rectTransform;
        private Camera _uiCamera;

        // Порядок карт в руке слева-направо (новые добавляются в конец = к краю).
        // Веер строится по этому порядку, а не по индексу слота, поэтому при розыгрыше
        // карты из середины оставшиеся «сдвигаются», а добор всегда ложится с краю.
        private readonly List<SimpleCardView> _handOrder = new List<SimpleCardView>();

        // ── Очередь событий руки ──────────────────────────────────────────────
        // ОДНА очередь на всё, что происходит с рукой: «пришла», «ушла», «сброшена». Раньше приход шёл
        // корутиной с задержкой, а уход и сброс выполнялись МГНОВЕННО — уход обгонял ещё не показанный
        // приход. Отсюда обе беды разом: «слот, который некому очистить» (карта легла после своего ухода)
        // и карта, пропавшая из UI, но живая в модели. Теперь порядок шагов = порядок событий, а замок
        // презентации держится, пока очередь не опустела.
        private enum HandStepKind { Add, Remove, Discard, DeckReveal }

        // Что случилось с картой, показанной крупным планом (зависание дуги). Единая точка ветвления
        // для будущих партиклов: розыгрыш и уничтожение выглядят одной траекторией, различает их VFX.
        public enum ShowcaseKind { ForcePlayed, Destroyed }

        private struct HandStep
        {
            public HandStepKind Kind;
            public int CardEntity;
            public CardAddedToHandUIEvent Added;    // только для Add
            public PlayCardData Reveal;             // только для DeckReveal (карта не из руки — визуал с собой)
            public ShowcaseKind RevealKind;         // только для DeckReveal
        }

        // List, а не Queue: из очереди приходится выкидывать СЕРЕДИНУ (карта ушла раньше, чем мы успели
        // её показать) и вставлять в НАЧАЛО (карта дождалась освободившегося слота).
        private readonly List<HandStep> _handQueue = new List<HandStep>();
        private bool _isHandQueueRunning;

        // Карты, разыгранные принудительно прямо с добора (Вонючее облако). Это НЕ отдельная дорога, а
        // ФЛАГ на приходе: такая карта не садится в руку, а с зависания дуги улетает вперёд. Её штатный
        // уход из руки приходит следом обычным шагом и просто не находит слота — гасить события не нужно.
        private readonly HashSet<int> _forcePlayed = new HashSet<int>();

        // Карты, пришедшие в занятую руку: ждут, пока освободится слот. Выдаёт их ReleaseSlot — то есть
        // ровно тот момент, когда слот реально освободился, а не по таймеру.
        private readonly Queue<CardAddedToHandUIEvent> _awaitingSlot = new Queue<CardAddedToHandUIEvent>();

        // ── Полёт добора ──────────────────────────────────────────────────────
        // Карты, которые ЛЕТЯТ в руку прямо сейчас. Пока набор не пуст, раздача держит замок презентации:
        // авто-касты не должны стартовать поверх летящей карты (ровно та гонка, из-за которой всё затевалось).
        private readonly HashSet<int> _drawInFlight = new HashSet<int>();
        // Их слоты. Веер ими НЕ управляет: позицию ведёт дуга, второй твин на тот же localPosition подрался бы
        // с ней. Слот выходит отсюда, когда сел в руку (или когда его освободили).
        private readonly HashSet<SimpleCardView> _flyingSlots = new HashSet<SimpleCardView>();

        // ── Init ─────────────────────────────────────────────────────────────

        public override SourceLayout Init()
        {
            // не вызываем base.Init() — у нас своя структура слотов
            _rectTransform = GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            _uiCamera = canvas != null ? canvas.worldCamera : null;
            _slots = BuildSlotArray();

            foreach (var slot in _slots)
                slot.Init();

            gameObject.SetActive(true);
            return this;
        }

        private Core.Slot.SourceSlot[] BuildSlotArray()
        {
            var list = new List<Core.Slot.SourceSlot>();
            if (_commanderSlot != null) list.Add(_commanderSlot);
            foreach (var s in _handSlots)
                if (s != null) list.Add(s);
            return list.ToArray();
        }

        public override void OnInject()
        {
            GameEventBus.Unsubscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Unsubscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Unsubscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);
            GameEventBus.Subscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Subscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Subscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);
            GameEventBus.Unsubscribe<CardForcePlayedFromDrawUIEvent>(OnCardForcePlayedFromDraw);
            GameEventBus.Subscribe<CardForcePlayedFromDrawUIEvent>(OnCardForcePlayedFromDraw);
            GameEventBus.Unsubscribe<CardMillFromDeckUIEvent>(OnCardMilledFromDeck);
            GameEventBus.Subscribe<CardMillFromDeckUIEvent>(OnCardMilledFromDeck);
            GameEventBus.Unsubscribe<CardPlayedFromDeckUIEvent>(OnCardPlayedFromDeck);
            GameEventBus.Subscribe<CardPlayedFromDeckUIEvent>(OnCardPlayedFromDeck);

            if (_commanderSlot != null)
                _commanderSlot.OnInject();

            foreach (var slot in _handSlots)
                slot?.OnInject();
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Unsubscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Unsubscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);
            GameEventBus.Unsubscribe<CardForcePlayedFromDrawUIEvent>(OnCardForcePlayedFromDraw);
            GameEventBus.Unsubscribe<CardMillFromDeckUIEvent>(OnCardMilledFromDeck);
            GameEventBus.Unsubscribe<CardPlayedFromDeckUIEvent>(OnCardPlayedFromDeck);

            // ВСЕ свои замки — долой. Это то, что заменяет сторожевой таймер: вьюху выключили/снесли
            // посреди анимации — пайплайн не должен ждать того, кого больше нет.
            PresentationLock.ReleaseAll(this);

            // Очередь и полёты этой руки закончились вместе с ней: корутины могут дожить кадр-другой,
            // а слоты в следующем матче те же — стартовать с недоигранными шагами и «летящими» нельзя.
            _handQueue.Clear();
            _awaitingSlot.Clear();
            _forcePlayed.Clear();
            _drawInFlight.Clear();
            _flyingSlots.Clear();

            _commanderSlot?.Unject();
            foreach (var slot in _handSlots)
                slot?.Unject();
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }

        // ── Event Handlers ───────────────────────────────────────────────────

        public void OnPreStartPhaseBegin(PreStartPhaseBeginUIEvent evt)
        {
            if (evt.HasCommander)
                EnqueueCard(evt.CommanderCard);

            if (evt.HandCards != null)
                foreach (var card in evt.HandCards)
                    EnqueueCard(card);
        }

        private void OnCardAddedToHand(CardAddedToHandUIEvent evt) => EnqueueCard(evt);

        private void OnCardRemovedFromHand(CardRemovedFromHandUIEvent evt) => EnqueueLeave(HandStepKind.Remove, evt.CardEntity);

        private void OnCardDiscard(CardDiscardFromHandUIEvent evt) => EnqueueLeave(HandStepKind.Discard, evt.CardEntity);

        // Карту уничтожили ПРЯМО В КОЛОДЕ — владелец должен УВИДЕТЬ, что потерял: дуга с зависанием
        // и вылет вперёд, как у форс-каста. Чужие карты показывает поп-ап MillNotificationView, не мы.
        private void OnCardMilledFromDeck(CardMillFromDeckUIEvent evt)
        {
            if (!evt.IsLocalOwner) return;
            EnqueueReveal(evt.CardEntity, evt.CardName, evt.Icon, evt.Visual, ShowcaseKind.Destroyed);
        }

        // Карта разыграна эффектом прямо ИЗ КОЛОДЫ (владелец её в руке не видел) — та же дуга, но с
        // VFX-развилкой «разыграна» (см. PlayShowcaseFx): траектория общая, различают партиклы.
        private void OnCardPlayedFromDeck(CardPlayedFromDeckUIEvent evt)
        {
            if (!evt.IsLocalOwner) return;
            EnqueueReveal(evt.CardEntity, evt.CardName, evt.Icon, evt.Visual, ShowcaseKind.ForcePlayed);
        }

        private void EnqueueReveal(int entity, string name, Sprite icon, Game.Core.Shared.CardVisualData visual, ShowcaseKind kind)
        {
            EnqueueStep(new HandStep
            {
                Kind       = HandStepKind.DeckReveal,
                CardEntity = entity,
                RevealKind = kind,
                Reveal     = new PlayCardData
                {
                    CardEntity = entity,
                    CardName   = name,
                    Icon       = icon,
                    CardType   = visual.CardType,
                    Element    = visual.Element,
                    Rarity     = visual.Rarity,
                    Visual     = visual,
                },
            });
        }

        // ── Очередь событий руки ──────────────────────────────────────────────

        private void EnqueueCard(CardAddedToHandUIEvent evt)
            => EnqueueStep(new HandStep { Kind = HandStepKind.Add, CardEntity = evt.CardEntity, Added = evt });

        // Уход карты. Единственная оптимизация порядка: если её приход ещё НЕ показан, обе записи
        // схлопываются — показывать «пришла и тут же ушла» нечего, карты в руке никто не видел.
        // Форс-каст — исключение: у него показ прихода и ЕСТЬ всё событие («пришла → разыгралась → ушла»),
        // поэтому запланированный шаг прихода остаётся, а уход придёт следом и просто не найдёт слота.
        // Но если карте негде даже приземлиться (стоит не в очереди шагов, а в ожидании свободного слота),
        // показывать нечем: выкидываем и приход, и пометку — иначе она позже легла бы в руку призраком.
        private void EnqueueLeave(HandStepKind kind, int cardEntity)
        {
            bool keepShow = _forcePlayed.Contains(cardEntity) && HasQueuedAdd(cardEntity);
            if (!keepShow && DropQueuedAdd(cardEntity))
            {
                _forcePlayed.Remove(cardEntity);
                return;
            }
            EnqueueStep(new HandStep { Kind = kind, CardEntity = cardEntity });
        }

        private bool HasQueuedAdd(int cardEntity)
        {
            for (int i = 0; i < _handQueue.Count; i++)
                if (_handQueue[i].Kind == HandStepKind.Add && _handQueue[i].CardEntity == cardEntity) return true;
            return false;
        }

        private void EnqueueStep(HandStep step)
        {
            _handQueue.Add(step);
            if (!_isHandQueueRunning)
                StartCoroutine(HandQueueCoroutine());
        }

        private IEnumerator HandQueueCoroutine()
        {
            _isHandQueueRunning = true;

            // ЗАМОК ПРЕЗЕНТАЦИИ на всю очередь: пока рука что-то показывает, пайплайн ждёт — так же, как он
            // ждёт полёт снаряда и анимацию каста. Именно это восстанавливает порядок «добрал → разыграл →
            // добрал»: без замка форс-касты успевали разыграться пачкой ДО того, как руку раздали.
            // Замок берём ЗДЕСЬ, потому что отпускаем тоже здесь — ждать можно только того, кто обещал.
            var hold = PresentationLock.Acquire(this, "анимация руки");
            try
            {
                while (_handQueue.Count > 0)
                {
                    var step = _handQueue[0];
                    _handQueue.RemoveAt(0);

                    switch (step.Kind)
                    {
                        case HandStepKind.Add:
                            yield return RunAddStep(step.Added);
                            // Между приходами держим только паузу раздачи: летящие карты друг другу не мешают
                            // (у каждой свой слот), поэтому пачка кладётся внахлёст, как и раньше.
                            if (_handQueue.Count > 0 && _handQueue[0].Kind == HandStepKind.Add)
                                yield return new WaitForSeconds(_dealInterval);
                            else
                                yield return WaitForFlights();
                            break;

                        case HandStepKind.Remove:
                            RunRemoveStep(step.CardEntity);
                            break;

                        case HandStepKind.Discard:
                            yield return RunDiscardStep(step.CardEntity);
                            break;

                        case HandStepKind.DeckReveal:
                            yield return RunDeckRevealStep(step.Reveal, step.RevealKind);
                            break;
                    }
                }
            }
            finally
            {
                // finally, а не просто в конце: корутину могут остановить (StopAllCoroutines, выключение
                // объекта) — замок обязан уйти в любом случае, иначе пайплайн запрётся навсегда.
                PresentationLock.Release(hold);
                _isHandQueueRunning = false;
            }
        }

        // Карты ещё ЛЕТЯТ. Отпустить замок или начать уход прямо сейчас = сделать это поверх летящей карты.
        // Ждём посадки — но с потолком: полёт могли оборвать мимо наших точек, а вечное ожидание = запертый
        // пайплайн. Потолок здесь безопаснее точности: худшее, что даст просрочка — рассинхрон косметики.
        private IEnumerator WaitForFlights()
        {
            float guard = _dealInDuration + 2f;
            while (_drawInFlight.Count > 0 && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }
        }

        // Приход карты. Обычная садится в веер; помеченная форс-кастом не садится вовсе: с зависания дуги
        // её ведёт тот же шаг — показ крупным планом и анимация сброса. Одна дорога на оба случая.
        private IEnumerator RunAddStep(CardAddedToHandUIEvent evt)
        {
            int entity = evt.CardEntity;
            var slot   = PlaceCard(evt);

            if (slot == null)
            {
                // Командир (свой слот, без дуги) или рука занята — карта ушла ждать слота в _awaitingSlot,
                // и её приход доиграется шагом, который поставит ReleaseSlot.
                yield break;
            }

            if (!_forcePlayed.Contains(entity))
                yield break;   // штатный прилёт: дуга сама сядет в веер, очередь её не сторожит

            // Дуга отдаёт нам карту на зависании (onHover), не сажая в руку.
            yield return WaitForFlights();

            yield return ShowcaseTail(slot, ShowcaseKind.ForcePlayed);
            _forcePlayed.Remove(entity);
        }

        // Показ карты, которой НЕ БЫЛО в руке (уничтожена/разыграна прямо из колоды): временный слот →
        // та же дуга с зависанием, что у форс-каста, → вылет вперёд. Слот берём из руки, но в веер
        // (_handOrder) карту НЕ регистрируем — она транзитная; на время показа слот занят, и параллельный
        // добор честно подождёт в _awaitingSlot (ReleaseSlot выдаст слот ему).
        private IEnumerator RunDeckRevealStep(PlayCardData data, ShowcaseKind kind)
        {
            var slot = GetFreeHandSlot();
            if (slot == null) yield break;   // рука забита — пропускаем косметику (карта в руку и не шла)

            slot.SetCard(data);
            _flyingSlots.Add(slot);   // позицией распоряжается дуга, веер не вмешивается

            bool atHover = false;
            slot.PlayDrawInAnimation(
                new Vector3(_dealInOffset.x, _dealInOffset.y, 0f),
                new Vector3(0f, _expandedCardY, 0f),   // «куда бы села» — задаёт форму дуги, посадки не будет
                0f, _dealInDuration, showcase: true,
                onHover: () => { atHover = true; return true; });   // забираем карту на зависании

            float guard = _dealInDuration + 2f;
            while (!atHover && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }

            yield return ShowcaseTail(slot, kind);
        }

        // Общий хвост показа крупным планом: VFX-развилка → пауза «прочитать» → вылет вперёд → слот свободен.
        private IEnumerator ShowcaseTail(SimpleCardView slot, ShowcaseKind kind)
        {
            PlayShowcaseFx(slot, kind);

            yield return new WaitForSeconds(_autoPlayHoldDuration);   // дать игроку прочитать карту

            bool done = false;
            slot.PlayDiscardAnimation(() => { ReleaseSlot(slot); done = true; }, _discardDuration);

            float guard = _discardDuration + 2f;
            while (!done && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }
        }

        // ЕДИНАЯ РАЗВИЛКА VFX показа: сюда подключаются партиклы, различающие «разыграна форсом» и
        // «уничтожена» — траектория у обоих случаев нарочно одна, читаться они должны эффектом.
        // Пока пусто: слот и вид события уже здесь, осталось воткнуть префабы/партиклы по kind.
        private void PlayShowcaseFx(SimpleCardView slot, ShowcaseKind kind)
        {
            switch (kind)
            {
                case ShowcaseKind.ForcePlayed:
                    // TODO VFX: «карта разыгрывается» (вспышка каста, свечение по рамке).
                    break;
                case ShowcaseKind.Destroyed:
                    // TODO VFX: «карту уничтожили» (разрыв/пепел/красная вспышка).
                    break;
            }
        }

        private void RunRemoveStep(int cardEntity)
        {
            _forcePlayed.Remove(cardEntity);

            var slot = FindHandSlot(cardEntity);
            if (slot != null) { ReleaseSlot(slot); return; }   // остальные сдвигаются при RefreshFan внутри

            // Командир — отдельный слот (_commanderSlot), FindHandSlot его не видит (ищет только среди
            // _handSlots). Призыв командира на стол снимает HandTag, и RunMoveCardToBoardSystem шлёт тот
            // же CardRemovedFromHandUIEvent, что и для обычных карт (см. его коммент «Освобождаем UI-слот
            // руки») — раньше это молча игнорировалось: слот оставался «занят» призраком до возврата
            // командира (смерть → SetCard), GetAllActiveSlots().Count был на 1 больше настоящего всё это
            // время, и последняя реальная карта руки никогда не получала t=0.5 → angle=0 (см. GetFanPose).
            if (_commanderSlot != null && _commanderSlot.IsOccupied && _commanderSlot.CardEntity == cardEntity)
            {
                // keepEntity: командирский CardEntity — та же ECS-сущность весь матч (в отличие от обычных
                // слотов, этот не отдают под другую карту). Сброс в -1 ронял CommanderOnCooldownUIEvent при
                // возврате командира в руку, если тот приходил раньше, чем CardLayout успевал показать
                // карту обратно (RunCommanderCooldownSystem и очередь CardLayout — независимые дороги).
                _commanderSlot.ClearCard(keepEntity: true);
                RefreshFan();
                return;
            }

            // Иначе — карту либо уже увели (форс-каст отыграл свой уход сам), либо её приход схлопнулся
            // с этим уходом ещё в очереди. Обе ветки штатные, гасить нечего.
        }

        private IEnumerator RunDiscardStep(int cardEntity)
        {
            _forcePlayed.Remove(cardEntity);

            var slot = FindHandSlot(cardEntity);
            if (slot == null) yield break;

            bool done = false;
            slot.PlayDiscardAnimation(() => { ReleaseSlot(slot); done = true; }, _discardDuration);

            float guard = _discardDuration + 2f;
            while (!done && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }
        }

        // Выкинуть ЕЩЁ НЕ ОТЫГРАННЫЙ приход этой карты (из очереди шагов или из ожидания слота).
        private bool DropQueuedAdd(int cardEntity)
        {
            for (int i = 0; i < _handQueue.Count; i++)
            {
                if (_handQueue[i].Kind != HandStepKind.Add || _handQueue[i].CardEntity != cardEntity) continue;
                _handQueue.RemoveAt(i);
                return true;
            }

            if (_awaitingSlot.Count == 0) return false;

            bool dropped = false;
            int count = _awaitingSlot.Count;
            for (int i = 0; i < count; i++)
            {
                var e = _awaitingSlot.Dequeue();
                if (!dropped && e.CardEntity == cardEntity) { dropped = true; continue; }
                _awaitingSlot.Enqueue(e);
            }
            return dropped;
        }

        // Точка старта полёта карты: обычно дефолтный оффскрин-угол (_dealInOffset), но если событие несёт
        // экранную точку ИСТОЧНИКА (раскопка/баунс/возврат командира — камеру уже применил HandUISystem,
        // ЭТА сборка не референсит Mono/BoardView, граница только через шину), конвертируем в локальные
        // координаты relativeTo (тот же space, в котором слот держит localPosition).
        private Vector3 ResolveDealFrom(Vector2? fromScreen, RectTransform relativeTo)
        {
            Vector3 fallback = new Vector3(_dealInOffset.x, _dealInOffset.y, 0f);
            if (fromScreen == null || relativeTo == null) return fallback;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(relativeTo, fromScreen.Value, _uiCamera, out var local))
                return fallback;

            return new Vector3(local.x, local.y, 0f);
        }

        // Возвращает слот, в который легла карта, или null (командир / рука занята — карта ушла ждать).
        private SimpleCardView PlaceCard(CardAddedToHandUIEvent evt)
        {
            var data = new PlayCardData
            {
                CardEntity  = evt.CardEntity,
                NetworkKey  = evt.NetworkKey,
                CardName    = evt.CardName,
                Icon        = evt.Icon,
                CardType    = evt.CardType,
                Element     = evt.Element,
                Rarity      = evt.Rarity,
                IsCommander = evt.IsCommander,
                Visual      = evt.Visual,
            };

            if (evt.IsCommander)
            {
                if (_commanderSlot != null)
                {
                    // Как обычные карты: перед показом ставим слот в точку добора и раскладываем веером.
                    // Иначе при ВОЗВРАТЕ командира в руку (после смерти) он появлялся там, где был отпущен
                    // при розыгрыше — OnEndDrag прячет карту, не сбрасывая localPosition, а SetCard позицию
                    // не трогает. Отсюда «зависание» командира в точке отпускания.
                    var crt = _commanderSlot.GetComponent<RectTransform>();
                    if (crt != null)
                    {
                        crt.DOKill();
                        crt.localPosition = ResolveDealFrom(evt.FromScreen, crt.parent as RectTransform);
                        crt.localRotation = Quaternion.identity;
                        crt.localScale    = Vector3.one;
                    }

                    _commanderSlot.SetCard(data);
                    RefreshFan();   // вернёт командира на его место в веере (анимированно)
                }
                return null;
            }

            var freeSlot = GetFreeHandSlot();
            if (freeSlot == null)
            {
                // КАРТУ НЕ ТЕРЯЕМ, а СТАВИМ В ОЧЕРЕДЬ ЗА СЛОТОМ. Свободного слота может не быть временно:
                // авто-каст с добора держит слот на «дай посмотреть» + анимацию сброса, и на каскаде
                // форс-кастов (десять Попоек подряд) обычный добор приходил в занятую руку. Раньше он тут
                // молча отбрасывался — карта оставалась в МОДЕЛИ, но исчезала из UI навсегда (2026-08-02).
                // Ждём НЕ по таймеру: слот выдаст себя сам, когда освободится (см. ReleaseSlot).
                _awaitingSlot.Enqueue(evt);
                return null;
            }

            // Pre-position: ставим слот в «точку добора» (откуда летит карта) — источник (см. ResolveDealFrom)
            // или дефолтный оффскрин-угол для обычного добора.
            var rt = freeSlot.GetComponent<RectTransform>();
            Vector3 dealFrom = ResolveDealFrom(evt.FromScreen, rt != null ? rt.parent as RectTransform : null);
            if (rt != null)
            {
                rt.DOKill();
                rt.localPosition = dealFrom;
                rt.localRotation = Quaternion.identity;
                rt.localScale    = Vector3.one;
            }

            freeSlot.SetCard(data);

            // ИНВАРИАНТ: новая карта = САМАЯ ПРАВАЯ. Остальные карты сдвигаются влево
            // (это делает RefreshFan через пересчёт позиций под новый count).
            // На всякий случай чистим возможный «хвост» от прошлой жизни этого слота.
            _handOrder.Remove(freeSlot);
            _handOrder.Add(freeSlot);

            int entity   = evt.CardEntity;
            var flying   = freeSlot;
            // Пометку читаем ОДИН раз и запоминаем: шаг прихода принимает решение сейчас, а колбэк дуги
            // сработает через полсекунды — расходись они, карта зависла бы на вершине без хозяина.
            bool forceShow = _forcePlayed.Contains(entity);
            // Показываем карту крупно (дуга с зависанием), если она пришла ОДНА или если у неё эффект
            // «при взятии» — там зависание не украшение, а сам смысл: игрок должен увидеть, что разыгралось.
            bool showcase = forceShow
                         || _handQueue.Count == 0
                         || _handQueue[0].Kind != HandStepKind.Add;

            _drawInFlight.Add(entity);
            _flyingSlots.Add(flying);

            // Сначала расставляем ОСТАЛЬНЫХ под новый count (летящую RefreshFan пропустит), и только потом
            // спрашиваем её конечную позу — иначе целились бы в место, которого уже нет.
            RefreshFan();

            var active = GetAllActiveSlots();
            GetFanPose(active.Count - 1, active.Count, out var landPos, out float landAngle);

            flying.PlayDrawInAnimation(
                dealFrom, landPos, landAngle,
                showcase ? _dealInDuration : _dealInBatchDuration,
                showcase,
                onHover: () =>
                {
                    // Карта с форс-кастом «при взятии» в руку не садится: на зависании её забирает шаг
                    // прихода (RunAddStep) — показ крупным планом и сброс. Слот остаётся в _flyingSlots,
                    // чтобы веер не потащил карту вниз, пока она висит перед игроком.
                    if (!forceShow) return false;
                    _drawInFlight.Remove(entity);
                    return true;
                },
                onComplete: () =>
                {
                    _drawInFlight.Remove(entity);
                    if (_flyingSlots.Remove(flying))
                        RefreshFan();   // веер мог перестроиться, пока карта летела — доводим на место
                });

            return flying;
        }

        private string DescribeOrder()
        {
            if (_handOrder.Count == 0) return "<empty>";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _handOrder.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"[{i}]={_handOrder[i].name}({_handOrder[i].CardEntity})");
            }
            return sb.ToString();
        }

        // ── Освобождение слота ───────────────────────────────────────────────

        // ЕДИНСТВЕННАЯ точка «слот освободился». Раньше очистка была размазана по трём местам, и ни одно
        // не знало, что кто-то ждёт слота — карта, пришедшая в занятую руку, просто терялась. Теперь любое
        // освобождение сразу выдаёт слот следующему ожидающему: чисто событийно, без таймеров и опроса.
        private void ReleaseSlot(SimpleCardView slot)
        {
            if (slot == null) return;
            // Слот могли освободить прямо в полёте (сброс/эффект оппонента поверх летящей карты). Снимаем
            // «летит» здесь, иначе очередь ждала бы посадки карты, которой в руке уже нет.
            _drawInFlight.Remove(slot.CardEntity);
            _flyingSlots.Remove(slot);
            slot.ClearCard();
            _handOrder.Remove(slot);
            RefreshFan();

            // Кто-то ждал места — возвращаем его приход в очередь ПЕРВЫМ шагом. Именно шагом, а не прямым
            // показом: у прихода есть своя длительность (дуга, а у форс-каста ещё показ и сброс), и вести
            // его должна та же очередь, иначе он снова окажется вне общего порядка.
            if (_awaitingSlot.Count == 0) return;
            var pending = _awaitingSlot.Dequeue();
            _handQueue.Insert(0, new HandStep
            {
                Kind = HandStepKind.Add, CardEntity = pending.CardEntity, Added = pending,
            });
            if (!_isHandQueueRunning)
                StartCoroutine(HandQueueCoroutine());
        }

        // ── Авто-каст с добора (Вонючее облако) ────────────────────────────────

        // Не отдельная дорога, а ПОМЕТКА на приходе: карта не сядет в руку, а с зависания дуги улетит
        // вперёд (см. RunAddStep). Событие приходит синхронно с добором, то есть ГАРАНТИРОВАННО раньше,
        // чем очередь дойдёт до шага прихода этой карты (CardAddedToHandUIEvent публикует HandUISystem
        // позже в том же кадре) — поэтому шагу достаточно посмотреть в набор.
        private void OnCardForcePlayedFromDraw(CardForcePlayedFromDrawUIEvent evt)
            => _forcePlayed.Add(evt.CardEntity);

        private SimpleCardView FindHandSlot(int cardEntity)
        {
            foreach (var slot in _handSlots)
                if (slot != null && slot.IsOccupied && slot.CardEntity == cardEntity)
                    return slot;
            return null;
        }

        // ── Selection ────────────────────────────────────────────────────────

        /// <summary>Вызывается из PlayCardView.OnClick через CardLayout.</summary>
        public void SelectCard(PlayCardView card)
        {
            if (_selectedCard != null && _selectedCard != card)
                _selectedCard.Deselect();
            _selectedCard = card;
        }

        public void DeselectAll()
        {
            if (_selectedCard != null)
                _selectedCard.Deselect();
            _selectedCard = null;
        }

        // ── Expand / Collapse ────────────────────────────────────────────────

        public void OnPointerClick(PointerEventData eventData) => Toggle();

        private void Toggle()
        {
            if (_isExpanded) Collapse();
            else Expand();
        }

        private void Expand()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            RefreshFan();
        }

        private void Collapse()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            DeselectAll();
            RefreshFan();
        }

        private void Update()
        {
            if (!_isExpanded) return;

            if (Input.GetMouseButtonDown(0) && !RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, Input.mousePosition, _uiCamera))
            {
                Collapse();
            }
        }

        // ── Fan Layout ───────────────────────────────────────────────────────

        private void RefreshFan()
        {
            var activeSlots = GetAllActiveSlots();
            int count = activeSlots.Count;

            if (count == 0) return;

            PlayCardView heldOnTop = null;   // зум/драг — переносим НАД всеми ПОСЛЕ обычного прохода, см. ниже

            for (int i = 0; i < count; i++)
            {
                var rt = (activeSlots[i] as MonoBehaviour)?.GetComponent<RectTransform>();
                if (rt == null) continue;

                // Карта «под пальцем» (зумится ИЛИ перетаскивается — CardBaseView.OnHoldTriggered /
                // PlayCardView.OnBeginDrag) ЗАМОРОЖЕНА целиком: ни позиция/поворот, ни место в иерархии
                // её не касаются, пока палец не отпущен. Раньше добор/уход ДРУГОЙ карты в этот момент
                // дрался с зум-твином/драгом на том же RectTransform, а на отпускании PlayCardView
                // восстанавливал застарелый (снятый ДО этого релэйаута) индекс/позицию — карта
                // проваливалась под соседей визуально «под всех» (баг 2026-08-04, зум; тот же класс
                // бага у драга). Теперь она просто пропускает проход; актуальную позу PlayCardView
                // допросит у CardLayout сам, когда палец отпущен (SnapCardIntoFan/RefreshFanNow).
                if (activeSlots[i] is PlayCardView held && (held.IsZoomed || held.IsDragging))
                {
                    heldOnTop = held;
                    continue;
                }

                // Порядок отрисовки (UGUI = порядок в иерархии): идём слева направо и каждую
                // делаем последней, поэтому каждая следующая карта рисуется ПОВЕРХ предыдущей →
                // самая правая (новейшая) оказывается сверху. Летящих это тоже касается: у новой карты
                // законное место — сверху, и пересчитать порядок дешевле, чем городить исключение.
                rt.SetAsLastSibling();

                // А вот ПОЗИЦИЕЙ летящей распоряжается дуга: второй твин на тот же localPosition
                // подрался бы с ней. Веер подхватит карту, когда та сядет.
                if (activeSlots[i] is SimpleCardView card && _flyingSlots.Contains(card)) continue;

                GetFanPose(i, count, out var pos, out float angle);

                rt.DOLocalMove(pos, _animDuration)
                  .SetEase(_isExpanded ? Ease.OutCubic : Ease.InCubic);
                rt.DOLocalRotate(new Vector3(0f, 0f, angle), _animDuration)
                  .SetEase(Ease.OutCubic);
            }

            // Карта под пальцем (зум/драг) — поверх всех соседей независимо от места в веере: пока её
            // держат/тащат, она «сейчас главная», порядок в руке тут ни при чём.
            if (heldOnTop != null)
            {
                var rt = heldOnTop.GetComponent<RectTransform>();
                if (rt != null) rt.SetAsLastSibling();
            }
        }

        // ── Zoom/drag hand-off (PlayCardView.CancelZoom/OnEndDrag) ──────────────
        // Пока карта под пальцем (держится/тащится), RefreshFan её не трогает (см. цикл выше) — поэтому
        // на выходе PlayCardView не может просто «вернуться туда, где стояла до этого»: рука могла
        // перестроиться (добор/уход другой карты меняют count → меняются offset/angle ВСЕХ слотов), и
        // такой снимок оказался бы устаревшим. Вместо восстановления снимка карта спрашивает у веера
        // её АКТУАЛЬНОЕ место.

        /// <summary>Обычный (анимированный) релэйаут «по требованию» — для отпускания зума БЕЗ старта
        /// драга: карта плавно уезжает из зумнутой позы туда, где ей сейчас стоять в веере.</summary>
        public void RefreshFanNow() => RefreshFan();

        /// <summary>Синхронно (без твина) поставить карту на её АКТУАЛЬНОЕ место в веере — момент, когда
        /// следующая же строка кода читает localPosition (старт драга сразу после выхода из зум-удержания:
        /// PlayCardView.CancelZoom(instant:true), твину к этому моменту ещё не доиграть бы). Порядок в
        /// иерархии довершает RefreshFan — SetAsLastSibling не анимируется, так что это тоже мгновенно.</summary>
        public void SnapCardIntoFan(PlayCardView card)
        {
            var active = GetAllActiveSlots();
            int idx = active.IndexOf(card);
            if (idx >= 0)
            {
                GetFanPose(idx, active.Count, out var pos, out float angle);
                var rt = card.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.localPosition = pos;
                    rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
            }
            RefreshFan();
        }

        // Поза карты в веере по её месту в раскладке. Вынесено из RefreshFan, потому что прилетающей карте
        // конечная точка нужна ДО того, как она полетит: дуга целится сразу в свой слот и в свой угол.
        private void GetFanPose(int index, int count, out Vector3 pos, out float angle)
        {
            // Командир — РАВНОПРАВНЫЙ участник веера (слот 0 в GetAllActiveSlots), угол/позиция считаются
            // по общим index/count без исключений. Формула сама даёт angle=0, когда слот ровно один
            // (t=0.5 → Lerp(half,-half,0.5)=0) — единственная карта, будь то командир один в руке или
            // одна карта руки при выведенном на стол командире, и так должна была выпрямляться. Если не
            // выпрямлялась — see FindHandSlot/RunRemoveStep: командир уходит на стол (HandTag снимается,
            // RunMoveCardToBoardSystem шлёт CardRemovedFromHandUIEvent), а этот эвент раньше НЕ находил
            // _commanderSlot (FindHandSlot искал только среди _handSlots) → слот оставался «занят»
            // призраком, count был на 1 больше настоящего, и «последняя карта» никогда не получала t=0.5.
            float halfAngle = _fanAngleRange * 0.5f;
            float baseY     = _isExpanded ? _expandedCardY : _collapsedCardY;
            float t         = count > 1 ? (float)index / (count - 1) : 0.5f;

            angle = Mathf.Lerp(halfAngle, -halfAngle, t);
            float xOffset = Mathf.Lerp(-_cardSpacing * (count - 1) * 0.5f,
                                        _cardSpacing * (count - 1) * 0.5f, t);
            float yOffset = baseY + (_isExpanded
                ? -_fanHeightCurve * Mathf.Abs(t - 0.5f) * 2f
                : 0f);

            pos = new Vector3(xOffset, yOffset, 0f);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SimpleCardView GetFreeHandSlot()
        {
            foreach (var slot in _handSlots)
                if (!slot.IsOccupied) return slot;
            return null;
        }

        private List<SimpleCardView> GetActiveHandSlots()
        {
            var result = new List<SimpleCardView>();
            foreach (var slot in _handSlots)
                if (slot.IsOccupied) result.Add(slot);
            return result;
        }

        private List<Component> GetAllActiveSlots()
        {
            var result = new List<Component>();
            if (_commanderSlot != null && _commanderSlot.IsOccupied)
                result.Add(_commanderSlot);
            // Порядок руки — по _handOrder (порядок добора), а не по индексу слота
            foreach (var slot in _handOrder)
                if (slot != null && slot.IsOccupied) result.Add(slot);
            return result;
        }
    }
}
