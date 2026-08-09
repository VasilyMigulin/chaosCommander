using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // === helper (static) ===
    /// <summary>
    /// Визуальный фидбэк «на карту В РУКЕ применили эффект» (Дупликатор скопировал, удешевление, бафф,
    /// сброс перед исчезновением). Публикует CardAffectedInHandUIEvent — но ТОЛЬКО если карта реально
    /// в руке (HandTag): для карт на столе/в колоде фидбэк руки бессмыслен. UI (PlayCardView) сам
    /// фильтрует событие по CardEntity и играет punch визуала + UI-VFX по Kind. Синк не нужен —
    /// эффекты применяются зеркально на обоих клиентах, косметика проигрывается локально.
    /// Одна строка на эффект → логика фидбэка не размазывается по эффектам (тот же приём, что PlayCardUtil).
    /// </summary>
    public static class CardFeedbackUtil
    {
        public static void MarkAffectedInHand(EcsWorld world, int cardEntity, CardAffectKind kind = CardAffectKind.Generic)
        {
            if (cardEntity < 0) return;
            if (!world.GetPool<HandTag>().Has(cardEntity)) return;   // только карты в руке
            GameEventBus.Publish(new CardAffectedInHandUIEvent { CardEntity = cardEntity, Kind = kind });
        }
    }
}
