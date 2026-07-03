namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Гейт «призыв → OnCast» (#2). Вешается RunInvokeCreatureSystem, когда существо вышло на стол:
    /// пока компонент висит, «при разыгрывании» (CardCastEvent) ещё НЕ опубликован — существо должно
    /// сначала появиться и отыграть анимацию призыва. RunPendingOnCastSystem снимает его и публикует
    /// CardCastEvent (если FireCast) после окончания анимации призыва.
    ///
    /// Пока висит — его ждут конец хода / активация / гейм-овер / ввод (как AttackAnimPendingTag), чтобы
    /// отложенный OnCast (в т.ч. SelfDestruct Всадников) успел отработать до передачи хода. Только активный
    /// клиент (пассив OnCast реплеит снапшотами, PendingOnCast ему не вешают).
    /// </summary>
    public struct PendingOnCastComponent
    {
        /// <summary>Публиковать ли CardCastEvent после призыва (!NotCast — настоящий каст, а не тихий призыв).</summary>
        public bool FireCast;

        /// <summary>Анти-софтлок: Time.time, после которого форсим публикацию (нет вью/аниматора).</summary>
        public float Deadline;
    }
}
