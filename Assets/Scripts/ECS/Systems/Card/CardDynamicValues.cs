using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Собирает «живые» значения эффектов карты по порядку для подстановки в описание (*N*).
    /// Проходит способности карты в порядке авторинга → эффекты в порядке массива → каждый
    /// IDynamicValue выдаёт свои значения. Результат — плоский список; i-е значение идёт в i-й
    /// токен *N* в тексте (см. CardDescriptionFormatter).
    ///
    /// Использование (живой ре-рендер описания под модификаторы):
    ///   var live = CardDynamicValues.Collect(world, cardEntity, playerEntity);
    ///   string desc = CardDescriptionFormatter.Format(descKey, template, cardType, turns, live);
    /// Вне боя (библиотека/колодостроитель) этот сбор не нужен — передаём null и берём числа из текста.
    /// </summary>
    public static class CardDynamicValues
    {
        public static List<int> Collect(EcsWorld world, int cardEntity, int playerEntity)
        {
            var values = new List<int>();

            var contPool = world.GetPool<AbilityContainerComponent>();
            if (!contPool.Has(cardEntity)) return values;

            var abilityEntities = contPool.Get(cardEntity).AbilityEntities;
            if (abilityEntities == null) return values;

            var effPool = world.GetPool<AbilityEffectContainerComponent>();
            foreach (int abilityEntity in abilityEntities)
            {
                if (abilityEntity < 0 || !effPool.Has(abilityEntity)) continue;
                var effects = effPool.Get(abilityEntity).Effects;
                if (effects == null) continue;

                foreach (var eff in effects)
                {
                    if (eff is not IDynamicValue dyn) continue;
                    int count = dyn.DynamicValueCount;
                    for (int i = 0; i < count; i++)
                        values.Add(dyn.GetDynamicValue(i, world, cardEntity, playerEntity));
                }
            }
            return values;
        }
    }
}
