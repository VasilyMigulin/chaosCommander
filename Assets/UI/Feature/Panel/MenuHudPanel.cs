using System;
using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Window;
using AwesomeUI.Interface;
using DG.Tweening;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Персистентный HUD меню: TopBar (аватар/ник, валюта, настройки) + BottomBar (нав-кнопки).
    /// **В инспекторе isAlwaysOpen = true** — SourceCanvas.OpenPanel держит его открытым, контент
    /// свапается «под» ним (в бою другой канвас — HUD выключается вместе с MenuCanvas).
    ///
    /// СКРЫТИЕ HUD. Гасит СВОЙ CanvasGroup (весь HUD целиком; безопасно — у isAlwaysOpen _isOpen=true,
    /// SourcePanel.OnOpen не перебивает альфу). Прячется, если ВЫПОЛНЕНО ЛЮБОЕ:
    ///   • открыта одна из панелей списка _hideForPanels (по id — дропдаун UI/Panel). Удобно для
    ///     фуллскрин-разделов (напр. осмотр карты в коллекции): указал id — HUD сам прячется/возвращается;
    ///   • кто-то позвал HudVisibility.Hide (или компонент HidesHud на попапе вне фреймворка).
    /// </summary>
    public class MenuHudPanel : SourcePanel
    {
        [Tooltip("Панели/окна, при открытии которых HUD скрывается (дропдаун UI/Panel). Работает и с " +
                 "SourcePanel (по PanelId), и с SourceWindow (по WindowId). Напр. фуллскрин-осмотр карты.")]
        [RegistryCategory(RegistryCategories.Panel)]
        [SerializeField] private RegistryId[] _hideForPanels;
        [SerializeField] private float _fadeDur = 0.2f;

        [Header("Top bar actions")]
        [Tooltip("Кнопка «Выход» (✖) в топ-баре. Настройки (⚙) — декларативный AwesomeButton с _targetPanelId=SettingsPanel.")]
        [SerializeField] private AwesomeButton _exitBtn;

        // Унифицированные проверки «открыто ли» — панель (isOpen) или окно (activeInHierarchy).
        readonly List<Func<bool>> _checks = new List<Func<bool>>();
        bool _resolvedOnce;
        bool _shown = true;
        Tween _fade;

        public override void OnInject()
        {
            base.OnInject();
            HidesHud.Armed = true;   // UI прогрелся — init-churn позади, HidesHud теперь можно реагировать

            // ЗАЩИТА ОТ ОШИБКИ ПРОВЯЗКИ. В _exitBtn однажды оказалась кнопка BtnBack (навигация «назад»),
            // и каждый уход на предыдущую панель дополнительно вызывал Application.Quit(): на Android
            // приложение просто закрывалось, а в РЕДАКТОРЕ баг невидим — там Application.Quit() ничего не
            // делает, видна только навигация. Поэтому навигационную кнопку на выход не вешаем и кричим в лог.
            if (_exitBtn != null && _exitBtn.IsNavigation)
            {
                Debug.LogError($"[MenuHudPanel] В поле «Кнопка Выход» назначена НАВИГАЦИОННАЯ кнопка "
                             + $"'{_exitBtn.name}' (Back/Target Panel). Выход не вешаю — иначе она закрывала бы "
                             + $"приложение на билде. Оставь поле пустым или назначь отдельную кнопку ✖.", this);
            }
            else if (_exitBtn != null)
            {
                _exitBtn.onClick.AddListener(Quit);
            }

            _shown = Desired();
            Apply(_shown, animate: false);
        }

        // Логируем ДО выхода: молчаливое закрытие на устройстве неотличимо от краша (проверено логами
        // logcat 2026-07-20 — на поиск ушёл час именно потому, что выход был безмолвным).
        static void Quit()
        {
            Debug.Log("[MenuHudPanel] Выход из приложения по кнопке ✖ (Application.Quit)");
            Application.Quit();
        }

        void Update()
        {
            bool desired = Desired();
            if (desired != _shown) { _shown = desired; Apply(desired, animate: true); }
        }

        // HUD виден, если никто не просил скрыть И не открыт ни один hide-раздел (панель/окно).
        bool Desired() => HudVisibility.Visible && !AnyHideOpen();

        bool AnyHideOpen()
        {
            ResolveHideList();
            foreach (var isOpen in _checks)
                if (isOpen()) return true;
            return false;
        }

        // Резолвим id ОДИН раз: панель через контроллер (isOpen), иначе окно по WindowId (activeInHierarchy).
        void ResolveHideList()
        {
            if (_resolvedOnce || _panelController == null) return;
            _resolvedOnce = true;
            _checks.Clear();
            if (_hideForPanels == null) return;

            SourceWindow[] windows = null;
            foreach (var id in _hideForPanels)
            {
                if (id.IsEmpty) continue;
                string key = id.Value;

                var panel = _panelController.GetPanel(key);
                if (panel != null) { var p = panel; _checks.Add(() => p.isOpen); continue; }

                if (windows == null) windows = FindObjectsOfType<SourceWindow>(true);   // включая неактивные
                SourceWindow found = null;
                foreach (var w in windows)
                    if (w != null && w.WindowId == key) { found = w; break; }
                if (found != null) { var wc = found; _checks.Add(() => wc != null && wc.gameObject.activeInHierarchy); }
            }
        }

        void Apply(bool visible, bool animate)
        {
            var cg = _canvasGroup != null ? _canvasGroup : GetComponent<CanvasGroup>();
            if (cg == null) return;

            cg.blocksRaycasts = visible;   // скрытый HUD не перехватывает клики попапа
            cg.interactable = visible;

            _fade?.Kill();
            if (animate) _fade = cg.DOFade(visible ? 1f : 0f, _fadeDur).SetUpdate(true);
            else cg.alpha = visible ? 1f : 0f;
        }

        public override void Unject()
        {
            if (_exitBtn != null) _exitBtn.onClick.RemoveListener(Quit);
            _fade?.Kill();
        }

        public override void OnDipose()
        {
            _fade?.Kill();
            base.OnDipose();
        }
    }
}
