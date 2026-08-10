using Game.Core.Service;
using Game.Core.Shared;

namespace Game.Core.Events
{
    // в”Ђв”Ђ Turn events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public struct TurnStartedEvent : IGameEvent
    {
        public int ActivePlayerId;
        public int TurnNumber;
    }

    public struct TurnEndedEvent : IGameEvent
    {
        public int ActivePlayerId;
    }

    // Публикует TurnTimerSystem на КАЖДОЕ изменение своего busy-гейта (анимации/резолв способностей блокируют
    // тикание). UI прячет Root таймера, пока IsBusy=true — так пауза видна ГЛАЗАМИ (не только по тому, что
    // секунды не уменьшаются, что легко не заметить). Единый источник правды (не путать с разрозненными
    // InputBlockedEvent/InputRestoredEvent — те шлются из нескольких систем сами по себе и не гарантируют
    // ровно ОДНО Restored на каждый Blocked).
    public struct TurnTimerBusyUIEvent : IGameEvent { public bool IsBusy; }

    public struct InputBlockedEvent : IGameEvent { }
    public struct InputRestoredEvent : IGameEvent { }

    public struct CardPlayedEvent : IGameEvent
    {
        public string CardName;
        public int CardEntity;
        public int PlayerId;
        public int TargetCell;   // board cell index, -1 if none
        public int TargetEntity; // entity target, -1 if none
    }

    /// <summary>Карту руки отвели в драг (синяя подсветка «готова к розыгрышу», см. PlayCardView.
    /// StartRealDrag) — Active=true просит подсветить на поле всех, кого заденет её OnCast-способность;
    /// Active=false (отпустили/вернули/розыграли) снимает эту подсветку. Слушает RunCardTargetPreviewSystem.</summary>
    public struct CardDragTargetPreviewEvent : IGameEvent
    {
        public int  CardEntity;
        public bool Active;
    }

    public struct CardDrawnEvent : IGameEvent
    {
        public int CardEntity;
        public int PlayerId;

        /// <summary>Сущность-источник (кастер раскопки/копатель) — если карта пришла НЕ обычным добором.
        /// Резолвится в мировую позицию ЛЕНИВО в HandUISystem: источник на момент трансляции обычно ещё
        /// на столе (сам не уходит никуда). Nullable (а не -1 сентинел): подавляющее большинство мест,
        /// публикующих это событие через object-initializer, поле вообще не трогают — default(int)=0 был бы
        /// ВАЛИДНОЙ сущностью (entity 0) и подсовывал бы источник туда, где его нет. null у Nullable<T> —
        /// честный «не задано» без риска зацепить чужую сущность.</summary>
        public int? SourceEntity;

        /// <summary>Мировая позиция, если её нужно снять СРАЗУ здесь: сама карта покидает поле (баунс/
        /// смерть командира) — к моменту, когда HandUISystem дойдёт до трансляции, её BoardPositionComponent
        /// уже снят, лениво через SourceEntity её не найти. null → пробуем SourceEntity, иначе обычная
        /// анимация добора «из-за края экрана».</summary>
        public UnityEngine.Vector3? FromWorld;
    }

    /// <summary>Локальный (turn-start) добор Count карт игроком-сущностью PlayerEntity. Шлёт DrawCardSystem
    /// только для Sync-доборов → CollectActionSystem отправляет ActionDrawData оппоненту (синк зеркала колоды/руки).</summary>
    public struct DeckDrawNetEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Count;
        /// <summary>Конкретные добранные сущности (локальные). Коллектор шлёт их ключи в ActionDrawData,
        /// пассив переносит в руку ИМЕННО эти карты по ключу (а не «верхние N» — иначе дрейф зеркала руки).</summary>
        public int[] DrawnEntities;
    }

    /// <summary>Полиморф (TransformEffect → RunTransformSystem): МУТИРОВАТЬ существо TargetEntity в карту
    /// ExpansionId/CardId НА МЕСТЕ — та же сущность/клетка/NetworkEntityKey, меняются модель/статы/кост/
    /// способности/архетипы/вьюха, баффы сгорают. NewOwnerId >= 0 → передать владение (Чертовщина: «чёрт под
    /// вашим контролем»); -1 → владелец не меняется (классический полиморф). СИНК: эффект публикует на ОБОИХ
    /// клиентах (ре-ран резолва), идентичность фиксирована ассетом → мутация зеркальна без спец-канала.</summary>
    public struct TransformCardEvent : IGameEvent
    {
        public int    TargetEntity;
        public string ExpansionId;
        public int    CardId;
        public int    NewOwnerId;
    }

    public struct CardDiedEvent : IGameEvent
    {
        public int CardEntity;
        public int PlayerId;
    }

    /// <summary>Тех-кнопка: выдать локальному игроку максимум маны и золота. Обрабатывает DebugCheatSystem.</summary>
    public struct DebugFillResourcesEvent : IGameEvent { }

    /// <summary>
    /// Замена добора (Адовый червь) разрешена активным игроком: выбрана карта из предложенных.
    /// CollectActionSystem шлёт это пассиву как ActionDrawReplacementData (offered/chosen — по ключам),
    /// пассив повторяет результат у оппонента. Публикует RunDrawReplacementSystem при резолве.
    /// </summary>
    public struct DrawReplacementResolvedNetEvent : IGameEvent
    {
        public int PlayerEntity;
        public int[] Offered;
        public int Chosen;
        public bool DestroyChosen;
    }

    /// <summary>ЛИПКИЙ хинт выбора цели для UI: Show=true при входе в режим выбора цели по доске (висит,
    /// пока выбираешь), Show=false при выборе/отмене. Публикует RunAbilityTargetSelectionSystem; показывает
    /// TargetHintView. Text — что показать («Выберите цель»).</summary>
    public struct TargetSelectionHintEvent : IGameEvent
    {
        public bool Show;
        public string Text;
    }
    // в”Ђв”Ђ Hand UI events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// РР·РјРµРЅРёР»Р°СЃСЊ РґРѕСЃС‚СѓРїРЅРѕСЃС‚СЊ РєР°СЂС‚С‹ РґР»СЏ СЂРѕР·С‹РіСЂС‹С€Р° (С…РІР°С‚Р°РµС‚/РЅРµ С…РІР°С‚Р°РµС‚ СЂРµСЃСѓСЂСЃРѕРІ).
    /// UI РІРєР»СЋС‡Р°РµС‚/РІС‹РєР»СЋС‡Р°РµС‚ С…Р°Р№Р»Р°Р№С‚РµСЂ "РјРѕР¶РЅРѕ СЂР°Р·С‹РіСЂР°С‚СЊ".
    /// </summary>
    public struct CardAffordableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsAffordable;
    }

    public struct CardAbilityReadyChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsReady;
    }

    /// <summary>UI: эффективная стоимость карты изменилась (модификатор стоимости — Гиперинфляция).
    /// Шлёт CardAffordabilitySystem; ловит PlayCardView → обновляет число стоимости и иконку оплаты.</summary>
    public struct CardCostChangedEvent : IGameEvent
    {
        public int CardEntity;
        public int EffectiveCost;
        /// <summary>Вид АЛЬТЕРНАТИВНОЙ уплаты владельца (AltCostKind, семейство «Бесчестный букмекер»):
        /// -1 = обычная оплата ресурсом (иконка по типу коста); >=0 = иконка уплаты из InterfaceConfig.</summary>
        public int AltCostKind;
    }

    /// <summary>Живые статы карты-СУЩЕСТВА в руке ЛОКАЛЬНОГО игрока изменились (бафф/дебафф/урон командиру) —
    /// PlayCardView красит статы относительно базы и панчит текст (см. CardBaseView.SetLiveStats).
    /// Публикует HandCardStatsViewSystem диффом (не каждый кадр). Initial=true — карта впервые увидена в руке
    /// (первичная отрисовка: цвет применить, панч не играть).</summary>
    public struct HandCardStatsChangedUIEvent : IGameEvent
    {
        public int CardEntity;
        public int Attack;
        public int MaxHealth;
        public int CurrentHealth;
        /// <summary>Скорость (Max). Добавлена 2026-07-30 под ауры, баффающие скорость карт В РУКЕ
        /// («Шальной десница»: +1 атаки и +1 СКОРОСТИ) — раньше канал вёз только атаку и HP,
        /// и прибавка скорости на карточке руки не показывалась.</summary>
        public int Speed;
        public bool Initial;
    }

    /// <summary>Глобальный модификатор стоимости карт изменился (AddCostModifierEffect). Сигнал для
    /// CardAffordabilitySystem пересчитать affordability + эффективную стоимость карт руки.</summary>
    public struct CostModifierChangedEvent : IGameEvent { }

    /// <summary>
    /// Публикуется PlayCardView когда карта физически отображена в руке (SetCard вызван).
    /// CardAffordabilitySystem реагирует и публикует актуальное состояние доступности.
    /// </summary>
    public struct CardPlacedInHandViewEvent : IGameEvent
    {
        public int CardEntity;
    }

    public struct CardAddedToHandUIEvent : IGameEvent
    {
        public int    CardEntity;
        public int    PlayerId;
        public string NetworkKey;
        public UnityEngine.Sprite Icon;
        public Game.Core.Service.EnumService.CardType CardType;
        public Game.Core.Service.EnumService.Element  Element;
        public Game.Core.Service.EnumService.Rarity   Rarity;
        public string CardName;
        public bool   IsCommander;
        public Game.Core.Shared.CardVisualData Visual;

        /// <summary>Экранная точка «источника» карты (раскопка/баунс/возврат командира) — HandUISystem уже
        /// спроецировал мировую позицию через боевую камеру (CardDrawnEvent.FromWorld/SourceEntity). ЭКРАННАЯ,
        /// а не мировая: UI-сборка НЕ референсит Mono/ECS (граница только через шину) — проекцию камерой
        /// обязан сделать ECS-слой, UI здесь остаётся чистым RectTransformUtility.ScreenPointToLocalPoint.
        /// null → обычный добор, CardLayout летит со своей дефолтной точки «из-за края экрана».</summary>
        public UnityEngine.Vector2? FromScreen;
    }
    public struct CardRemovedFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
    }
     
    public struct CardDiscardFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
        public string CardName;
        public UnityEngine.Sprite Icon;
        public Game.Core.Shared.CardVisualData Visual;
    }

    /// <summary>
    /// На карту, лежащую В РУКЕ, применили эффект (Дупликатор скопировал её, удешевление, бафф и т.п.).
    /// Чистая косметика: PlayCardView фильтрует по CardEntity и играет punch визуала карты + UI-VFX по Kind
    /// (даёт понять, к КАКОЙ карте применился эффект). Публикуется через CardFeedbackUtil.MarkAffectedInHand —
    /// только если карта действительно в руке (HandTag). Синк не нужен: эффекты применяются зеркально на
    /// обоих клиентах, фидбэк проигрывается локально у владельца соответствующей вьюхи.
    /// </summary>
    public struct CardAffectedInHandUIEvent : IGameEvent
    {
        public int CardEntity;
        public CardAffectKind Kind;
    }

    /// <summary>
    /// У карты-с-уровнями сменился уровень (или первый показ). Публикует CardTierSystem. UI (PlayCardView)
    /// фильтрует по CardEntity: показывает баннер уровня (Tier+1) и обновляет текст описания на уже
    /// перерендеренный под активный уровень (ключ card.{exp}.{id}.tier.{N}.desc). Tier — 0-based индекс.
    /// </summary>
    public struct CardTierChangedUIEvent : IGameEvent
    {
        public int CardEntity;
        public int Tier;
        public string Description;   // готовое описание активного уровня (отформатировано)
    }

    /// <summary>
    /// Живой перерендер описания карты БЕЗ смены уровня/баннера — сейчас только у чар в руке под
    /// матчевый бонус длительности (Зачарованный/CharmDurationBonusService, см. CharmHandDurationPreviewSystem):
    /// печатное «Действует N ходов» превращается в живое «N+бонус», пока карта ещё в руке (реальный
    /// TurnsRemaining применяется отдельно, в момент розыгрыша — RunMoveCardToBoardSystem). UI (PlayCardView)
    /// фильтрует по CardEntity и просто ставит текст (SetDescriptionLive), без баннера.
    /// </summary>
    public struct CardDescriptionChangedUIEvent : IGameEvent
    {
        public int CardEntity;
        public string Description;
    }

    /// <summary>Токен с OnDraw-форс-кастом (Вонючее облако) авто-разыгран сразу при доборе. Чистая косметика
    /// у владельца: CardLayout показывает пришедшую карту и проигрывает анимацию сброса («пришла → ушла»).
    /// Сама симуляция розыгрыша идёт отдельно (PlayCardUtil.Play). CardLayout берёт на себя удаление слота,
    /// поэтому штатное CardRemovedFromHandUIEvent для этой карты игнорируется.</summary>
    public struct CardForcePlayedFromDrawUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    /// <summary>
    /// Карта уничтожена ПРЯМО В КОЛОДЕ (Mill) → кладбище. Показ разведён по владельцу:
    /// СВОЯ карта (IsLocalOwner) — CardLayout выводит её полноценной картой по дуге с зависанием
    /// («у вас уничтожили ЭТО»), чужая — компактный поп-ап MillNotificationView («вы сожгли ЭТО»).
    /// </summary>
    public struct CardMillFromDeckUIEvent : IGameEvent
    {
        public int CardEntity;
        public string CardName;
        public UnityEngine.Sprite Icon;
        public Game.Core.Shared.CardVisualData Visual;
        public bool IsLocalOwner;   // колода ЛОКАЛЬНОГО игрока → крупный показ вместо поп-апа
    }

    /// <summary>
    /// Карта разыграна ЭФФЕКТОМ прямо ИЗ КОЛОДЫ (PlayCardUtil: Гомункул/Барабук и т.п.) — владелец её
    /// в руке не видел. CardLayout показывает той же дугой с зависанием, что и уничтожение из колоды,
    /// но с другим видом развилки VFX (см. CardLayout.PlayShowcaseFx) — «разыграна», а не «сожжена».
    /// Публикуется симулятором (актив); у пассива в MP зеркало едет реплеем со своим поп-апом.
    /// </summary>
    public struct CardPlayedFromDeckUIEvent : IGameEvent
    {
        public int CardEntity;
        public string CardName;
        public UnityEngine.Sprite Icon;
        public Game.Core.Shared.CardVisualData Visual;
        public bool IsLocalOwner;
    }
     
    public struct CommanderOnCooldownUIEvent : IGameEvent
    {
        public int CardEntity;
        public int CooldownTurns;
    }
     
    public struct CommanderCooldownExpiredUIEvent : IGameEvent
    {
        public int CardEntity;
    }
     public struct CreatureMovedEvent : IGameEvent
     {
         public int CreatureEntity;
         public int FromRow;
         public int FromCol;
         public int FromOwnerId;
         public int ToRow;
         public int ToCol;
         public int ToOwnerId;
     }

    public struct CreatureAttackedEvent : IGameEvent
    {
        public int AttackerEntity;
        public int DefenderEntity;
    }

    public struct CreatureDamagedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int Amount;
    }

    /// <summary>ПОСТОЯННЫЙ визуал свойства/статуса (Key: "Shielded"/"Taunt"/"Poisoned"/...) появился/пропал
    /// на существе — РЕАКТИВНЫЙ сигнал для PropertyAuraVisualSystem, чтобы не пушить состояние покадрово
    /// по всем существам. Publish-точки: Apply/Remove соответствующего ICreatureProperty (грант/аура-ревёрт),
    /// TakeDamageSystem (щит спалил заряды в ноль, яд навешен), DieSystem (смерть — служебная зачистка).
    /// Active=false публикуется ТОЛЬКО если статус реально был — не спамим «выключился», когда и так не горел.</summary>
    public struct CreaturePropertyAuraChangedEvent : IGameEvent
    {
        public int CreatureEntity;
        public string Key;
        public bool Active;
    }

    /// <summary>Здоровье существа изменилось НЕ уроном (дебафф/бафф статов, снятие/навешивание HP-модификатора).
    /// Публикуют точки, меняющие HealthComponent модификаторами (BuffStatsEffect, BuffPerCharmSystem…).
    /// LethalHealthSystem реагирует: если Current ≤ 0 → смерть (урон свою смерть разруливает в TakeDamageSystem).</summary>
    public struct CreatureHealthChangedEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    public struct AbilityActivatedEvent : IGameEvent
    {
        public int SourceEntity;
        public int AbilityIndex;
    }

    public struct AbilityCascadeStartedEvent : IGameEvent { }
    public struct AbilityCascadeEndedEvent : IGameEvent { }

    public struct ResourceChangedEvent : IGameEvent
    {
        public bool isLocalPlayer;
        public EnumService.ResourceType Type;
        public int NewValue;
        public int MaxValue;
    }

    /// <summary>Счётчик руки/колоды ЛОКАЛЬНОГО игрока — для ResourceIndicatorView (та же панель, что
    /// золото/мана/здоровье; см. PlayerStatsViewSystem). Не переиспользует ResourceChangedEvent/ResourceType
    /// намеренно — ResourceType означает «трачу на каст», а рука/колода не тратятся, это просто счётчик.
    /// HandMax — HandComponent.MaxHandSize (6); DeckMax=0 — у колоды нет фиксированного максимума (не рисуем
    /// слайдер, ResourceIndicatorView просто покажет число).</summary>
    public struct HandDeckCountChangedUIEvent : IGameEvent
    {
        public int HandCount;
        public int HandMax;
        public int DeckCount;
    }

    /// <summary>Активные чары ЛОКАЛЬНОГО игрока изменились (появилась/исчезла/тикнул таймер) — статус-бар
    /// аур (см. PlayerStatsViewSystem). Оппоненту те же данные кладутся НАПРЯМУЮ в AvatarPlayerView.SetAuras
    /// (как HP/золото/мана), без этого события — оно только для локального. TurnsRemaining[i] соответствует
    /// Visuals[i]; -1 = чара постоянная (без CharmTimerComponent).</summary>
    public struct AuraStatusChangedUIEvent : IGameEvent
    {
        public Game.Core.Shared.CardVisualData[] Visuals;
        public int[] TurnsRemaining;
        /// <summary>Размер стака одноимённых чар (слот показывает ×N при N>1); параллелен Visuals.</summary>
        public int[] StackCounts;
    }

    public struct CellSelectedEvent : IGameEvent
    {
        public int Row;
        public int Col;
        public int OwnerId;
    }

    /// <summary>Удержание пальца на существе на столе (CreatureView) ≥ порога — просьба показать карточку
    /// существа. Show=false — палец отпущен, скрыть. Row/Col/OwnerId — та же конвенция клетки, что у
    /// CellSelectedEvent; CreatureInspectSystem резолвит существо на этой клетке и строит CardVisualData.</summary>
    public struct CreatureHoldUIEvent : IGameEvent
    {
        public int Row;
        public int Col;
        public int OwnerId;
        public bool Show;
    }

    /// <summary>Показ/скрытие карточки-инспектора ЛЮБОЙ карты (существо на столе — CreatureInspectSystem;
    /// миниатюра истории розыгрышей — PlayHistoryDrawerView вызывает CardDetailPopupView напрямую, без
    /// события, тип карты не важен).</summary>
    public struct CardDetailUIEvent : IGameEvent
    {
        public Game.Core.Shared.CardVisualData Visual;
        public bool Show;
    }

    public struct CreatureSelectedEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    public struct CreatureDeselectedEvent : IGameEvent { }

    // ── Драг-розыгрыш существа «под пальцем» (UI ↔ CreatureDragPreviewSystem) ──
    // UI шлёт сырой ввод драга (экранная позиция), система делает всё Mono-осознанное:
    // рейкаст в доску, превью-модель под пальцем, подсветку клетки, коммит/отмену.

    /// <summary>UI → система: палец с картой двигается (каждый кадр драга). Для не-существ система игнорит.</summary>
    public struct CreatureDragMovedEvent : IGameEvent
    {
        public int CardEntity;
        public UnityEngine.Vector2 ScreenPosition;
    }

    /// <summary>UI → система: палец отпущен. Система коммитит размещение (валидная клетка) или шлёт
    /// TargetSelectionCancelledEvent (UI вернёт карту в руку штатным путём).</summary>
    public struct CreatureDragReleasedEvent : IGameEvent
    {
        public int CardEntity;
        public UnityEngine.Vector2 ScreenPosition;
    }

    /// <summary>Система → UI: карта над полем (true → карта растворяется, превью существа видно) /
    /// ушла с поля (false → карта проявляется обратно).</summary>
    public struct CreatureDragOverFieldChangedEvent : IGameEvent
    {
        public int CardEntity;
        public bool OverField;
    }

    /// <summary>Система → UI (один раз на драг): карта — СУЩЕСТВО, размещается только дропом на поле.
    /// UI на отпускании вне поля возвращает её в руку, НЕ уходя в старый клик-путь (OnUse →
    /// PendingSelectCell). Без события (нет системы/борда) UI работает по-старому — graceful fallback.</summary>
    public struct CreatureDragStartedEvent : IGameEvent
    {
        public int CardEntity;
    }

    public struct MulliganStartedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int[] OfferedCardEntities;
        public CardVisualData[] OfferedCardVisuals;
        public int MaxReplacements;
    }

    public struct MulliganCardReplacedEvent : IGameEvent
    { 
        public int[] OldCardEntity;
        public int[] NewCardEntity;
        public CardVisualData[] NewCardVisual;
    }

    public struct MulliganCompletedEvent : IGameEvent
    {
        public int PlayerEntity;
    }

    public struct AllMulligansCompletedEvent : IGameEvent { }

    /// <summary>
    /// Публикуется когда сервер даёт команду инициализировать игровое состояние.
    /// BattleState подписывается на это событие и только тогда инициализирует ECS
    /// и отправляет RPC_NotifyStateReady.
    /// </summary>
    public struct TriggerStateInitEvent : IGameEvent { }

    /// <summary>
    /// Публикуется когда хост начинает PreStart фазу (до начала первого хода).
    /// UI закрывает мулиган и показывает руку с анимацией раздачи карт.
    /// Содержит готовые данные карт руки чтобы UI не лазил в ECS.
    /// </summary>
    public struct PreStartPhaseBeginUIEvent : IGameEvent
    {
        public CardAddedToHandUIEvent[] HandCards;
        public CardAddedToHandUIEvent   CommanderCard;
        public bool HasCommander;
    }

    public struct MulliganReplaceRequestedUIEvent : IGameEvent
    { 
        public int CardEntity;
    }
    
    public struct LocalTurnStartedEvent : IGameEvent
    {
        public int TurnNumber; 
        public float TurnDurationSeconds;
    }
     
    public struct OpponentTurnEndedEvent : IGameEvent { } 
    /// <summary>«Кто-то разыграл карту» — несмотря на имя (историческое, оставлено намеренно — см. память
    /// project_build_debug_map.md, «Единый фид розыгрышей»), публикуется теперь на ЛЮБОЙ каст (свой/чужой/
    /// авто-сгенерированный), не только оппонента — см. IsLocalPlayer.</summary>
    public struct OpponentCardPlayedUIEvent : IGameEvent
    {
        public string CardName;
        public UnityEngine.Sprite Icon;
        /// <summary>Полные визуальные данные карты — выкат рендерит её как настоящую карту (арт/имя/стоимость/
        /// статы/описание) через CardBaseView. CardName/Icon оставлены как fallback.</summary>
        public Game.Core.Shared.CardVisualData Visual;
        /// <summary>true — разыграл ЛОКАЛЬНЫЙ игрок (свой каст); false — оппонент/чужая реплеенная карта.</summary>
        public bool IsLocalPlayer;
    }
    public struct PlayerAssignedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Side;         // 1 РёР»Рё 2
        public bool IsLocalPlayer;
        public int PlayerId;     // PlayerComponent.PlayerId владельца (== OwnerId карт) — фильтр «свой/чужой» в трекерах задач
    }

    /// <summary>Активный игрок завершил ход — коллектор шлёт ActionEndTurnData оппоненту.</summary>
    public struct EndTurnNetEvent : IGameEvent { }

    /// <summary>Итог матча с точки зрения локального игрока.</summary>
    public enum MatchResult { Win, Lose, Draw }

    /// <summary>
    /// Матч завершён: у одного/нескольких игроков HP ≤ 0 после оседания каскада действий.
    /// Публикует GameOverCheckSystem. UI показывает поп-ап результата; турн-системы встают.
    /// </summary>
    public struct MatchEndedEvent : IGameEvent
    {
        public MatchResult LocalResult;
        public bool        IsDraw;
        public int[]       WinnerPlayerIds;
        public int[]       LoserPlayerIds;
    }

    /// <summary>Игрок нажал «выход в меню» в поп-апе результата. Хук для навигации/teardown боя
    /// (корректный дисконнект Photon + смена состояния) — обрабатывается на уровне States.</summary>
    public struct ExitToMenuRequestedEvent : IGameEvent { }

    /// <summary>
    /// Сервер рассчитал рейтинг за матч (Elo по взаимному подтверждению — MatchReportService).
    /// MatchResultWindow показывает дельту на шаге рейтинга; MatchRewardsWindow — золото за матч.
    /// </summary>
    public struct RatingUpdatedEvent : IGameEvent
    {
        public int NewMmr;
        public int Delta;
        public string RewardCode;    // валюта награды за матч ("GD"); пусто → награды не было
        public int    RewardAmount;
    }

    /// <summary>
    /// Прогресс задач, накопленный ЗА ЭТОТ матч, отправлен на сервер (флеш TaskTrackingService).
    /// Параллельные массивы type→amount (пустые — прогресса не было). MatchRewardsWindow фильтрует
    /// по ним задачи из DailyService.GetState для секции «задачи за матч».
    /// </summary>
    public struct MatchTasksFlushedUIEvent : IGameEvent
    {
        public string[] Types;
        public int[]    Amounts;
    }

    // ── Сетевое здоровье MP (NetConnectionWatchSystem / TurnChecksumSystem) ─────────────────

    /// <summary>Вид проблемы соединения в MP-бою.</summary>
    public enum ConnectionIssueKind
    {
        OpponentSilent = 0,   // сигналы от пира перестали приходить (heartbeat/действия), соединение формально живо
        OpponentLeft   = 1,   // Fusion зафиксировал выход оппонента из сессии
        ConnectionLost = 2,   // потеряно СВОЁ соединение с сервером
    }

    /// <summary>
    /// Проблема соединения: UI показывает баннер «ожидание соперника / соединение потеряно» с обратным
    /// отсчётом. Публикуется NetConnectionWatchSystem раз в секунду, пока проблема активна. По истечении
    /// SecondsLeft матч закрывается техническим результатом (MatchEndedEvent).
    /// </summary>
    public struct ConnectionIssueUIEvent : IGameEvent
    {
        public ConnectionIssueKind Kind;
        public int SecondsLeft;
    }

    /// <summary>Проблема соединения ушла (пир снова шлёт сигналы) — UI прячет баннер ожидания.</summary>
    public struct ConnectionRestoredUIEvent : IGameEvent { }

    /// <summary>
    /// ДЕСИНК: чексуммы состояния мира на границе хода BoundaryTurn разошлись между клиентами
    /// (TurnChecksumSystem). Триггер self-heal ресинка (WorldResyncSystem): пассив запрашивает у актива
    /// полный снэпшот мира и применяет его под затемнением.
    /// </summary>
    public struct NetDesyncDetectedEvent : IGameEvent
    {
        public int   BoundaryTurn;
        public ulong LocalHash;
        public ulong RemoteHash;
    }

    /// <summary>
    /// Затемнение экрана на время ресинка (как в HS при восстановлении соединения): Show=true — фейд в
    /// чёрное с надписью «Синхронизация…» (под ним пересобирается мир, чтобы не было дёрганых телепортов),
    /// Show=false — мир пересобран, плавное проявление. Публикует WorldResyncSystem, слушает ResyncFadeView.
    /// </summary>
    public struct WorldResyncUIEvent : IGameEvent
    {
        public bool Show;
    }

    /// <summary>UI/дев-запрос «завершить ход» — система навесит EndTurnRequestEvent на локального активного.</summary>
    public struct RequestEndTurnUIEvent : IGameEvent { }

    /// <summary>Запрос старта PvE-боя (дев-кнопка оверлея). Обрабатывает MenuState.StartPveBattle:
    /// чисто гасит активный матчмейкинг, ставит PveMode и грузит BattleScene.
    /// EncounterPath — путь энкаунтера в Resources (null/пусто = текущий/последний).</summary>
    public struct PveStartRequestedEvent : IGameEvent { public string EncounterPath; }

    /// <summary>Туториал: показать подсказку шага (TutorialHintView). Публикует TutorialDirectorSystem
    /// на каждом переходе шага. Ключ локализации + фолбэк-текст.</summary>
    public struct TutorialHintUIEvent : IGameEvent
    {
        public string TextKey;
        public string FallbackText;
        /// <summary>ИНФО-шаг: игрок просто читает и жмёт «Далее» (вью показывает кнопку и шлёт
        /// TutorialContinueEvent). false — шаг-действие: ждём событие боя, кнопки нет.</summary>
        public bool NeedsContinue;
    }

    /// <summary>Игрок нажал «Далее» на инфо-шаге туториала (TutorialHintView → TutorialDirectorSystem).</summary>
    public struct TutorialContinueEvent : IGameEvent { }

    /// <summary>Подсветить цель туториала: оверлей TutorialHighlightView вырезает дырку в затемнении вокруг
    /// якоря (TutorialAnchor с этим id). Anchor=None или Show=false — подсветка снимается.</summary>
    public struct TutorialHighlightUIEvent : IGameEvent
    {
        public TutorialAnchorId Anchor;
        public bool Show;
        /// <summary>ИНФО-шаг: блокируем ВЕСЬ ввод (включая дырку) — игрок только читает и жмёт «Далее».
        /// Иначе он сыграет наперёд, а шаг-действие потом будет ждать того, что уже нечем сделать (софт-лок).
        /// false — шаг-действие: сквозь дырку кликать МОЖНО, всё остальное перекрыто.</summary>
        public bool BlockAll;
    }

    /// <summary>Стори: выданы карты-награды за ПЕРВОЕ прохождение энкаунтера. Публикует BattleState
    /// (PvE, после победы); RewardToastView показывает очередь «получена карта» (иконка+имя+кол-во).</summary>
    public struct RewardCardsGrantedUIEvent : IGameEvent
    {
        public Game.Core.Shared.CardVisualData Visual;   // визуал полученной карты
        public int Count;                                // сколько копий
    }

    /// <summary>Стори: реплика «говорящей головы» (портрет + локализованный текст). Публикует BattleState
    /// (PvE: интро после мулигана, реплики победы/поражения — из PveEncounterConfig.*Lines);
    /// показывает StoryDialogueView очередью. Ключи локализации резолвит вью (CardTextLocalization).</summary>
    public struct StoryLineUIEvent : IGameEvent
    {
        public UnityEngine.Sprite Portrait;
        public string SpeakerKey;     // ключ имени говорящего (пусто = без имени)
        public string TextKey;        // ключ реплики
        public string FallbackText;   // текст, если ключа нет в таблице
        public float Duration;        // сек на экране
    }

    public struct DeckSyncedEvent : IGameEvent
    {
        public int PlayerEntity;
    }

    public struct DeckReadyToSyncEvent : IGameEvent
    {
        public int      PlayerEntity;
        public int      PlayerId;
        public string[] DeckExpansionIds;
        public int[]    DeckCardIds;
        public string[] DeckNetworkKeys;
        public string[] HandExpansionIds;
        public int[]    HandCardIds;
        public string[] HandNetworkKeys;
    }
     
    public struct AbilityReadyEvent : IGameEvent
    { 
        public int AbilityEntity; 
        public int CardEntity;
    }
     
    public struct AbilityNotReadyEvent : IGameEvent
    {
        public int AbilityEntity;
        public int CardEntity;
    }
     
    public struct CardPlayableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsPlayable;
    }
     
    public struct CreateCardEvent : IGameEvent
    {
        public string ExpansionId;

        public int CardId;

        public string NetworkEntityKey;

        public int PlayerOwnerEntity;

        public int OwnerId;

        public bool IsEnemy;

        public bool IsCommander;

        public bool InHand;

        /// <summary>Если true — карта создаётся в «отложенных» (сайдборд Сказочника), а не в колоде/руке.
        /// Используется зеркалом MP: сущности сайдборда оппонента нужны, чтобы разрешались ключи, когда
        /// владелец достанет карту раскопкой.</summary>
        public bool InSideboard;

        /// <summary>Если true — сразу разместить на доске (см. BoardRow/BoardCol/BoardOwnerId).</summary>
        public bool InBoard;
        public int BoardRow;
        public int BoardCol;
        public int BoardOwnerId;

        /// <summary>Если true — сразу отправить на кладбище (для призыва без места).</summary>
        public bool InGrave;

        /// <summary>Если true — после создания добавить сущность в HandComponent/DeckComponent владельца
        /// (PlayerOwnerEntity) и для InHand опубликовать CardDrawnEvent (для локального владельца).
        /// Нужно эффектам-генераторам: обычный CreateCardEvent ставит только зональный тег, но списки
        /// зон не правит. Старые вызовы (мулиган-синк/деки) флаг не ставят → поведение не меняется.</summary>
        public bool RegisterInZoneList;

        /// <summary>Если true — созданную карту разыграть автоматически (Фокус-покус): CreateCardSystem вешает
        /// AutoCastComponent, AutoCastSystem форс-кастит её (Free) у активного. См. также ForceRandomTarget —
        /// нужен ли ЭТОЙ карте ещё и авто-выбор цели (не всегда одно и то же).</summary>
        public bool AutoCast;

        /// <summary>Форсить ForceRandomTargetingComponent (Selected трактуется как Random, без окна выбора у
        /// игрока) — НЕЗАВИСИМО от AutoCast: генератор (напр. Фокус-покус) может резолвиться от OnCast (сам
        /// источник разыгрывается игроком интерактивно → цель порождённой карты тоже вправе выбрать игрок) или
        /// от иного триггера (не в интерактивном контексте → цель форсится случайно). См. GenerateCardEffect.Spawn.</summary>
        public bool ForceRandomTarget;

        /// <summary>Сущность-источник (кастер) — пробрасывается в CardDrawnEvent.SourceEntity для InHand-создания
        /// (раскопка из пула: карта ещё не существует на момент Apply, поэтому источник передаётся здесь, а не
        /// через ленивый лукап SourceEntity в HandUISystem). null → обычная анимация добора «из-за края экрана».</summary>
        public int? SourceEntity;
    }
    // ── Network turn coordination events ────────────────────────────────────

    /// <summary>Активный игрок запрашивает конец хода → летит на хост через RPC.</summary>
    public struct TurnEndRequestedNetEvent : IGameEvent
    {
        public int RequestingPlayerId;
    }

    /// <summary>Этот клиент завершил отработку TurnEnd-способностей → хост ждёт обоих.</summary>
    public struct TurnEndReadyNetEvent : IGameEvent
    {
        public int PlayerId;
    }

    /// <summary>Этот клиент завершил отработку TurnStart-способностей → хост ждёт обоих.</summary>
    public struct TurnStartReadyNetEvent : IGameEvent
    {
        public int PlayerId;
    }

    // ── Target selection events ──────────────────────────────────────────────

    /// <summary>
    /// Публикуется когда игрок отменил выбор цели (кликнул по невалидной клетке).
    /// UI убирает подсветку, карта остаётся в руке.
    /// </summary>
    public struct TargetSelectionCancelledEvent : IGameEvent
    {
        public int CardEntity;
    }

    // ── Card play request (from UI) ──────────────────────────────────────────

    /// <summary>
    /// Публикуется из UI когда игрок кликает по карте в руке.
    /// CardInputSystem читает это событие и либо сразу создаёт CastEvent (карты без цели),
    /// либо добавляет PendingTargetCardComponent на игрока (карты с целью).
    /// </summary>
    public struct CardPlayRequestedEvent : IGameEvent
    {
        public int CardEntity;
    }

    // ── Card play network sync ────────────────────────────────────────────────

    /// <summary>
    /// Публикуется после каста карты для сетевой синхронизации с оппонентом.
    /// </summary>
    public struct CardCastNetSyncEvent : IGameEvent
    {
        public string CardNetworkKey;
        public string TargetNetworkKey; // пусто если цель не entity
        public int    TargetCellIndex;  // -1 если цель не клетка
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCardCast.
    /// RemoteCastSystem берёт этот ивент и создаёт CastEvent по ключам.
    /// </summary>
    public struct RemoteCardCastEvent : IGameEvent
    {
        public string CardEntityKey;
        public string TargetEntityKey; // "" если цели нет
        public int    TargetCell;      // -1 если цель не клетка
    }

    // ── Card pick (discover) events ─────────────────────────────────────────

    /// <summary>
    /// Публикуется когда игроку нужно выбрать карту из предложенных (раскопка).
    /// UI подписывается и показывает панель выбора.
    ///
    /// Публиковать МОЖНО только получив слот у CardPickBrokerSystem (см. PickTicket.Ready): окно одно,
    /// каналов несколько, и без арбитра они перебивали друг друга в одном кадре.
    /// </summary>
    public struct CardPickOfferedEvent : IGameEvent
    {
        /// <summary>
        /// Токен запроса (PickRequestId). Окно возвращает его эхом в Chosen/Cancelled — ИМЕННО по нему
        /// продюсер узнаёт свой выбор.
        ///
        /// Раньше корреляция шла по CastingCardEntity, и это был сломанный контракт: для трёх каналов это
        /// entity КАРТЫ, а для замены добора — entity ИГРОКА, при том что id живут в одном пространстве ECS
        /// и событие уходит на общую шину всем каналам сразу.
        /// </summary>
        public int RequestId;

        /// <summary>Entity карты которую разыгрывает игрок. Полезная нагрузка/диагностика, НЕ ключ корреляции.</summary>
        public int CastingCardEntity;

        /// <summary>Entity игрока-владельца.</summary>
        public int PlayerEntity;

        /// <summary>Entity карт предложенных для выбора.</summary>
        public int[] OfferedCardEntities;

        /// <summary>Визуальные данные предложенных карт (для UI, параллельно OfferedCardEntities).</summary>
        public Game.Core.Shared.CardVisualData[] OfferedCardVisuals;

        /// <summary>Количество предложенных карт.</summary>
        public int OfferedCount;

        /// <summary>
        /// Разрешена ли кнопка отмены. Пик бывает ОБЯЗАТЕЛЬНЫМ (замена добора «Адовый червь»: одну из трёх
        /// обязан выбрать) — окно общее на все каналы, и всегда доступная отмена оставляла бы такой канал
        /// с невыполненным pending, то есть с заглушенным добором до конца матча.
        /// </summary>
        public bool AllowCancel;
    }

    /// <summary>
    /// Публикуется UI когда игрок кликнул по одной из предложенных карт.
    /// CardPickSelectionSystem ждёт это событие чтобы зафиксировать выбор.
    /// </summary>
    public struct CardPickChosenEvent : IGameEvent
    {
        /// <summary>Эхо CardPickOfferedEvent.RequestId — ключ корреляции. Чужие токены продюсер игнорирует.</summary>
        public int RequestId;

        /// <summary>Entity карты которую разыгрывает игрок. Полезная нагрузка, НЕ ключ корреляции.</summary>
        public int CastingCardEntity;

        /// <summary>Entity выбранной игроком карты.</summary>
        public int ChosenCardEntity;
    }

    /// <summary>
    /// Публикуется когда игрок отменил выбор (закрыл панель без выбора).
    /// Продюсер снимает свой pending и освобождает окно.
    /// </summary>
    public struct CardPickCancelledEvent : IGameEvent
    {
        /// <summary>Эхо CardPickOfferedEvent.RequestId — ключ корреляции.</summary>
        public int RequestId;

        public int CastingCardEntity;
    }

    /// <summary>
    /// Ход игрока закончился — все его незавершённые пики обязаны свернуться (окно не переживает конец
    /// хода). Публикует CardPickBrokerSystem, по одному событию на конец хода.
    ///
    /// Момент задаёт брокер, СПОСОБ выбирает продюсер: раскопка и замена добора форсят случайный вариант
    /// из предложенного (семантика Йогг-Сарона), выбор цели и пик перед кастом — отменяются.
    /// Адресация по PlayerId (а не по талону), чтобы гасились и запросы, ещё не получавшие слот.
    /// </summary>
    public struct CardPickExpiredEvent : IGameEvent
    {
        public int PlayerId;
    }

    /// <summary>
    /// Публикуется после того как выбор зафиксирован — для сетевой репликации.
    /// Содержит достаточно данных чтобы второй клиент воспроизвёл результат.
    /// </summary>
    public struct CardPickResolvedNetEvent : IGameEvent
    {
        /// <summary>Entity карты которую разыгрывал игрок (локальное).</summary>
        public int CastingCardEntity;

        /// <summary>NetworkEntityKey карты-источника (для сетевой репликации выбора).</summary>
        public string CastingCardNetworkKey;

        /// <summary>Entity выбранной карты (локальное, не для сетки).</summary>
        public int ChosenCardEntity;

        /// <summary>ModelId выбранной карты.</summary>
        public int ChosenCardModelId;

        /// <summary>NetworkEntityKey выбранной карты (существующей или создаваемой из пула).</summary>
        public string ChosenCardNetworkKey;

        /// <summary>true — выбор из пула: оппонент должен создать сущность.</summary>
        public bool CreateFromPool;

        /// <summary>Для CreateFromPool: ExpansionId создаваемой карты.</summary>
        public string ChosenExpansionId;

        /// <summary>Для CreateFromPool: CardId (ModelId) создаваемой карты.</summary>
        public int ChosenCardId;
    }

    /// <summary>
    /// Активный клиент: способность разрешилась (цели выбраны). Публикуется
    /// RunResolveAbilityEffectSystem после ResolveTargets+Shape для СВОИХ карт.
    /// CollectActionSystem ловит и шлёт оппоненту ActionAbilityData со списком целей —
    /// пассивный клиент не ре-резолвит, а применяет эффекты по этим целям.
    /// </summary>
    public struct AbilityResolvedNetEvent : IGameEvent
    {
        /// <summary>Entity карты-источника (локальное).</summary>
        public int SourceCardEntity;

        /// <summary>Индекс способности в AbilityContainerComponent карты.</summary>
        public int AbilityIndex;

        /// <summary>Сущности целей (локальные). CollectActionSystem конвертит в NetworkEntityKey/"PLAYER:{id}".</summary>
        public int[] TargetEntities;

        /// <summary>Сущности, ПРИЗВАННЫЕ этим резолвом (локальные) — чтобы пассив применил к ним
        /// модификаторы призыва (бафф/таймер). CollectActionSystem конвертит в NetworkEntityKey.</summary>
        public int[] SummonedEntities;

        // ── Цепочки (составные способности): шаг + контекст для пассив-реплея стадии ──
        /// <summary>Индекс стадии цепочки (0 для обычных способностей).</summary>
        public int StepIndex;
        /// <summary>Накопленный KilledCount к моменту стадии (контекст для CountSource=ChainKilled).</summary>
        public int KilledCount;
        /// <summary>Идентичности случайно-сгенерированных карт стадии (GainRandomCardEffect) — пассив
        /// воспроизводит ИХ вместо своего ролла. Параллельные массивы exp/cardId.</summary>
        public string[] GeneratedExpansionIds;
        public int[] GeneratedCardIds;
    }

    /// <summary>
    /// Существо погибло от СИСТЕМНОГО таймера («умрёт через N ходов») на активе. Не воспроизводится
    /// детерминированно у пассива (он не тикает чужой таймер в чужой ход) → CollectActionSystem
    /// шлёт это как ActionDeathData, пассив вешает DeadTag. Публикует CreatureTimerTickSystem.
    /// </summary>
    public struct TimerDeathNetEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    /// <summary>
    /// Временный контроль над существом истёк на активе (контролёр дотикал TurnsRemaining в конце своего хода).
    /// Актив откатил владельца локально → CollectActionSystem шлёт ActionControlRevertData, пассив повторяет
    /// откат по ключу. Публикует TempControlRevertSystem. (Аналог TimerDeathNetEvent: считает только актив.)
    /// </summary>
    public struct ControlRevertedNetEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCreatureMove.
    /// RemoteCreatureMoveSystem добавит MoveRequestEvent на нужную сущность.
    /// </summary>
    public struct RemoteCreatureMoveEvent : IGameEvent
    {
        public string CreatureEntityKey;
        public int    ToRow;
        public int    ToCol;
        public int    ToOwnerId;
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCreatureAttack.
    /// RemoteCreatureAttackSystem добавит AttackRequestEvent на нужную сущность.
    /// </summary>
    public struct RemoteCreatureAttackEvent : IGameEvent
    {
        public string AttackerEntityKey;
        public string DefenderEntityKey;
    }
}

