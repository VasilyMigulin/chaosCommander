namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Просьба переставить ВЬЮШКУ существа на клетку (Row/Col/OwnerId) — чистый визуал, не логика.
    /// Логическую позицию (BoardPositionComponent) уже выставил эффект (напр. «занять место» Бешеной
    /// бабки); RepositionViewSystem двигает GameObject и снимает компонент. Транзиент.
    /// Визуал локален → синка не требует (эффект ставит позицию на обоих клиентах детерминированно).
    /// </summary>
    public struct ViewRepositionRequest
    {
        public int Row;
        public int Col;
        public int OwnerId;
    }
}
