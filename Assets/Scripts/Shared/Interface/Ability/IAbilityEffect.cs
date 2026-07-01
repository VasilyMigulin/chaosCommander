using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IAbilityEffect : IComponent
    {
        public int AbilityEntity { get; }
        /// <summary>
        /// Копируем компонент на сущность эффекта, чтобы в дальнейшем использовать
        /// </summary>
        /// <param name="world"></param>
        /// <param name="effectEntity"></param> 
        void ApplyEffect(EcsWorld world, int effectEntity);
    }
}
