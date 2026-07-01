using DG.Tweening;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Единственная точка инстанцирования боевых VFX. Подписывается на косметические события (Beam/Projectile/
    /// Area/Hit), которые ECS публикует с ГОТОВЫМИ мировыми позициями + префабом из спеки способности.
    /// Ничего не знает про ECS/синк — чистая презентация. Повесить ОДИН раз на сцену боя.
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

        // ── Луч: мгновенная линия каст→цель ──────────────────────────────────
        void OnBeam(BeamVfxEvent e)
        {
            if (e.Prefab == null) return;
            Vector3 from = Lift(e.From), to = Lift(e.To);

            var go = Instantiate(e.Prefab, from, Quaternion.identity);
            var lr = go.GetComponentInChildren<LineRenderer>();
            if (lr != null)
            {
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, from);
                lr.SetPosition(1, to);
            }
            AutoDestroy(go);
            SpawnHit(e.HitPrefab, to);
        }

        // ── Снаряд: летит каст→цель, на прилёте — хит ────────────────────────
        void OnProjectile(ProjectileVfxEvent e)
        {
            if (e.Prefab == null) return;
            Vector3 from = Lift(e.From), to = Lift(e.To);

            var go = Instantiate(e.Prefab, from, Quaternion.identity);
            Vector3 dir = to - from;
            if (dir.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(dir);

            float speed = e.Speed > 0.01f ? e.Speed : 12f;
            float dur = Mathf.Max(0.05f, Vector3.Distance(from, to) / speed);

            int token = e.Token;
            go.transform.DOMove(to, dur).SetEase(Ease.Linear).OnComplete(() =>
            {
                SpawnHit(e.HitPrefab, to);
                // Сигнал резолву: снаряд долетел → применить отложенные эффекты (если способность ждёт хит).
                if (token >= 0) GameEventBus.Publish(new VfxArrivedEvent { Token = token });
                AutoDestroy(go);   // дать шлейфу/частицам догаснуть
            });
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
