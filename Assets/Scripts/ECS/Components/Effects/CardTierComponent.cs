namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Рантайм-состояние карты-с-уровнями (надстройка CardModel.Tiers). Навешивается в CardModel.Init, если у
    /// модели есть уровни. CardTierSystem устанавливает статы/стоимость уровня как НОВУЮ БАЗУ (SetBase/SetBaseMax —
    /// НЕ бафф-модификаторы): устойчиво к ClearModifiers (смерть/возврат), баффы складываются поверх, возврат в
    /// руку/из кладбища лечит до полного (Current=Max уровня). CurrentTier читает AbilityFire.Mark (гейт способностей
    /// по уровню, AbilityTierGateComponent). Синк ДАРОМ: источник уровня (золото) зеркален у обоих клиентов.
    /// </summary>
    public struct CardTierComponent
    {
        public int  CurrentTier;    // индекс активного уровня в CardModel.Tiers
        public bool Announced;      // UI уже уведомлён (первый показ баннера/описания) — иначе публикуем даже без смены
    }
}
