using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Model.Ability.FieldAbility
{
    public class AbilityToField : Ability
    {
        public int FieldHeight;
        public int FieldWidth;
        public GameObject FieldPrefView;
        public GameObject HitPrefView;

        protected override void OnInit(EcsWorld world, int entity, int entityCard)
        { 
            if (FieldPrefView != null)
            {
                world.GetPool<FieldViewComponent>().Add(entity).Prefab = FieldPrefView;
            }
            if (HitPrefView != null)
            {
                world.GetPool<HitViewComponent>().Add(entity).Prefab = HitPrefView;
            }
        }
    }
}
