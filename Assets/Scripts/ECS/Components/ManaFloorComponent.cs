namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// ПОЛ маны игрока до конца матча (Вечная попойка, спелл-архетип). В начале КАЖДОГО хода RunTurnStartSystem
    /// поднимает ману до Floor, если она ниже (выше — не трогает). Ставится SetManaFloorEffect (OnMatchStart),
    /// персистентен до конца матча. Не путать с обычным доходом маны (её нет — только за киллы существ), это
    /// именно нижняя граница, чтобы колода без существ имела ману на заклинания.
    /// </summary>
    public struct ManaFloorComponent
    {
        public int Floor;
    }
}
