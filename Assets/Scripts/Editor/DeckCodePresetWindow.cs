using System.Collections.Generic;
using System.IO;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Собирает колоду визуально в игре (коллекция/редактор колод) → «Скопировать код» → вставить сюда →
    /// указать имя → «Собрать» → получить готовый DeckPreset-ассет (Game/Deck Preset), не перебирая карты
    /// руками в инспекторе. Код — тот же формат, что и DeckBuildPanel.OnCopyCodeClicked (DeckCode.Encode).
    ///
    /// Сайдборд (Сказочник и подобные) в DeckPreset НЕ переносится — у формата пресета нет такого поля
    /// (пресеты сейчас служат тест-колодами для InitState, там сайдборд не нужен).
    ///
    /// Tools → Cards → Deck Code → Preset.
    /// </summary>
    public sealed class DeckCodePresetWindow : EditorWindow
    {
        const string CardConfigResourcePath = "Configs/CardConfig";
        const string DefaultFolder = "Assets/Resources/DeckPresets";

        string _code = "";
        string _presetName = "Новая колода";
        string _status = "";
        MessageType _statusType = MessageType.None;

        [MenuItem("Tools/Cards/Deck Code → Preset")]
        static void Open()
        {
            var w = GetWindow<DeckCodePresetWindow>("Deck Code → Preset");
            w.minSize = new Vector2(360, 260);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Код колоды (из «Скопировать код» в редакторе колод)", EditorStyles.boldLabel);
            _code = EditorGUILayout.TextArea(_code, GUILayout.MinHeight(90));

            EditorGUILayout.Space();
            _presetName = EditorGUILayout.TextField("Имя пресета", _presetName);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_code) || string.IsNullOrWhiteSpace(_presetName)))
            {
                if (GUILayout.Button("Собрать", GUILayout.Height(30)))
                    Build();
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, _statusType);
            }
        }

        void Build()
        {
            if (!DeckCode.TryDecode(_code, out var data))
            {
                SetStatus("Код не распознан — проверь, что скопирован целиком (кнопка «Скопировать код»).", MessageType.Error);
                return;
            }

            var cardConfig = Resources.Load<CardConfig>(CardConfigResourcePath);
            if (cardConfig == null)
            {
                SetStatus($"Не найден CardConfig по пути Resources/{CardConfigResourcePath}.", MessageType.Error);
                return;
            }

            var commanderInst = cardConfig.Get(data.Commander.ExpansionId, data.Commander.CardId);
            if (commanderInst == null)
            {
                SetStatus($"Командир не найден в каталоге: {data.Commander.ExpansionId}/{data.Commander.CardId}.", MessageType.Error);
                return;
            }

            var entries = new List<DeckPreset.Entry>();
            int skipped = 0;
            if (data.Cards != null)
            {
                foreach (var card in data.Cards)
                {
                    var inst = cardConfig.Get(card.ExpansionId, card.CardId);
                    if (inst == null) { skipped++; continue; }
                    entries.Add(new DeckPreset.Entry { Card = inst, Count = card.Count });
                }
            }

            var preset = ScriptableObject.CreateInstance<DeckPreset>();
            preset.DeckName  = _presetName;
            preset.Commander = commanderInst;
            preset.Cards     = entries;

            if (!Directory.Exists(DefaultFolder)) Directory.CreateDirectory(DefaultFolder);
            string safeName = string.Join("_", _presetName.Split(Path.GetInvalidFileNameChars()));
            string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultFolder}/{safeName}.asset");

            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);

            string msg = $"Готово: {path} (командир + {entries.Count} карт)";
            if (skipped > 0) msg += $" — не найдено в каталоге: {skipped}";
            SetStatus(msg, skipped > 0 ? MessageType.Warning : MessageType.Info);
        }

        void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }
    }
}
