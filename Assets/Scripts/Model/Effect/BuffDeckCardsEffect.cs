using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Бафф статов всем картам в указанных зонах владельца цели. Используется
    /// «Молитва о здравии» (+2 HP в колоде и на поле), «Патриарх» (+1/1 в колоде).
    /// </summary>
    public class BuffDeckCardsEffect : AbilityEffect
    {
        public BuffZone Zones = BuffZone.Deck;
        public int AttackBonus;
        public int HealthBonus;
        public int SpeedBonus;

        public BuffDeckCardsEffect() { }
        private BuffDeckCardsEffect(BuffDeckCardsEffect s)
        {
            Zones = s.Zones; AttackBonus = s.AttackBonus; HealthBonus = s.HealthBonus; SpeedBonus = s.SpeedBonus;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<BuffDeckCardsEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.Zones = Zones; c.AttackBonus = AttackBonus; c.HealthBonus = HealthBonus; c.SpeedBonus = SpeedBonus;
            }
        }

        public override IAbilityEffect Clone() => new BuffDeckCardsEffect(this);
    }
}
