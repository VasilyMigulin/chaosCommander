using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// ПАССИВНАЯ АУРА КАРТЫ: «пока эта карта в зоне SourceZone — все цели по Filters получают Buff»
    /// («Шальной десница»: пока в РУКЕ, другие ваши существа получают +1 атаки и +1 скорости).
    /// Вешается на сущность КАРТЫ в PassiveAuraEffect.Init (то есть работает независимо от того,
    /// разыграна карта или нет — зона проверяется рантайм).
    ///
    /// Отличие от tracked-баффов (AddBuffEffect{Tracked}): та схема реактивна к СОБЫТИЮ (сработал
    /// триггер → выдали бафф) и требует пары «Field-способность + OnCreatureInvoked для новичков»,
    /// а эта — к СОСТОЯНИЮ: PassiveAuraSystem держит набор целей актуальным сам (вышло новое существо —
    /// получит; умерло/ушло — снимется; источник покинул зону — аура выключилась целиком).
    ///
    /// Applied — кому бафф реально выдан (для точного отката именно своей выдачи).
    /// СИНК: зоны и борд зеркальны на обоих клиентах, дифф считается локально по одинаковым данным.
    /// </summary>
    public struct PassiveAuraComponent
    {
        /// <summary>Где должна лежать карта-источник, чтобы аура работала (Hand — «пока в руке»,
        /// Board — обычная аура с поля, Deck/Grave — экзотика, Any — всегда).</summary>
        public TargetZone SourceZone;

        /// <summary>ГДЕ ИСКАТЬ ЦЕЛИ: Board (умолчание — существа на столе, классика аур), Hand («ваши
        /// существа В РУКЕ получают…» — статы карт руки живые, HandCardStatsViewSystem их показывает),
        /// Any — и там, и там. Не путать с SourceZone: та про источник, эта про получателей.</summary>
        public TargetZone TargetZone;

        public IBuffable Buff;
        public ITargetFilter[] Filters;

        /// <summary>Сущности, которым бафф выдан ЭТОЙ аурой (снимаем ровно их).</summary>
        public List<int> Applied;
    }
}
