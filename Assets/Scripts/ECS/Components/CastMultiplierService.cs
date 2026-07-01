using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === helper (static) ===
    /// <summary>
    /// Множители ЧАСТОТЫ срабатывания способностей по ключу (владелец + тип триггера). Временная петля
    /// бампит (owner, "OnTurnStart"/"OnTurnEnd") → способности с этими триггерами кастуются N раз. Глобальный
    /// статик: новые существа тоже смотрят его в момент срабатывания (AbilityFire.Mark) → охват без скана/
    /// подписки. Сбрасывается на старте матча (EcsRunHandler).
    ///
    /// СИНК: бамп делает способность Временной петли (резолвится на обоих → сервис одинаков). Множитель влияет
    /// на число резолвов АКТИВНОГО (→ N обычных ActionAbilityData), пассив реплеит N — спец-канал не нужен.
    /// </summary>
    public static class CastMultiplierService
    {
        static readonly Dictionary<(int ownerId, string key), int> _extra = new Dictionary<(int, string), int>();

        // Временные бампы: ждут N ходов ВЛАДЕЛЬЦА, по истечении откатываются (TickOwnerTurnEnd).
        sealed class Temp { public int ownerId; public string key; public int delta; public int turnsLeft; }
        static readonly List<Temp> _temp = new List<Temp>();

        /// <summary>Сколько раз кастовать (>=1). 1 + накопленные бампы по (owner,key).</summary>
        public static int Casts(int ownerId, string key)
            => 1 + (key != null && _extra.TryGetValue((ownerId, key), out int v) ? v : 0);

        public static void Add(int ownerId, string key, int delta)
        {
            if (string.IsNullOrEmpty(key)) return;
            _extra.TryGetValue((ownerId, key), out int v);
            _extra[(ownerId, key)] = v + delta;
        }

        /// <summary>Временный бамп: применяется сразу, держится turns ходов владельца, потом откатывается.
        /// СИНК: AddTemporary идёт на обоих (резолв ре-ранится) → записи одинаковы; откат — в конце хода
        /// владельца, который видят оба (актив — Step B EndTurn; пассив — ReplayEndTurn) → TickOwnerTurnEnd.</summary>
        public static void AddTemporary(int ownerId, string key, int delta, int turns)
        {
            if (string.IsNullOrEmpty(key) || delta == 0 || turns <= 0) return;
            Add(ownerId, key, delta);
            _temp.Add(new Temp { ownerId = ownerId, key = key, delta = delta, turnsLeft = turns });
        }

        /// <summary>Конец хода владельца ownerId: тикаем его временные бампы; истёкшие — откатываем.</summary>
        public static void TickOwnerTurnEnd(int ownerId)
        {
            for (int i = _temp.Count - 1; i >= 0; i--)
            {
                if (_temp[i].ownerId != ownerId) continue;
                if (--_temp[i].turnsLeft <= 0)
                {
                    Add(ownerId, _temp[i].key, -_temp[i].delta);   // откат
                    _temp.RemoveAt(i);
                }
            }
        }

        public static void Clear() { _extra.Clear(); _temp.Clear(); }
    }
}
