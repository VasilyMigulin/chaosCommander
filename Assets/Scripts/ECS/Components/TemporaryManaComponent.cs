namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Сколько маны вернётся обратно (вычтется) у игрока в конце ЕГО хода.
    /// Используется «Освежающий напиток» (+5 маны до конца хода).
    /// </summary>
    public struct TemporaryManaComponent
    {
        public int RefundAmount;
    }
}
