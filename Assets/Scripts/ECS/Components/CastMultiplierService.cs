using System.Collections.Generic;
using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    // === helper (static) ===
    /// <summary>
    /// Множители ЧАСТОТЫ срабатывания способностей по КОМБИНИРОВАННОМУ ключу «ТипКарты/Триггер» (решение
    /// пользователя 2026-07-04): "Spell/OnCast" (экзотик «спеллы дважды»), "Creature/OnTurnEnd" и т.п.;
    /// wildcard "*/OnTurnStart" = любой тип (Временная петля). Итог для срабатывания = 1 + бамп wildcard +
    /// бамп типизированного ключа (комбинации складываются). Глобальный статик: новые карты тоже смотрят его
    /// в момент срабатывания (AbilityFire.Mark) → охват без скана/подписки. Сбрасывается на старте матча.
    ///
    /// СИНК: бамп делает способность-источник (резолвится на обоих → сервис одинаков). Множитель влияет
    /// на число резолвов АКТИВНОГО (→ N обычных ActionAbilityData), пассив реплеит N — спец-канал не нужен.
    /// </summary>
    public static class CastMultiplierService
    {
        /// <summary>Wildcard-тип «любая карта» в составном ключе.</summary>
        public const string AnyType = "*";

        /// <summary>Составной ключ множителя: "Spell/OnCast", "*/OnTurnEnd" и т.п.</summary>
        public static string ComposeKey(string typeKey, string trigger) => typeKey + "/" + trigger;

        static readonly Dictionary<(int ownerId, string key), int> _extra = new Dictionary<(int, string), int>();

        // Временные бампы: ждут N ходов ВЛАДЕЛЬЦА, по истечении откатываются (TickOwnerTurnEnd).
        sealed class Temp { public int ownerId; public string key; public int delta; public int turnsLeft; }
        static readonly List<Temp> _temp = new List<Temp>();

        /// <summary>Сколько раз кастовать (>=1) для срабатывания триггера trigger на карте типа cardType:
        /// 1 + бамп "*/trigger" + бамп "Тип/trigger" (складываются).</summary>
        public static int Casts(int ownerId, EnumService.CardType cardType, string trigger)
        {
            if (trigger == null) return 1;
            return 1 + Get(ownerId, ComposeKey(AnyType, trigger))
                     + Get(ownerId, ComposeKey(cardType.ToString(), trigger));
        }

        static int Get(int ownerId, string key)
            => _extra.TryGetValue((ownerId, key), out int v) ? v : 0;

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
