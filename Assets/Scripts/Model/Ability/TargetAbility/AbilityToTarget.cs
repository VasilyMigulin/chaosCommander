using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Model.Ability.Target
{
    public class AbilityToTarget : Ability
    {
        public int TargetCount;
        public GameObject ProjectilePrefView;
        public GameObject HitPrefView;


        protected override void OnInit(Leopotam.EcsLite.EcsWorld world, int entity, int entityCard)
        {
            if (ProjectilePrefView != null)
            {
                world.GetPool<ProjectileViewComponent>().Add(entity).Prefab = ProjectilePrefView;
            }

            if (HitPrefView != null)
            {
                world.GetPool<HitViewComponent>().Add(entity).Prefab = HitPrefView;
            }
        }
    }
}