using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // БИБЛИОТЕКА ЭФФЕКТОВ — «что делает способность». Применяет RunResolveAbilityQueueSystem
    // (по одному, IsReady-гейт). Каждый эффект держит опциональное составное условие
    // ConditionRoot (null = всегда готов), как сэмплы DealDamage/GainMana.
    //
    // СИНК (replay-authoritative): эффект применяется на активном клиенте, пассив
    // воспроизводит по снапшоту целей и применяет ТЕ ЖЕ эффекты детерминированно —
    // поэтому изменения должны зависеть только от (cardEntity, target), без локальных
    // гейтов. На доске стат-бара нет → отдельный UI-рефреш статов не требуется.
    // ─────────────────────────────────────────────────────────────────────────

    // === helper === резолв сущности игрока-владельца карты (для ресурс/добор-эффектов).
    internal static class EffectUtil
    {
        public static void RaiseResource(EcsWorld world, int playerEntity, EnumService.ResourceType type, int current, int max)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            GameEventBus.Publish(new ResourceChangedEvent
            {
                isLocalPlayer = playerPool.Has(playerEntity) && playerPool.Get(playerEntity).IsLocalPlayer,
                Type = type, NewValue = current, MaxValue = max,
            });
        }
    }

    // === class (OOP) === Лечение цели-существа (до Max). Target — существо.
    [Serializable]
    public sealed class HealEffect : EffectBase, IDynamicValue
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Heal;
        public int Amount = 1;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Amount;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<HealthComponent>();
            if (!pool.Has(target)) return;
            ref var hp = ref pool.Get(target);
            hp.Current = Math.Min(hp.Current + Amount, hp.Max);
        }
    }

    // === class (OOP) === Бафф статов цели (+ATK/+HP/+Speed) через МОДИФИКАТОРЫ (Base иммутабелен).
    // Permanent=false (по умолч.) → мягкий модификатор: остаётся, но ЧИСТИТСЯ при смерти существа
    // (напр. Тайный воздыхатель — «не до конца», теряется при гибели). Permanent=true → переживает смерть.
    // Для снимаемых аур используйте ApplyTrackedBuff/RevertTrackedBuff, а не этот эффект.
    [Serializable]
    public sealed class BuffStatsEffect : EffectBase, IDynamicValue
    {
        public int AttackBonus = 0;
        public int HealthBonus = 0;
        public int SpeedBonus  = 0;
        public bool Permanent  = false;   // true → ModifiersPermanent (переживает смерть)

        // ИИ: знак бонусов определяет роль — минусовые статы = дебафф врага, плюсовые = бафф своих.
        public override AiEffectRole AiRole
            => (AttackBonus < 0 || HealthBonus < 0 || SpeedBonus < 0) ? AiEffectRole.DebuffEnemy : AiEffectRole.BuffAlly;

        // Только НЕнулевые бонусы, в порядке атака→здоровье→скорость — чтобы число токенов *N*
        // в тексте совпадало с тем, что автор реально пишет («+*1*/+*2*» = atk,hp; «+*1* к скор.» = speed).
        public int DynamicValueCount
            => (AttackBonus != 0 ? 1 : 0) + (HealthBonus != 0 ? 1 : 0) + (SpeedBonus != 0 ? 1 : 0);

        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity)
        {
            int i = 0;
            if (AttackBonus != 0) { if (i == index) return AttackBonus; i++; }
            if (HealthBonus != 0) { if (i == index) return HealthBonus; i++; }
            if (SpeedBonus  != 0) { if (i == index) return SpeedBonus; }
            return 0;
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (AttackBonus != 0)
            {
                var atk = world.GetPool<AttackComponent>();
                if (atk.Has(target)) { ref var a = ref atk.Get(target); a.AddModifier(AttackBonus, Permanent); }
            }
            if (HealthBonus != 0)
            {
                var hpPool = world.GetPool<HealthComponent>();
                if (hpPool.Has(target))
                {
                    ref var hp = ref hpPool.Get(target);
                    hp.AddModifier(HealthBonus, Permanent);
                    if (HealthBonus > 0) hp.Current += HealthBonus;   // классический «+X/+X» — заодно лечит
                    // HP изменился модификатором → сигналим LethalHealthSystem (дебафф до ≤0 = смерть, не через урон).
                    GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target });
                }
            }
            if (SpeedBonus != 0)
            {
                var sp = world.GetPool<SpeedComponent>();
                if (sp.Has(target))
                {
                    ref var s = ref sp.Get(target);
                    s.AddModifier(SpeedBonus, Permanent);   // поднимает Max (RecalculateValue только ЗАЖИМАЕТ Remaining вниз)
                    // ...поэтому бюджет действий сам не растёт. Как «+X/+X» лечит Current, так и +скорость даёт
                    // дополнительное действие СРАЗУ (иначе «За работу!»: на старте хода Remaining уже восстановлен
                    // до старого Max, бафф потом поднял Max — но лишнее действие в этот ход недоступно).
                    if (SpeedBonus > 0) { s.Remaining += SpeedBonus; if (s.Remaining > s.Max) s.Remaining = s.Max; }
                }
            }
        }
    }

    // === class (OOP) === Уничтожить цель-существо: вешаем DeadTag → DieSystem
    // разруливает (кладбище/возврат командира/limbo токена). Не урон — нет источника.
    [Serializable]
    public sealed class DestroyEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!world.GetPool<CreatureTag>().Has(target)) return;   // только существа
            var dead = world.GetPool<DeadTag>();
            if (dead.Has(target)) return;
            if (world.GetPool<InvulnerableTag>().Has(target)) return;   // «Неуязвимый»: принудительно не уничтожается
            var hpPool = world.GetPool<HealthComponent>();
            if (hpPool.Has(target)) hpPool.Get(target).Current = 0;
            KillAttribution.Mark(world, cardEntity, target);   // атрибуция → мана за вражеского (DieSystem)
            dead.Add(target);
        }
    }

    // === helper === Атрибуция ПРЯМОГО уничтожения (не-урон): владелец карты-источника пишется в
    // KilledByComponent жертвы ДО DeadTag — DieSystem начислит ману за вражеское существо (единая точка,
    // как у смертей от урона через TakeDamageSystem). Эффекты ре-ранятся на обоих клиентах → зеркально.
    internal static class KillAttribution
    {
        public static void Mark(EcsWorld world, int sourceCard, int victim)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(sourceCard)) return;
            var pool = world.GetPool<KilledByComponent>();
            if (!pool.Has(victim)) pool.Add(victim);
            pool.Get(victim).PlayerId = ownerPool.Get(sourceCard).OwnerId;
        }
    }

    // === class (OOP) === Уничтожить ВСЕХ существ на поле, КРОМЕ цели (Вот это потасовка! = HS Brawl). Цель =
    // случайный ВЫЖИВШИЙ, выбранный ТАРГЕТИНГОМ: AbilityToTarget{Random, Count 1, Filters=[CardTypeFilter.Creature]}
    // (обе стороны, без side-фильтра) → выбор синкается target-ключом (оба клиента получают того же выжившего).
    // Здесь — просто DeadTag всем прочим существам на борде: OnDie/предсмертные хрипы срабатывают, как Brawl в HS
    // (в отличие от RemoveEffect). Детерминировано по борду → ре-ран зеркален. Пустой борд/1 существо → безвредно.
    [Serializable]
    public sealed class DestroyAllExceptTargetEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Removal;

        public override void Apply(EcsWorld world, int cardEntity, int survivor)
        {
            var dead = world.GetPool<DeadTag>();
            var hpPool = world.GetPool<HealthComponent>();
            var invulnerablePool = world.GetPool<InvulnerableTag>();
            var buf = new System.Collections.Generic.List<int>();
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Exc<DeadTag>().End())
                if (e != survivor) buf.Add(e);
            foreach (var e in buf)
            {
                if (invulnerablePool.Has(e)) continue;   // «Неуязвимый» переживает потасовку
                if (hpPool.Has(e)) hpPool.Get(e).Current = 0;
                KillAttribution.Mark(world, cardEntity, e);   // мана за КАЖДОГО вражеского (свои — не в счёт, гейт в DieSystem)
                if (!dead.Has(e)) dead.Add(e);
            }
        }
    }

    // === class (OOP) === Добор карт владельцу. NonTarget — target = сущность игрока,
    // но добор всегда у ВЛАДЕЛЬЦА (кэшируем на ините), а не у произвольной цели.
    [Serializable]
    public sealed class DrawCardEffect : EffectBase, ICasterScopedEffect, IDynamicValue
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int Count = 1;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Count;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<DrawCardEvent>();
            if (pool.Has(PlayerEntity)) pool.Get(PlayerEntity).Count += Count;   // накапливаем
            else                        pool.Add(PlayerEntity).Count  = Count;
        }
    }

    // === class (OOP) === «Занять место цели» (Бешеная бабка): переставляет ИСТОЧНИК на клетку цели.
    // Применять ПОСЛЕ DestroyEffect (сначала убить, потом занять место). Логическую позицию ставим
    // здесь (синк через ре-ран эффекта — клетка цели детерминирована/синкнута), вьюшку двигает
    // RepositionViewSystem по ViewRepositionRequest. Пайплайн размещения не трогаем — это просто эффект.
    [Serializable]
    public sealed class MoveSelfToTargetCellEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target == cardEntity) return;
            var posPool = world.GetPool<BoardPositionComponent>();
            if (!posPool.Has(target) || !posPool.Has(cardEntity)) return;   // обе стороны на поле

            // Копируем клетку цели в локали ДО второго Get (ref на тот же пул).
            ref var tp = ref posPool.Get(target);
            int row = tp.Row, col = tp.Col, owner = tp.OwnerId;

            ref var sp = ref posPool.Get(cardEntity);
            sp.Row = row; sp.Col = col; sp.OwnerId = owner;

            var reqPool = world.GetPool<ViewRepositionRequest>();
            if (!reqPool.Has(cardEntity)) reqPool.Add(cardEntity);
            ref var req = ref reqPool.Get(cardEntity);
            req.Row = row; req.Col = col; req.OwnerId = owner;
        }
    }

    // === class (OOP) === Вешает цели-командиру блокировку розыгрыша на N ходов (Проклятье для принцессы:
    // «если командира нет на поле — заблокируйте на 3 хода») — тот же CommanderCooldownComponent, что
    // RunCommanderCooldownSystem вешает при возврате в руку после смерти; тикает RunTurnStartSystem как
    // обычно. Max, не перезапись — не сокращаем уже висящий более долгий кулдаун.
    [Serializable]
    public sealed class ApplyCommanderCooldownEffect : EffectBase, IDynamicValue
    {
        public int Turns = 3;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Turns;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Turns <= 0 || target < 0) return;
            if (!world.GetPool<CommanderTag>().Has(target)) return;
            var pool = world.GetPool<CommanderCooldownComponent>();
            if (!pool.Has(target)) pool.Add(target);
            ref var cd = ref pool.Get(target);
            cd.TurnsRemaining = System.Math.Max(cd.TurnsRemaining, Turns);
        }
    }

    // === class (OOP) === Вешает цели-существу таймер смерти (умрёт через N своих ходов).
    // Тикает CreatureTimerTickSystem на старте хода владельца. Самостоятельный target-эффект:
    // годится и как модификатор призыва (SummonEffect.SummonModifiers), и как обычный эффект
    // («целевое существо умрёт через 2 хода»).
    [Serializable]
    public sealed class DeathTimerEffect : EffectBase, IDynamicValue
    {
        public int Turns = 3;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Turns;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Turns <= 0) return;
            if (!world.GetPool<CreatureTag>().Has(target)) return;
            var pool = world.GetPool<CreatureTimerComponent>();
            if (!pool.Has(target)) pool.Add(target);
            pool.Get(target).TurnsRemaining = Turns;
        }
    }

    // === class (OOP) === Вешает цели-карте таймер сброса (сброшена через N своих ходов, пока лежит в
    // руке — Сделка с чертом). Тикает HandDiscardTimerTickSystem на старте хода владельца. Как
    // DeathTimerEffect, но для карт руки: самостоятельный target-эффект, годится и как модификатор
    // генерации (GainRandomCardEffect.Modifiers), и как обычный эффект на уже выбранной карте.
    [Serializable]
    public sealed class AddHandDiscardTimerEffect : EffectBase, IDynamicValue
    {
        public int Turns = 3;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Turns;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Turns <= 0 || target < 0) return;
            var pool = world.GetPool<HandDiscardTimerComponent>();
            if (!pool.Has(target)) pool.Add(target);
            pool.Get(target).TurnsRemaining = Turns;
        }
    }

    // Изменение СТОИМОСТИ карты — это БАФФ (перм/мягкий, стакается, снимается): BuffCost (AbilityBuffs.cs)
    // через AddBuffEffect{Buff=BuffCost{Delta}} или как модификатор дискавера/призыва. ModifyCostEffect удалён.

    // === class (OOP) === +золото владельцу (cap Max). NonTarget-стиль, но владелец — кэш.
    [Serializable]
    public sealed class GainGoldEffect : EffectBase, ICasterScopedEffect, IDynamicValue
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 1;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Amount;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<GoldComponent>();
            if (!pool.Has(PlayerEntity)) return;
            ref var gold = ref pool.Get(PlayerEntity);
            // Даём ТОЛЬКО Current, Max не трогаем (не перманентный доход — разовая прибавка). БЕЗ клампа к
            // Max: доход хода (RunTurnStartSystem) синхронно рефиллит Current=Max в САМОМ НАЧАЛЕ хода, а этот
            // эффект чаще всего приходит из интерактивного выбора того же OnTurnStartTrigger (напр. Развилка →
            // «Богатство») — пик-окно ждёт клика игрока, резолвится ПОЗЖЕ рефилла, и Current к этому моменту
            // уже = Max → клампнутый +Amount не давал бы вообще ничего. Временный перелив НЕ висит вечно:
            // на СЛЕДУЮЩЕМ старте хода тот же RunTurnStartSystem безусловно перезапишет Current=Max — лишнее
            // само сгорает (как «Освежающий напиток» с временной маной), отдельного возврата не нужно.
            gold.Current += Amount;
            EffectUtil.RaiseResource(world, PlayerEntity, EnumService.ResourceType.Gold, gold.Current, gold.Max);
        }
    }

    // === class (OOP) === Увеличить ЗАПАС золота владельца (Max), без выдачи Current — сиблинг
    // GainGoldEffect (тот трогает только Current, см. его коммент). Раздельные эффекты, а не один флаг
    // «AlsoMax» на GainGoldEffect (Казначей: «получите 1 золота» + «увеличьте запас золота на 1» — два
    // разных действия в тексте карты, комбинируются в Effects двумя отдельными записями).
    // Кап 10 — как у золота/маны (см. RunTurnStartSystem.cs:93, DieSystem.cs ManaCap-коммент).
    [Serializable]
    public sealed class GainGoldMaxEffect : EffectBase, ICasterScopedEffect, IDynamicValue
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 1;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Amount;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            const int GoldCap = 10;
            var pool = world.GetPool<GoldComponent>();
            if (!pool.Has(PlayerEntity)) return;
            ref var gold = ref pool.Get(PlayerEntity);
            gold.Max = Math.Min(gold.Max + Amount, GoldCap);
            EffectUtil.RaiseResource(world, PlayerEntity, EnumService.ResourceType.Gold, gold.Current, gold.Max);
        }
    }

    // === class (OOP) === Форс-активировать способности ВСЕХ своих чар на столе (Медиум шарлатан) — не
    // «переиздать CharmInvokedEvent» (тот узкий канал — только «чара родилась сразу на столе», см.
    // OnCharmInvokedTrigger), а буквально дёрнуть AbilityFire.Mark на КАЖДОЙ загруженной способности каждой
    // чары, вне зависимости от того, какой у неё триггер (OnTurnStart/OnCast/что угодно) — ровно «включить
    // ещё раз», как обычный ручной вызов той же точки, которой уже пользуется любой триггер-класс. Чара без
    // подходящих условий/правил под способностью просто не резолвится — то же самое, как если бы её родной
    // триггер сработал вхолостую.
    [Serializable]
    public sealed class ActivateCharmsEffect : EffectBase, ICasterScopedEffect
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(PlayerEntity)) return;
            int ownerId = playerPool.Get(PlayerEntity).PlayerId;

            var ownerPool = world.GetPool<OwnerComponent>();
            var containerPool = world.GetPool<AbilityContainerComponent>();
            foreach (var e in world.Filter<CharmTag>().Inc<BoardTag>().Inc<OwnerComponent>().End())
            {
                if (e == cardEntity) continue;                       // не дёргаем сами себя
                if (ownerPool.Get(e).OwnerId != ownerId) continue;
                if (!containerPool.Has(e)) continue;
                var abilities = containerPool.Get(e).AbilityEntities;
                if (abilities == null) continue;
                foreach (var abilityEntity in abilities)
                    AbilityFire.Mark(world, abilityEntity, e, PlayerEntity);
            }
        }
    }

    // === class (OOP) === Временная мана до конца хода (Освежающий напиток). NonTarget: target = игрок.
    // +Amount к Current сейчас (может превышать Max — временная), а TemporaryManaRefundSystem вернёт
    // RefundAmount в конце хода. КАВЕАТ синка: возврат у ПАССИВА для маны оппонента косметичен —
    // пассив не гоняет turn-end оппонента; для своего игрока (кто пил) всё корректно.
    [Serializable]
    public sealed class GainTemporaryManaEffect : EffectBase, ICasterScopedEffect
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 5;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var manaPool = world.GetPool<ManaComponent>();
            if (!manaPool.Has(target)) return;   // target = сущность игрока (NonTarget)
            ref var mana = ref manaPool.Get(target);
            mana.Current += Amount;

            var tempPool = world.GetPool<TemporaryManaComponent>();
            if (!tempPool.Has(target)) tempPool.Add(target);
            tempPool.Get(target).RefundAmount += Amount;

            EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Mana, mana.Current, mana.Max);
        }
    }

    // === class (OOP) === ПОЛ маны игрока до конца матча (Вечная попойка, спелл-архетип): вешает
    // ManaFloorComponent{Floor}; RunTurnStartSystem в начале КАЖДОГО хода поднимает ману до Floor, если ниже.
    // Сразу поднимает и текущую (работает уже в ход применения, а не со следующего). Условие «нет существ
    // в колоде» вешается штатным ConditionRoot (NoCreaturesInDeckCondition), не флагом (см. осевую архитектуру).
    // target = сущность игрока (NonTarget). Синк: OnMatchStart ре-ранится на обоих → маркер зеркален.
    [Serializable]
    public sealed class SetManaFloorEffect : EffectBase, ICasterScopedEffect
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Floor = 3;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Floor <= 0 || target < 0) return;

            var floorPool = world.GetPool<ManaFloorComponent>();
            if (!floorPool.Has(target)) floorPool.Add(target);
            ref var f = ref floorPool.Get(target);
            if (Floor > f.Floor) f.Floor = Floor;   // не понижаем уже установленный пол

            // Сразу поднять ману до пола (иначе эффект заработал бы только со следующего старта хода).
            var manaPool = world.GetPool<ManaComponent>();
            if (manaPool.Has(target))
            {
                ref var mana = ref manaPool.Get(target);
                if (mana.Max < Floor) mana.Max = Floor;
                if (mana.Current < Floor) mana.Current = Floor;
                EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Mana, mana.Current, mana.Max);
            }
        }
    }

    // BuffCreaturesInDeckEffect УДАЛЁН (2026-06-19): заменён композицией
    // AbilityToField{Zone=Deck, Filters=[AllyTargetFilter]} + BuffStatsEffect. Зоны теперь в таргетинге (TargetZone).

    // === class (OOP) === Модификатор стоимости карт (Гиперинфляция: «все карты на 1 дороже до конца матча»).
    // AllPlayers=true → +Amount всем игрокам (глобально); иначе только владельцу источника. Одноразовый
    // перм. эффект (не аура). Читается в RunCastRouterSystem/CardAffordabilitySystem через CostModifierUtil.
    [Serializable]
    public sealed class AddCostModifierEffect : EffectBase, ICasterScopedEffect
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 1;
        public bool AllPlayers = true;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<CostModifierComponent>();
            if (AllPlayers)
            {
                foreach (var pe in world.Filter<PlayerComponent>().End())
                {
                    if (!pool.Has(pe)) pool.Add(pe);
                    pool.Get(pe).Amount += Amount;
                }
            }
            else
            {
                if (!pool.Has(target)) pool.Add(target);   // target = сущность игрока (NonTarget)
                pool.Get(target).Amount += Amount;
            }

            // UI: пересчитать отображаемую стоимость карт руки (логика-то уже через CostModifierUtil).
            GameEventBus.Publish(new CostModifierChangedEvent());
        }
    }

    // === class (OOP) === АУРА-модификатор стоимости карт, ПОКА источник жив на столе (Носитель кодила:
    // «карты обоих игроков без жёлтого цвета стоят на 1 дороже») — в отличие от AddCostModifierEffect
    // («Гиперинфляция», одноразовый ПЕРМАНЕНТНЫЙ эффект), это настоящая аура: снимается при смерти/уходе
    // источника с поля (см. AuraCostModifiers.RemoveBySource, вызывается из DieSystem/RunLeaveBoardSystem —
    // тем же приёмом, что TrackedBuffs/AppliedBuffs.RemoveTarget). ExcludeElement — маска цветов
    // (флаговый EnumService.Element), которых модификатор НЕ касается; пусто = касается всех цветов.
    [Serializable]
    public sealed class AddCostAuraEffect : EffectBase, ICasterScopedEffect
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 1;
        public bool AllPlayers = true;
        public EnumService.Element ExcludeElement;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (AllPlayers)
            {
                foreach (var pe in world.Filter<PlayerComponent>().End())
                    AuraCostModifiers.Add(world, pe, cardEntity, Amount, ExcludeElement);
            }
            else
            {
                AuraCostModifiers.Add(world, target, cardEntity, Amount, ExcludeElement);   // target = игрок (NonTarget)
            }

            GameEventBus.Publish(new CostModifierChangedEvent());
        }
    }

    // === class (OOP) === Постоянный бонус к длительности БУДУЩИХ чар владельца (Зачарованный: «ваши чары
    // в этом матче длятся на 1 дольше»). Одноразовый перм. эффект на игроке (не аура — не откатывается со
    // смертью источника), хранится в CharmDurationBonusService (match-lifetime, как CastMultiplierService).
    // Читает CardCharmModel.OnInit при инициализации КАЖДОЙ следующей чары владельца; уже стоящие на
    // столе чары бонус не получают (как и полагается «+N к печатному значению»).
    [Serializable]
    public sealed class AddCharmDurationBonusEffect : EffectBase
    {
        public int Amount = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Amount == 0 || target < 0) return;   // NonTarget → target = сущность игрока-владельца
            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(target)) return;
            int ownerId = playerPool.Get(target).PlayerId;
            CharmDurationBonusService.Add(ownerId, Amount);

            // Ретроактивно: продлеваем и УЖЕ РАЗЫГРАННЫЕ чары владельца на столе, не только будущие (юзер
            // 2026-08-21: «Зачарованный не продлевает уже разыгранные» — было осознанным дизайном, юзер
            // решил сделать симметрично). RefreshDescription (CardConfig) отсюда не позвать — цикл сборок
            // Ability→Configs, поэтому только правим сам TurnsRemaining и просим Ecs.Systems перерендерить.
            var ownerPool = world.GetPool<OwnerComponent>();
            var timerPool = world.GetPool<CharmTimerComponent>();
            foreach (var e in world.Filter<CharmTag>().Inc<BoardTag>().Inc<CharmTimerComponent>().Inc<OwnerComponent>().End())
            {
                if (ownerPool.Get(e).OwnerId != ownerId) continue;
                timerPool.Get(e).TurnsRemaining += Amount;
                GameEventBus.Publish(new CharmTimerBumpedEvent { CardEntity = e });
            }
        }
    }

    // === class (OOP) === СЕМЕЙСТВО «альтернативная уплата»: «следующие Charges разыгранных вами карт
    // оплачиваются НЕ ресурсом, а Kind» — урон себе на эффективную стоимость (Бесчестный букмекер,
    // пейн-архетип), сброс случайной карты руки, жертва своего существа, карта из колоды. Вешает маркер
    // AltCostComponent на игрока-владельца; повторный инсталл ПЕРЕЗАПИСЫВАЕТ вид и заряды (второй
    // букмекер при висящем маркере сам оплатится альтернативой своим кастом — «следующая карта»).
    // Потребление/уплата — RunCastRouterSystem (актив, жертвы роллит он) и ReplayActionSystem (пассив,
    // по ActionCastData.AltPaid*). Free-касты маркер не трогают. Урон DamageSelf штатный → Вуду-редирект
    // работает (задумка); уплата сбросом фейрит OnDiscard-триггеры (синергия discard-архетипа).
    // NonTarget (в ассете AbilityNonTarget). СИНК: Apply ре-ранится на обоих → маркер зеркален.
    [Serializable]
    public sealed class InstallAltCostEffect : EffectBase, ICasterScopedEffect
    {
        [Tooltip("Вид уплаты: урон себе (эффективная стоимость) / сброс случайной карты руки / " +
                 "жертва своего случайного существа / карта из колоды в кладбище.")]
        public AltCostKind Kind = AltCostKind.DamageSelf;
        [Tooltip("На сколько СЛЕДУЮЩИХ разыгранных карт действует (заряды маркера).")]
        public int Charges = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (PlayerEntity < 0) return;
            var pool = world.GetPool<AltCostComponent>();
            if (!pool.Has(PlayerEntity)) pool.Add(PlayerEntity);
            ref var alt = ref pool.Get(PlayerEntity);
            alt.Kind    = Kind;                          // повторный инсталл перезаписывает вид и заряды
            alt.Charges = Charges <= 0 ? 1 : Charges;    // старые ассеты без поля (0) → 1
            // Рука должна подсветиться СРАЗУ (карты играбельны при любых ресурсах) — тот же сигнал
            // пересчёта, что у Гиперинфляции/пер-карточных кост-баффов.
            GameEventBus.Publish(new CostModifierChangedEvent());
        }
    }

    // === class (OOP) === ПАССИВ «карта стоит РОВНО Cost, пока выполнено условие» (Запойное время: Cost=0 +
    // ConditionRoot=NoCreaturesInDeckCondition). НЕ триггер-эффект (паттерн BuffPerCharmEffect): работает
    // с Init (карта создана — в колоде/руке, на ОБОИХ клиентах), реактивно по ConditionRoot.Changed.
    // Реализация: Override в кост-компоненте карты (Cost = принудительное значение, база и ВЕСЬ стек
    // модификаторов игнорируются; потеря условия возвращает обычный расчёт) — а не «скидка», чтобы
    // печатная цена честно ЗАМЕНЯЛАСЬ независимо от навешанных баффов/дебаффов. Глобальный модификатор
    // игрока (Гиперинфляция) — по-прежнему ПОВЕРХ (0 + 1 = 1), консистентно с остальными нулёвыми картами.
    // В ассете кладётся в способность БЕЗ триггеров (напр. AbilityToSelf; Apply — no-op). Пустой
    // ConditionRoot → эффект неактивен (забытое условие не должно молча раздавать бесплатные карты).
    // СИНК: условие зеркально (скан по зеркальным тегам зон) + Init на обоих клиентах → Override зеркален.
    [Serializable]
    public sealed class SetCostWhileConditionEffect : EffectBase
    {
        [Tooltip("Принудительная стоимость, пока условие выполнено (0 = бесплатно).")]
        public int Cost = 0;

        EcsWorld _world;
        int _card = -1;
        bool _applied;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);   // инитит ConditionRoot
            _world = world; _card = cardEntity;
            if (ConditionRoot != null) ConditionRoot.Changed += Sync;
            Sync();
        }

        public override void Dispose()
        {
            if (ConditionRoot != null) ConditionRoot.Changed -= Sync;
            if (_applied) Toggle(false);   // карта уходит/ре-инит — не оставляем висячий Override
            base.Dispose();
        }

        void Sync()
        {
            bool ready = ConditionRoot != null && ConditionRoot.IsReady;
            if (ready == _applied) return;
            Toggle(ready);
        }

        void Toggle(bool on)
        {
            _applied = on;
            if (_world == null || _card < 0) return;
            if (on) BuffCost.SetOverride(_world, _card, Cost);
            else    BuffCost.ClearOverride(_world, _card);
        }

        public override void Apply(EcsWorld world, int cardEntity, int target) { }   // пассив: резолвить нечего
    }

    // === class (OOP) === Установить замену добора владельцу (Адовый червь): до конца матча вместо
    // обычного добора в начале хода — «смотри LookCount, выбери одну». NonTarget (target = игрок).
    // Персистентно (не снимается). Перехват/выбор — RunDrawReplacementSystem (+ UI PickupWindow).
    [Serializable]
    public sealed class InstallDrawReplacementEffect : EffectBase, ICasterScopedEffect
    {
        public int LookCount = 3;
        public bool DestroyChosen = true;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<DrawReplacementComponent>();
            if (!pool.Has(target)) pool.Add(target);
            ref var r = ref pool.Get(target);
            r.LookCount = LookCount;
            r.DestroyChosen = DestroyChosen;
        }
    }

    // === class (OOP) === УНИВЕРСАЛЬНАЯ обёртка «повторить Inner N раз». N — фиксированный или из
    // контекста цепочки (ChainKilled = число погибших на прошлой стадии). Так «сколько раз» отделено
    // от «что делать»: RepeatEffect{ChainKilled, DrawCardEffect}=добрать по числу убитых,
    // {ChainKilled, GainRandomCardEffect}=случайная карта за убитого, и т.д. — для ЛЮБОГО эффекта.
    // Источники счёта расширяются добавлением в enum (не трогая сами эффекты).
    [Serializable]
    public sealed class RepeatEffect : EffectBase, IDynamicValue
    {
        // ВАЖНО: значения ЗАКРЕПЛЕНЫ явно. Unity сериализует enum по ЧИСЛУ, а ассеты хранят это число —
        // любая перестановка/вставка члена без явного значения ломает уже настроенные карты (Грыз считал
        // не то, потому что MatchArchetypeInvoked сдвинулся с 7 на 8). Новые члены добавлять ТОЛЬКО с новым
        // числом в конце, существующие числа не менять.
        public enum CountSource
        {
            Fixed = 0, ChainKilled = 1,
            MatchPlayedSelf = 2,        // сколько раз СВОЯ карта разыграна в матче (Позвать рой)
            MatchPlayedCard = 3,        // сколько раз разыграна КОНКРЕТНАЯ карта CountCard (любая по ассету)
            MatchDrawnSelf = 4,         // сколько раз СВОЯ карта взята (Вонючее облако)
            MatchDrawnCard = 5,         // сколько раз взята КОНКРЕТНАЯ карта CountCard
            MatchGenerated = 6,         // сколько раз карта CountCard замешана в матче ВСЕГО (любым игроком) — Гнидальф → Вонючее облако
            MatchGeneratedSelf = 7,     // сколько раз карта CountCard замешана ВАМИ (по инициатору, в т.ч. через гранёные эффекты — Газовое вздутие)
            MatchArchetypeInvoked = 8,  // сколько существ архетипа ArchetypeKey призвано (Грыз → "Imp")
            OwnCreaturesOnBoard = 9,    // сколько ЖИВЫХ существ у владельца на поле СЕЙЧАС (включая источник, если он на поле)
            MatchSpellsPlayedSelf = 10, // сколько ЗАКЛИНАНИЙ разыграно владельцем в матче (Моментум)
            SelfResolves = 11,          // порядковый номер ТЕКУЩЕГО применения этой способности: 1,2,3… (Нечищенный источник). НЕ для цепочек (AbilityChain)
            ChainDiscardedCost = 12,    // стоимость карты, СБРОШЕННОЙ прошлой стадией цепочки (Утилизация: урон = ей). Читает ChainContext.LastDiscardedCost
            Stat = 13,                  // ТЕКУЩИЙ стат (StatKind) источника или его владельца (StatSource) — «повторить по атаке существа»
            CountByFilter = 14,         // существа на поле, прошедшие CountFilters (пусто = все живые, любая сторона) — «Расстрелять»: EnemyTargetFilter
        }

        public CountSource Source = CountSource.Fixed;
        public int FixedCount = 1;

        [Tooltip("Для Source=Stat: какой стат читаем.")]
        public StatKind Stat = StatKind.Attack;
        [Tooltip("Для Source=Stat: чей стат — сам источник или его владелец-игрок (нужно для Mana/Gold).")]
        public StatSourceEntity StatSource = StatSourceEntity.Self;

        [Tooltip("Показывать число повторов в описании как *N* (карты-с-уровнями/динамический count). По умолчанию " +
                 "ВЫКЛ — чтобы не добавлять лишний слот и не сдвигать *N* в описаниях уже настроенных карт.")]
        public bool ShowCountInDescription = false;

        [Tooltip("Для MatchPlayedCard/MatchDrawnCard/MatchGenerated/MatchGeneratedSelf: ассет карты, чьи розыгрыши/взятия/генерации считаем.")]
        public ScriptableObject CountCard;
        [Tooltip("Для MatchArchetypeInvoked: архетип, чьи призывы считаем (Грыз → ImpArchetype). Ключ берётся из него.")]
        [SerializeReference] public ICreatureTag Archetype;

        [Tooltip("Для Source=CountByFilter: фильтры существ на поле, которые считаем (пусто = все живые, " +
                 "любая сторона). Та же семантика combos, что в AbilityToField.Filters — селекторы " +
                 "(Ally/Enemy) между собой ИЛИ, остальное — И (см. TargetGather.FiltersOk).")]
        [SerializeReference] public List<ITargetFilter> CountFilters = new();

        [SerializeReference] public IEffect Inner;

        // ИИ: RepeatEffect — обёртка «сколько раз», суть карты определяет ВНУТРЕННИЙ эффект.
        public override AiEffectRole AiRole => Inner != null ? Inner.AiRole : AiEffectRole.Generic;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            Inner?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            Inner?.Dispose();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Inner == null) return;
            int n = ResolveCount(world, cardEntity, target);
            // ВРЕМЕННАЯ ДИАГНОСТИКА (Вонючее облако «не наносит урон»): видим источник счёта, n, модель карты,
            // сущность игрока и счётчик. n=0 → проблема в трекинге (CountsByModelId не инкрементнулся / не тот
            // playerEntity / не та модель). n≥1, но урона нет → проблема в Inner/цели (target<0 или не тот).
            var mp = world.GetPool<CardModelComponent>();
            int selfModel = mp.Has(cardEntity) ? mp.Get(cardEntity).ModelId : -1;
            var cp = world.GetPool<MatchCounterComponent>();
            int played = (PlayerEntity >= 0 && cp.Has(PlayerEntity) && cp.Get(PlayerEntity).CountsByModelId != null
                          && cp.Get(PlayerEntity).CountsByModelId.TryGetValue(selfModel, out int pv)) ? pv : -999;
            UnityEngine.Debug.Log($"[Repeat] card={cardEntity} src={Source} n={n} selfModel={selfModel} playerEntity={PlayerEntity} target={target} CountsByModel[{selfModel}]={played} innerReady={Inner.IsReady}");
            for (int i = 0; i < n; i++)
                if (Inner.IsReady)
                    Inner.Apply(world, cardEntity, target);
        }

        int ResolveCount(EcsWorld world, int cardEntity, int target)
            => AbilityCount.Resolve(world, PlayerEntity, cardEntity, Source, FixedCount, CountCard, Archetype, Stat, StatSource, target, CountFilters);

        // IDynamicValue (opt-in): *N* = резолвнутое число повторов. Та же величина, что читает Apply (инвариант).
        public int DynamicValueCount => ShowCountInDescription ? 1 : 0;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity)
            => AbilityCount.Resolve(world, playerEntity, cardEntity, Source, FixedCount, CountCard, Archetype, Stat, StatSource, -1, CountFilters);
    }

    // === helper === резолв «сколько» для RepeatEffect И count-driven эффектов (SummonTokensByCount).
    // Матч-счётчики берём с сущности игрока-владельца (зеркальны на обоих клиентах → детерминировано).
    internal static class AbilityCount
    {
        public static int Resolve(EcsWorld world, int playerEntity, int cardEntity,
                                  RepeatEffect.CountSource source, int fixedCount,
                                  ScriptableObject countCard, ICreatureTag archetype,
                                  StatKind statKind = StatKind.Attack, StatSourceEntity statSource = StatSourceEntity.Self,
                                  int target = -1, IReadOnlyList<ITargetFilter> countFilters = null)
        {
            if (source == RepeatEffect.CountSource.Fixed) return fixedCount;
            if (source == RepeatEffect.CountSource.ChainKilled) return ChainContext.CurrentKilled;
            if (source == RepeatEffect.CountSource.ChainDiscardedCost) return ChainContext.LastDiscardedCost;
            if (source == RepeatEffect.CountSource.Stat)
            {
                int statEntity = StatSourceUtil.Resolve(world, cardEntity, statSource, target);
                return StatCompareTargetFilter.TryRead(world, statEntity, statKind, out int sv) ? sv : 0;
            }

            // SelfResolves — номер текущего применения способности (скрэтч ставит RunResolveAbilityQueueSystem
            // ДО эффектов → в обычном резолве всегда ≥1, первое срабатывание = 1; на пассиве резолв идёт тем же
            // путём → зеркально). 1,2,3… — «Нечищенный источник». 0 бывает ТОЛЬКО ВНЕ резолва — отложенные
            // применения (модификаторы генерации в CreateCardSystem, модификаторы дискавера в PlacePicked,
            // стадии цепочек у пассива): там «номер применения» не определён — трактуем как ПЕРВОЕ (1),
            // иначе Inner молча не сработал бы ни разу (мёртвая карта вместо явного поведения).
            if (source == RepeatEffect.CountSource.SelfResolves)
                return Math.Max(1, AbilityResolveContext.ResolveCount);

            // OwnCreaturesOnBoard — живые существа владельца источника на поле в момент резолва.
            // Борд зеркален на обоих клиентах при резолве (актив применяет, пассив ре-ранит по снапшоту
            // до оседания следующих действий) → счёт детерминирован, отдельный синк не нужен.
            if (source == RepeatEffect.CountSource.OwnCreaturesOnBoard)
                return RuleUtil.CountCreaturesOnBoard(world, OwnerIdOf(world, cardEntity, playerEntity));

            // CountByFilter — общий счётчик существ на поле по ПРОИЗВОЛЬНОМУ набору фильтров (Расстрелять:
            // EnemyTargetFilter → «за каждого врага»). В отличие от OwnCreaturesOnBoard не завязан на владельца
            // жёстко — пусто = все живые существа, любая сторона. Своя мини-версия TargetGather.FiltersOk
            // (селекторы Ally/Enemy между собой ИЛИ, остальное — И): Systems→Ability однонаправленно,
            // AbilityCount (в Ability) не может звать TargetGather (в Systems).
            if (source == RepeatEffect.CountSource.CountByFilter)
            {
                int n = 0;
                foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Exc<DeadTag>().End())
                    if (CreatureFiltersOk(world, e, cardEntity, playerEntity, countFilters))
                        n++;
                return n;
            }

            // MatchGenerated — ГЛОБАЛЬНО: «за каждое замешанное в этом матче» (кем угодно, в любую колоду —
            // Гнидальф/Старый колдун мешают оппоненту, Газовое вздутие — оппонент сам себе). Суммируем
            // GeneratedByModelId по счётчикам ВСЕХ игроков. Генерация идёт через резолв способности (на обоих
            // клиентах) → CardGeneratedEvent зеркален → сумма одинакова на активе/пассиве.
            if (source == RepeatEffect.CountSource.MatchGenerated)
            {
                if (!(countCard is ICreatable cc)) return 0;
                var gpool = world.GetPool<MatchCounterComponent>();
                int total = 0;
                foreach (var pe in world.Filter<PlayerComponent>().End())
                    if (gpool.Has(pe)) total += Get(gpool.Get(pe).GeneratedByModelId, cc.CardId);
                return total;
            }

            var pool = world.GetPool<MatchCounterComponent>();
            if (!pool.Has(playerEntity)) return 0;
            ref var c = ref pool.Get(playerEntity);

            switch (source)
            {
                case RepeatEffect.CountSource.MatchPlayedSelf:
                    return Get(c.CountsByModelId, SelfModel(world, cardEntity));
                case RepeatEffect.CountSource.MatchPlayedCard:
                    return countCard is ICreatable pc ? Get(c.CountsByModelId, pc.CardId) : 0;
                case RepeatEffect.CountSource.MatchDrawnSelf:
                    return Get(c.DrawnByModelId, SelfModel(world, cardEntity));
                case RepeatEffect.CountSource.MatchDrawnCard:
                    return countCard is ICreatable dc ? Get(c.DrawnByModelId, dc.CardId) : 0;
                // MatchGenerated обрабатывается выше (глобально по всем игрокам).
                case RepeatEffect.CountSource.MatchGeneratedSelf:   // только ваш бакет (атрибуция по инициатору)
                    return countCard is ICreatable gc ? Get(c.GeneratedByModelId, gc.CardId) : 0;
                case RepeatEffect.CountSource.MatchArchetypeInvoked:
                    var key = archetype?.Key;
                    return (c.InvokedByArchetype != null && !string.IsNullOrEmpty(key)
                            && c.InvokedByArchetype.TryGetValue(key, out int v)) ? v : 0;
                case RepeatEffect.CountSource.MatchSpellsPlayedSelf:
                    return c.SpellsPlayed;
                default: return 0;
            }
        }

        static int Get(System.Collections.Generic.Dictionary<int, int> d, int key)
            => (d != null && d.TryGetValue(key, out int v)) ? v : 0;

        // Мини-копия TargetGather.FiltersOk (та сборка недоступна отсюда, см. коммент у CountByFilter):
        // селекторы (ITargetSelector — Ally/Enemy и т.п.) между собой ИЛИ, остальные фильтры — И. Пусто/null → true.
        static bool CreatureFiltersOk(EcsWorld world, int candidate, int casterCard, int casterPlayer, IReadOnlyList<ITargetFilter> filters)
        {
            if (filters == null || filters.Count == 0) return true;

            bool hasSelector = false;
            bool selectorHit = false;
            foreach (var f in filters)
            {
                if (f == null) continue;
                if (f is ITargetSelector)
                {
                    hasSelector = true;
                    if (f.Match(world, candidate, casterCard, casterPlayer)) selectorHit = true;
                }
                else if (!f.Match(world, candidate, casterCard, casterPlayer))
                {
                    return false;
                }
            }
            return !hasSelector || selectorHit;
        }

        static int SelfModel(EcsWorld world, int cardEntity)
        {
            var p = world.GetPool<CardModelComponent>();
            return p.Has(cardEntity) ? p.Get(cardEntity).ModelId : -1;
        }

        // «Ваши существа» = сторона владельца ИСТОЧНИКА (карта могла сменить владельца — Обращение),
        // фолбэк — PlayerId игрока-владельца способности.
        static int OwnerIdOf(EcsWorld world, int cardEntity, int playerEntity)
        {
            var owner = world.GetPool<OwnerComponent>();
            if (owner.Has(cardEntity)) return owner.Get(cardEntity).OwnerId;
            var player = world.GetPool<PlayerComponent>();
            return player.Has(playerEntity) ? player.Get(playerEntity).PlayerId : -1;
        }
    }
}
