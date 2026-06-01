using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет SummonFromZoneEffectComponent: находит до Count карт в указанной зоне
    /// (колода/рука/кладбище своя или оппонента) по предикату и переносит их на доску
    /// владельца источника, чередуя L/R от позиции источника (как ApplySummonSystem).
    /// </summary>
    public sealed class ApplySummonFromZoneSystem : IEcsRunSystem
    {
        const int Cols = 5;
        const int CenterCol = 2;

        static readonly int[] SourceOffsets = { -1, +1, -2, +2, -3, +3, -4, +4 };
        static readonly int[] AvatarOffsets = { 0, -1, +1, -2, +2, -3, +3, -4, +4 };

        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<HitComponent, SummonFromZoneEffectComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<SummonFromZoneEffectComponent> _summonPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;

        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool = default;
        readonly EcsPoolInject<CreatureTag> _creaturePool = default;
        readonly EcsPoolInject<SpellTag> _spellPool = default;

        readonly EcsPoolInject<RedTag>    _red    = default;
        readonly EcsPoolInject<BlueTag>   _blue   = default;
        readonly EcsPoolInject<GreenTag>  _green  = default;
        readonly EcsPoolInject<YellowTag> _yellow = default;
        readonly EcsPoolInject<WhiteTag>  _white  = default;
        readonly EcsPoolInject<BlackTag>  _black  = default;

        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsFilterInject<Inc<BoardTag, BoardPositionComponent>> _boardFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _summonPool.Value.Get(effectEntity);
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                int sourceCard = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity : -1;

                if (sourceCard < 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                int ownerPlayerId = _ownerPool.Value.Has(sourceCard)
                    ? _ownerPool.Value.Get(sourceCard).OwnerId : -1;
                int searchOwnerId = IsOpponentZone(data.Source) ? OpponentOf(ownerPlayerId) : ownerPlayerId;

                var candidates = CollectCandidates(data, searchOwnerId, sourceCard);
                if (candidates.Count == 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                int desired = System.Math.Max(1, data.Count);
                var picked = Pick(candidates, data.PickMode, desired, abilityEntity);

                bool sourceOnBoard = _boardPosPool.Value.Has(sourceCard);
                int row, sideOwnerId, anchorCol;
                int[] offsets;
                if (sourceOnBoard)
                {
                    ref var spos = ref _boardPosPool.Value.Get(sourceCard);
                    row = spos.Row; sideOwnerId = spos.OwnerId; anchorCol = spos.Col; offsets = SourceOffsets;
                }
                else
                {
                    row = 0; sideOwnerId = ownerPlayerId; anchorCol = CenterCol; offsets = AvatarOffsets;
                }

                var freeCols = CollectFreeColumns(row, sideOwnerId, anchorCol, offsets);

                for (int i = 0; i < picked.Count && i < freeCols.Count; i++)
                    PlaceOnBoard(picked[i], row, freeCols[i], sideOwnerId);

                _world.Value.DelEntity(effectEntity);
            }
        }

        // ── Сборка кандидатов ────────────────────────────────────────────────
        List<int> CollectCandidates(in SummonFromZoneEffectComponent data, int searchPlayerId, int sourceCard)
        {
            var result = new List<int>();
            int playerEntity = FindPlayerEntity(searchPlayerId);
            if (playerEntity < 0) return result;

            switch (data.Source)
            {
                case SummonFromZoneSource.OwnDeck:
                case SummonFromZoneSource.OpponentDeck:
                    if (_deckPool.Value.Has(playerEntity))
                    {
                        ref var deck = ref _deckPool.Value.Get(playerEntity);
                        if (deck.CardEntities != null)
                            foreach (var ce in deck.CardEntities)
                                if (Matches(ce, data, sourceCard)) result.Add(ce);
                    }
                    break;

                case SummonFromZoneSource.OwnHand:
                case SummonFromZoneSource.OpponentHand:
                    if (_handPool.Value.Has(playerEntity))
                    {
                        ref var hand = ref _handPool.Value.Get(playerEntity);
                        if (hand.CardEntities != null)
                            foreach (var ce in hand.CardEntities)
                                if (Matches(ce, data, sourceCard)) result.Add(ce);
                    }
                    break;

                case SummonFromZoneSource.OwnGrave:
                case SummonFromZoneSource.OpponentGrave:
                    foreach (var ce in _world.Value.Filter<GraveTag>().Inc<OwnerComponent>().End())
                    {
                        if (_ownerPool.Value.Get(ce).OwnerId != searchPlayerId) continue;
                        if (Matches(ce, data, sourceCard)) result.Add(ce);
                    }
                    break;
            }
            return result;
        }

        bool Matches(int cardEntity, in SummonFromZoneEffectComponent data, int sourceCard)
        {
            if (data.ExcludeSelf && cardEntity == sourceCard) return false;

            int cost = CostOf(cardEntity);
            if (data.CostMax >= 0 && cost > data.CostMax) return false;
            if (cost < data.CostMin) return false;

            if (data.CreatureOnly && !_creaturePool.Value.Has(cardEntity)) return false;
            if (data.SpellOnly && !_spellPool.Value.Has(cardEntity)) return false;

            if (data.ExactModelId >= 0)
            {
                if (!_modelPool.Value.Has(cardEntity)) return false;
                if (_modelPool.Value.Get(cardEntity).ModelId != data.ExactModelId) return false;
            }

            if (data.RequiredColors != 0 || data.ForbiddenColors != 0)
            {
                var colors = ColorsOf(cardEntity);
                if (data.ForbiddenColors != 0 && (colors & data.ForbiddenColors) != 0) return false;
                if (data.RequiredColors != 0 && (colors & data.RequiredColors) == 0) return false;
            }

            return true;
        }

        int CostOf(int e)
        {
            if (_manaCostPool.Value.Has(e)) return _manaCostPool.Value.Get(e).Cost;
            if (_goldCostPool.Value.Has(e)) return _goldCostPool.Value.Get(e).Cost;
            return 0;
        }

        EnumService.Element ColorsOf(int e)
        {
            EnumService.Element c = 0;
            if (_red   .Value.Has(e)) c |= EnumService.Element.Red;
            if (_blue  .Value.Has(e)) c |= EnumService.Element.Blue;
            if (_green .Value.Has(e)) c |= EnumService.Element.Green;
            if (_yellow.Value.Has(e)) c |= EnumService.Element.Yellow;
            if (_white .Value.Has(e)) c |= EnumService.Element.White;
            if (_black .Value.Has(e)) c |= EnumService.Element.Black;
            return c;
        }

        // ── PickMode ────────────────────────────────────────────────────────
        List<int> Pick(List<int> candidates, SummonFromZonePickMode mode, int count, int abilityEntity)
        {
            var res = new List<int>();
            switch (mode)
            {
                case SummonFromZonePickMode.First:
                    for (int i = 0; i < count && i < candidates.Count; i++) res.Add(candidates[i]);
                    break;
                case SummonFromZonePickMode.Random:
                {
                    var rng = new System.Random(abilityEntity);
                    int take = System.Math.Min(count, candidates.Count);
                    for (int i = 0; i < take; i++)
                    {
                        int j = i + rng.Next(0, candidates.Count - i);
                        int tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
                        res.Add(candidates[i]);
                    }
                    break;
                }
                case SummonFromZonePickMode.MaxCost:
                    candidates.Sort((a, b) => CostOf(b).CompareTo(CostOf(a)));
                    for (int i = 0; i < count && i < candidates.Count; i++) res.Add(candidates[i]);
                    break;
                case SummonFromZonePickMode.MinCost:
                    candidates.Sort((a, b) => CostOf(a).CompareTo(CostOf(b)));
                    for (int i = 0; i < count && i < candidates.Count; i++) res.Add(candidates[i]);
                    break;
            }
            return res;
        }

        // ── Перенос карты на доску ──────────────────────────────────────────
        void PlaceOnBoard(int cardEntity, int row, int col, int sideOwnerId)
        {
            // Снимаем из текущей зоны
            int ownerId = _ownerPool.Value.Has(cardEntity) ? _ownerPool.Value.Get(cardEntity).OwnerId : -1;
            int playerEntity = FindPlayerEntity(ownerId);
            if (playerEntity >= 0)
            {
                if (_handPool.Value.Has(playerEntity))
                {
                    ref var hand = ref _handPool.Value.Get(playerEntity);
                    if (hand.CardEntities != null && hand.CardEntities.Remove(cardEntity))
                        hand.Count = hand.CardEntities.Count;
                }
                if (_deckPool.Value.Has(playerEntity))
                {
                    ref var deck = ref _deckPool.Value.Get(playerEntity);
                    if (deck.CardEntities != null && deck.CardEntities.Remove(cardEntity))
                        deck.Count = deck.CardEntities.Count;
                }
            }

            if (_handTagPool.Value.Has(cardEntity))  _handTagPool.Value.Del(cardEntity);
            if (_deckTagPool.Value.Has(cardEntity))  _deckTagPool.Value.Del(cardEntity);
            if (_graveTagPool.Value.Has(cardEntity)) _graveTagPool.Value.Del(cardEntity);

            if (!_boardTagPool.Value.Has(cardEntity)) _boardTagPool.Value.Add(cardEntity);

            if (_boardPosPool.Value.Has(cardEntity))
            {
                ref var bp = ref _boardPosPool.Value.Get(cardEntity);
                bp.Row = row; bp.Col = col; bp.OwnerId = sideOwnerId;
            }
            else
            {
                ref var bp = ref _boardPosPool.Value.Add(cardEntity);
                bp.Row = row; bp.Col = col; bp.OwnerId = sideOwnerId;
            }
            // SpawnCreatureViewSystem подхватит BoardTag + BoardPosition и создаст вид.
        }

        // ── Помощники ───────────────────────────────────────────────────────
        List<int> CollectFreeColumns(int row, int sideOwnerId, int anchorCol, int[] offsets)
        {
            var occupied = new HashSet<int>();
            foreach (var ce in _boardFilter.Value)
            {
                ref var pos = ref _boardPosPool.Value.Get(ce);
                if (pos.Row != row || pos.OwnerId != sideOwnerId) continue;
                occupied.Add(pos.Col);
            }
            var result = new List<int>();
            for (int i = 0; i < offsets.Length; i++)
            {
                int col = anchorCol + offsets[i];
                if (col < 0 || col >= Cols) continue;
                if (occupied.Contains(col)) continue;
                occupied.Add(col);
                result.Add(col);
            }
            return result;
        }

        int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }

        int OpponentOf(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
            {
                int pid = _playerPool.Value.Get(pe).PlayerId;
                if (pid != playerId) return pid;
            }
            return -1;
        }

        bool IsOpponentZone(SummonFromZoneSource src)
        {
            return src == SummonFromZoneSource.OpponentDeck
                || src == SummonFromZoneSource.OpponentHand
                || src == SummonFromZoneSource.OpponentGrave;
        }
    }
}
