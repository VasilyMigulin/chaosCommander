using System;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // БАЗА ЭФФЕКТОВ — снимает повторяющийся boilerplate (ConditionRoot/Init/Dispose/
    // IsReady), который иначе дублируется в каждом IEffect. Наследник реализует только
    // Apply. Опциональное составное условие ConditionRoot: null = всегда готов.
    //
    // Кэшируем PlayerEntity (сущность игрока-владельца) — многим эффектам нужен именно
    // владелец, а не произвольная цель (добор/ресурсы/призыв на свою сторону).
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    public abstract class EffectBase : IEffect
    {
        [SerializeReference] public Condition ConditionRoot;

        protected int PlayerEntity { get; private set; }

        public virtual void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            PlayerEntity = playerEntity;
            ConditionRoot?.Init(new AbilityContext { World = world, CardEntity = cardEntity, PlayerEntity = playerEntity });
        }

        public virtual void Dispose() => ConditionRoot?.Dispose();

        public bool IsReady => ConditionRoot == null || ConditionRoot.IsReady;

        public abstract void Apply(EcsWorld world, int cardEntity, int target);
    }
}
