namespace Game.Core.Service
{
    /// <summary>
    /// Вид воздействия на карту В РУКЕ — по нему UI (CardBaseView) выбирает свой punch/UI-VFX-фидбэк.
    /// Живёт в Game.Core.Service (общая enum-сборка): её видят все три стороны — UI-карта
    /// (AwesomeUI.Core.Card), шина (Game.Core.Events, несёт CardAffectedInHandUIEvent) и эффекты
    /// (Game.Core.Ability, публикуют через CardFeedbackUtil). Game.Core.Shared не подходит — на неё
    /// не ссылается Game.Core.Ability.
    /// </summary>
    public enum CardAffectKind { Generic, Copied, CostChanged, Buffed, Discarded }
}
