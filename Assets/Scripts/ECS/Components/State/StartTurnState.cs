namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Транзитный маркер каскада НАЧАЛА хода. Вешается на сущность игрока, который становится активным
    /// (на его клиенте). Пока он висит, идёт каскад: восстановление ресурсов/добор/OnTurnStart-способности,
    /// и игрок ещё НЕ активен (нет ActiveState) → не может действовать. Когда каскад осел
    /// (Resolved=true и очередь способностей/анимации пусты), RunActivateSystem вешает ActiveState
    /// и снимает этот маркер.
    /// </summary>
    public struct StartTurnState
    {
        /// <summary>Одноразовая часть (ресурсы/добор/поднятие триггеров) уже выполнена.</summary>
        public bool Resolved;

        public int TurnNumber;
        public int PersonalTurnNumber;
    }
}
