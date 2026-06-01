namespace Game.Core.Service
{
    /// <summary>
    /// Форма области эффекта относительно выбранного якоря (клетки/существа).
    /// Применяется ПОСЛЕ выбора цели: TargetMask решает «кто годится», TargetShape — «какие клетки вокруг».
    /// Область считается в пределах доски владельца якоря (5×2).
    /// </summary>
    public enum TargetShape
    {
        /// <summary>Только сам якорь.</summary>
        Single,
        /// <summary>Якорь + 8 соседних клеток.</summary>
        Adjacent,
        /// <summary>Якорь + 4 ортогональных соседа (крест, без диагоналей).</summary>
        Cross,
        /// <summary>Весь горизонтальный ряд якоря.</summary>
        Row,
        /// <summary>Вся вертикальная колонка якоря.</summary>
        Column,
    }
}
