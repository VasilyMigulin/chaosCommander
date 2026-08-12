using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === helper === Публикация косметических VFX-событий по (spec, caster, targets) — общая логика для
    // ТРЁХ мест, которые её используют: RunResolveAbilityQueueSystem (обычный резолв, активный клиент),
    // RunChainSystem (цепочка/RepeatAbility, активный клиент) и ReplayActionSystem (реплей цепочки на
    // ПАССИВНОМ клиенте — тот применяет эффекты стадии напрямую из снапшота, но раньше вообще не показывал
    // VFX, т.к. RunChainSystem у пассива не крутится, см. TurnGate). Раньше эмиссия была продублирована в
    // первых двух местах — вынесена сюда, чтобы третье не плодило ЕЩЁ одну копию.
    internal static class VfxEmitUtil
    {
        // Мировая позиция сущности с гарантированным фолбэком (центр доски) — VFX всегда должен лететь
        // КУДА-ТО. Поверх общей цепочки резолва позиции EntityWorldPosUtil (Board→View→Owner-аватар→сам-игрок).
        public static Vector3 WorldPos(EcsWorld world, BoardView bv, int entity)
            => EntityWorldPosUtil.TryGet(world, bv, entity, out var pos) ? pos : bv.BoardCenter;

        // Hit(Kind=None+HitPrefab)/Beam/Area — МГНОВЕННАЯ косметика (эффекты уже применены к этому моменту,
        // ждать нечего). Projectile — отдельно, см. LaunchProjectile (там решение «ждать ли прилёт» разное
        // у разных вызывающих).
        public static void EmitInstantVfx(EcsWorld world, BoardView bv, VfxSpec spec, int caster, int[] targets)
        {
            if (spec == null || bv == null || targets == null || targets.Length == 0) return;

            if (spec.Kind == VfxKind.None)
            {
                if (spec.HitPrefab == null) return;
                foreach (var t in targets)
                    GameEventBus.Publish(new HitVfxEvent { At = WorldPos(world, bv, t), Prefab = spec.HitPrefab });
                return;
            }

            if (spec.Prefab == null) return;

            switch (spec.Kind)
            {
                case VfxKind.Beam:
                {
                    Vector3 from = WorldPos(world, bv, caster);
                    var to = new Vector3[targets.Length];
                    for (int i = 0; i < targets.Length; i++) to[i] = WorldPos(world, bv, targets[i]);
                    GameEventBus.Publish(new BeamVfxEvent
                    {
                        From = from, Targets = to, Prefab = spec.Prefab, HitPrefab = spec.HitPrefab,
                        Delivery = spec.Delivery, Style = spec.BeamVisual,
                        LightningSegments = spec.LightningSegments, LightningJitter = spec.LightningJitter,
                    });
                    break;
                }
                case VfxKind.Area:
                {
                    var centers = new Vector3[targets.Length];
                    for (int i = 0; i < targets.Length; i++) centers[i] = WorldPos(world, bv, targets[i]);
                    GameEventBus.Publish(new AreaVfxEvent
                    { CellCenters = centers, CellSize = bv.CellSize, Prefab = spec.Prefab, HitPrefab = spec.HitPrefab, Merge = spec.MergeArea });
                    break;
                }
            }
        }

        // Публикует ProjectileVfxEvent (Kind=Projectile, Prefab задан — проверяется ВЫЗЫВАЮЩИМ, у него
        // разная логика «а стоит ли вообще» до этого). token>=0 → VfxPresenter опубликует VfxArrivedEvent
        // по завершении ВСЕЙ доставки — вызывающий сам вешает гейт-компонент, если резолв должен ждать.
        // token<0 → чистая косметика без арривал-сигнала (пассивный реплей: эффекты УЖЕ применены
        // синхронно из снапшота, ждать нечего — снаряд просто летит НА ВИД).
        public static void LaunchProjectile(EcsWorld world, BoardView bv, VfxSpec spec, int caster, int[] targets, int token)
        {
            if (spec == null || bv == null || targets == null || targets.Length == 0) return;

            Vector3 from = WorldPos(world, bv, caster);
            var to = new Vector3[targets.Length];
            for (int i = 0; i < targets.Length; i++) to[i] = WorldPos(world, bv, targets[i]);

            GameEventBus.Publish(new ProjectileVfxEvent
            {
                From = from, Targets = to,
                Prefab = spec.Prefab, HitPrefab = spec.HitPrefab,
                Speed = spec.ProjectileSpeed, Scale = spec.ProjectileScale > 0.001f ? spec.ProjectileScale : 1f, Token = token,
                Delivery = spec.Delivery, Trajectory = spec.Trajectory, BallisticHeight = spec.BallisticHeight,
                Wobble = spec.Wobble, WobbleAmount = spec.WobbleAmount, WobbleFrequency = spec.WobbleFrequency,
            });
        }
    }
}
