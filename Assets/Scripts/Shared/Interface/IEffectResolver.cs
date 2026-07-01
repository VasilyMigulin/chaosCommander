namespace Game.Core.Shared.Interface
{
    public interface IEffectResolver
    {
        void Resolve(Leopotam.EcsLite.EcsWorld world, int targetEntity, int abilityEntity);
    }
}
