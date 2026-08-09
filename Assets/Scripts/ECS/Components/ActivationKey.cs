namespace Game.Core.Ecs.Components
{
    // === struct (значение) ===
    /// <summary>
    /// ПОРЯДОК АКТИВАЦИИ в очереди способностей. Резолв всегда берёт МИНИМАЛЬНЫЙ ключ.
    ///
    /// Ключ иерархический: у КОРНЕВОЙ активации (игрок разыграл карту, каскад начала хода) это
    /// (Wave, Entry, Index); СЛЕДСТВИЕ — предсмертный хрип, реакция на урон, карта, разыгранная эффектом —
    /// наследует ключ своей ПРИЧИНЫ и дописывает себе уровень пути. Лексикографическое сравнение тогда
    /// само ставит следствие сразу ПОСЛЕ вызвавшей его активации и ДО её соседей:
    ///
    ///     способность 1   (N, e, 0)
    ///       хрип от неё   (N, e, 0 · 0)   ← вклинивается здесь
    ///         его спелл   (N, e, 0 · 0 · 0)
    ///       второй хрип   (N, e, 0 · 1)
    ///     способность 2   (N, e, 1)
    ///
    /// РАНЬШЕ следствие получало свежую волну (номер кадра) и уезжало В КОНЕЦ очереди — то есть карта из
    /// трёх «нанеси 2 случайному врагу» сначала стреляла трижды и только потом отрабатывали все хрипы.
    ///
    /// ГЛУБИНА НЕ ОГРАНИЧЕНА (сознательно). Раньше путь лежал в четырёх фиксированных полях, и цепочка
    /// глубже четырёх обрывалась в корень — но это было ограничение ХРАНЕНИЯ, а не правило игры: сложный
    /// каскад («пиньята умерла → хрип призвал пиньяту → другая её способность разбила новую → её хрип…»)
    /// абсолютно легитимен и должен доигрываться целиком. От зацикливания глубина всё равно не спасает:
    /// бесконечная цепочка порождает бесконечно много активаций независимо от того, как считается порядок.
    ///
    /// ЛОКАЛЬНЫЙ (не зеркалится): сортирует только актив, пассив реплеит его порядок из снапшотов.
    /// </summary>
    public struct ActivationKey
    {
        /// <summary>Глубина, ниже которой каскад почти наверняка зациклился. НЕ меняет поведение —
        /// только повод написать в лог (см. RunAbilityTargetingSystem).</summary>
        public const int SuspiciousDepth = 32;

        public int Wave;    // волна корня: номер кадра постановки
        public int Entry;   // порядок ВЫХОДА карты-корня на стол (BoardEntryOrder); спелл/не на столе = 0
        public int Index;   // AbilityIndex корня — порядок способностей внутри одной карты

        /// <summary>Путь вложения следствий. null/пусто = корневая активация. Каждый элемент — номер
        /// следствия среди следствий одной и той же причины.</summary>
        public int[] Path;

        public int Depth => Path?.Length ?? 0;

        /// <summary>Корневая активация: собственное действие, а не следствие чужого резолва.</summary>
        public static ActivationKey Root(int wave, int entry, int index)
            => new ActivationKey { Wave = wave, Entry = entry, Index = index, Path = null };

        /// <summary>Ключ следствия ЭТОЙ активации: тот же корень + ещё один уровень пути.
        /// childIndex — порядковый номер следствия среди следствий одной причины.</summary>
        public ActivationKey Child(int childIndex)
        {
            int len = Depth;
            var path = new int[len + 1];
            for (int i = 0; i < len; i++) path[i] = Path[i];
            path[len] = childIndex;
            return new ActivationKey { Wave = Wave, Entry = Entry, Index = Index, Path = path };
        }

        /// <summary>Лексикографическое сравнение: волна → корневой порядок → путь следствий.
        /// При общем префиксе КОРОЧЕ значит РАНЬШЕ — поэтому причина всегда идёт перед своими следствиями.</summary>
        public int CompareTo(in ActivationKey o)
        {
            if (Wave  != o.Wave)  return Wave  < o.Wave  ? -1 : 1;
            if (Entry != o.Entry) return Entry < o.Entry ? -1 : 1;
            if (Index != o.Index) return Index < o.Index ? -1 : 1;

            int a = Depth, b = o.Depth;
            int common = a < b ? a : b;
            for (int i = 0; i < common; i++)
                if (Path[i] != o.Path[i]) return Path[i] < o.Path[i] ? -1 : 1;

            return a == b ? 0 : (a < b ? -1 : 1);
        }

        public override string ToString()
        {
            if (Depth == 0) return $"({Wave},{Entry},{Index})";
            var sb = new System.Text.StringBuilder($"({Wave},{Entry},{Index}");
            foreach (var p in Path) sb.Append('·').Append(p);
            return sb.Append(')').ToString();
        }
    }
}
