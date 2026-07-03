using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// «Материализация» существа (dissolve-in). Драг-розыгрыш: существо проявляется под пальцем.
    ///
    /// Если задан dissolve-материал (см. <see cref="SharedDissolve"/> — привяжи один раз, или положи
    /// материал в Resources/CreatureMaterialize) — временно подменяет материалы всех рендереров на его
    /// инстансы (копируя базовую текстуру/цвет) и гонит _DissolveAmount 0→1, затем возвращает оригиналы.
    /// Если материала нет — мягкий фолбэк: «поп»-масштаб (видно и без шейдера).
    ///
    /// Только визуал/локально. Компонент добавляется в рантайме на превью/инстанс, самоуничтожается по концу.
    /// </summary>
    public sealed class MaterializeEffect : MonoBehaviour
    {
        /// <summary>Общий dissolve-материал (URP-шейдер CreatureMaterialize). Привяжи один раз из бутстрапа
        /// боя ИЛИ оставь null — тогда берётся Resources.Load("CreatureMaterialize"), иначе фолбэк-масштаб.</summary>
        public static Material SharedDissolve;

        static readonly int DissolveId = Shader.PropertyToID("_DissolveAmount");
        static readonly int BaseMapId  = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId  = Shader.PropertyToID("_MainTex");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<(Renderer r, Material[] original)> _restore = new();
        readonly List<Material> _instances = new();
        Coroutine _routine;

        public void PlayIn(float duration = 0.35f)
        {
            var mat = ResolveDissolve();
            if (mat == null)
            {
                // Фолбэк без шейдера: «поп»-масштаб.
                transform.DOKill();
                Vector3 target = transform.localScale;
                transform.localScale = target * 0.5f;
                transform.DOScale(target, duration).SetEase(Ease.OutBack)
                    .OnComplete(() => Destroy(this));
                return;
            }

            SwapToDissolve(mat);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Dissolve(duration));
        }

        static Material ResolveDissolve()
        {
            if (SharedDissolve != null) return SharedDissolve;
            SharedDissolve = Resources.Load<Material>("CreatureMaterialize");   // опционально
            return SharedDissolve;
        }

        void SwapToDissolve(Material dissolve)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var orig = r.sharedMaterials;
                _restore.Add((r, orig));

                var swapped = new Material[orig.Length];
                for (int i = 0; i < orig.Length; i++)
                {
                    var inst = new Material(dissolve);
                    CopyBase(orig[i], inst);
                    inst.SetFloat(DissolveId, 0f);
                    _instances.Add(inst);
                    swapped[i] = inst;
                }
                r.materials = swapped;
            }
        }

        static void CopyBase(Material from, Material to)
        {
            if (from == null) return;
            Texture tex = from.HasProperty(BaseMapId) ? from.GetTexture(BaseMapId)
                        : (from.HasProperty(MainTexId) ? from.GetTexture(MainTexId) : null);
            if (tex != null && to.HasProperty(BaseMapId)) to.SetTexture(BaseMapId, tex);
            if (from.HasProperty(BaseColorId) && to.HasProperty(BaseColorId))
                to.SetColor(BaseColorId, from.GetColor(BaseColorId));
        }

        IEnumerator Dissolve(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / duration);
                foreach (var m in _instances) if (m != null) m.SetFloat(DissolveId, a);
                yield return null;
            }
            Restore();
            Destroy(this);
        }

        void Restore()
        {
            foreach (var (r, original) in _restore)
                if (r != null) r.sharedMaterials = original;
            foreach (var m in _instances) if (m != null) Destroy(m);
            _instances.Clear();
            _restore.Clear();
        }

        void OnDestroy()
        {
            if (_routine != null) StopCoroutine(_routine);
            Restore();
        }
    }
}
