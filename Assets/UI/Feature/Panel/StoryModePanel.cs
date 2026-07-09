using System.Collections.Generic;
using System.Linq;
using AwesomeUI.Core.Panel;
using Game.Core.Configs;
using Game.Core.Service;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Стори-режим: список уровней (PvE-энкаунтеров) из слотов StoryLevelViewSlot. Уровни = ассеты
    /// PveEncounterConfig в Resources/Encounter/, порядок по имени ассета (encounter_001, encounter_002, …)
    /// → новый уровень добавляется просто новым ассетом, без правок кода/префабов.
    ///
    /// Прогрессия: уровень открыт, если он ПЕРВЫЙ или предыдущий пройден; пройденные можно переигрывать.
    /// Пройденность: PlayerPrefs[PveMode.DoneKey(path)] — пишет BattleState при победе в PvE.
    /// Клик по открытому уровню → PveMode + загрузка BattleScene (бой против ИИ).
    ///
    /// Префаб: _backBtn («Назад»), _levelsRoot (контейнер с Vertical/GridLayoutGroup),
    /// _levelSlotPrefab (префаб StoryLevelViewSlot; может лежать неактивным внутри панели).
    /// </summary>
    public class StoryModePanel : SourcePanel
    {
        const string CampaignsFolder = "Campaign";       // Assets/Resources/Campaign/ (ассеты кампаний)
        const string PveResourcesFolder = "Encounter";   // Assets/Resources/Encounter/ (фолбэк без кампании)
        const int BattleSceneIndex = 3;

        // Старт боя идёт через MenuState.StartPveBattle (гасит активный матчмейкинг перед LoadScene —
        // иначе его асинхронный фейл открывал меню-канвас поверх боевого UI).
        [AwesomeUI.Core.Attributes.UIInject] private Game.Core.Shared.Interface.IMenuStateContext _state;

        [Header("Navigation")]
        [SerializeField] private Button _backBtn;

        [Header("Levels")]
        [SerializeField] private Transform _levelsRoot;
        [SerializeField] private StoryLevelViewSlot _levelSlotPrefab;

        readonly List<StoryLevelViewSlot> _slots = new();

        public override void OnInject()
        {
            base.OnInject();

            if (_backBtn != null)
                _backBtn.onClick.AddListener(() => _panelController.OpenPanel<MainMenuPanel>());

            RebuildLevelList();
        }

        void RebuildLevelList()
        {
            ClearList();
            if (_levelsRoot == null || _levelSlotPrefab == null)
            {
                Debug.LogWarning("[StoryModePanel] _levelsRoot/_levelSlotPrefab не назначены в префабе — список уровней не построен.");
                return;
            }

            // Источник уровней: КАМПАНИЯ (Resources/Campaign, первая по имени — MVP: одна кампания,
            // выбор из нескольких добавим позже). Фолбэк без кампаний — скан Resources/Encounter.
            PveEncounterConfig[] encounters;
            var campaigns = Resources.LoadAll<CampaignConfig>(CampaignsFolder).OrderBy(c => c.name).ToArray();
            if (campaigns.Length > 0)
            {
                encounters = campaigns[0].Encounters.Where(e => e != null).ToArray();
                if (campaigns.Length > 1)
                    Debug.LogWarning($"[StoryModePanel] Кампаний {campaigns.Length} — показываю первую ('{campaigns[0].name}'); выбор кампании появится позже.");
            }
            else
            {
                encounters = Resources.LoadAll<PveEncounterConfig>(PveResourcesFolder).OrderBy(e => e.name).ToArray();
            }

            if (encounters.Length == 0)
            {
                Debug.LogWarning($"[StoryModePanel] Нет уровней: ни кампании в Resources/{CampaignsFolder}/, ни энкаунтеров в Resources/{PveResourcesFolder}/.");
                return;
            }

            bool previousDone = true;   // первый уровень всегда открыт
            for (int i = 0; i < encounters.Length; i++)
            {
                var enc = encounters[i];
                bool done = PlayerPrefs.GetInt(PveMode.DoneKey(enc.name), 0) == 1;   // прогресс по ИМЕНИ ассета
                bool locked = !previousDone;   // открыт, если предыдущий пройден

                var slot = Instantiate(_levelSlotPrefab, _levelsRoot);
                slot.gameObject.SetActive(true);
                slot.Init();
                var captured = enc;   // замыкание на элемент цикла
                slot.SetData(i, enc.EncounterName, done, locked, icon: null,
                             onStart: () => StartLevel(captured));
                _slots.Add(slot);

                previousDone = done;
            }
        }

        void StartLevel(PveEncounterConfig encounter)
        {
            // Кампания передаёт энкаунтер ПРЯМОЙ ссылкой: PveEncounterLocator возьмёт её приоритетнее
            // Resources-пути; прогресс пишется по имени ассета (PveMode.EncounterId → CurrentDoneKey).
            PveMode.EncounterAsset = encounter;
            PveMode.EncounterId = encounter.name;

            if (_state != null) { _state.StartPveBattle(null); return; }

            // Фолбэк без контекста меню (не должен случаться): прямой старт.
            PveMode.Enabled = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene(BattleSceneIndex);
        }

        void ClearList()
        {
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                slot.Dispose();
                Destroy(slot.gameObject);
            }
            _slots.Clear();
        }

        public override void Unject()
        {
            if (_backBtn != null) _backBtn.onClick.RemoveAllListeners();
            ClearList();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
