namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Величина последнего урона, полученного игроком. Пишет TakeDamageSystem; читают эффекты, которым
    /// нужна сумма только что полученного урона (Вуду-будду → редирект). Зеркально на обоих клиентах
    /// (урон синкается) → эффект редиректа берёт одинаковое значение без спец-канала суммы.
    /// </summary>
    public struct LastDamageTakenComponent
    {
        public int Amount;
    }
}
