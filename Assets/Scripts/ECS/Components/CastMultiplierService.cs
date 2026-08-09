using System.Collections.Generic;
using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    // === helper (static) ===
    /// <summary>
    /// Множители ЧАСТОТЫ срабатывания способностей по КОМБИНИРОВАННОМУ ключу «ТипКарты/Триггер» (решение
    /// пользователя 2026-07-04): "Spell/OnCast" (экзотик «спеллы дважды»), "Creature/OnTurnEnd" и т.п.;
    /// wildcard "*/OnTurnStart" = любой тип (Временная петля). Глобальный статик: новые карты тоже смотрят
    /// его в момент срабатывания (AbilityFire.Mark) → охват без скана/подписки. Сбрасывается на старте матча.
    ///
    /// АРИФМЕТИКА (решение пользователя 2026-08-05): каждый источник хранит СВОЁ ЛИЦО — «сколько раз сработает,
    /// будь он один» (2 = дважды, 3 = трижды). Итог = 1 (обычный каст), если активных источников нет; иначе —
    /// СУММА ЛИЦ всех активных источников («дважды» + «трижды» = пять, а не «1 + дельты»). Поэтому источники
    /// хранятся СПИСКОМ (не одним аккумулированным int) — без списка нельзя было бы восстановить, сколько
    /// РАЗНЫХ источников дали текущую сумму, когда один из них истекает/тратится.
    ///
    /// Три вида источников (различаются ТОЛЬКО истощением):
    ///   • Permanent — не истощается (до конца матча).
    ///   • Turns     — откатывается по ходам ВЛАДЕЛЬЦА (TickOwnerTurnEnd), как было.
    ///   • Charges   — НОВОЕ: списывается по 1 заряду за КАЖДОЕ фактическое срабатывание с множителем
    ///     (ConsumeCharge, зовёт AbilityFire.Mark в момент применения — не по ходам). 0 зарядов → источник исчезает.
    ///
    /// СИНК: бамп/списание делает способность-источник САМА (резолвится на обоих клиентах одинаково) →
    /// сервис одинаков без спец-канала. Множитель влияет на число резолвов АКТИВНОГО (→ N ActionAbilityData),
    /// пассив реплеит N — спец-канал не нужен.
    /// </summary>
    public static class CastMultiplierService
    {
        /// <summary>Wildcard-тип «любая карта» в составном ключе.</summary>
        public const string AnyType = "*";

        /// <summary>Составной ключ множителя: "Spell/OnCast", "*/OnTurnEnd" и т.п.</summary>
        public static string ComposeKey(string typeKey, string trigger) => typeKey + "/" + trigger;

        enum Kind { Permanent, Turns, Charges }

        sealed class Source
        {
            public int ownerId;
            public string key;
            public int value;       // ЛИЦО множителя («дважды»=2, «трижды»=3) — НЕ дельта от базы
            public Kind kind;
            public int turnsLeft;   // используется только Kind.Turns
            public int charges;     // используется только Kind.Charges
        }

        static readonly List<Source> _sources = new List<Source>();

        /// <summary>Сколько раз кастовать (>=1) для срабатывания триггера trigger на карте типа cardType.
        /// Нет активных источников → 1 (обычный каст). Иначе — сумма лиц ВСЕХ активных ("*/trigger" +
        /// "Тип/trigger" считаются раздельными ключами, но складываются в общую сумму).</summary>
        public static int Casts(int ownerId, EnumService.CardType cardType, string trigger)
        {
            if (trigger == null) return 1;
            int sum = 0;
            bool any = false;
            SumKey(ownerId, ComposeKey(AnyType, trigger), ref sum, ref any);
            SumKey(ownerId, ComposeKey(cardType.ToString(), trigger), ref sum, ref any);
            return any ? sum : 1;
        }

        static void SumKey(int ownerId, string key, ref int sum, ref bool any)
        {
            foreach (var s in _sources)
            {
                if (s.ownerId != ownerId || s.key != key) continue;
                sum += s.value;
                any = true;
            }
        }

        /// <summary>Постоянный источник (не истощается до конца матча). value — лицо («дважды»=2).</summary>
        public static void Add(int ownerId, string key, int value)
        {
            if (string.IsNullOrEmpty(key) || value <= 1) return;
            _sources.Add(new Source { ownerId = ownerId, key = key, value = value, kind = Kind.Permanent });
        }

        /// <summary>Временный источник: применяется сразу, держится turns ходов владельца, потом откатывается
        /// (TickOwnerTurnEnd). СИНК: идёт на обоих (резолв ре-ранится) → записи одинаковы; откат — в конце хода
        /// владельца, который видят оба (актив — Step B EndTurn; пассив — ReplayEndTurn).</summary>
        public static void AddTemporary(int ownerId, string key, int value, int turns)
        {
            if (string.IsNullOrEmpty(key) || value <= 1 || turns <= 0) return;
            _sources.Add(new Source { ownerId = ownerId, key = key, value = value, kind = Kind.Turns, turnsLeft = turns });
        }

        /// <summary>Съедаемый источник (Волшебник Упс): value применяется, пока есть заряды; заряд списывается
        /// РОВНО когда триггер РЕАЛЬНО сработал с этим множителем (ConsumeCharge из AbilityFire.Mark) — не по
        /// ходам. charges — сколько срабатываний переживёт источник, прежде чем исчезнуть.</summary>
        public static void AddCharges(int ownerId, string key, int value, int charges)
        {
            if (string.IsNullOrEmpty(key) || value <= 1 || charges <= 0) return;
            _sources.Add(new Source { ownerId = ownerId, key = key, value = value, kind = Kind.Charges, charges = charges });
        }

        /// <summary>Конец хода владельца ownerId: тикаем его временные (по ходам) источники; истёкшие — удаляем.</summary>
        public static void TickOwnerTurnEnd(int ownerId)
        {
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                var s = _sources[i];
                if (s.ownerId != ownerId || s.kind != Kind.Turns) continue;
                if (--s.turnsLeft <= 0) _sources.RemoveAt(i);
            }
        }

        /// <summary>Триггер РЕАЛЬНО сработал с множителем (AbilityFire.Mark, casts>1): списать по 1 заряду со
        /// ВСЕХ съедаемых источников, участвовавших в этом ключе у владельца (и "*/trigger", и "Тип/trigger").
        /// Permanent/по-ходам не трогает. Один вызов = ОДНА активация, а не одно из N внутренних резолв-повторов
        /// (иначе съедаемый(2) сгорал бы за один же тройной каст) — зовётся из Mark один раз на срабатывание.</summary>
        public static void ConsumeCharge(int ownerId, EnumService.CardType cardType, string trigger)
        {
            if (trigger == null) return;
            ConsumeKey(ownerId, ComposeKey(AnyType, trigger));
            ConsumeKey(ownerId, ComposeKey(cardType.ToString(), trigger));
        }

        static void ConsumeKey(int ownerId, string key)
        {
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                var s = _sources[i];
                if (s.ownerId != ownerId || s.key != key || s.kind != Kind.Charges) continue;
                if (--s.charges <= 0) _sources.RemoveAt(i);
            }
        }

        public static void Clear() => _sources.Clear();
    }
}
