using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Ручная подгонка VFX-эффекта под РЕАЛЬНЫЕ границы игрового поля (BoardView) — для эффектов, чья
    /// исходная форма не совпадает со столом (круглый эффект на прямоугольном поле и т.п.). Никакого
    /// отдельного гизмо/BoxCollider двигать не нужно — границы берутся прямо у доски (GetFieldBounds).
    ///
    /// Источник размера — ЯВНО назначенный список Particle System (_sourceSystems), а НЕ авто-сбор всех
    /// Renderer в детях: у префаба обычно несколько систем (ядро/искры/дым), и не все из них должны считаться
    /// «телом» эффекта. Меряем ДЕТЕРМИНИРОВАННО через Shape-модуль (radius/scale) — авторские данные, не
    /// зависят от того, идёт ли симуляция ПРЯМО СЕЙЧАС (через живые Renderer.bounds меряли раньше — поймали
    /// баг: почти нулевой замер вне симуляции → деление на почти-ноль → масштаб улетал в бесконечность,
    /// Unity ARP: "Invalid worldAABB"/NaN sort distance).
    ///
    /// Coverage — какая ДОЛЯ измеренного габарита должна попасть внутрь границ поля. 1 = весь габарит
    /// эффекта = границам стола (жёсткий край). 0.6 — только центральные 60% укладываются в поле, эффект
    /// крупнее, мягкий спад по краям специально уходит ЗА пределы стола.
    /// </summary>
    [ExecuteAlways]
    public sealed class VfxFieldFit : MonoBehaviour
    {
        [Tooltip("Доска, чьи границы берём (BoardView.GetFieldBounds). Пусто → найдёт единственный BoardView " +
                 "в сцене сам.")]
        [SerializeField] BoardView _board;

        [Tooltip("КАКИЕ Particle System считать «телом» эффекта для замера размера. Перетащи сюда только те " +
                 "дочерние системы, что реально формируют видимую область (не искры/дым поверх). Радиус/scale " +
                 "их Shape-модуля — авторские данные, читаются даже когда система не проигрывается.")]
        [SerializeField] ParticleSystem[] _sourceSystems;

        [Range(0.05f, 2f)]
        [Tooltip("Доля измеренного габарита _sourceSystems, которая должна попасть в границы поля. 1 = впритык " +
                 "по обеим осям. Меньше 1 — эффект крупнее, в поле укладывается только его центральная часть.")]
        [SerializeField] float _coverage = 1f;

        [Tooltip("Пересчитывать в Edit Mode при каждом изменении полей. Реально сработает только когда доска " +
                 "уже построена (BoardView.IsBuilt — т.е. игра запущена, BuildBoard прошёл в Awake). Выключи, " +
                 "если эффект сам меняет масштаб рантаймом — подгони один раз и выключи.")]
        [SerializeField] bool _liveInEditor = false;

        // Анти-катастрофа: даже при плохом входе масштаб не может улететь за эти границы — тот самый
        // "Invalid worldAABB" больше физически не собрать.
        const float MinFactor = 0.02f;
        const float MaxFactor = 50f;

        BoardView Board
        {
            get
            {
                if (_board == null) _board = FindFirstObjectByType<BoardView>();
                return _board;
            }
        }

        void OnEnable() => Fit();
        void OnValidate() => Fit();

        void Update()
        {
            if (Application.isPlaying || !_liveInEditor) return;
            Fit();
        }

        /// <summary>Пересчитать масштаб/позицию под текущие границы доски/_sourceSystems/Coverage. ТОЛЬКО
        /// Edit Mode — это инструмент авторинга/превью, а не рантайм-компонент: в Play карту этого же
        /// префаба спавнит VfxPresenter.OnArea и САМ корректно подгоняет её под ЗОНУ конкретного каста
        /// (Own/Enemy/All — не обязательно всё поле, см. FitParticlesToBounds/ZoneBounds). Если пустить
        /// сюда ещё и Fit() на рантайм-клоне — OnEnable этого компонента сработает СЛЕДОМ и перезатрёт уже
        /// верно выставленные позицию/масштаб подгонкой под ВСЁ поле, что бы ни считал VfxPresenter (баг:
        /// «кто-то» рожает под каждым кастом смещённый/неверно проскейленный эффект).</summary>
        [ContextMenu("Fit Now")]
        public void Fit()
        {
            if (Application.isPlaying) return;

            var board = Board;
            if (board == null || !board.IsBuilt) return;   // доска ещё не построена (BuildBoard — только в Play) — подгонять не по чему

            Vector2 sourceSize = MeasureSourceSize();
            if (sourceSize.x <= 0.0001f || sourceSize.y <= 0.0001f) return;   // нечего мерить — не трогаем масштаб

            Bounds fieldBounds = board.GetFieldBounds();
            Vector3 target = fieldBounds.size;
            if (target.x <= 0.0001f || target.z <= 0.0001f) return;

            float coverage = Mathf.Max(0.01f, _coverage);
            float factor = Mathf.Min(target.x / sourceSize.x, target.z / sourceSize.y) / coverage;
            if (float.IsNaN(factor) || float.IsInfinity(factor)) return;

            float clamped = Mathf.Clamp(factor, MinFactor, MaxFactor);
            if (!Mathf.Approximately(clamped, factor))
                Debug.LogWarning($"[VfxFieldFit] {name}: посчитанный масштаб {factor:F3} вышел за разумные границы " +
                                  $"[{MinFactor}, {MaxFactor}] — зажат. Проверь _sourceSystems.");

            transform.localScale = Vector3.one * clamped;
            transform.position = new Vector3(fieldBounds.center.x, transform.position.y, fieldBounds.center.z);
        }

        /// <summary>X/Z-габарит по Shape-модулям _sourceSystems (радиус/scale, БЕЗ учёта транcформа —
        /// как «отпечаток» эффекта при масштабе 1,1,1). Системы без включённого Shape/незнакомого типа
        /// в замер не входят (не портят его нулями/мусором).</summary>
        Vector2 MeasureSourceSize()
        {
            float maxExtent = 0f;
            if (_sourceSystems != null)
            {
                foreach (var ps in _sourceSystems)
                {
                    if (ps == null) continue;
                    float e = ShapeExtent(ps);
                    if (e > maxExtent) maxExtent = e;
                }
            }
            float diameter = maxExtent * 2f;
            return new Vector2(diameter, diameter);
        }

        static float ShapeExtent(ParticleSystem ps)
        {
            var shape = ps.shape;
            if (!shape.enabled) return 0f;

            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.Cone:
                    return shape.radius * Mathf.Max(shape.scale.x, shape.scale.z);
                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                    return Mathf.Max(shape.scale.x, shape.scale.z) * 0.5f;
                default:
                    return 0f;   // экзотика (Mesh/Sprite и т.п.) — не пытаемся угадать, просто не участвует
            }
        }

        void OnDrawGizmosSelected()
        {
            var board = Board;
            if (board == null || !board.IsBuilt) return;
            Bounds b = board.GetFieldBounds();
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
