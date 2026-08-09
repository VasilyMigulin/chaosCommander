using System.Collections.Generic;
using Leopotam.EcsLite;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// Канонизация + FNV-1a64 хэш ЗЕРКАЛИРУЕМОГО состояния мира. ЕДИНЫЙ для TurnChecksumSystem (детект
    /// десинка на границе хода) и WorldResyncSystem (контрольная сверка после применения снэпшота) — расширяя
    /// набор полей здесь, расширяешь оба механизма синхронно. В хэше: существа на доске (ключ|атака|HP|позиция|
    /// владелец, без DeadTag), HP аватаров, руки/колоды как МНОЖЕСТВА ключей (порядок колод легитимно дрейфует —
    /// реплей доборов идёт по явным ключам DrawnKeys). НЕ в хэше: золото/мана (пассив не зеркалит доход),
    /// скорость/лимит атак (восстанавливаются в разных точках), чары-таймеры. Сортировка Ordinal — порядок
    /// итерации ECS-фильтров не гарантирован между клиентами.
    /// </summary>
    public static class WorldStateHash
    {
        static readonly List<string> _scratch = new(64);

        /// <param name="capture">Не null → сюда копируются канонические строки (дамп при десинке для diff двух клиентов).</param>
        public static ulong Compute(EcsWorld world, List<string> capture = null)
        {
            _scratch.Clear();

            var netPool    = world.GetPool<NetworkEntityComponent>();
            var atkPool    = world.GetPool<AttackComponent>();
            var hpPool     = world.GetPool<HealthComponent>();
            var posPool    = world.GetPool<BoardPositionComponent>();
            var playerPool = world.GetPool<PlayerComponent>();
            var handPool   = world.GetPool<HandComponent>();
            var deckPool   = world.GetPool<DeckComponent>();

            // Существа на доске: ключ | атака | HP тек/макс | позиция | владелец.
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<BoardPositionComponent>().Exc<DeadTag>().End())
            {
                string key = netPool.Has(e) ? netPool.Get(e).NetworkEntityKey : $"local{e}";
                int atk = atkPool.Has(e) ? atkPool.Get(e).Value : 0;
                int cur = hpPool.Has(e) ? hpPool.Get(e).Current : 0;
                int max = hpPool.Has(e) ? hpPool.Get(e).Max : 0;
                ref var pos = ref posPool.Get(e);
                _scratch.Add($"B|{key}|{atk}|{cur}|{max}|{pos.Row}|{pos.Col}|{pos.OwnerId}");
            }

            // Игроки: HP аватара + множества ключей руки и колоды.
            foreach (var pe in world.Filter<PlayerComponent>().Inc<HealthComponent>().End())
            {
                int pid = playerPool.Get(pe).PlayerId;
                _scratch.Add($"P|{pid}|{hpPool.Get(pe).Current}");

                if (handPool.Has(pe))
                {
                    var hand = handPool.Get(pe).CardEntities;
                    if (hand != null)
                        foreach (var c in hand)
                            _scratch.Add($"H|{pid}|{(netPool.Has(c) ? netPool.Get(c).NetworkEntityKey : $"local{c}")}");
                }
                if (deckPool.Has(pe))
                {
                    var deck = deckPool.Get(pe).CardEntities;
                    if (deck != null)
                        foreach (var c in deck)
                            _scratch.Add($"D|{pid}|{(netPool.Has(c) ? netPool.Get(c).NetworkEntityKey : $"local{c}")}");
                }
            }

            _scratch.Sort(System.StringComparer.Ordinal);
            if (capture != null) { capture.Clear(); capture.AddRange(_scratch); }

            ulong hash = 14695981039346656037UL;
            foreach (var s in _scratch)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 1099511628211UL;
                }
                hash ^= '\n';
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
