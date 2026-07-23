using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AwesomeUI.Core.Card.EditorTools
{
    /// <summary>
    /// Инспектор-тулза для CardBaseView (и ЛЮБОГО наследника — второй параметр атрибута true): один слайдер
    /// меняет размер карты, сохраняя пропорции базового шаблона (200×360, соотношение 5:9). Работает только
    /// на самом RectTransform карты — все дочерние элементы с процентными (stretch) якорями подхватывают
    /// новый размер САМИ (см. память project_ui_layer.md); отдельно докручивает LayoutElement-детей
    /// (напр. цветовые индикаторы Red/Blue/…) — их preferredWidth/Height абсолютные пиксели, якорями не
    /// тянутся, поэтому масштабируются тут вручную на тот же коэффициент.
    /// </summary>
    [CustomEditor(typeof(CardBaseView), true)]
    [CanEditMultipleObjects]
    public class CardBaseViewEditor : Editor
    {
        const float BaseWidth = 200f;
        const float BaseHeight = 360f;
        const float MinScale = 0.5f;
        const float MaxScale = 3f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var view = (CardBaseView)target;
            var rt = view.transform as RectTransform;
            if (rt == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Card Size", EditorStyles.boldLabel);

            float currentScale = rt.sizeDelta.x > 0 ? rt.sizeDelta.x / BaseWidth : 1f;

            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.Slider("Scale (база 200×360)", currentScale, MinScale, MaxScale);
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newScale, currentScale))
                ApplyScale(rt, currentScale, newScale);

            EditorGUILayout.LabelField($"Итоговый размер: {BaseWidth * currentScale:0}×{BaseHeight * currentScale:0}", EditorStyles.miniLabel);

            // Нормализация к эталону: у карточных префабов разная история ресайзов (руками/старой тулзой) —
            // внутренние пиксельные значения (LayoutElement.preferred*, шрифты TMP) разъехались относительно
            // размера карты, и, например, полоска CreatureStatsHolder у всех выглядит по-разному. Кнопка
            // проставляет значения из БАЗОВОГО CardBaseView.prefab × текущий масштаб карты (матч детей по
            // пути в иерархии; лишние элементы, которых нет в эталоне, не трогаются).
            if (GUILayout.Button("Normalize to base (LayoutElement + шрифты из эталона)"))
                NormalizeToBase(rt, currentScale);
        }

        const string BasePrefabPath = "Assets/UI/Feature/Prefab/CardView/CardBaseView.prefab";

        void NormalizeToBase(RectTransform rt, float currentScale)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            if (basePrefab == null)
            {
                Debug.LogError($"[CardBaseViewEditor] Эталон не найден: {BasePrefabPath}");
                return;
            }
            var baseRoot = basePrefab.transform;

            int applied = 0, skipped = 0;

            foreach (var tmp in rt.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                var baseTmp = FindByPath<TextMeshProUGUI>(baseRoot, RelativePath(rt.transform, tmp.transform));
                if (baseTmp == null) { skipped++; continue; }

                Undo.RecordObject(tmp, "Normalize Card (Font)");
                tmp.fontSize = baseTmp.fontSize * currentScale;
                if (tmp.enableAutoSizing)
                {
                    tmp.fontSizeMin = baseTmp.fontSizeMin;                 // Min не масштабируем (см. ApplyScale)
                    tmp.fontSizeMax = baseTmp.fontSizeMax * currentScale;
                }
                EditorUtility.SetDirty(tmp);
                applied++;
            }

            foreach (var le in rt.GetComponentsInChildren<LayoutElement>(true))
            {
                var baseLe = FindByPath<LayoutElement>(baseRoot, RelativePath(rt.transform, le.transform));
                if (baseLe == null) { skipped++; continue; }

                Undo.RecordObject(le, "Normalize Card (LayoutElement)");
                if (baseLe.preferredWidth  > 0) le.preferredWidth  = baseLe.preferredWidth  * currentScale;
                if (baseLe.preferredHeight > 0) le.preferredHeight = baseLe.preferredHeight * currentScale;
                if (baseLe.minWidth  > 0) le.minWidth  = baseLe.minWidth  * currentScale;
                if (baseLe.minHeight > 0) le.minHeight = baseLe.minHeight * currentScale;
                EditorUtility.SetDirty(le);
                applied++;
            }

            Debug.Log($"[CardBaseViewEditor] Normalize to base: применено {applied}, пропущено (нет в эталоне) {skipped} " +
                      $"(масштаб {currentScale:0.##})");
        }

        // Путь ребёнка относительно корня карты ("CreatureStatsHolder/Attack/Text") — для матча с эталоном.
        static string RelativePath(Transform root, Transform child)
        {
            if (child == root) return "";
            var path = child.name;
            for (var t = child.parent; t != null && t != root; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        static T FindByPath<T>(Transform baseRoot, string path) where T : Component
        {
            if (string.IsNullOrEmpty(path)) return baseRoot.GetComponent<T>();
            var t = baseRoot.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        void ApplyScale(RectTransform rt, float oldScale, float newScale)
        {
            Undo.RecordObject(rt, "Resize Card");
            rt.sizeDelta = new Vector2(BaseWidth * newScale, BaseHeight * newScale);
            EditorUtility.SetDirty(rt);

            // LayoutElement-дети (фиксированные пиксельные размеры — не тянутся якорями родителя) — докручиваем
            // на тот же коэффициент, чтобы, например, цветовые индикаторы 20×20 при базовом размере остались
            // в той же ПРОПОРЦИИ к карте на любом её размере.
            float factor = oldScale > 0 ? newScale / oldScale : 1f;
            foreach (var le in rt.GetComponentsInChildren<LayoutElement>(true))
            {
                Undo.RecordObject(le, "Resize Card (LayoutElement)");
                if (le.preferredWidth  > 0) le.preferredWidth  *= factor;
                if (le.preferredHeight > 0) le.preferredHeight *= factor;
                if (le.minWidth  > 0) le.minWidth  *= factor;
                if (le.minHeight > 0) le.minHeight *= factor;
                EditorUtility.SetDirty(le);
            }

            // TMP-тексты (напр. Description) — fontSize и ВЕРХ диапазона autoSize заданы в px под базовый
            // размер 200×360 и сами не растут вместе с контейнером — докручиваем на тот же коэффициент.
            // fontSizeMin НЕ масштабируем: это нижний предел ужатия — на больших картах он раздувался
            // (до ~30) и autoSize не мог ужать длинные описания ниже него → текст вылезал за границы.
            foreach (var tmp in rt.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                Undo.RecordObject(tmp, "Resize Card (Font)");
                tmp.fontSize *= factor;
                if (tmp.enableAutoSizing)
                    tmp.fontSizeMax *= factor;
                EditorUtility.SetDirty(tmp);
            }
        }
    }
}
