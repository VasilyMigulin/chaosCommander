namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Безвозвратное удаление TargetEntity из игры: запись пропадает из всех зон
    /// (рука/колода/доска/кладбище), визуал уничтожается, сама сущность удаляется
    /// из мира. Никаких триггеров смерти не запускает.
    /// </summary>
    public struct BanishEffectComponent { }
}
