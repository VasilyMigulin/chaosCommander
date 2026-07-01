using System.Collections.Generic;
using Game.Core.Instance.Card;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Кастомный инспектор CardInstanceData: сверху добавляет поле поиска по
    /// человекочитаемому Name (CardData.Name). Вводишь название — список совпадений
    /// прямо в инспекторе; клик переключает инспектор на нужный ассет
    /// (Selection.activeObject), не открывая отдельное окно.
    ///
    /// Наследуется от OdinEditor, поэтому тело карты (включая [SerializeReference]
    /// CardData) рисует Odin как обычно — кастом только добавляет блок поиска сверху.
    /// </summary>
    [CustomEditor(typeof(CardInstanceData))]
    public sealed class CardInstanceDataEditor : OdinEditor
    {
        const string ExpansionRoot = "Assets/Resources/Expansion";
        const int MaxResults = 12;

        struct Hit
        {
            public CardInstanceData Card;
            public string Name;
            public int Id;
        }

        // Кэш общий на все инспекторы — не пересобираем на каждую перерисовку.
        static readonly List<Hit> _all = new List<Hit>();
        static bool _built;

        string _query = "";
        readonly List<Hit> _results = new List<Hit>();
        bool _foldout = true;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!_built) Rebuild();
        }

        static void Rebuild()
        {
            _all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { ExpansionRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null) continue;

                _all.Add(new Hit
                {
                    Card = card,
                    Name = card.CardData != null ? card.CardData.Name : null,
                    Id   = card.CardData != null ? card.CardData.Id : -1,
                });
            }
            _all.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            _built = true;
        }

        void ApplyFilter()
        {
            _results.Clear();
            string q = _query == null ? "" : _query.Trim();
            if (q.Length == 0) return;

            foreach (var h in _all)
            {
                bool match =
                    (!string.IsNullOrEmpty(h.Name) && h.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (int.TryParse(q, out int id) && h.Id == id);

                if (match)
                {
                    _results.Add(h);
                    if (_results.Count >= MaxResults) break;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            DrawSearchBox();

            // Ручная фиксация Id/регистрации — на случай, если авто-назначение на импорте не сработало
            // (модель в CardData назначена ПОСЛЕ создания ассета → постпроцессор её пропустил).
            // НЕ делаем это авто на каждое изменение: Undo/SetDirty по [SerializeReference]-ассету во
            // время правки может обнулить только что добавленные данные. Поэтому только по кнопке.
            if (target is CardInstanceData card && card.CardData != null)
            {
                // Автосинк метки имени карты (для поиска в пикере l:<имя>). Идемпотентно + meta-only →
                // безопасно звать каждую перерисовку (постпроцессор импорта ловит создание ассета, когда
                // модель/имя ещё пустые, поэтому досинкиваем здесь, когда имя уже введено).
                CardNameLabelTool.Apply(card);

                if (GUILayout.Button("Assign / Fix Card Id"))
                {
                    CardIdAssigner.EnsureProcessed(card);
                    CardNameLabelTool.Apply(card);
                    Rebuild();
                }
            }

            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }

        void DrawSearchBox()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _foldout = EditorGUILayout.Foldout(_foldout, "🔍 Поиск карты по названию", true);
                if (!_foldout) return;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _query = EditorGUILayout.TextField(_query);
                    if (EditorGUI.EndChangeCheck())
                        ApplyFilter();

                    if (GUILayout.Button("⟳", GUILayout.Width(26)))
                    {
                        Rebuild();
                        ApplyFilter();
                    }
                }

                if (string.IsNullOrEmpty(_query)) return;

                if (_results.Count == 0)
                {
                    EditorGUILayout.LabelField("Ничего не найдено", EditorStyles.miniLabel);
                    return;
                }

                foreach (var h in _results)
                {
                    string label = string.IsNullOrEmpty(h.Name) ? "<no name>" : h.Name;
                    bool isCurrent = h.Card == target;

                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        if (GUILayout.Button($"{label}   (Id {h.Id})", EditorStyles.miniButton))
                        {
                            Selection.activeObject = h.Card;
                            EditorGUIUtility.PingObject(h.Card);
                            GUIUtility.ExitGUI(); // выходим из OnGUI — target меняется
                        }
                    }
                }
            }
        }
    }
}
