namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Память об ИЗНАЧАЛЬНОМ владельце существа (первый, до любых перехватов контроля). Ставится ОДИН раз
    /// при первом TakeControlEffect (фиксирует владельца до захвата) и больше не перезаписывается. Текущий
    /// владелец — в OwnerComponent.OwnerId. «Изначальный владелец» = OriginalOwnerComponent.OwnerId если есть,
    /// иначе OwnerComponent.OwnerId (никогда не перехватывали). СИНК: OwnerId абсолютен, ставится в резолве на обоих.
    /// </summary>
    public struct OriginalOwnerComponent
    {
        public int OwnerId;
    }
}
