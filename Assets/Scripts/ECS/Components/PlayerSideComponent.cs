namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Сторона поля игрока. Player1 — ближняя к игроку 1, Player2 — ближняя к игроку 2.
    /// Вешается на entity игрока и на entity карт/клеток принадлежащих ему.
    /// </summary>
    public struct PlayerSideComponent
    {
        public int Side; // 1 или 2
    }
}
