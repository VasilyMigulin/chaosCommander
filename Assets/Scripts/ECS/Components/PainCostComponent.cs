namespace Game.Core.Ecs.Components
{
    /// <summary>Вид альтернативной уплаты (семейство «Бесчестный букмекер»). Значения ЗАКРЕПЛЕНЫ —
    /// на них ссылаются заавторенные ассеты (InstallAltCostEffect.Kind); перестановка ломает карты.</summary>
    public enum AltCostKind
    {
        DamageSelf        = 0,  // урон себе на ЭФФЕКТИВНУЮ стоимость карты (Бесчестный букмекер)
        DiscardHand       = 1,  // сбросить СЛУЧАЙНУЮ свою карту из руки (кроме разыгрываемой/командира)
        SacrificeCreature = 2,  // уничтожить СВОЁ случайное существо на поле (не командира)
        MillDeck          = 3,  // уничтожить СЛУЧАЙНУЮ карту своей колоды
    }

    /// <summary>
    /// СЕМЕЙСТВО «альтернативная уплата»: следующие Charges разыгранных ВАМИ карт оплачиваются НЕ ресурсом,
    /// а альтернативой по Kind (урон себе / сброс / жертва существа / карта из колоды). Маркер на СУЩНОСТИ
    /// ИГРОКА; ставит InstallAltCostEffect (ре-ран на обоих клиентах → зеркален), повторный инсталл
    /// ПЕРЕЗАПИСЫВАЕТ вид и заряды. Потребляет RunCastRouterSystem (актив: заряд−1, уплата исполняется,
    /// жертвы роллятся активом) и ReplayActionSystem (пассив: по ActionCastData.AltPaid* — та же уплата,
    /// жертвы по ключам). Free-касты (авто-розыгрыши) маркер не трогают. «Пустая» уплата (карта за 0 /
    /// нет жертв) тратит заряд осознанно. Урон DamageSelf штатный → Вуду-редирект/пейн-триггеры работают.
    /// </summary>
    public struct AltCostComponent
    {
        public AltCostKind Kind;
        public int Charges;
    }

    /// <summary>Транзит «оплачено альтернативой» от роутера к коллектору снапшотов: RunCastRouterSystem
    /// вешает на КАРТУ результат уплаты, CollectActionSystem переносит в ActionCastData.AltPaid* (и снимает).</summary>
    public struct AltPaidComponent
    {
        public AltCostKind Kind;
        public int Amount;      // DamageSelf: величина урона
        public string[] Keys;   // жертвы (discard/sacrifice/mill): NetworkEntityKey для реплея у пассива
    }
}
