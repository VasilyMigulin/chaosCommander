namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер «этим игроком управляет ИИ» (PvE). Вешается на сущность игрока-оппонента в
    /// InitPlayerSystem (PvE-ветка). ВАЖНО: у ИИ-игрока НЕТ RemoteComponent (он не сетевой) —
    /// соло-ветки систем (EndTurnRequestSystem) различают PvE по этому маркеру.
    /// Ходами ИИ управляет RunAiTurnSystem (когда на этой сущности ActiveState).
    /// </summary>
    public struct AiPlayerComponent { }
}
