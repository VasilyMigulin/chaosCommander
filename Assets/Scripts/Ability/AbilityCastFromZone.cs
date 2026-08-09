using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // КЛАСТЕР 6 (часть: копия последнего спелла + перенос карты зоны в руку). Барабук («разыгрывает
    // случайное заклинание из колоды» — реально кастует/резолвит) — отдельным шагом (сложнее).
    // Очиститель («случайное заклинание другого цвета в руку») = готовый GainRandomCardEffect{Pool} + OnTurnEnd.
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Положить в руку владельца КОПИЮ последнего разыгранного им заклинания (Попадос).
    // Идентичность — из LastPlayedSpellComponent игрока (обновляет LastPlayedSpellTrackerSystem на CardPlayed,
    // синк на обоих). Создание — Generate (детерм. ключ) → синк бесплатный.
    [Serializable]
    public sealed class CopyLastSpellToHandEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<LastPlayedSpellComponent>();
            if (!pool.Has(PlayerEntity)) return;
            ref var last = ref pool.Get(PlayerEntity);
            if (!last.HasValue) return;
            GenerateCardEffect.Spawn(world, cardEntity, last.ExpansionId, last.ModelId, toHand: true);
        }
    }

    // === class (OOP) === Перенести цель-карту (из колоды/кладбища) в руку ЕЁ владельца (Библиотекарь:
    // Random Zone=Deck + [OwnerOwned, CardType=Spell] → MoveToHand). target — карта в зоне (выбор синкается
    // target-ключом). Владелец не меняется (своя карта → своя рука).
    [Serializable]
    public sealed class MoveToHandEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(target)) return;
            int ownerId = ownerPool.Get(target).OwnerId;

            var deckTag = world.GetPool<DeckTag>();
            if (deckTag.Has(target)) { deckTag.Del(target); ZoneListUtil.RemoveFromDeck(world, target, ownerId); }
            var graveTag = world.GetPool<GraveTag>();
            if (graveTag.Has(target)) graveTag.Del(target);

            int pe = ZoneListUtil.FindPlayerEntity(world, ownerId);

            // Лимит руки (единое правило, HandSpace): места нет → карта СГОРАЕТ вместо переноса в руку.
            // Зональные теги уже сняты выше, поэтому «оставить как было» невозможно — только сжечь.
            if (pe < 0 || !HandSpace.HasRoom(world, pe))
            {
                HandSpace.Burn(world, target, "перенос в руку (MoveToHand)");
                return;
            }

            var handTag = world.GetPool<HandTag>();
            if (!handTag.Has(target)) handTag.Add(target);

            ZoneListUtil.AddToHand(world, target, pe);
            // SourceEntity = копатель (cardEntity) — UI летит визуально ОТ него, а не «из-за края экрана»
            // (см. HandUISystem.TryGet): раскопка существом/заклинанием (Библиотекарь и т.п.).
            GameEventBus.Publish(new CardDrawnEvent { CardEntity = target, PlayerId = pe, SourceEntity = cardEntity });
        }
    }
}
