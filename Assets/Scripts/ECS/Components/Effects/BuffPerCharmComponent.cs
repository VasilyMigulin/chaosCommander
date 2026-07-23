namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Динамический самобафф «за каждую чару под контролем владельца» (Обжора: +2/+2 за чару).
    /// Висит на сущности КАРТЫ-существа (ставит BuffPerCharmEffect.Init — пассив, без триггера).
    /// Пересчитывает BuffPerCharmSystem РЕАКТИВНО через стек модификаторов (AddModifier/RemoveModifier —
    /// живой аура-паттерн; покадровый AuraRecalcSystem из пайплайна удалён): бонус = Per × число чар
    /// владельца на поле, Applied* — сколько сейчас реально навешано (для диффа). Чары считаются ВКЛЮЧАЯ
    /// токены («под вашим контролем» — любая чара на поле). СИНК ДАРОМ: чары на поле зеркальны у обоих.
    /// </summary>
    public struct BuffPerCharmComponent
    {
        public int AttackPerCharm;
        public int HealthPerCharm;

        // Рантайм-состояние диффа (заполняет BuffPerCharmSystem, в ассетах не авторится).
        public int AppliedAttack;
        public int AppliedHealth;
    }
}
