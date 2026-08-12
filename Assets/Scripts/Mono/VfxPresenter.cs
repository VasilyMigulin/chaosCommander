using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Единственная точка инстанцирования боевых VFX. Подписывается на косметические события (Beam/Projectile/
    /// Area/Hit), которые ECS публикует с ГОТОВЫМИ мировыми позициями + префабом из спеки способности.
    /// Ничего не знает про ECS/синк — чистая презентация. Повесить ОДИН раз на сцену боя.
    ///
    /// ДОСТАВКА К НЕСКОЛЬКИМ ЦЕЛЯМ (2026-08-11): и Beam, и Projectile теперь несут МАССИВ целей одним
    /// событием, а не по событию на цель — нужно для Delivery.Chain (эстафета цель→цель одним объектом/
    /// лучом). Split — старое поведение (все параллельно из каста), просто теперь в одном событии.
    ///
    /// ДВИЖЕНИЕ СНАРЯДА (ComputePosition): раньше — прямая линия через DOTween.DOMove. Теперь — ручная
    /// покадровая интерполяция, чтобы поддержать Trajectory (Direct/Ballistic-дуга) и Wobble (Linear/
    /// Drift-снос/Vortex-спираль) поверх неё. Wobble всегда затухает к 0 на обоих концах ноги (envelope =
    /// sin(t·π)) — снаряд визуально дрожит В ПОЛЁТЕ, но всё равно ТОЧНО приходит в цель.
    /// </summary>
    public sealed class VfxPresenter : MonoBehaviour
    {
        [Tooltip("Сколько секунд держать инстанс, если у префаба нет ParticleSystem (для оценки времени жизни).")]
        [SerializeField] float defaultLife = 2.5f;

        [Tooltip("Подъём VFX над плоскостью клеток (Y), чтобы эффект не тонул в полу.")]
        [SerializeField] float yOffset = 0.4f;

        void OnEnable()
        {
            GameEventBus.Subscribe<BeamVfxEvent>(this, OnBeam);
            GameEventBus.Subscribe<ProjectileVfxEvent>(this, OnProjectile);
            GameEventBus.Subscribe<AreaVfxEvent>(this, OnArea);
            GameEventBus.Subscribe<HitVfxEvent>(this, OnHit);
        }

        void OnDisable() => GameEventBus.UnsubscribeAll(this);

        // ── Луч: прямая линия ИЛИ ломаная молния, Split (независимо на каждую цель) или Chain (одна
        //    ломаная через все цели по очереди) ────────────────────────────────
        void OnBeam(BeamVfxEvent e)
        {
            if (e.Prefab == null || e.Targets == null || e.Targets.Length == 0) return;
            Vector3 from = Lift(e.From);

            if (e.Delivery == ProjectileDelivery.Chain)
            {
                var anchors = new Vector3[e.Targets.Length + 1];
                anchors[0] = from;
                for (int i = 0; i < e.Targets.Length; i++) anchors[i + 1] = Lift(e.Targets[i]);

                var points = BuildBeamPath(anchors, e.Style, e.LightningSegments, e.LightningJitter);
                SpawnBeamLine(e.Prefab, points);
                foreach (var t in e.Targets) SpawnHit(e.HitPrefab, Lift(t));
                return;
            }

            // Split — независимый луч кастер→каждая цель.
            foreach (var target in e.Targets)
            {
                Vector3 to = Lift(target);
                var points = e.Style == BeamStyle.Lightning
                    ? BuildBeamPath(new[] { from, to }, e.Style, e.LightningSegments, e.LightningJitter)
                    : new[] { from, to };
                SpawnBeamLine(e.Prefab, points);
                SpawnHit(e.HitPrefab, to);
            }
        }

        void SpawnBeamLine(GameObject prefab, Vector3[] points)
        {
            var go = Instantiate(prefab, points[0], Quaternion.identity);
            var lr = go.GetComponentInChildren<LineRenderer>();
            if (lr != null)
            {
                lr.useWorldSpace = true;
                lr.positionCount = points.Length;
                lr.SetPositions(points);
            }
            AutoDestroy(go);
        }

        // Разбивает ломаную anchors[0]→anchors[1]→…→anchors[n-1] на мелкие сегменты со случайным джиттером
        // ПОПЕРЁК каждой ноги (Lightning); Straight — anchors как есть (просто зигзаг по целям, без дрожи).
        // Джиттер затухает к 0 на КОНЦАХ каждой ноги — сами точки-цели остаются точными.
        static Vector3[] BuildBeamPath(Vector3[] anchors, BeamStyle style, int segmentsPerLeg, float jitter)
        {
            if (style == BeamStyle.Straight || anchors.Length < 2) return anchors;

            int segs = Mathf.Max(1, segmentsPerLeg);
            var points = new List<Vector3>(anchors.Length * segs) { anchors[0] };

            for (int leg = 0; leg < anchors.Length - 1; leg++)
            {
                Vector3 a = anchors[leg], b = anchors[leg + 1];
                Vector3 dir = b - a;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                Vector3 perp = Vector3.Cross(dir.normalized, Vector3.up);
                if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;   // нога вертикальна — берём другую ось
                perp.Normalize();

                for (int i = 1; i <= segs; i++)
                {
                    float t = (float)i / segs;
                    Vector3 p = Vector3.Lerp(a, b, t);
                    if (i < segs)   // на самой цели (i==segs) джиттер не накладываем — попадание точное
                    {
                        float envelope = Mathf.Sin(t * Mathf.PI);
                        p += perp * ((UnityEngine.Random.value * 2f - 1f) * jitter * envelope);
                    }
                    points.Add(p);
                }
            }
            return points.ToArray();
        }

        // ── Снаряд: летит каст→цели, Split (параллельно) или Chain (эстафета, один объект по очереди) ──
        void OnProjectile(ProjectileVfxEvent e)
        {
            // ВРЕМЕННО (баг: снаряд цепочки не летит) — видим, дошло ли событие сюда вообще.
            Debug.Log($"[ChainVfx] VfxPresenter.OnProjectile prefab={(e.Prefab != null ? e.Prefab.name : "NULL")} targets={(e.Targets?.Length ?? -1)} delivery={e.Delivery} token={e.Token}");
            if (e.Prefab == null || e.Targets == null || e.Targets.Length == 0) return;

            Vector3 from = Lift(e.From);
            var targets = new Vector3[e.Targets.Length];
            for (int i = 0; i < e.Targets.Length; i++) targets[i] = Lift(e.Targets[i]);

            float speed = e.Speed > 0.01f ? e.Speed : 12f;
            float scale = e.Scale > 0.001f ? e.Scale : 1f;
            var motion = new Motion(e.Trajectory, e.BallisticHeight, e.Wobble, e.WobbleAmount, e.WobbleFrequency);

            if (e.Delivery == ProjectileDelivery.Chain)
                StartCoroutine(RunChainProjectile(e.Prefab, e.HitPrefab, from, targets, speed, scale, motion, e.Token));
            else
                StartCoroutine(RunSplitProjectiles(e.Prefab, e.HitPrefab, from, targets, speed, scale, motion, e.Token));
        }

        // Split: N снарядов летят одновременно (каждый свой инстанс) из одной точки к своей цели;
        // общий VfxArrivedEvent — когда долетел ПОСЛЕДНИЙ.
        IEnumerator RunSplitProjectiles(GameObject prefab, GameObject hitPrefab, Vector3 from, Vector3[] targets, float speed, float scale, Motion motion, int token)
        {
            int remaining = targets.Length;
            foreach (var to in targets)
            {
                var go = Instantiate(prefab, from, Quaternion.identity);
                if (scale != 1f) go.transform.localScale *= scale;
                StartCoroutine(MoveAlongLeg(go, from, to, speed, motion, () =>
                {
                    SpawnHit(hitPrefab, to);
                    AutoDestroy(go);
                    remaining--;
                }));
            }
            while (remaining > 0) yield return null;
            if (token >= 0) GameEventBus.Publish(new VfxArrivedEvent { Token = token });
        }

        // Chain: ОДИН снаряд-инстанс летит от→цель[0]→цель[1]→…, хит-вспышка на КАЖДОЙ посещённой цели;
        // VfxArrivedEvent — когда долетел до ПОСЛЕДНЕЙ.
        IEnumerator RunChainProjectile(GameObject prefab, GameObject hitPrefab, Vector3 from, Vector3[] targets, float speed, float scale, Motion motion, int token)
        {
            var go = Instantiate(prefab, from, Quaternion.identity);
            if (scale != 1f) go.transform.localScale *= scale;
            Vector3 legFrom = from;
            foreach (var to in targets)
            {
                yield return MoveAlongLeg(go, legFrom, to, speed, motion, null);
                SpawnHit(hitPrefab, to);
                legFrom = to;
            }
            AutoDestroy(go);
            if (token >= 0) GameEventBus.Publish(new VfxArrivedEvent { Token = token });
        }

        // Покадровое движение ОДНОЙ ноги (from→to) с учётом Trajectory/Wobble; поворот кадр-в-кадр к
        // мгновенному направлению движения (не статично на to — иначе дугу/вихрь «косит носом»).
        IEnumerator MoveAlongLeg(GameObject go, Vector3 from, Vector3 to, float speed, Motion motion, Action onComplete)
        {
            float dur = Mathf.Max(0.05f, Vector3.Distance(from, to) / speed);
            float elapsed = 0f;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                Vector3 pos = motion.ComputePosition(from, to, t);
                go.transform.position = pos;

                Vector3 ahead = motion.ComputePosition(from, to, Mathf.Clamp01(t + 0.02f));
                Vector3 dir = ahead - pos;
                if (dir.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(dir);

                yield return null;
            }

            go.transform.position = to;   // защёлкнуть точно в цель (плавающая точка могла недобрать)
            onComplete?.Invoke();
        }

        // Параметры движения ноги пути — Trajectory (форма) + Wobble (дрожь поверх формы), см. VfxSpec.
        readonly struct Motion
        {
            readonly ProjectileTrajectory _trajectory;
            readonly float _ballisticHeight;
            readonly ProjectileWobble _wobble;
            readonly float _wobbleAmount;
            readonly float _wobbleFrequency;

            public Motion(ProjectileTrajectory trajectory, float ballisticHeight,
                          ProjectileWobble wobble, float wobbleAmount, float wobbleFrequency)
            {
                _trajectory = trajectory;
                _ballisticHeight = ballisticHeight;
                _wobble = wobble;
                _wobbleAmount = wobbleAmount;
                _wobbleFrequency = wobbleFrequency;
            }

            public Vector3 ComputePosition(Vector3 from, Vector3 to, float t)
            {
                Vector3 pos = Vector3.Lerp(from, to, t);

                if (_trajectory == ProjectileTrajectory.Ballistic)
                    pos += Vector3.up * (_ballisticHeight * 4f * t * (1f - t));   // парабола, пик на t=0.5

                if (_wobble != ProjectileWobble.Linear && _wobbleAmount > 0.0001f)
                {
                    Vector3 dir = to - from;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    dir.Normalize();
                    Vector3 right = Vector3.Cross(dir, Vector3.up);
                    if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
                    right.Normalize();

                    float envelope = Mathf.Sin(t * Mathf.PI);   // 0 на краях — снаряд точно приходит в цель
                    float phase = t * _wobbleFrequency * Mathf.PI * 2f;

                    if (_wobble == ProjectileWobble.Drift)
                    {
                        pos += right * (Mathf.Sin(phase) * _wobbleAmount * envelope);
                    }
                    else   // Vortex — спираль в обеих поперечных осях
                    {
                        Vector3 up2 = Vector3.Cross(right, dir).normalized;
                        pos += right * (Mathf.Cos(phase) * _wobbleAmount * envelope)
                             + up2   * (Mathf.Sin(phase) * _wobbleAmount * envelope);
                    }
                }

                return pos;
            }
        }

        // ── Область: один эффект на Bounds (Merge) или по эффекту на клетку ──
        void OnArea(AreaVfxEvent e)
        {
            if (e.Prefab == null || e.CellCenters == null || e.CellCenters.Length == 0) return;

            if (e.Merge)
            {
                Bounds b = CellsBounds(e.CellCenters, e.CellSize);
                var go = Instantiate(e.Prefab, Lift(b.center), Quaternion.identity);
                FitParticlesToBounds(go, b.size);
                AutoDestroy(go);
                foreach (var c in e.CellCenters) SpawnHit(e.HitPrefab, Lift(c));
            }
            else
            {
                float s = e.CellSize > 0.01f ? e.CellSize : 1f;
                foreach (var c in e.CellCenters)
                {
                    var go = Instantiate(e.Prefab, Lift(c), Quaternion.identity);
                    go.transform.localScale = Vector3.one * s;   // взрыв «в размер клетки»
                    AutoDestroy(go);
                    SpawnHit(e.HitPrefab, Lift(c));
                }
            }
        }

        void OnHit(HitVfxEvent e) => SpawnHit(e.Prefab, Lift(e.At), e.Scale);

        // ── helpers ──────────────────────────────────────────────────────────

        void SpawnHit(GameObject prefab, Vector3 at, float scale = 0f)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, at, Quaternion.identity);
            if (scale > 0.01f) go.transform.localScale = Vector3.one * scale;
            AutoDestroy(go);
        }

        Vector3 Lift(Vector3 p) => new Vector3(p.x, p.y + yOffset, p.z);

        /// <summary>Bounds набора клеток (центр±половина клетки), небольшая высота для объёма эмиссии.</summary>
        static Bounds CellsBounds(Vector3[] centers, float cellSize)
        {
            float s = cellSize > 0.01f ? cellSize : 1f;
            var cell = new Vector3(s, 0.3f, s);
            var b = new Bounds(centers[0], cell);
            for (int i = 1; i < centers.Length; i++) b.Encapsulate(new Bounds(centers[i], cell));
            return b;
        }

        /// <summary>Растягивает ОБЪЁМ эмиссии PS под Bounds (Box-shape.scale), НЕ трогая размер частиц.
        /// Плотность частиц подгоняем по площади (бёрст ∝ число «клеток» в области).</summary>
        static void FitParticlesToBounds(GameObject go, Vector3 size)
        {
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps == null) return;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;

            // плотность под площадь (в «клетках» ~ size.x*size.z)
            float area = Mathf.Max(1f, size.x * size.z);
            var emission = ps.emission;
            emission.burstCount = 1;   // гарантируем слот перед SetBurst (иначе бросит при пустых бёрстах)
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(area * 12f, 12f, 400f)));
            ps.Play();
        }

        void AutoDestroy(GameObject go)
        {
            float life = defaultLife;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                life = main.duration + main.startLifetime.constantMax;
            }
            Destroy(go, life);
        }
    }
}
