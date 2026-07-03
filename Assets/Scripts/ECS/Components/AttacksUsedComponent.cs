namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Сколько раз существо атаковало в ТЕКУЩЕМ ходу. Сбрасывается в начале хода владельца
    /// (RunTurnStartSystem, рядом со сбросом скорости). Лимит атак за ход = базовый 1 (+ будущие
    /// бонусы вроде «Неистовства ветра»), см. RunSelectCellSystem.MaxAttacksPerTurn.
    ///
    /// Гейт атаки живёт на ВВОДЕ (RunSelectCellSystem, только активный клиент) → синка не требует:
    /// пассив реплеит присланные атаки без повторной проверки лимита.
    /// </summary>
    public struct AttacksUsedComponent
    {
        public int Value;
    }
}
