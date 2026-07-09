using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Выбор языка при ПЕРВОМ запуске (до логина/туториала). Кнопки языков генерятся из
    /// LocaleService.Available (новый язык в игре → кнопка появится сама). Клик по языку =
    /// применить (LocaleService.SetLanguage) + отметить FirstRunFlow.LanguageChosen + перейти дальше
    /// (LoginPanel; когда появится сцена туториала — роутинг подхватит InitState/TutorialState).
    /// Панель открывает InitState при первом запуске: canvas.OpenPanel&lt;LanguageSelectPanel&gt;().
    ///
    /// Префаб (на LoginCanvas): _optionsRoot (контейнер с LayoutGroup),
    /// _optionButtonPrefab (Button + TMP_Text в детях; можно неактивным внутри панели).
    /// </summary>
    public class LanguageSelectPanel : SourcePanel
    {
        [Header("Options")]
        [SerializeField] private Transform _optionsRoot;
        [SerializeField] private Button _optionButtonPrefab;

        readonly List<GameObject> _spawned = new();

        Coroutine _waitLocales;
        int _builtCount;

        public override void OnInject()
        {
            base.OnInject();
            RebuildOptions();
            // Поллинг локалей стартует в OnEnable: UIHandler.Inject зовёт OnInject у ВСЕХ панелей,
            // включая НЕАКТИВНЫЕ — StartCoroutine на неактивном объекте кидает ошибку.
            TryStartLocalesPolling();
        }

        void OnEnable() => TryStartLocalesPolling();

        void OnDisable()
        {
            if (_waitLocales != null) { StopCoroutine(_waitLocales); _waitLocales = null; }
        }

        // Unity Localization инициализируется АСИНХРОННО: в момент открытия панели список локалей
        // может нести только одну (дефолтную) — тогда кнопок «не хватает». Поллим список и
        // перестраиваемся, когда докатятся остальные (без зависимости UI на пакет локализации).
        void TryStartLocalesPolling()
        {
            if (!isActiveAndEnabled) return;   // неактивная панель — поллинг начнётся в её OnEnable
            if (_waitLocales != null) StopCoroutine(_waitLocales);
            _waitLocales = StartCoroutine(WaitForMoreLocales());
        }

        System.Collections.IEnumerator WaitForMoreLocales()
        {
            float deadline = Time.unscaledTime + 6f;
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                int count = 0;
                foreach (var _ in LocaleService.Available) count++;
                if (count != _builtCount)
                    RebuildOptions();
            }
            _waitLocales = null;
        }

        void RebuildOptions()
        {
            ClearOptions();
            if (_optionsRoot == null || _optionButtonPrefab == null)
            {
                Debug.LogWarning("[LanguageSelectPanel] _optionsRoot/_optionButtonPrefab не назначены в префабе.");
                return;
            }

            _builtCount = 0;
            foreach (var _ in LocaleService.Available) _builtCount++;

            foreach (var opt in LocaleService.Available)
            {
                var btn = Instantiate(_optionButtonPrefab, _optionsRoot);
                btn.gameObject.SetActive(true);
                _spawned.Add(btn.gameObject);

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = opt.DisplayName;

                string code = opt.Code;
                btn.onClick.AddListener(() => Choose(code));
            }
        }

        void Choose(string code)
        {
            LocaleService.SetLanguage(code);
            FirstRunFlow.LanguageChosen = true;
            Debug.Log($"[LanguageSelectPanel] выбран язык '{code}' → дальше по циклу первого захода");

            // Дальше по циклу: туториал (если не пройден) → иначе логин.
            if (!FirstRunFlow.TutorialDone)
                UnityEngine.SceneManagement.SceneManager.LoadScene(4);   // TutorialScene
            else
                _panelController.OpenPanel<Login.LoginPanel>();
        }

        void ClearOptions()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
        }

        public override void Unject()
        {
            if (_waitLocales != null) { StopCoroutine(_waitLocales); _waitLocales = null; }
            ClearOptions();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
