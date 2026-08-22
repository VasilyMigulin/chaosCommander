using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Object = UnityEngine.Object;   // снять CS0104 'Object' (System.Object vs UnityEngine.Object)

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // КЛАСТЕР 3 (часть) — баунс/баниш/цвет. Все детерминированы по (caster, target) → пассив реплеит.
    // Уход с поля идёт через LeaveBoardEvent + RunLeaveBoardSystem (визуал/зоны/списки централизованы там).
    // Контроль/трансформ (Машина пропаганды/Еретик/Обращение/Чертовщина) — отдельным шагом (сложнее: флип
    // стороны доски + респаун визуала).
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Вернуть цель-существо в РУКУ владельца (Скелетон при смерти; Призвать ураган — поле).
    [Serializable]
    public sealed class BounceToHandEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            // ВРЕМЕННО: Скелетон (OnDie+AbilityToSelf) не возвращался. Видим, дошёл ли эффект и валиден ли target.
            bool isCreature = target >= 0 && world.GetPool<CreatureTag>().Has(target);
            UnityEngine.Debug.Log($"[Bounce] card={cardEntity} target={target} isCreature={isCreature} active={TurnGate.IsLocalActive(world)} → Hand");
            LeaveBoardUtil.Request(world, target, LeaveDestination.Hand);
        }
    }

    // === class (OOP) === Замешать цель-существо в КОЛОДУ владельца (Закопать заживо; Массовое захоронение — поле).
    [Serializable]
    public sealed class BounceToDeckEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        public override void Apply(EcsWorld world, int cardEntity, int target)
            => LeaveBoardUtil.Request(world, target, LeaveDestination.Deck);
    }

    // === class (OOP) === RemoveEffect — «убрать существо из игры» (Красный подарок Санты; зачистка вражеского
    // борда). Уводит цель в Limbo через LeaveBoardEvent — вне игры БЕЗ CreatureDiedEvent: OnDie/предсмертные
    // хрипы НЕ срабатывают. Аура удаляемого гаснет сама (рабочие ауры реактивные — пересчёт от BoardTag
    // источника; ушёл с борда → off). Стат-модификаторы снимает RunLeaveBoardSystem. По сути «уничтожение без
    // предсмертного эффекта». Field-контейнер (AbilityToField) применит его к каждой цели → чистка стола.
    // (бывш. BanishEffect — переименован; на ассеты не ссылался, сериализация не задета.)
    [Serializable]
    public sealed class RemoveEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        public override void Apply(EcsWorld world, int cardEntity, int target)
            => LeaveBoardUtil.Request(world, target, LeaveDestination.Limbo);
    }

    // === helper ===
    static class LeaveBoardUtil
    {
        public static void Request(EcsWorld world, int target, LeaveDestination dest)
        {
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target)) return;   // только существа
            var pool = world.GetPool<LeaveBoardEvent>();
            if (!pool.Has(target)) pool.Add(target);
            pool.Get(target).Destination = dest;
        }
    }

    // === class (OOP) === Снять у цели указанный цвет (Отлучение «лишает жёлтого»). Меняет CardModelComponent.Element
    // (по нему работают цвет-фильтры WithoutColor/Color). NB: цветовые ECS-теги (RedTag…) и визуал не трогаем.
    [Serializable]
    public sealed class RemoveColorEffect : EffectBase
    {
        public EnumService.Element Color;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            m.Element &= ~Color;
        }
    }

    // === class (OOP) === Дать цели указанный цвет (Обращение «даёт жёлтый»; Великое обращение — поле).
    [Serializable]
    public sealed class AddColorEffect : EffectBase
    {
        public EnumService.Element Color;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            m.Element |= Color;
        }
    }

    // === class (OOP) === Передать цель-существо под контроль ВЛАДЕЛЬЦА источника (Машина пропаганды — Field;
    // Еретик — Random+Temporary; Обращение — Selected). КОНТРОЛЬ НА МЕСТЕ: существо остаётся на своей клетке —
    // меняем только OwnerComponent.OwnerId (по нему турн-системы решают, чьё существо ходит) + свопаем
    // Own/EnemyCardTag (клиент-относит. → toggle на каждом клиенте). Позицию/сторону/визуал НЕ трогаем.
    // Память: OriginalOwnerComponent (первый владелец, ставится один раз). Temporary=true → TempControlledComponent
    // (TempControlRevertSystem вернёт владельца в конце хода нового владельца). СИНК: OwnerId абсолютен (одинаково
    // у обоих); теги клиент-относительны (toggle у каждого) → корректно на обеих сторонах.
    [Serializable]
    public sealed class TakeControlEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        public bool Temporary = false;
        public int Turns = 1;   // для Temporary: сколько ходов КОНТРОЛЁРА держится контроль (1 = до конца этого хода)

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            // Диагностика вместо тихих выходов: «Машина пропаганды» из раскопки в MP не давала контроль
            // (2026-07-27), а причина не была видна в логах — каждый гейт теперь именуется.
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target))
            { UnityEngine.Debug.LogWarning($"[TakeControl] card={cardEntity}: target={target} не существо → skip"); return; }
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(target))
            { UnityEngine.Debug.LogWarning($"[TakeControl] card={cardEntity}: target={target} без OwnerComponent → skip"); return; }

            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(PlayerEntity))
            { UnityEngine.Debug.LogWarning($"[TakeControl] card={cardEntity}: PlayerEntity={PlayerEntity} не игрок (битый Init эффекта — pool/resync-пересоздание?) → skip"); return; }
            int newOwnerId = playerPool.Get(PlayerEntity).PlayerId;

            int origOwnerId = ownerPool.Get(target).OwnerId;
            if (origOwnerId == newOwnerId) return;   // уже мой

            var ownTag = world.GetPool<OwnCardTag>();
            var enemyTag = world.GetPool<EnemyCardTag>();
            bool wasOwn = ownTag.Has(target);

            // ПАМЯТЬ об изначальном владельце — ставим ОДИН раз (первый владелец переживает любые перехваты).
            var origPool = world.GetPool<OriginalOwnerComponent>();
            if (!origPool.Has(target)) origPool.Add(target).OwnerId = origOwnerId;

            if (Temporary)
            {
                var tempPool = world.GetPool<TempControlledComponent>();
                if (!tempPool.Has(target))
                {
                    ref var t = ref tempPool.Add(target);
                    t.OriginalOwnerId = origOwnerId;
                    t.OriginalWasOwn = wasOwn;
                    t.ExpiresOnPlayerId = newOwnerId;   // тикаем в конце хода контролёра (нового владельца)
                    t.TurnsRemaining = Turns <= 0 ? 1 : Turns;
                }
            }

            // КОНТРОЛЬ НА МЕСТЕ: существо остаётся на своей клетке/стороне — меняем только ВЛАДЕЛЬЦА (по нему
            // турн-системы решают, чьё существо ходит) + клиент-относительные теги Own/Enemy. Позицию/сторону/
            // визуал НЕ трогаем (никакого переезда и респауна).
            ownerPool.Get(target).OwnerId = newOwnerId;

            if (wasOwn) { ownTag.Del(target); if (!enemyTag.Has(target)) enemyTag.Add(target); }
            else        { if (enemyTag.Has(target)) enemyTag.Del(target); if (!ownTag.Has(target)) ownTag.Add(target); }

            // ВРЕМЕННЫЙ контроль всегда освежает действия: рефил скорости на старте хода (RunTurnStartSystem)
            // уже прошёл ДО резолва OnTurnStart-способности Еретика и существо ещё числилось за оппонентом —
            // без этого украденное приходило «выдохшимся» (Remaining=0) и весь контроль был бессмыслен.
            // Дизайн-инвариант: «одолжил — значит может действовать». Перманентные кражи (Машина пропаганды/
            // Обращение) не трогаем — там существо честно ждёт следующего старта хода нового владельца.
            // СИНК ДАРОМ: эффект резолвится на обоих клиентах (реплей), значения зеркальны.
            if (Temporary)
            {
                var speedPool = world.GetPool<SpeedComponent>();
                if (speedPool.Has(target))
                {
                    ref var sp = ref speedPool.Get(target);
                    sp.Remaining = sp.Max;
                }
                var attacksPool = world.GetPool<AttacksUsedComponent>();
                if (attacksPool.Has(target)) attacksPool.Get(target).Value = 0;
            }

            // ХС-семантика: способности украденного «служат» новому владельцу — триггеры начала/конца
            // хода срабатывают на ЕГО ходу, бенефициар/таргетинг смотрят с его стороны (см. util).
            AbilityOwnershipUtil.Rebind(world, target, PlayerEntity);

            UnityEngine.Debug.Log($"[TakeControl] card={cardEntity}: target={target} {origOwnerId}→{newOwnerId} temporary={Temporary}");
        }
    }

    // === class (OOP) === ПОЛИМОРФ (переработан 2026-07-28): превратить цель-существо в существо из ассета
    // НА МЕСТЕ — как Полиморф/Хекс в ХС. Сущность НЕ пересоздаётся: та же клетка, тот же NetworkEntityKey
    // (внешние ссылки/синк живут), меняются модель/статы/кост/способности/архетипы/вьюха, баффы/дебаффы
    // СГОРАЮТ. Мутацию выполняет RunTransformSystem по TransformCardEvent (у системы есть CardConfig —
    // модель по идентичности ассета). TransferOwnership=true → превращённое существо переходит под контроль
    // владельца ИСТОЧНИКА (Чертовщина: «чёрт под вашим контролем» — ВКЛЮЧИТЬ У ЕЁ АССЕТА!); false (умолч.) —
    // остаётся владельцу цели (классический полиморф: враг остаётся с бараном).
    // СИНК ДАРОМ: Apply ре-ранится на обоих клиентах → событие и мутация зеркальны.
    [Serializable]
    public sealed class TransformEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        [Tooltip("Ассет CardInstanceData существа, в которое превращаем (перетащить).")]
        public ScriptableObject Source;
        [Tooltip("true → превращённое существо переходит под контроль владельца ИСТОЧНИКА (Чертовщина); " +
                 "false — остаётся у владельца цели (классический полиморф).")]
        public bool TransferOwnership = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target)) return;
            // Командир неполиморфим. ЛОГ обязателен: без него способность «резолвится и ничего не делает»,
            // и по консоли не отличить «выбрали командира» от настоящей поломки (2026-08-01, Мастер
            // трансмутаций). Чтобы командира вообще нельзя было выбрать — Not{CommanderTargetFilter} в Filters.
            if (world.GetPool<CommanderTag>().Has(target))
            {
                UnityEngine.Debug.Log($"[Transform] target={target} — командир, неполиморфим → skip " +
                                      "(добавь Not{{CommanderTargetFilter}} в Filters, чтобы его нельзя было выбрать)");
                return;
            }
            if (!(Source is ICreatable c)) return;

            int newOwnerId = -1;
            if (TransferOwnership)
            {
                var ownerPool = world.GetPool<OwnerComponent>();
                if (ownerPool.Has(cardEntity)) newOwnerId = ownerPool.Get(cardEntity).OwnerId;
            }

            GameEventBus.Publish(new TransformCardEvent
            {
                TargetEntity = target,
                ExpansionId  = c.ExpansionId,
                CardId       = c.CardId,
                NewOwnerId   = newOwnerId,
            });
        }
    }

    // === class (OOP) === Превратить цель-существо в СЛУЧАЙНУЮ карту из ПУЛА (эволюция). Мутация НА МЕСТЕ —
    // TransformCardEvent → RunTransformSystem (как TransformEffect). СИНК: ролл недетерминирован → актив
    // роллит и пишет в GeneratedCardChannel (едет в снапшоте способности), пассив TryReplay берёт ту же
    // идентичность (паттерн GainRandomCard/Фокус-покус).
    [Serializable]
    public sealed class TransformFromPoolEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        [Tooltip("Ассет CardPool (по критериям, напр. «все существа»). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();
        [Tooltip("true → превращённое существо переходит под контроль владельца ИСТОЧНИКА; " +
                 "false — остаётся у владельца цели (классический полиморф).")]
        public bool TransferOwnership = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
            => TransformRoll.Apply(world, cardEntity, target, PoolAsset, Pool, cost: -1, TransferOwnership);
    }

    // === class (OOP) === Превратить цель-существо в случайную карту из пула СТОИМОСТЬЮ X («за X»).
    // X — фиксированный Cost («Канализационный мутаген»: за 1) или, при CostFromTimesPlayed, число
    // розыгрышей ЭТОЙ карты владельцем в матче, ВКЛЮЧАЯ текущий («Мастер трансмутаций»; счётчик
    // MatchCounter.CountsByModelId — как Позвать рой, зеркален: трекер инкрементит синхронно на
    // CardPlayedEvent, ДО резолва). Нет карт ровно за X → берём ближайшие по |Δстоимости| (не фуззлимся:
    // «за 100» превратит в самое дорогое из пула). Остальное — как TransformFromPoolEffect.
    [Serializable]
    public sealed class TransformFromPoolWithCostEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        [Tooltip("Ассет CardPool (по критериям, напр. «все существа»). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();
        [Tooltip("true → превращённое существо переходит под контроль владельца ИСТОЧНИКА; " +
                 "false — остаётся у владельца цели (классический полиморф).")]
        public bool TransferOwnership = false;

        [Tooltip("Целевая стоимость случайной карты («за X»). Игнорируется при CostFromTimesPlayed.")]
        public int Cost = 1;
        [Tooltip("X = сколько раз ЭТА карта разыграна владельцем в матче, включая текущий розыгрыш (Мастер трансмутаций).")]
        public bool CostFromTimesPlayed = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            int x = Cost;
            if (CostFromTimesPlayed)
            {
                x = 1;   // счётчик не найден → минимум «за 1» (первый розыгрыш и так даёт 1)
                var counters  = world.GetPool<MatchCounterComponent>();
                var modelPool = world.GetPool<CardModelComponent>();
                if (PlayerEntity >= 0 && counters.Has(PlayerEntity) && modelPool.Has(cardEntity))
                {
                    var counts = counters.Get(PlayerEntity).CountsByModelId;
                    if (counts != null && counts.TryGetValue(modelPool.Get(cardEntity).ModelId, out int played) && played > 0)
                        x = played;
                }
            }
            TransformRoll.Apply(world, cardEntity, target, PoolAsset, Pool, x, TransferOwnership);
        }
    }

    // === helper === общий ролл+мутация Transform-from-pool: фильтр по косту (cost >= 0 → карты ровно за X,
    // нет таких → ближайшие по |Δcost|; random среди равных), синк ролла через GeneratedCardChannel,
    // сама мутация — TransformCardEvent (RunTransformSystem, на месте, ключ/клетка сохраняются).
    internal static class TransformRoll
    {
        public static void Apply(EcsWorld world, int cardEntity, int target,
                                 ScriptableObject poolAsset, List<ScriptableObject> pool, int cost, bool transferOwnership)
        {
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target)) return;
            // Командир неполиморфим — и молчать об этом нельзя (см. TransformEffect). Выходим ДО ролла,
            // поэтому GeneratedCardChannel не пишется: актив и пассив пропускают эффект одинаково, синк цел.
            if (world.GetPool<CommanderTag>().Has(target))
            {
                UnityEngine.Debug.Log($"[Transform] target={target} — командир, неполиморфим → skip " +
                                      "(добавь Not{{CommanderTargetFilter}} в Filters, чтобы его нельзя было выбрать)");
                return;
            }

            string exp; int cardId;
            if (GeneratedCardChannel.TryReplay(out exp, out cardId))
            {
                // пассив: присланная активом идентичность
            }
            else
            {
                var pick = PoolUtil.PickByCost(poolAsset, pool, cost);   // общий выбор «за X» (см. PoolUtil)
                if (pick == null) return;
                exp = pick.ExpansionId; cardId = pick.CardId;
                GeneratedCardChannel.Record(exp, cardId);
            }

            int newOwnerId = -1;
            if (transferOwnership)
            {
                var ownerPool = world.GetPool<OwnerComponent>();
                if (ownerPool.Has(cardEntity)) newOwnerId = ownerPool.Get(cardEntity).OwnerId;
            }

            GameEventBus.Publish(new TransformCardEvent
            {
                TargetEntity = target,
                ExpansionId  = exp,
                CardId       = cardId,
                NewOwnerId   = newOwnerId,
            });
        }

    }

    // === class (OOP) === УНИВЕРСАЛЬНО разыграть ЦЕЛЕВУЮ карту (target) БЕСПЛАТНО, где бы она ни лежала
    // (Барабук: случайный спелл из колоды; «Дополнительные возможности»: Modifier раскопки — target там =
    // выбранная карта). Зону/выбор задаёт ВНЕШНИЙ контекст — таргетинг (AbilityToTarget{Zone, Random/Selected})
    // или пайплайн Modifiers дискавера; сам эффект зону НЕ выбирает, он лишь «играет» target (за это и
    // переименован из PlayCardFromZoneEffect — старое имя намекало на выбор зоны внутри). Существо — авто на
    // свободную клетку фронта (как Гомункул) + InvokeEvent (свой OnCast). Спелл/чара — через роутер с Free
    // (роутер: спелл→кладбище+cast, чара→борд+cast). СИНК как у призыва: делает активный, пассив получает
    // обычным каст-синком (ActionCastData/ActionAbilityData).
    [Serializable]
    [MovedFrom(true, sourceClassName: "PlayCardFromZoneEffect")]   // ремап: spell_card_052 / creature_card_022
    public sealed class PlayTargetCardEffect : EffectBase
    {
        [Tooltip("Всегда форсить случайную цель у разыгранной карты (как Йогг-Сарон), даже если сам источник " +
                 "разыгрывается через OnCast (где по умолчанию цель выбирает игрок).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            if (!TurnGate.IsLocalActive(world)) return;   // форс-розыгрыш — активный; пассив реплеит снапшоты
            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;
            PlayCardUtil.Play(world, target, free: true, forceRandomTarget: forceRandom);
        }
    }

    // === class (OOP) === Modifier для Discover: снять таймер жизни у чары-цели (Зачаровать матч: «выберите
    // чару и разыграйте, её длительность продлевается до конца матча»). По контракту CardCharmModel 0
    // таймеров = ПОСТОЯННАЯ чара — снятие CharmTimerComponent и есть «до конца матча». Не-чара / чара без
    // таймера (TurnsAlive=0, уже постоянная) — no-op. Чара с FixedCharmDurationTag (CardCharmModel.FixTurns) —
    // тоже no-op, длительность зафиксирована и «Зачаровать матч» её не трогает. Ставить в DiscoverEffect.Modifiers
    // РЯДОМ с PlayTargetCardEffect (порядок между ними не важен — до первого тика таймера ещё как минимум ход).
    [Serializable]
    public sealed class MakeCharmPermanentEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            if (world.GetPool<FixedCharmDurationTag>().Has(target)) return;
            var timerPool = world.GetPool<CharmTimerComponent>();
            if (timerPool.Has(target)) timerPool.Del(target);
        }
    }

    // === class (OOP) === Modifier для Discover/генерации: скидка К СТОИМОСТИ выбранной карты, ТОЛЬКО ЕСЛИ
    // её цвет содержит Color (Батюшка-барыга: «-2, если оно жёлтого цвета»). ConditionRoot тут не годится —
    // резолвится ОДИН РАЗ при Init источника, до того как известна выбранная цель; цвет цели узнать раньше
    // просто негде, поэтому смотрим его прямо в Apply. Permanent — переживает смерть/зоны, как обычный
    // AddBuffEffect{BuffCost}.
    [Serializable]
    public sealed class DiscountIfColorEffect : EffectBase
    {
        public EnumService.Element Color = EnumService.Element.Yellow;
        public int Delta = -2;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0 || Delta == 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target) || (pool.Get(target).Element & Color) == 0) return;
            BuffCost.Add(world, target, Delta, permanent: true);
        }
    }

    // === class (OOP) === Modifier для Discover: ЗАПОМНИТЬ выбранную карту НА ИСТОЧНИКЕ (cardEntity) вместо
    // того, чтобы класть её в руку (Королевский шут: «посмотрите 3 шутки и выберите 1» — выбранная не идёт
    // в руку, а ждёт хрипа). Снимает HandTag с target СРАЗУ — тем же путём, что PlayTargetCardEffect у
    // «Приглашения»/«Дополнительной возможности»: CreateCardSystem регистрирует карту в списке руки и шлёт
    // CardDrawnEvent, ТОЛЬКО ЕСЛИ карта «всё ещё в заявленной зоне» (StillInDeclaredZone) — модификаторы
    // discover'а прогоняются ДО этой проверки, так что уход HandTag здесь её надёжно отменяет: карта
    // остаётся зоно-независимой сущностью (не рука/колода/кладбище), пока PlayRememberedCardEffect её не
    // разыграет. Ставить в DiscoverEffect.Modifiers.
    [Serializable]
    public sealed class RememberCardForLaterPlayEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;

            var rememberPool = world.GetPool<RememberedPlayTargetComponent>();
            if (!rememberPool.Has(cardEntity)) rememberPool.Add(cardEntity);
            rememberPool.Get(cardEntity).Entity = target;

            // Снимаем ПОЛНОСТЬЮ, как Graft снимает донора — иначе визуальная карточка (CardLayout) остаётся
            // висеть в своём слоте живьём (сняли только ECS-тег, UI об уходе никто не известил), а
            // PutRememberedCardToHandEffect в конце сборки кладёт СВЕЖУЮ карточку в ДРУГОЙ слот с уже
            // смёрженным текстом — игрок продолжает держать/видеть СТАРУЮ, не убранную (баг 2026-08-21:
            // «описание не переносится», хотя данные в CardViewDataComponent были верны).
            var handTag = world.GetPool<HandTag>();
            if (handTag.Has(target))
            {
                handTag.Del(target);
                var ownerPool = world.GetPool<OwnerComponent>();
                if (ownerPool.Has(target)) ZoneListUtil.RemoveFromHand(world, target, ownerPool.Get(target).OwnerId);
                GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = target });
            }
        }
    }

    // === class (OOP) === Снимает TokenTag с target. Донор-ассеты пула (спелл-«кирпичи» Проклятья для
    // принцессы и подобные) держат IsToken=1 НА АССЕТЕ ради CardPool.Matches (IncludeTokens=false по
    // умолчанию — иначе они бы выпадали как обычные карты из бустеров/генераторов). Но собранная из них
    // ИТОГОВАЯ карта — не расходник, а обычная чара, которой играют как всем остальным: лимит чар на столе
    // (CharmCount) исключает токены, и без этого шага собранное проклятье обошло бы лимит в 5 чар бесплатно.
    // Ставить Modifier'ом на раскопку БАЗОВОГО тира (target — только что материализованная база, ДО
    // RememberCardForLaterPlayEffect неважно, до или после — компонент не трогает HandTag/remember).
    [Serializable]
    public sealed class RemoveTokenTagEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var tokenPool = world.GetPool<TokenTag>();
            if (tokenPool.Has(target)) tokenPool.Del(target);
        }
    }

    // === class (OOP) === Разыграть карту, запомненную RememberCardForLaterPlayEffect (free force-cast —
    // ровно то же, что делает PlayTargetCardEffect с обычной целью). Вешать на OnDie ТОГО ЖЕ источника
    // (Королевский шут: «при смерти разыграйте выбранную шутку»). Компонент снимается сразу — одноразово.
    [Serializable]
    public sealed class PlayRememberedCardEffect : EffectBase
    {
        [Tooltip("Форсить случайную цель у разыгранной карты (как Йогг-Сарон) — по умолчанию true: розыгрыш " +
                 "идёт не от ручного OnCast игрока (хрип), интерактивное окно выбора цели тут не место.")]
        public bool ForceRandomTarget = true;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!TurnGate.IsLocalActive(world)) return;   // форс-розыгрыш — активный; пассив реплеит снапшоты

            var rememberPool = world.GetPool<RememberedPlayTargetComponent>();
            if (!rememberPool.Has(cardEntity)) return;
            int remembered = rememberPool.Get(cardEntity).Entity;
            rememberPool.Del(cardEntity);

            PlayCardUtil.Play(world, remembered, free: true, forceRandomTarget: ForceRandomTarget);
        }
    }

    // === class (OOP) === Положить в РУКУ карту, запомненную RememberCardForLaterPlayEffect — вместо
    // форс-каста (см. PlayRememberedCardEffect). Вешать на OnDie ТОГО ЖЕ источника (Контрабандист: «при
    // смерти положите в руку выбранную карту»). Компонент снимается сразу — одноразово. Лимит руки —
    // общее правило HandSpace (нет места → карта сгорает, как выбор дискавера в руку).
    [Serializable]
    public sealed class PutRememberedCardToHandEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var rememberPool = world.GetPool<RememberedPlayTargetComponent>();
            if (!rememberPool.Has(cardEntity)) return;
            int remembered = rememberPool.Get(cardEntity).Entity;
            rememberPool.Del(cardEntity);
            if (remembered < 0) return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(remembered)) return;
            int playerEntity = ZoneListUtil.FindPlayerEntity(world, ownerPool.Get(remembered).OwnerId);
            if (playerEntity < 0) return;

            if (!HandSpace.HasRoom(world, playerEntity))
            {
                HandSpace.Burn(world, remembered, "Контрабандист: запомненная карта не влезла в руку");
                return;
            }

            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(remembered)) handTag.Add(remembered);
            ZoneListUtil.AddToHand(world, remembered, playerEntity);

            // SourceEntity = кастер (Контрабандист) — UI летит визуально от него/его трупа, а не «из-за края экрана».
            GameEventBus.Publish(new CardDrawnEvent { CardEntity = remembered, PlayerId = playerEntity, SourceEntity = cardEntity });
        }
    }

    // === class (OOP) === Запоминает ИДЕНТИЧНОСТЬ target (ExpansionId+CardId) в DiscoverExclusionComponent
    // карты-источника — «Проклятье для принцессы»: второй проход по тому же пулу эффектов должен видеть,
    // что первый уже взял (DiscoverFromPoolEffect.ExcludeAlreadyPicked читает этот список). Ставить ПЕРВЫМ
    // Modifier'ом (до Graft/прочих) на КАЖДОЙ раскопке, чью цель нужно исключить из следующих проходов.
    [Serializable]
    public sealed class RecordDiscoverPickEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var modelPool = world.GetPool<CardModelComponent>();
            if (!modelPool.Has(target)) return;
            ref var m = ref modelPool.Get(target);
            string key = DiscoverExclusionComponent.KeyOf(m.ExpansionId, m.ModelId);

            var pool = world.GetPool<DiscoverExclusionComponent>();
            if (!pool.Has(cardEntity)) pool.Add(cardEntity).UsedKeys = new List<string>();
            ref var comp = ref pool.Get(cardEntity);
            comp.UsedKeys ??= new List<string>();
            if (!comp.UsedKeys.Contains(key)) comp.UsedKeys.Add(key);
        }
    }

    // === class (OOP) === Клонирует ЖИВЫЕ способности с target на сущность, ЗАПОМНЕННУЮ
    // RememberCardForLaterPlayEffect (Проклятье для принцессы — «Elise/Kazakus»: несколько раскопок-эффектов
    // подряд на ОДНОЙ карте грузят способности в ОДНУ строящуюся чару). target — уже ПОЛНОСТЬЮ материализован
    // (DiscoverFromPoolEffect создаёт карту через CreateCardSystem → CardModel.Init ДО применения Modifiers,
    // у target уже настоящий AbilityContainerComponent) — берём его живые Ability-объекты (AbilityRefComponent),
    // DeepClone (та же процедура, что CardModel.InitOneAbility при обычном ините) и дописываем в контейнер
    // цели-стройки, не заменяя то, что уже там (несколько проходов копятся). Сам RememberedPlayTargetComponent
    // не трогает и не снимает — ставить ДО PutRememberedCardToHandEffect/PlayRememberedCardEffect (те
    // потребляют компонент одноразово финальным шагом).
    //
    // ДОНОР (target) после копирования — расходник, сам по себе он не нужен и НЕ должен повиснуть в руке
    // (DiscoverFromPoolEffect кладёт раскопанное в руку владельца безусловно): сжигаем через HandSpace.Burn
    // (IsToken на ассете-доноре ОБЯЗАТЕЛЕН — иначе он «сгорит» в кладбище вместо лимбо, некрасиво, но не баг).
    [Serializable]
    public sealed class GraftAbilitiesFromTargetEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var rememberPool = world.GetPool<RememberedPlayTargetComponent>();
            if (!rememberPool.Has(cardEntity)) return;
            int dest = rememberPool.Get(cardEntity).Entity;
            if (dest < 0) return;

            var containerPool = world.GetPool<AbilityContainerComponent>();
            var refPool = world.GetPool<AbilityRefComponent>();
            if (containerPool.Has(target))
            {
                var srcEntities = containerPool.Get(target).AbilityEntities;
                if (srcEntities != null && srcEntities.Length > 0)
                {
                    if (!containerPool.Has(dest)) containerPool.Add(dest).AbilityEntities = Array.Empty<int>();
                    ref var destContainer = ref containerPool.Get(dest);
                    var merged = new List<int>(destContainer.AbilityEntities ?? Array.Empty<int>());
                    int index = merged.Count;

                    foreach (var srcAbilityEntity in srcEntities)
                    {
                        if (!refPool.Has(srcAbilityEntity)) continue;
                        var clone = (Ability)AbilityCloneUtil.DeepClone(refPool.Get(srcAbilityEntity).Ability);
                        int newAbilityEntity = world.NewEntity();
                        refPool.Add(newAbilityEntity).Ability = clone;
                        clone.Init(world, newAbilityEntity, dest, PlayerEntity, index++);
                        merged.Add(newAbilityEntity);
                    }
                    destContainer.AbilityEntities = merged.ToArray();
                }
            }

            // Текст донора переезжает в описание стройки — иначе готовая чара так и осталась бы с текстом
            // одной лишь основы, без слов о собранных эффектах. Каждый проход дописывает СВОЮ строку.
            var viewPool = world.GetPool<CardViewDataComponent>();
            if (viewPool.Has(target) && viewPool.Has(dest))
            {
                string donorText = viewPool.Get(target).Description;
                if (!string.IsNullOrEmpty(donorText))
                {
                    ref var destView = ref viewPool.Get(dest);
                    string current = destView.Description ?? string.Empty;
                    if (string.IsNullOrEmpty(current))
                    {
                        destView.Description = donorText;
                    }
                    else
                    {
                        // dest — ЧАРА: у неё С САМОГО Init уже есть строка длительности («Действует N ходов»),
                        // description НИКОГДА не пуст — старая проверка «пусто?» всегда шла в else и клеила
                        // текст донора ПОСЛЕ неё, длительность залезала в середину, а не в конец (баг 2026-08-21,
                        // видно на скрине: «Действует 7 ходов» дважды). Вставляем донор-текст ПЕРЕД последней
                        // строкой — длительность остаётся последней и после нескольких проходов графта.
                        int lastNl = current.LastIndexOf('\n');
                        string head = lastNl >= 0 ? current.Substring(0, lastNl) : string.Empty;
                        string tail = lastNl >= 0 ? current.Substring(lastNl) : ("\n" + current);
                        destView.Description = (string.IsNullOrEmpty(head) ? donorText : head + "\n" + donorText) + tail;
                    }

                    // dest ЕЩЁ прячется в руке (HandTag снят RememberCardForLaterPlayEffect) — правка
                    // CardViewDataComponent тут НЕВИДИМА сама по себе: HandUISystem снимает Description
                    // в CardAddedToHandUIEvent только ОДИН раз, на самое первое появление в руке (базовый
                    // тир), а второй показ (PutRememberedCardToHandEffect) просто переиздаёт HandTag без
                    // нового прихода. Живой перерендер уже есть готовым — тот же канал, что у Charm-таймера
                    // (CharmHandDurationPreviewSystem) — PlayCardView сам фильтрует по CardEntity (баг 2026-08-21:
                    // «текст описания в неё не положился» после сборки «Проклятья для принцессы»).
                    GameEventBus.Publish(new CardDescriptionChangedUIEvent { CardEntity = dest, Description = destView.Description });
                }
            }

            // Донор — расходник, убираем из руки (см. коммент класса выше).
            var ownerPool = world.GetPool<OwnerComponent>();
            var handTag = world.GetPool<HandTag>();
            if (handTag.Has(target))
            {
                handTag.Del(target);
                if (ownerPool.Has(target)) ZoneListUtil.RemoveFromHand(world, target, ownerPool.Get(target).OwnerId);
                GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = target });
            }
            HandSpace.Burn(world, target, "Проклятье для принцессы: донор эффекта потрачен");
        }
    }

    // === class (OOP) === Разыграть ВЕРХНЮЮ карту колоды владельца (Шальной принц: «в начале хода разыграйте
    // верхнюю карту колоды»). Верх = DeckComponent.CardEntities[0] — тот же конец, что берёт DrawCardSystem.
    // Играется бесплатно через PlayCardUtil; ForceRandomTarget по умолч. true (розыгрыш идёт НЕ от ручного
    // OnCast игрока — как Барабук/Йогг, окно ручного выбора цели не место). PlayCardUtil сам снимает карту с
    // колоды. СИНК как у PlayTargetCardEffect: форсит активный (IsLocalActive), пассив — обычным каст-синком.
    [Serializable]
    public sealed class PlayTopDeckCardEffect : EffectBase
    {
        public bool ForceRandomTarget = true;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!TurnGate.IsLocalActive(world)) return;   // форс-розыгрыш — активный; пассив реплеит снапшоты
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            var pp = world.GetPool<PlayerComponent>();
            var dp = world.GetPool<DeckComponent>();
            int top = -1;
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                if (pp.Get(pe).PlayerId != ownerId) continue;
                ref var deck = ref dp.Get(pe);
                if (deck.CardEntities != null && deck.CardEntities.Count > 0) top = deck.CardEntities[0];
                break;
            }
            if (top < 0) return;                          // колода пуста
            PlayCardUtil.Play(world, top, free: true, forceRandomTarget: ForceRandomTarget);
        }
    }

    // === class (OOP) === Замешать САМ ИСТОЧНИК из руки обратно в колоду и добрать замену (Распрекрасная
    // принцесса: «в начале матча замешивается в колоду», даже если пережила мулиган и попала в стартовую руку).
    // Работает ТОЛЬКО если источник в РУКЕ (иначе no-op: в колоде она и так). Двигает РЕАЛЬНУЮ карту (не копию,
    // как ShuffleCopyOfSelfEffect): снимает HandTag→DeckTag, перекладывает в списках зон, шлёт UI-снятие из руки,
    // добирает 1 (DrawReplacement). Кладём в КОНЕЦ колоды (детерминированно — порядок колоды и так тасован;
    // важно, что не в top, иначе тут же вернётся). СИНК: ре-ран на ОБОИХ клиентах (инжект ActionAbilityData у
    // пассива), как DrawCardEffect: зоны зеркальны по ключам, добор — с верха order-синхронной колоды, UI-события
    // для чужого зеркала no-op. БЫВШИЙ гейт IsLocalActive здесь но-опил инжект у пассива → зеркало оппонента
    // оставляло карту в руке (класс бага «Попойки», расхождение чексуммы H/D) — снят 2026-07-28.
    [Serializable]
    public sealed class ShuffleSelfIntoDeckEffect : EffectBase
    {
        public bool DrawReplacement = true;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(cardEntity)) return;         // не в руке → уже в колоде, ничего не делаем

            var ownerPool = world.GetPool<OwnerComponent>();
            int ownerId = ownerPool.Has(cardEntity) ? ownerPool.Get(cardEntity).OwnerId : -1;

            // рука → колода (реальная карта)
            handTag.Del(cardEntity);
            ZoneListUtil.RemoveFromHand(world, cardEntity, ownerId);
            GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = cardEntity });

            var deckTag = world.GetPool<DeckTag>();
            if (!deckTag.Has(cardEntity)) deckTag.Add(cardEntity);

            var pp = world.GetPool<PlayerComponent>();
            var dp = world.GetPool<DeckComponent>();
            int playerEntity = -1;
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                if (pp.Get(pe).PlayerId != ownerId) continue;
                ref var deck = ref dp.Get(pe);
                deck.CardEntities ??= new System.Collections.Generic.List<int>();
                if (!deck.CardEntities.Contains(cardEntity)) deck.CardEntities.Add(cardEntity);
                deck.Count = deck.CardEntities.Count;
                playerEntity = pe;
                break;
            }

            if (DrawReplacement && playerEntity >= 0)
            {
                var drawPool = world.GetPool<DrawCardEvent>();
                if (!drawPool.Has(playerEntity)) drawPool.Add(playerEntity);
                drawPool.Get(playerEntity).Count += 1;
            }
        }
    }

    // === class (OOP) === Разыграть из руки владельца карту(ы) с ТЕМ ЖЕ названием, что у источника (Грядущий
    // шторм — спелл; Гомункул — существо). ЕДИНАЯ логика розыгрыша — PlayCardUtil (сама ветвит существо→борд /
    // спелл-чара→роутер), поэтому одним эффектом и для существ, и для спеллов, без SummonEffect-багажа.
    // Цепочка-«волна» сама: разыгранная копия снова фейрит свой OnCast → этот эффект найдёт следующую → пока
    // одноимённые есть в руке. СИНК: форс делает АКТИВ (гейт IsLocalActive), пассив — обычным каст-синком
    // (ActionCastData копии + ActionAbilityData её способности), сам не ищет.
    [Serializable]
    [MovedFrom(true, sourceClassName: "CastSameNamedFromHandEffect")]   // ремап временного имени (spell_card_029)
    public sealed class PlaySameNameFromHandEffect : EffectBase
    {
        public int Count = 1;       // сколько одноимённых разыграть за раз (текст «и её» → 1; волна — через ре-триггер)
        public bool Free = true;    // спеллы/чары — бесплатно (существа призываются бесплатно в любом случае)

        [Tooltip("Всегда форсить случайную цель у разыгранной копии (как Йогг-Сарон), даже если сам источник " +
                 "разыгрывается через OnCast (где по умолчанию цель выбирает игрок).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!TurnGate.IsLocalActive(world)) return;   // актив форсит, пассив реплеит снапшоты

            var modelPool = world.GetPool<CardModelComponent>();
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!modelPool.Has(cardEntity) || !ownerPool.Has(cardEntity)) return;
            string name = modelPool.Get(cardEntity).CardName;
            if (string.IsNullOrEmpty(name)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            // собрать одноимённые в руке (играть во время foreach по фильтру нельзя → буфер)
            var found = new List<int>();
            foreach (var e in world.Filter<HandTag>().Inc<CardModelComponent>().Inc<OwnerComponent>().End())
            {
                if (e == cardEntity) continue;                          // не сам источник
                if (ownerPool.Get(e).OwnerId != ownerId) continue;     // только своя рука
                if (modelPool.Get(e).CardName != name) continue;       // то же название
                found.Add(e);
                if (found.Count >= Count) break;
            }
            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;
            foreach (var e in found) PlayCardUtil.Play(world, e, Free, forceRandom);
        }
    }

    // === helper === ЕДИНОЕ ядро «разыграть карту из любой зоны»: снять с зоны, существо → на свободную клетку
    // фронта + InvokeEvent (свой OnCast), спелл/чара → RequestCardCastEvent{free} (роутер ведёт по типу).
    // Гейт IsLocalActive — на вызывающем эффекте (форс делает актив, пассив реплеит снапшоты). Используют
    // PlayTargetCardEffect (по цели) и PlaySameNameFromHandEffect (по названию).
    internal static class PlayCardUtil
    {
        // forceRandomTarget: если true — таргетинг РАЗЫГРАННОЙ карты форсится случайным (ForceRandomTargetingComponent),
        // даже если её собственная способность Selected. Нужен, когда розыгрыш идёт НЕ от OnCast (игрок в этот
        // момент физически не играет карту-источник сам — триггер мог сработать в чужой ход/асинхронно) — окно
        // интерактивного выбора цели в такой ситуации не место (см. AbilityResolveContext.TriggerKey).
        public static void Play(EcsWorld world, int card, bool free, bool forceRandomTarget = false)
        {
            if (card < 0) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            int ownerId = ownerPool.Has(card) ? ownerPool.Get(card).OwnerId : -1;

            // Розыгрыш инициирован ЭФФЕКТОМ → пишем карте записку о причине: её способности встанут
            // в очередь сразу за вызвавшей активацией. См. CausedByActivationComponent.
            CauseStamp.Mark(world, card);

            if (forceRandomTarget)
            {
                var frt = world.GetPool<ForceRandomTargetingComponent>();
                if (!frt.Has(card)) frt.Add(card);
            }

            var deckTag = world.GetPool<DeckTag>();
            var handTag = world.GetPool<HandTag>();
            var graveTag = world.GetPool<GraveTag>();
            if (deckTag.Has(card))
            {
                deckTag.Del(card);
                ZoneListUtil.RemoveFromDeck(world, card, ownerId);

                // Владелец эту карту В РУКЕ НЕ ВИДЕЛ — без показа розыгрыш «из ниоткуда» нечитаем.
                // CardLayout выводит её той же дугой с зависанием, что и уничтожение из колоды
                // (CardMillFromDeckUIEvent), но с VFX-развилкой «разыграна» (PlayShowcaseFx).
                var viewPool = world.GetPool<CardViewDataComponent>();
                GameEventBus.Publish(new CardPlayedFromDeckUIEvent
                {
                    CardEntity   = card,
                    CardName     = viewPool.Has(card) ? viewPool.Get(card).CardName : "",
                    Icon         = viewPool.Has(card) ? viewPool.Get(card).ArtImage : null,
                    Visual       = viewPool.Has(card) ? viewPool.Get(card).ToVisual() : default,
                    IsLocalOwner = world.GetPool<OwnCardTag>().Has(card),
                });
            }
            if (handTag.Has(card))
            {
                handTag.Del(card);
                ZoneListUtil.RemoveFromHand(world, card, ownerId);
                // UI: PlayCardUtil снимает HandTag САМ → RunMoveCardToBoardSystem/Grave увидят wasInHand=false и
                // НЕ пошлют событие удаления из руки-UI → карта остаётся висеть в руке у владельца (Гомункул/
                // Грядущий шторм/Барабук разыграли её, а визуально она здесь). Шлём событие сами.
                GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = card });
            }
            if (graveTag.Has(card)) graveTag.Del(card);

            if (world.GetPool<CreatureTag>().Has(card))
            {
                // существо: авто на свободную клетку фронта владельца (ClaimFreeCell — безопасно для мульти-розыгрыша)
                int col = BoardFrontRow.ClaimFreeCell(world, ownerId);
                UnityEngine.Debug.Log($"[Play] card={card} ВЕТКА СУЩЕСТВА owner={ownerId} freeCol={col}");
                if (col < 0) { UnityEngine.Debug.LogWarning($"[Play] card={card} нет свободной клетки → застрял (снят из руки, не размещён)"); return; }   // некуда ставить
                var movePool = world.GetPool<MoveCardToBoardEvent>();
                if (!movePool.Has(card)) movePool.Add(card);
                ref var m = ref movePool.Get(card);
                m.Row = BoardFrontRow.FrontRow; m.Col = col; m.OwnerId = ownerId;
                var invokePool = world.GetPool<InvokeEvent>();
                if (!invokePool.Has(card)) invokePool.Add(card);   // NotCast=false → свой OnCast
            }
            else
            {
                // спелл/чара: через роутер (роутит по типу + CardCastEvent + синк). НЕ ставим RequestCardCastEvent
                // напрямую — этот метод вызывается ИЗ RunResolveAbilityQueueSystem, который в _abilitySystems идёт
                // ПОСЛЕ RunCastRouterSystem: одно-кадровое событие тут же смёл бы DelHere<RequestCardCastEvent>
                // (последняя группа кадра) ДО того, как роутер вообще увидел бы его на следующем кадре (карта
                // молча пропадала бы без каста/эффекта — баг с Грядущим штормом/Барабуком). ПЕРСИСТЕНТНЫЙ
                // AutoCastComponent (тот же приём, что у OnDrawForcePlayTrigger) переживает границу кадра —
                // AutoCastSystem стоит ДО роутера и на следующем кадре превратит его в RequestCardCastEvent.
                UnityEngine.Debug.Log($"[Play] card={card} ВЕТКА СПЕЛЛ/ЧАРА (free={free}) → AutoCastComponent");
                var autoCast = world.GetPool<AutoCastComponent>();
                if (!autoCast.Has(card)) autoCast.Add(card);
                autoCast.Get(card).Free = free;
            }
        }
    }
}


