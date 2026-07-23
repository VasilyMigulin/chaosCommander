namespace Game.Core.Progression
{
    /// <summary>
    /// Канонические строки типов задач. ОБЯЗАНЫ совпадать с полем "type" в taskConfig.json и со сравнением на
    /// сервере (bumpProgress матчит задачи по строке type). Одни и те же строки шлют клиентские трекеры.
    ///
    /// Задача может быть и дневной, и недельной — разница только в target (кол-ве). Один type ⇒ один трекер.
    /// </summary>
    public static class TaskTypes
    {
        public const string PlayCards       = "play_cards";        // разыграть карту (свой каст)
        public const string KillCreatures   = "kill_creatures";    // уничтожить существо противника
        public const string WinGames        = "win_games";         // победа в матче
        public const string SummonCreatures = "summon_creatures";  // призвать существо на поле (не «разыграть»)
        public const string PlayGames       = "play_games";        // сыграть матч (любой исход)
        public const string SpendMana       = "spend_mana";        // потратить ману
        public const string RestoreMana     = "restore_mana";      // восстановить ману
        public const string FillBoard       = "fill_board";        // заполнить свою сторону (нет места под призыв)
        public const string CharmTurns      = "charm_turns";       // контролировать чары × ходов
        public const string OpenBoosters    = "open_boosters";     // открыть бустер (инкрементит СЕРВЕР в OpenBooster)
        public const string DealDamage      = "deal_damage";       // нанести урон врагу
        public const string Deathrattle     = "deathrattle";       // сработал хрип «При смерти» своего существа
    }
}
