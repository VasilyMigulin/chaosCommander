using System.Collections.Generic;
using Game.Core.Instance;   // InstanceData (карта-награда ссылкой → корректный нижний itemId)
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Авторинг задач/наград журнала В UNITY. Экспортёр (Tools → Backend → Export Task Config) выгружает Title
    /// Data JSON "taskConfig" в ТОМ ЖЕ формате, что читают серверные функции (GetDailyState / Claim* / Report*):
    /// resets + loginRewards + daily + weekly; reward = currencies/cards/boosters/avatars.
    ///
    /// Тип задачи — выпадающий список (TaskKind); строки типов ДОЛЖНЫ совпадать с Game.Core.Progression.TaskTypes
    /// и с клиентскими трекерами (по строке type сервер инкрементит задачи). Одна задача может быть и дневной,
    /// и недельной — разница только в target. id пуст → сгенерится из типа (d_/w_ + type) при экспорте.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Task Config", fileName = "TaskConfig")]
    public class TaskConfigAsset : ScriptableObject
    {
        /// <summary>Тип отслеживаемого действия. Держать в синке с TaskTypes (Game.Core.Progression) и трекерами.</summary>
        public enum TaskKind
        {
            PlayCards, KillCreatures, WinGames, SummonCreatures, PlayGames, SpendMana,
            RestoreMana, FillBoard, CharmTurns, OpenBoosters, DealDamage, Deathrattle
        }

        /// <summary>Строка type для сервера/трекеров (совпадает с TaskTypes.*).</summary>
        public static string TypeString(TaskKind k) => k switch
        {
            TaskKind.PlayCards       => "play_cards",
            TaskKind.KillCreatures   => "kill_creatures",
            TaskKind.WinGames        => "win_games",
            TaskKind.SummonCreatures => "summon_creatures",
            TaskKind.PlayGames       => "play_games",
            TaskKind.SpendMana       => "spend_mana",
            TaskKind.RestoreMana     => "restore_mana",
            TaskKind.FillBoard       => "fill_board",
            TaskKind.CharmTurns      => "charm_turns",
            TaskKind.OpenBoosters    => "open_boosters",
            TaskKind.DealDamage      => "deal_damage",
            TaskKind.Deathrattle     => "deathrattle",
            _                        => "play_games",
        };

        [System.Serializable]
        public class CurrencyReward
        {
            [Tooltip("Код валюты: GD (золото) / GM (самоцветы) / SC (обрывки).")]
            public string Code = "GD";
            public int Amount = 100;
        }

        [System.Serializable]
        public class CardReward
        {
            [Tooltip("Карта-награда ссылкой (даёт корректный нижний itemId). Пусто → впиши ItemIdOverride.")]
            public InstanceData Card;
            [Tooltip("Явный itemId, если ссылки нет. Задан → перекрывает Card.")]
            public string ItemIdOverride;
            [Min(1)] public int Amount = 1;

            public string ResolveItemId()
            {
                string id = !string.IsNullOrEmpty(ItemIdOverride) ? ItemIdOverride
                          : (Card != null ? Card.ItemId : null);
                return string.IsNullOrEmpty(id) ? null : id.ToLowerInvariant();
            }
        }

        [System.Serializable]
        public class RewardConfig
        {
            public List<CurrencyReward> Currencies = new List<CurrencyReward>();
            public List<CardReward>     Cards      = new List<CardReward>();
            [Tooltip("itemId бустеров-наград (booster_standard).")]
            public List<string>         Boosters   = new List<string>();
            [Tooltip("itemId аватаров-наград (avatar_...).")]
            public List<string>         Avatars    = new List<string>();
        }

        [System.Serializable]
        public class TaskEntry
        {
            [Tooltip("Уникальный id (в пределах daily/weekly). Пусто → сгенерится из типа при экспорте (d_/w_ + type).")]
            public string Id;
            public TaskKind Type = TaskKind.PlayGames;
            [Min(1)] public int Target = 1;
            public RewardConfig Reward = new RewardConfig();

            public string ResolveId(bool weekly)
            {
                if (!string.IsNullOrEmpty(Id)) return Id;
                return (weekly ? "w_" : "d_") + TypeString(Type);
            }
        }

        [System.Serializable]
        public class LoginReward
        {
            [Min(1)] public int Day = 1;
            public RewardConfig Reward = new RewardConfig();
        }

        [System.Serializable]
        public class ResetConfig
        {
            [Tooltip("Час ежедневного сброса (UTC).")]
            [Range(0, 23)] public int DailyHourUtc = 0;
            [Tooltip("День недельного сброса (0=Вс .. 6=Сб; среда=3).")]
            [Range(0, 6)]  public int WeeklyDayUtc = 3;
            [Range(0, 23)] public int WeeklyHourUtc = 0;
        }

        public ResetConfig       Resets       = new ResetConfig();
        public List<LoginReward> LoginRewards = new List<LoginReward>();
        public List<TaskEntry>   Daily        = new List<TaskEntry>();
        public List<TaskEntry>   Weekly       = new List<TaskEntry>();
    }
}
