using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Талон на ЕДИНСТВЕННОЕ окно выбора карт (PickupWindow). Окно — общий ресурс: его хотят несколько
    /// независимых каналов (раскопка, замена добора «Адовый червь», выбор цели из не-борд зон, пик перед
    /// кастом). Раньше арбитража не было — кто последний опубликовал CardPickOfferedEvent в кадре, тот и
    /// занимал окно, а перебитый запрос оставался жить без UI (Развилка + Адовый червь: окно мигало и
    /// подменялось, выбор первой карты терялся).
    ///
    /// Правило: продюсер НЕ публикует оффер, пока брокер (CardPickBrokerSystem) не выдал ему слот
    /// (Granted). Талон вешается на сущность-держатель самого продюсера (запрос раскопки / сущность
    /// игрока / сущность способности), поэтому его ВРЕМЯ ЖИЗНИ автоматически равно времени жизни запроса:
    /// умер продюсер — исчез талон — слот освободился. Это исключает вечную блокировку окна.
    ///
    /// СИНК: талон — чисто ПРЕЗЕНТАЦИОННЫЙ. Окно показывается только владельцу карты, ветка реплея
    /// (чужой выбор из CardPickReplayStore) талонов не берёт вовсе. Задержка оффера на кадр-другой меняет
    /// лишь порядок локального UI; результат выбора уезжает оппоненту прежними каналами
    /// (CardPickResolvedNetEvent / DrawReplacementResolvedNetEvent). Десинк создать не может.
    /// </summary>
    public struct PickTicketComponent
    {
        /// <summary>Токен корреляции: им (а НЕ «сырым» entity) продюсер узнаёт свой выбор в CardPickChosenEvent.</summary>
        public int RequestId;

        /// <summary>Владелец пика — его ход, его окно. Брокер по нему гасит талоны на конце хода.</summary>
        public int PlayerEntity;

        /// <summary>Брокер выдал слот: продюсеру можно публиковать CardPickOfferedEvent.</summary>
        public bool Granted;

        /// <summary>Ход владельца закончился, продюсер обязан свернуть пик. Не свернул за кадр — брокер
        /// снимет талон сам (страховка от навсегда занятого окна) и предупредит в лог.</summary>
        public bool Expired;
    }

    /// <summary>
    /// Источник монотонных RequestId для окна выбора. Ноль — «нет запроса» (sentinel), поэтому счётчик
    /// начинается с единицы. Сбрасывается на матч в CardPickBrokerSystem.Init.
    /// Порядок выдачи слотов брокером = порядок RequestId, то есть порядок появления запросов.
    /// </summary>
    public static class PickRequestId
    {
        static int _next;

        public static int Next()  => ++_next;
        public static void Reset() => _next = 0;
    }

    /// <summary>
    /// Общий протокол работы продюсера пика со слотом окна. Вынесен из систем, чтобы четыре канала
    /// занимали окно ОДИНАКОВО, а не каждый по-своему.
    /// </summary>
    public static class PickTicket
    {
        /// <summary>
        /// Заявить/подтвердить право на окно. Зовётся продюсером КАЖДЫЙ кадр, когда он готов показать пик
        /// (все его собственные гейты уже пройдены). Первый вызов ставит талон и возвращает false —
        /// слот выдаст брокер (кадром позже, он идёт раньше продюсеров в пайплайне). true — окно наше,
        /// можно публиковать CardPickOfferedEvent с этим RequestId.
        /// </summary>
        public static bool Ready(EcsWorld world, int holder, ref int requestId, int playerEntity)
        {
            var pool = world.GetPool<PickTicketComponent>();

            if (!pool.Has(holder))
            {
                if (requestId == 0) requestId = PickRequestId.Next();
                ref var fresh = ref pool.Add(holder);
                fresh.RequestId    = requestId;
                fresh.PlayerEntity = playerEntity;
                return false;
            }

            return pool.Get(holder).Granted;
        }

        /// <summary>Пик завершён (выбор/отмена/истечение) — освободить окно. Идемпотентно.</summary>
        public static void Release(EcsWorld world, int holder)
        {
            var pool = world.GetPool<PickTicketComponent>();
            if (pool.Has(holder)) pool.Del(holder);
        }
    }
}
