namespace Game.Core.Shared.Interface
{
    /// <summary>Как выбирается цель у Target-способности.</summary>
    public enum TargetSelection
    {
        Random,    // система берёт случайные из валидных
        Selected,  // игрок выбирает кликом (async, через UI)
        Strongest, // система берёт Count с наибольшей атакой (Всадник Смерти). Синк через target-ключи (как Random).
    }

    /// <summary>Область действия Field-способности.</summary>
    public enum FieldArea
    {
        All,    // всё поле (обе стороны)
        Own,    // сторона игрока-кастера
        Enemy,  // сторона оппонента
    }

    /// <summary>Зона, из которой берутся кандидаты-цели. По умолчанию Board (существа на поле + игроки).
    /// Для не-Board зон (колода/рука/кладбище) Selected идёт через окно выбора, а не клики по клеткам.</summary>
    public enum TargetZone
    {
        Board,   // существа на поле + игроки
        Deck,    // карты в колоде
        Hand,    // карты в руке
        Grave,   // карты в кладбище
    }
}
