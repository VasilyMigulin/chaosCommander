using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// ПОСТОЯННЫЕ визуалы свойств/статусов (щит-бабл «Укреплённого», крутящаяся аура «Защитника», значок
    /// «Отравлен» над головой) — РЕАКТИВНО, по CreaturePropertyAuraChangedEvent, а не покадровым опросом всех
    /// существ на поле: состояние меняется в ЧЁТКО ИЗВЕСТНЫХ точках (Apply/Remove свойства, TakeDamageSystem,
    /// DieSystem), а не непрерывно (в отличие от HP/атаки/скорости у CreatureStatsViewSystem — там события
    /// пришлось бы городить на КАЖДЫЙ модификатор стата). Новый статус с постоянным визуалом = новая пара
    /// «Key → (префаб, точка атача)» в KeyToVfx ниже, без нового класса-системы.
    ///
    /// ПОВТОРНАЯ ПОПЫТКА (_waitingForView): ПЕЧАТНОЕ свойство (CardCreatureModel.Properties) применяется в
    /// OnInit — В МОМЕНТ СОЗДАНИЯ СУЩНОСТИ, задолго до выхода на стол (карта может лежать в руке/колоде).
    /// ViewRefComponent на сущности УЖЕ есть (добавлен тем же OnInit), но его .View (сам GameObject) —
    /// null до SpawnCreatureViewSystem. Если применить событие СРАЗУ и .View оказался null — не теряем
    /// его: копим в очереди ожидания и пробуем каждый кадр, пока вью не появится (или сущность не пропадёт
    /// из ViewRefComponent — тогда ждать нечего). Без этого печатный Щит/Защитник никогда не получал бы
    /// ауру — только та, что навешена УЖЕ НА СТОЛЕ (баг 2026-08-11: «Block отображается, Aura — нет»).
    ///
    /// CATCH-UP В Init() (Catchup): ВСЯ стартовая колода создаётся СИНХРОННО в Init() другой системы
    /// (InitDeckSystem) — Properties.Apply() и публикация CreaturePropertyAuraChangedEvent случаются ТАМ ЖЕ,
    /// на старте матча. Если InitDeckSystem зарегистрирована РАНЬШЕ этой системы в списке (см.
    /// EcsRunHandler/TutorialEcsHandler), Subscribe() ещё не случился к моменту публикации — событие уходит
    /// в пустоту НАВСЕГДА (баг 2026-08-11 v2: «PropAura вообще нет», ни разу за весь матч). Ретрай-очередь
    /// эту дыру не закрывает — она спасает только «событие пришло, но вью ещё нет», а не «событие пришло,
    /// когда никто не слушал». Чиню тем же способом, каким лечат подобные гонки автозагрузки: РАЗОВЫЙ скан
    /// уже существующих ShieldComponent/TauntTag/PoisonComponent сразу после Subscribe(), не завязанный на
    /// порядок регистрации систем (self-healing, а не «переставь Init пораньше и надейся»).
    /// </summary>
    public sealed class PropertyAuraVisualSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, IDisposable
    {
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsCustomInject<DefaultAbilityVfxConfig> _defaultVfx = default;

        readonly Queue<CreaturePropertyAuraChangedEvent> _incoming = new Queue<CreaturePropertyAuraChangedEvent>();
        List<CreaturePropertyAuraChangedEvent> _waitingForView = new List<CreaturePropertyAuraChangedEvent>();
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            Subscribe();
            Catchup(systems.GetWorld());
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        // Разово досканировать уже существующие компоненты свойств — на случай, если их Apply() (и публикация
        // события) случился ДО этого Init (см. класс-докстринг). Ключи и компоненты — те же, что в ResolvePrefab.
        void Catchup(EcsWorld world)
        {
            foreach (var e in world.Filter<ShieldComponent>().End())
                _waitingForView.Add(new CreaturePropertyAuraChangedEvent { CreatureEntity = e, Key = "Shielded", Active = true });
            foreach (var e in world.Filter<TauntTag>().End())
                _waitingForView.Add(new CreaturePropertyAuraChangedEvent { CreatureEntity = e, Key = "Taunt", Active = true });
            foreach (var e in world.Filter<PoisonComponent>().End())
                _waitingForView.Add(new CreaturePropertyAuraChangedEvent { CreatureEntity = e, Key = "Poisoned", Active = true });
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CreaturePropertyAuraChangedEvent>(OnChanged);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CreaturePropertyAuraChangedEvent>(OnChanged);
        }

        void OnChanged(CreaturePropertyAuraChangedEvent e)
        {
            // ВРЕМЕННО (баг 2026-08-11: «нет ни щита, ни таунта»): видим, что событие вообще дошло досюда.
            UnityEngine.Debug.Log($"[PropAura] event entity={e.CreatureEntity} key={e.Key} active={e.Active}");
            _incoming.Enqueue(e);
        }

        public void Run(IEcsSystems systems)
        {
            while (_incoming.Count > 0) TryApply(_incoming.Dequeue(), _waitingForView);

            if (_waitingForView.Count == 0) return;

            var stillWaiting = new List<CreaturePropertyAuraChangedEvent>();
            foreach (var e in _waitingForView) TryApply(e, stillWaiting);
            _waitingForView = stillWaiting;
        }

        // true — применено (или применять уже нечего, вью с CreatureView не найти НИКОГДА). false — вью
        // ещё не заспавнен, событие ушло в waitList (retryList текущего кадра ИЛИ следующего) для повтора.
        bool TryApply(CreaturePropertyAuraChangedEvent e, List<CreaturePropertyAuraChangedEvent> waitList)
        {
            if (!_viewPool.Value.Has(e.CreatureEntity))
            {
                // Компонента нет вовсе (сущность удалена целиком) — вью не появится НИКОГДА, ждать нечего
                // (см. докстринг класса — раньше код это утверждал, но на деле копил в waitList навечно).
                UnityEngine.Debug.Log($"[PropAura] entity={e.CreatureEntity} key={e.Key}: НЕТ ViewRefComponent — сдаюсь");
                return true;
            }
            var go = _viewPool.Value.Get(e.CreatureEntity).View;
            if (go == null)
            {
                // Active=false (снять визуал ауры, обычно ClearStatModifiers при смерти) — снимать уже
                // не с чего, вью уже destroy()-нулась вместе с существом: копить в очереди НАВЕЧНО (баг
                // 2026-08-23 — спам «View==null — жду» до конца матча на каждого умершего Щит/Защитника)
                // смысла нет. Active=true — легитимный кейс «печатное свойство раньше спавна вью», ждём.
                if (!e.Active)
                {
                    UnityEngine.Debug.Log($"[PropAura] entity={e.CreatureEntity} key={e.Key}: View==null, Active=false — снимать нечего, сдаюсь");
                    return true;
                }
                UnityEngine.Debug.Log($"[PropAura] entity={e.CreatureEntity} key={e.Key}: View==null — жду");
                waitList.Add(e);
                return false;
            }

            var view = go.GetComponent<CreatureView>();
            if (view == null)
            {
                UnityEngine.Debug.Log($"[PropAura] entity={e.CreatureEntity} key={e.Key}: View есть, но БЕЗ CreatureView-компонента — сдаюсь");
                return true;   // вью есть, но без CreatureView — переждать нечего, это не наш случай
            }

            var (prefab, point) = ResolvePrefab(e.Key);
            UnityEngine.Debug.Log($"[PropAura] entity={e.CreatureEntity} key={e.Key} active={e.Active}: применяю, prefab={(prefab != null ? prefab.name : "NULL")}, point={point}");
            view.SetStatusVfx(e.Key, prefab, e.Active, point);
            return true;
        }

        // Key → (дефолтный префаб, точка атача). Единственное место, которое трогать при добавлении
        // НОВОГО статуса с постоянным визуалом.
        (UnityEngine.GameObject, CreatureAttachPoint) ResolvePrefab(string key)
        {
            var cfg = _defaultVfx.Value;
            if (cfg == null) return (null, CreatureAttachPoint.Body);

            switch (key)
            {
                case "Shielded": return (cfg.ShieldedAuraVfxPrefab, CreatureAttachPoint.Body);
                case "Taunt":    return (cfg.TauntAuraVfxPrefab,    CreatureAttachPoint.Body);
                case "Poisoned": return (cfg.PoisonedStatusVfxPrefab, CreatureAttachPoint.Head);
                default:         return (null, CreatureAttachPoint.Body);
            }
        }
    }
}
