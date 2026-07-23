using System;
using UnityEngine;

namespace Game.Core.Shared.Interface
{
    /// <summary>Именованные точки атача VFX на вью существа. Мапятся на Transform'ы в CreatureView
    /// (список _vfxAttachPoints, правится в инспекторе префаба существа). Root — сам корень вью (фолбэк,
    /// если точка не проставлена).</summary>
    public enum CreatureAttachPoint { Root, Body, Head, HandLeft, HandRight, Weapon, Custom1, Custom2 }

    /// <summary>Куда крепить финальный эффект призыва: клетка появления или существо (точка атача).</summary>
    public enum SummonVfxAnchor { Cell, Creature }

    /// <summary>
    /// КОСМЕТИЧЕСКАЯ спека появления существа (эпичный вход лег/экзотов; у простых — обычно только Pre-пыль).
    /// Авторится на CardCreatureModel (SummonVfx), на game-state и синк НЕ влияет — оба клиента играют
    /// призыв локально. Три фазы поверх Summon-анимации (оркеструет CreatureView.PlaySummon):
    ///   Pre     — на КЛЕТКЕ появления, в момент спавна вью (портал/пыль); PreLeadSeconds &gt; 0 —
    ///             существо прячется на это время (портал успевает открыться ДО выхода);
    ///   Resolve — на СУЩЕСТВЕ в точке атача (огненная аура на вылетающем чёрте), с задержкой от
    ///             старта Summon-анимации; Follow — парентится к точке (едет за анимацией);
    ///   Finish  — заключительный аккорд в конце окна призыва, на клетке ИЛИ существе.
    /// Лежит в Shared.Interface (мост Model↔Components↔Mono без ссылок на Ecs.Components из Mono —
    /// Components строго чистые данные, см. IAuraStatusReceiver-паттерн).
    /// </summary>
    [Serializable]
    public class SummonVfxSpec
    {
        [Header("Pre — на клетке, в момент появления")]
        [Tooltip("Портал/пыль на клетке. Null — фаза пропускается.")]
        public GameObject PrePrefab;
        [Tooltip("Сколько секунд существо СКРЫТО после появления Pre-эффекта (портал открывается до выхода). " +
                 "0 — существо появляется сразу вместе с эффектом. Удлиняет окно призыва (OnCast подождёт).")]
        public float PreLeadSeconds = 0f;

        [Header("Resolve — на существе, в точке атача")]
        [Tooltip("Эффект на самом существе (аура/свечение). Null — фаза пропускается.")]
        public GameObject ResolvePrefab;
        public CreatureAttachPoint ResolveAttach = CreatureAttachPoint.Body;
        [Tooltip("Задержка от старта Summon-анимации (обрезается длительностью окна призыва).")]
        public float ResolveDelaySeconds = 0.1f;
        [Tooltip("Парентить к точке атача (эффект следует за анимацией) и погасить в конце призыва. " +
                 "false — one-shot в позиции точки, живёт по своим частицам.")]
        public bool ResolveFollow = true;

        [Header("Finish — заключительный аккорд (конец окна призыва)")]
        [Tooltip("Финальный эффект. Null — фаза пропускается.")]
        public GameObject FinishPrefab;
        public SummonVfxAnchor FinishAnchor = SummonVfxAnchor.Creature;
        [Tooltip("Точка атача для FinishAnchor=Creature (one-shot в её позиции, не парентится).")]
        public CreatureAttachPoint FinishAttach = CreatureAttachPoint.Root;

        /// <summary>Есть ли хоть одна фаза (пустую спеку не таскаем на сущность).</summary>
        public bool HasAny => PrePrefab != null || ResolvePrefab != null || FinishPrefab != null;
    }
}
