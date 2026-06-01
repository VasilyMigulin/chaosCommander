using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Двинуть кастера на клетку, сохранённую предыдущим шагом цепочки
    /// (ChainStateComponent.Captured*). Обычно используется парой
    /// «Destroy → MoveSourceToCell» с TargetSource = Source.
    /// </summary>
    public class MoveSourceToCellEffect : AbilityEffect
    {
        public MoveSourceToCellEffect() { }
        private MoveSourceToCellEffect(MoveSourceToCellEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<MoveSourceToCellEffectComponent>();
            if (!pool.Has(effectEntity))
                pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new MoveSourceToCellEffect(this);
    }
}
