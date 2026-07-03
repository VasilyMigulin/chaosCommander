namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Маркер: эффект действует на ВЛАДЕЛЬЦА способности (добор/золото/мана/кост-модификатор), а НЕ на
    /// конкретную цель. Такой эффект применяется РОВНО ОДИН РАЗ за резолв, независимо от числа целей —
    /// иначе в мультицельной способности (Field/Random c N целями) он отработал бы N раз (добор N карт и т.п.).
    ///
    /// RunResolveAbilityQueueSystem применяет помеченные эффекты один раз с target = сущностью игрока-владельца
    /// (AbilityOwnerComponent.PlayerEntity), а остальные — по каждой цели как обычно. Для NonTarget-способностей
    /// (target = [владелец]) поведение не меняется.
    /// </summary>
    public interface ICasterScopedEffect : IEffect { }
}
