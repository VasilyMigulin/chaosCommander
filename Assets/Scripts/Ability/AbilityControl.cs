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

    // === class (OOP) === Удалить цель-существо из игры безвозвратно (Низвести до атомов) → лимбо, без OnDie.
    [Serializable]
    public sealed class BanishEffect : EffectBase
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
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target)) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(target)) return;

            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(PlayerEntity)) return;
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
        }
    }

    // === class (OOP) === Превратить цель-существо в существо из ассета под контролем владельца источника
    // (Чертовщина → черт). Реализация: цель уходит из игры (лимбо), на свободную клетку фронта владельца
    // создаётся новое существо (Generate, детерм. ключ → синк как обычная генерация). NB: новое существо
    // встаёт на СВОЮ сторону (не на клетку цели) — упрощение позиции.
    [Serializable]
    public sealed class TransformEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;
        [Tooltip("Ассет CardInstanceData существа, в которое превращаем (перетащить).")]
        public ScriptableObject Source;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0 || !world.GetPool<CreatureTag>().Has(target)) return;
            if (!(Source is ICreatable c)) return;

            LeaveBoardUtil.Request(world, target, LeaveDestination.Limbo);   // исходное существо из игры

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            var free = BoardFrontRow.FreeCells(world, ownerId);
            if (free.Count > 0)
                GenerateCardEffect.SpawnToBoard(world, cardEntity, c.ExpansionId, c.CardId, BoardFrontRow.FrontRow, free[0]);
        }
    }

    // === class (OOP) === УНИВЕРСАЛЬНО разыграть выбранную карту (target) из ЛЮБОЙ зоны БЕСПЛАТНО (Барабук:
    // случайный спелл из колоды). Зону/выбор задаёт таргетинг (AbilityToTarget{Zone, Random/Selected}); этот
    // эффект лишь «играет» target. Существо — авто на свободную клетку фронта (как Гомункул) + InvokeEvent
    // (свой OnCast). Спелл/чара — через роутер с Free (роутер: спелл→кладбище+cast, чара→борд+cast). СИНК как
    // у призыва: делает активный, пассив получает обычным каст-синком (ActionCastData/ActionAbilityData).
    [Serializable]
    public sealed class PlayCardFromZoneEffect : EffectBase
    {
        [Tooltip("Всегда форсить случайную цель у разыгранной карты (как Йогг-Сарон), даже если сам источник " +
                 "разыгрывается через OnCast (где по умолчанию цель выбирает игрок).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            if (!TurnGate.IsLocalActive(world)) return;   // форс-розыгрыш — активный; пассив реплеит снапшоты
            bool forceRandom = ForceRandomTarget || AbilityResolveContext.TriggerKey != TriggerKeys.OnCast;
            PlayCardUtil.Play(world, target, free: true, forceRandomTarget: forceRandom);
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
            bool forceRandom = ForceRandomTarget || AbilityResolveContext.TriggerKey != TriggerKeys.OnCast;
            foreach (var e in found) PlayCardUtil.Play(world, e, Free, forceRandom);
        }
    }

    // === helper === ЕДИНОЕ ядро «разыграть карту из любой зоны»: снять с зоны, существо → на свободную клетку
    // фронта + InvokeEvent (свой OnCast), спелл/чара → RequestCardCastEvent{free} (роутер ведёт по типу).
    // Гейт IsLocalActive — на вызывающем эффекте (форс делает актив, пассив реплеит снапшоты). Используют
    // PlayCardFromZoneEffect (по цели) и PlaySameNameFromHandEffect (по названию).
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

            if (forceRandomTarget)
            {
                var frt = world.GetPool<ForceRandomTargetingComponent>();
                if (!frt.Has(card)) frt.Add(card);
            }

            var deckTag = world.GetPool<DeckTag>();
            var handTag = world.GetPool<HandTag>();
            var graveTag = world.GetPool<GraveTag>();
            if (deckTag.Has(card))  { deckTag.Del(card);  ZoneListUtil.RemoveFromDeck(world, card, ownerId); }
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
