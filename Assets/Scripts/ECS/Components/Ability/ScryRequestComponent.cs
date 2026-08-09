namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Запрос «предсказания» (scry): NonTarget-эффект «посмотрите LookCount ВЕРХНИХ карт колоды, выберите
    /// 1 — она НАВЕРХ, остальные — В КОНЕЦ колоды (в исходном относительном порядке)». В отличие от
    /// DiscoverRequestComponent ничего не покидает зону колоды и не создаётся заново — реордер СУЩЕСТВУЮЩИХ
    /// карт, поэтому терминал проще (нет Dest/TakeOwnership/Modifiers/FromPool/Zone/Filters).
    /// Окно/синк — тот же общий канал пика (PickupWindow/CardPickBrokerSystem/CardPickResolvedNetEvent),
    /// что у раскопки: RunScrySystem.
    /// </summary>
    public struct ScryRequestComponent
    {
        public int SourceCardEntity;
        public int OwnerPlayerEntity;
        public int OwnerId;
        public int LookCount;

        public bool Offered;
        public int  RequestId;
        public int  Seq;

        public int[]    ShownTokens;   // реальные сущности верха колоды (в порядке «от верха»)
        public string[] ShownExp;
        public int[]    ShownCardId;
    }
}
