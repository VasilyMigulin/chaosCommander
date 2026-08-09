namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Свойство «Неуязвимый»: блок ЛЮБОГО урона (TakeDamageSystem.ApplyDamage — единая точка, бой и
    /// способности) и принудительного уничтожения (DestroyEffect/DestroyAllExceptTargetEffect). НЕ блокирует
    /// трансмутацию и смерть от обнуления HP дебаффом статов (LethalHealthSystem) — юзер явно разрешил оба
    /// обхода. Маркер, без состояния.
    /// </summary>
    public struct InvulnerableTag { }
}
