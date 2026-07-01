namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос розыгрыша карты. Ставится UI (через IGameStateContext.World.GetPool)
    /// либо временным RunCardPlayRequestBridgeSystem из старого bus-эвента.
    /// Жизненный цикл: один кадр — потребляется Run*CastSystem-цепочкой.
    /// </summary>
    public struct RequestCardCastEvent
    {
        public int CardEntity;
        public bool Free;   // true → пропустить оплату стоимости (форс-розыгрыш: Барабук «разыграть из колоды»)
    }
}
