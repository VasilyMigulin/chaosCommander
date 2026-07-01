using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AwesomeUI.EditorTools.Fonts
{
    /// <summary>
    /// Генерация TMP SDF-шрифт-ассетов из .ttf с гарантированным покрытием RU+EN+ES:
    /// ASCII + Latin-1 (испанские á é í ó ú ñ ¿ ¡ ü) + кириллица + типографика (« » — … “ ”).
    ///
    /// Атлас — Dynamic: любой недостающий глиф догенерится сам, но весь основной набор
    /// «прожигается» сразу (TryAddCharacters) — поэтому нет хитча на первом показе и глифы
    /// видно в редакторе. Снимает риск «квадратов» на русском/испанском.
    ///
    /// Запуск: Tools → UI → Generate TMP Fonts (RU+EN+ES).
    /// По умолчанию берёт основные начертания Rubik из Assets/Fonts/static.
    /// Если в Project выделить .ttf — обработает именно их.
    /// Результат: Assets/UI/Fonts/TMP/&lt;Имя&gt; SDF.asset (атлас и материал — суб-ассетами).
    /// </summary>
    public static class TmpFontGenerator
    {
        const string DefaultFontDir = "Assets/Fonts/static";
        const string OutDir = "Assets/UI/Fonts/TMP";

        static readonly string[] DefaultWeights =
        {
            "Rubik-Regular", // тело, описания карт (с Auto Size)
            "Rubik-Medium",  // акценты
            "Rubik-Bold",    // кнопки, заголовки
            "Rubik-Black",   // титул/лого
        };

        // Параметры атласа. 90pt/1024 — хороший баланс чёткости и размера для UI.
        const int SamplingPointSize = 90;
        const int AtlasPadding = 9;
        const int AtlasW = 1024;
        const int AtlasH = 1024;

        [MenuItem("Tools/UI/Generate TMP Fonts (RU+EN+ES)")]
        public static void Generate()
        {
            var fonts = CollectFonts();
            if (fonts.Count == 0)
            {
                EditorUtility.DisplayDialog("TMP Fonts",
                    "Не найдено .ttf.\nВыдели шрифты в окне Project, либо положи Rubik в " + DefaultFontDir, "OK");
                return;
            }

            EnsureFolder(OutDir);
            FontEngine.InitializeFontEngine();

            string charset = BuildCharset();
            int ok = 0;

            try
            {
                for (int i = 0; i < fonts.Count; i++)
                {
                    var font = fonts[i];
                    EditorUtility.DisplayProgressBar("TMP Fonts", font.name, (float)i / fonts.Count);
                    try
                    {
                        if (Build(font, charset)) ok++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[TMP Fonts] {font.name}: {e.Message}\n{e}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TMP Fonts] Готово: {ok}/{fonts.Count} ассет(ов) → {OutDir}");
        }

        static bool Build(Font font, string charset)
        {
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasW, AtlasH, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError($"[TMP Fonts] Не удалось создать ассет из {font.name}");
                return false;
            }

            fontAsset.name = font.name + " SDF";

            string path = $"{OutDir}/{fontAsset.name}.asset";
            AssetDatabase.DeleteAsset(path); // перегенерация поверх старого

            // ВАЖНО — порядок как в самом TMP (TMP_FontAsset_CreationMenu):
            // сначала персистим ассет и кладём атлас-текстуру/материал суб-ассетами,
            // и ТОЛЬКО ПОТОМ прожигаем глифы. Иначе TryAddCharacters дёргает редакторный
            // коллбэк на ещё не сохранённую текстуру → UnassignedReferenceException (m_AtlasTextures).
            AssetDatabase.CreateAsset(fontAsset, path);

            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
            {
                var tex = fontAsset.atlasTextures[0];
                tex.name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            // Теперь текстура — суб-ассет, можно безопасно прожигать набор глифов.
            fontAsset.TryAddCharacters(charset, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[TMP Fonts] {font.name}: нет {missing.Length} символов из набора " +
                                 "(обычно редкая типографика — не критично).");

            EditorUtility.SetDirty(fontAsset);
            return true;
        }

        static List<Font> CollectFonts()
        {
            var list = new List<Font>();

            var selected = Selection.GetFiltered<Font>(SelectionMode.Assets);
            if (selected != null && selected.Length > 0)
            {
                list.AddRange(selected);
                return list;
            }

            foreach (var name in DefaultWeights)
            {
                string p = $"{DefaultFontDir}/{name}.ttf";
                var f = AssetDatabase.LoadAssetAtPath<Font>(p);
                if (f != null) list.Add(f);
                else Debug.LogWarning($"[TMP Fonts] Не найден {p}");
            }
            return list;
        }

        /// <summary>Набор символов под RU/EN/ES + типографика, встречающаяся в текстах карт.</summary>
        static string BuildCharset()
        {
            var sb = new StringBuilder();
            for (int c = 0x0020; c <= 0x007E; c++) sb.Append((char)c); // ASCII (латиница, цифры, пунктуация)
            for (int c = 0x00A0; c <= 0x00FF; c++) sb.Append((char)c); // Latin-1 (ñ á é í ó ú ü ¿ ¡ « »)
            for (int c = 0x0400; c <= 0x04FF; c++) sb.Append((char)c); // кириллица
            sb.Append("—–…“”„‘’•№€™");                                 // частая типографика
            return sb.ToString();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
