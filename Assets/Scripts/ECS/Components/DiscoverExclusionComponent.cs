using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>Карты (ExpansionId+CardId), уже выбранные раскопками ЭТОЙ карты-источника за текущий каст
    /// (Проклятье для принцессы: несколько проходов по ОДНОМУ пулу эффектов подряд — второй проход не
    /// должен предлагать/выбрать то, что уже взял первый). Живёт на карте-источнике (cardEntity);
    /// RecordDiscoverPickEffect дописывает, DiscoverFromPoolEffect.Configure фильтрует пул по ней.</summary>
    public struct DiscoverExclusionComponent
    {
        public List<string> UsedKeys;

        public static string KeyOf(string expansionId, int cardId) => expansionId + ":" + cardId;
    }
}
