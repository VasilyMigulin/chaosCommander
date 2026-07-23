namespace Game.Core.Ecs.Components
{
    /// <summary>Константы хода (заменяет InitTurnSystem.TurnDuration).</summary>
    public static class TurnConfig
    {
        /// <summary>Длительность хода, сек (1:20). Кнопка «Конец хода» завершает раньше; таймер — страховка.</summary>
        public const float TurnDuration = 80f;
    }
}
