using System;
using Game.Core.Events;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // БИБЛИОТЕКА ТРИГГЕРОВ — «когда срабатывает способность». Все bus-driven (как
    // OnCast/OnDie в AbilitySamples): подписка с owner=this, отписка через
    // GameEventBus.UnsubscribeAll(this). При срабатывании ВЕШАЮТ AbilityCastEvent
    // через AbilityFire.Mark (тот гейтит пассив: симулирует только активный клиент).
    //
    // ВАЖНО про зону: AbilityFire не проверяет зону. Турн-триггеры и реакции
    // существ на поле тут сами требуют BoardTag у источника (иначе карта в руке
    // тоже реагировала бы) — это их семантика. Остальные зональные ограничения
    // вешайте ПРАВИЛАМИ (см. AbilityRules), как требует архитектура.
    // ─────────────────────────────────────────────────────────────────────────

    // === helper === общие проверки для триггеров.
    internal static class TriggerUtil
    {
        public static bool OnBoard(EcsWorld world, int cardEntity)
            => world.GetPool<BoardTag>().Has(cardEntity);

        /// <summary>PlayerId владельца карты-источника (через сущность игрока).</summary>
        public static int OwnerPlayerId(EcsWorld world, int playerEntity)
        {
            var pool = world.GetPool<PlayerComponent>();
            return pool.Has(playerEntity) ? pool.Get(playerEntity).PlayerId : -1;
        }

        /// <summary>Сейчас ход ВЛАДЕЛЬЦА (все фазы: начало/середина/конец)? Фазовые состояния
        /// StartTurnState/ActiveState/EndTurnState висят на АКТИВНОМ игроке — т.е. на владельце ровно в
        /// его ход, включая каскады начала/конца. Проверяем сущность владельца НАПРЯМУЮ, а не «локальный
        /// активен» (TurnGate.IsLocalActive): в PvE один клиент симулирует ОБОИХ, и тот гейт истинен и в
        /// ход ИИ — реакции «на своём ходу» (Вуду-будду) утекали бы на чужой ход.</summary>
        public static bool IsOwnersTurn(EcsWorld world, int playerEntity)
        {
            if (playerEntity < 0) return false;
            return world.GetPool<ActiveState>().Has(playerEntity)
                || world.GetPool<StartTurnState>().Has(playerEntity)
                || world.GetPool<EndTurnState>().Has(playerEntity);
        }
    }

    // === class (OOP) === Начало хода ВЛАДЕЛЬЦА (источник на поле). Сигнал — bus
    // TurnStartedEvent (публикует RunTurnStartSystem у активного клиента).
    [Serializable]
    public sealed class OnTurnStartTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public bool AiTurnCycle => true;   // ценность каждый ход → ИИ прячет носителя (см. ITrigger)

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<TurnStartedEvent>(this, OnTurnStart);
        }

        void OnTurnStart(TurnStartedEvent e)
        {
            if (e.ActivePlayerId != TriggerUtil.OwnerPlayerId(_world, _playerEntity)) return; // мой ход?
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;                            // на поле?
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, "OnTurnStart"); // множитель (Временная петля)
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === «В начале матча» = ПЕРВЫЙ ход владельца (решение пользователя 2026-06-18).
    // Срабатывает ОДИН раз на первом TurnStartedEvent владельца, БЕЗ требования быть на поле
    // (карта в колоде/руке). Едет по обычному турновому синку: активный симулирует → ActionAbilityData
    // → пассив реплеит. Так не нужен спец-канал «до ходов для обоих» — первый игрок отрабатывает свой
    // match-start на ходу 1, второй — на своём первом ходу. КАВЕАТ: для карты, ПОРОЖДЁННОЙ уже в игре,
    // сработает на её первом владельческом ходу (в MVP таких нет).
    [Serializable]
    public sealed class OnMatchStartTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;
        bool _fired;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<TurnStartedEvent>(this, OnTurnStart);
        }

        void OnTurnStart(TurnStartedEvent e)
        {
            if (_fired) return;                                                              // только первый раз
            if (e.ActivePlayerId != TriggerUtil.OwnerPlayerId(_world, _playerEntity)) return; // мой первый ход?
            _fired = true;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Конец хода ВЛАДЕЛЬЦА (источник на поле). Сигнал — bus
    // TurnEndedEvent (публикует EndTurnRequestSystem.Шаг A, пока активный ещё
    // «симулятор» через EndTurnState → ability-пайплайн успевает осесть до передачи).
    [Serializable]
    public sealed class OnTurnEndTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public bool AiTurnCycle => true;   // ценность каждый ход → ИИ прячет носителя (см. ITrigger)

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<TurnEndedEvent>(this, OnTurnEnd);
        }

        void OnTurnEnd(TurnEndedEvent e)
        {
            if (e.ActivePlayerId != TriggerUtil.OwnerPlayerId(_world, _playerEntity)) return;
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, "OnTurnEnd"); // множитель (Временная петля)
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Источник атакует (этот конкретный аттакер). Сигнал — bus
    // CreatureAttackedEvent (AttackerEntity/DefenderEntity).
    [Serializable]
    public sealed class OnAttackTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureAttackedEvent>(this, OnAttacked);
        }

        void OnAttacked(CreatureAttackedEvent e)
        {
            if (e.AttackerEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Источник получил урон. Сигнал — bus CreatureDamagedEvent
    // (публикует TakeDamageSystem при любом уроне — бой и способности).
    [Serializable]
    public sealed class OnTakeDamageTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureDamagedEvent>(this, OnDamaged);
        }

        void OnDamaged(CreatureDamagedEvent e)
        {
            if (e.CreatureEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Источник кого-то убил. Сигнал — bus CreatureDiedEvent,
    // KillerEntity == источник.
    [Serializable]
    public sealed class OnKillTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureDiedEvent>(this, OnDied);
        }

        void OnDied(CreatureDiedEvent e)
        {
            if (e.KillerEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === «НА ВЫХОДЕ»: на поле ВЫШЛО существо (ЛЮБОЕ — союзное или вражеское; кого
    // затрагивать решает фильтр AbilityToField). Сигнал — bus CreatureInvokedEvent (публикуется при
    // ЛЮБОМ выходе на стол: розыгрыш/призыв), а НЕ CardCastEvent — поэтому ловит и призванных (Работяга),
    // и реагирует на врагов (ауры по врагам). Для реактивных аур: при появлении новой цели источник
    // переприменяет бафф/дебафф (ApplyTrackedBuffEffect — идемпотентно). Источник должен быть на поле.
    // NB: токены через CreateCardEvent{InBoard} (FillRow) идут мимо RunInvokeCreatureSystem → пока не ловятся.
    [Serializable]
    public sealed class OnCreatureInvokedTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureInvokedEvent>(this, OnInvoked);
        }

        void OnInvoked(CreatureInvokedEvent e)
        {
            if (e.CardEntity == _cardEntity) return;                           // сам источник — это OnCast
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;             // аура активна только на поле
            if (!_world.GetPool<CreatureTag>().Has(e.CardEntity)) return;      // только существа
            // Виновник (вышедшее существо) → TriggerSubjectComponent (для TargetSelection.TriggerSubject).
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, subjectEntity: e.CardEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Взята ИМЕННО ЭТА карта (Вонючее облако: «при взятии»). Без OnBoard — карта в
    // момент добора не на поле. Сигнал — bus CardDrawnEvent (CardEntity = взятая карта).
    [Serializable]
    public sealed class OnSelfDrawnTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CardDrawnEvent>(this, OnDrawn);
        }

        void OnDrawn(CardDrawnEvent e)
        {
            if (e.CardEntity != _cardEntity) return;   // взяли именно эту карту
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === При взятии ЭТОЙ карты — ПРИНУДИТЕЛЬНО её разыграть СО СВОИМ ЭФФЕКТОМ (force-cast self).
    // Паттерн «OnDraw форсит каст»: карта не висит в руке — играется сама и применяет свой эффект (Вонючее облако
    // замешивается врагу и бьёт того, кто его вытянул; Подстава-токен и пр.).
    //
    // ОДНОГО этого триггера ДОСТАТОЧНО: он и авто-розыгрывает карту (OnDrawn), и запускает её способность на
    // получившемся касте (OnCast) — как OnCastTrigger. Раньше требовалось ДОПОЛНИТЕЛЬНО вешать OnCastTrigger на ту
    // же способность, иначе карта игралась, но эффект молча не срабатывал (частые грабли). Если OnCastTrigger всё
    // же стоит рядом — AbilityFire.Mark идемпотентен на ability-сущности, двойного резолва не будет.
    // СИНК: форс делает АКТИВ (гейт IsLocalActive), пассив — обычным каст-синком (ActionCastData/ActionAbilityData).
    [Serializable]
    public sealed class OnDrawForcePlayTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CardDrawnEvent>(this, OnDrawn);
            GameEventBus.Subscribe<CardCastEvent>(this, OnCast);   // эффект СВОЕЙ способности на касте (как OnCastTrigger)
        }

        // Карта разыграна (в т.ч. нашим же авто-розыгрышем ниже) → запускаем её способность. Порядок верный:
        // OnCast идёт ПОСЛЕ каста, поэтому ActionCastData собирается раньше ActionAbilityData (синк не ломается).
        void OnCast(CardCastEvent e)
        {
            if (e.CardEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, TriggerKeys.OnCast);
        }

        void OnDrawn(CardDrawnEvent e)
        {
            if (e.CardEntity != _cardEntity) return;            // взяли именно эту карту
            if (!TurnGate.IsLocalActive(_world)) return;        // форс делает актив; пассив реплеит каст-снапшоты

            // Косметика у владельца: рука покажет карту и проиграет анимацию сброса («пришла → ушла»).
            GameEventBus.Publish(new CardForcePlayedFromDrawUIEvent { CardEntity = _cardEntity });

            // РАНЬШЕ: PlayCardUtil.Play напрямую вешал ОДНОКАДРОВЫЙ RequestCardCastEvent. На доборе турнстарта
            // (ActiveState ещё нет) форс откладывался до LocalTurnStartedEvent, который публикует RunActivateSystem
            // в ПОЗДНЕЙ группе кадра (_castSystems) — а DelHere<RequestCardCastEvent> (последняя группа) удалял
            // событие В ТОМ ЖЕ кадре, ДО того как роутер (более ранняя группа) увидит его в следующем кадре.
            // Итог: карта уходила из руки, но каст (и его эффекты/урон) терялся = «автокаст без эффекта».
            //
            // ТЕПЕРЬ: помечаем карту ПЕРСИСТЕНТНЫМ AutoCastComponent (как Фокус-покус). AutoCastSystem стоит ДО
            // роутера в той же группе и гейтит IsLocalActive → превращает маркер в RequestCardCastEvent в том же
            // кадре, где роутер его и потребляет (без гонки с DelHere). Маркер — компонент (не в списке DelHere),
            // поэтому переживает любой момент добора (старт-каскад ИЛИ добор эффектом по ходу). ForceRandom —
            // авто-выбор целей для Selected (без окна выбора/софт-лока), безвреден для NonTarget. Роутер
            // принимает каст и на StartTurnState → форс резолвится как часть каскада старта, до ActiveState.
            var autoCast = _world.GetPool<AutoCastComponent>();
            if (!autoCast.Has(_cardEntity)) autoCast.Add(_cardEntity);
            autoCast.Get(_cardEntity).Free = true;
            var forceRandom = _world.GetPool<ForceRandomTargetingComponent>();
            if (!forceRandom.Has(_cardEntity)) forceRandom.Add(_cardEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === ВЛАДЕЛЕЦ источника взял карту (Дерзкий расхититель: «когда берёте карту»).
    // Источник на поле. Сигнал — bus CardDrawnEvent (PlayerId = сущность игрока-добравшего). Срабатывает
    // на ЛЮБОЙ добор владельца (turn-start/эффекты). NB: для «при взятии ЭТОЙ карты» (Вонючее облако) нужен
    // отдельный self-вариант без OnBoard (карта в момент добора не на поле) — сделаем со счётчиками.
    [Serializable]
    public sealed class OnCardDrawnTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CardDrawnEvent>(this, OnDrawn);
        }

        void OnDrawn(CardDrawnEvent e)
        {
            if (e.PlayerId != _playerEntity) return;                  // карту взял МОЙ владелец
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;    // источник на поле
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === ВЛАДЕЛЬЦУ источника нанесён урон (Вуду-будду: «когда вы получаете урон»). Источник
    // (чара) на поле. Сигнал — bus PlayerDamagedEvent. По умолчанию ТОЛЬКО на ходу владельца: гейт по фазовым
    // состояниям владельца (TriggerUtil.IsOwnersTurn), НЕ по AbilityFire.Mark/IsLocalActive — в PvE один
    // клиент симулирует обоих, и тот гейт истинен в ход ИИ, так что реакция «на своём ходу» утекала на чужой
    // ход (урон отражался в оппонента и на его ходу). AnyTurn=true снимает ограничение (реакция в любой ход;
    // синк-гейт IsLocalActive внутри Mark остаётся — он про актив/пассив, не про семантику хода).
    // Величину урона эффект берёт из LastDamageTakenComponent.
    [Serializable]
    public sealed class OnOwnerDamagedTrigger : ITrigger
    {
        // ИНВЕРТИРОВАННЫЙ флаг НАМЕРЕННО: default(bool)=false → «только свой ход» = поведение старых ассетов.
        // OwnTurnOnly=true сломал бы существующие карты, если SerializeReference проигнорит инициализатор
        // (см. [[project_countsource_enum_serialization]]). Новая карта «реагируй на урон в любой ход» → AnyTurn=true.
        [UnityEngine.Tooltip("false (умолч.) — только на ходу владельца («на своём ходу», Вуду-будду). "
                           + "true — реагировать на урон в ЛЮБОЙ ход (в т.ч. ход оппонента).")]
        public bool AnyTurn = false;

        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<PlayerDamagedEvent>(this, OnDamaged);
        }

        void OnDamaged(PlayerDamagedEvent e)
        {
            if (e.PlayerEntity != _playerEntity) return;                 // урон ИМЕННО владельцу
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;       // источник (чара) на поле
            if (!AnyTurn && !TriggerUtil.IsOwnersTurn(_world, _playerEntity)) return;   // умолч. — «на вашем ходу» (с каскадами начала/конца)
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Погибло ДРУЖЕСТВЕННОЕ существо (не сам источник).
    // Источник должен быть на поле. Сигнал — bus CreatureDiedEvent.
    [Serializable]
    public sealed class OnAllyDiedTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureDiedEvent>(this, OnDied);
        }

        void OnDied(CreatureDiedEvent e)
        {
            if (e.CardEntity == _cardEntity) return;                       // не сам источник
            if (!TriggerUtil.OnBoard(_world, _cardEntity)) return;
            var owner = _world.GetPool<OwnerComponent>();
            if (!owner.Has(e.CardEntity) || !owner.Has(_cardEntity)) return;
            if (owner.Get(e.CardEntity).OwnerId != owner.Get(_cardEntity).OwnerId) return; // союзник?
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }
}
