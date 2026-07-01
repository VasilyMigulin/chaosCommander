using System.Collections.Generic;
using Game.Core.Instance.Card;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Окно поиска карт (CardInstanceData) по человекочитаемому Name (CardData.Name),
    /// а не по имени ассета (creature_card_000 и т.п.).
    ///
    /// Tools → Cards → Find Card By Name. Ввод фильтрует по подстроке в Name
    /// (без учёта регистра); дополнительно матчит по Id и имени ассета.
    /// Клик по строке — пинг в Project; двойной клик / кнопка Select — выделить ассет.
    /// </summary>
    public sealed class CardFinderWindow : EditorWindow
    {
        const string ExpansionRoot = "Assets/Resources/Expansion";

        struct Entry
        {
            public CardInstanceData Card;
            public string Name;
            public int Id;
            public string AssetName;
            public string Path;
        }

        readonly List<Entry> _all = new List<Entry>();
        readonly List<Entry> _filtered = new List<Entry>();

        string _query = "";
        Vector2 _scroll;

        [MenuItem("Tools/Cards/Find Card By Name %#t")]
        static void Open()
        {
            var w = GetWindow<CardFinderWindow>("Find Card");
            w.minSize = new Vector2(380, 300);
            w.Refresh();
        }

        void OnEnable() => Refresh();

        void Refresh()
        {
            _all.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { ExpansionRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null) continue;

                _all.Add(new Entry
                {
                    Card      = card,
                    Name      = card.CardData != null ? card.CardData.Name : null,
                    Id        = card.CardData != null ? card.CardData.Id : -1,
                    AssetName = card.name,
                    Path      = path,
                });
            }

            _all.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            ApplyFilter();
        }

        void ApplyFilter()
        {
            _filtered.Clear();
            string q = _query == null ? "" : _query.Trim();

            foreach (var e in _all)
            {
                if (q.Length == 0 || Matches(e, q))
                    _filtered.Add(e);
            }
        }

        static bool Matches(Entry e, string q)
        {
            if (!string.IsNullOrEmpty(e.Name) &&
                e.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(e.AssetName) &&
                e.AssetName.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return int.TryParse(q, out int id) && e.Id == id;
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _query = GUILayout.TextField(_query, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck())
                    ApplyFilter();

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    _query = "";
                    GUI.FocusControl(null);
                    ApplyFilter();
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    Refresh();
            }

            EditorGUILayout.LabelField($"Найдено: {_filtered.Count} / {_all.Count}", EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var e in _filtered)
                DrawRow(e);
            EditorGUILayout.EndScrollView();
        }

        void DrawRow(Entry e)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                string title = string.IsNullOrEmpty(e.Name) ? "<no name>" : e.Name;

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Id {e.Id}   •   {e.AssetName}", EditorStyles.miniLabel);
                }

                if (GUILayout.Button("Ping", GUILayout.Width(46), GUILayout.Height(28)))
                    EditorGUIUtility.PingObject(e.Card);

                if (GUILayout.Button("Select", GUILayout.Width(56), GUILayout.Height(28)))
                {
                    Selection.activeObject = e.Card;
                    EditorGUIUtility.PingObject(e.Card);
                }
            }
        }
    }
}
