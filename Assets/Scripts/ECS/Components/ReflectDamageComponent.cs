namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Маркер на ИГРОКЕ (Вуду-будду): на ЕГО ходу входящий урон БЛОКИРУЕТСЯ (ReflectDamageSystem снимает
    /// TakeDamageEvent ДО TakeDamageSystem → HP не трогается) и редиректится оппоненту способностью чары-
    /// токена (обычный пайплайн). Вешает спелл (AddPlayerReflectMarkerEffect). Персистентный (до конца матча).
    /// </summary>
    public struct ReflectDamageComponent { }
}
