using System.Collections.Generic;

namespace Game.Core.Events
{
    // === helper (static) ===
    /// <summary>
    /// ЗАМОК ПРЕЗЕНТАЦИИ: «идёт анимация, пайплайн ждёт». Тот же смысл, что у AbilityAnimPendingComponent
    /// (анимация каста) и AbilityCastPendingComponent (полёт снаряда), но владелец — UI, у которого нет
    /// доступа к ECS-пулам. Учитывается в PipelineGate.IsSettled, поэтому очередь авто-кастов и резолв
    /// ждут анимацию руки так же, как ждут удар и смерть.
    ///
    /// ГЛАВНОЕ ПРАВИЛО — ЗАМОК БЕРЁТ ТОТ, КТО БУДЕТ ЕГО ОТПУСКАТЬ. Раньше ожидание анимации руки заводил
    /// ГЕЙМПЛЕЙ (AutoCastSystem ждал «карта показана в руке»), и когда UI не мог показать карту — слотов
    /// не было — сигнал не приходил НИКОГДА, спасал только сторожевой таймаут 4с (лог 2026-08-02: каждая
    /// карта каскада «кастую принудительно»). Теперь ждать можно только того, кто сам обещал ответить:
    /// нет UI (пассив в MP, ход ИИ, headless) — никто не взял замок, никто и не ждёт. Поэтому сторожевые
    /// таймеры тут не нужны не «по храбрости», а по построению.
    ///
    /// ВРЕМЯ ЖИЗНИ привязано к владельцу: MonoBehaviour обязан звать ReleaseAll(this) в OnDisable/OnDestroy.
    /// Вьюху снесли посреди анимации — её замки уходят вместе с ней, зависнуть нечему.
    ///
    /// НЕ ВЛИЯЕТ НА ЛОГИКУ: замок только ЗАДЕРЖИВАЕТ следующий шаг, но ничего не решает. Поэтому у пассива
    /// (реплей) и в PvE он безопасен — исход не зависит от того, брал его кто-то или нет.
    /// </summary>
    public static class PresentationLock
    {
        // === class === выданный замок. Ссылочный тип: сам объект и есть идентификатор.
        public sealed class Handle
        {
            public object Owner;
            public string Reason;
        }

        static readonly HashSet<Handle> _held = new HashSet<Handle>();

        /// <summary>Хоть один замок держится → пайплайн НЕ осел.</summary>
        public static bool IsBusy => _held.Count > 0;

        public static int Count => _held.Count;

        /// <summary>Взять замок. Держи хендл — только им можно отпустить.
        /// reason нужен для диагностики: если пайплайн встал, видно, чья анимация не завершилась.</summary>
        public static Handle Acquire(object owner, string reason)
        {
            var h = new Handle { Owner = owner, Reason = reason };
            _held.Add(h);
            return h;
        }

        /// <summary>Отпустить конкретный замок. Повторный вызов безвреден (колбэк анимации может
        /// прилететь и после того, как владелец уже всё отпустил).</summary>
        public static void Release(Handle handle)
        {
            if (handle != null) _held.Remove(handle);
        }

        /// <summary>Отпустить ВСЕ замки владельца. Обязательно звать в OnDisable/OnDestroy вьюхи —
        /// это то, что заменяет сторожевой таймер.</summary>
        public static void ReleaseAll(object owner)
        {
            if (owner == null) return;
            _held.RemoveWhere(h => ReferenceEquals(h.Owner, owner));
        }

        /// <summary>Матч кончился / мир пересоздан. Статик переживает сцену, поэтому чистим явно —
        /// иначе замок из прошлого боя запер бы новый (та же защита, что у MatchState/CastMultiplierService).</summary>
        public static void Clear() => _held.Clear();

        /// <summary>Кто держит — для лога при подозрении на зависший пайплайн.</summary>
        public static string Describe()
        {
            if (_held.Count == 0) return "<свободно>";
            var sb = new System.Text.StringBuilder();
            foreach (var h in _held)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(h.Reason ?? "?");
            }
            return sb.ToString();
        }
    }
}
