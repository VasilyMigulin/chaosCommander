using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет SummonEffectComponent: размещает копии указанной карты/токена
    /// чередуясь L/R от источника (или от центра аватара владельца, если источник
    /// не на доске). При отсутствии места обычные карты идут на кладбище, токены —
    /// исчезают.
    ///
    /// Реальное создание сущности — через CreateCardEvent: NetworkEntityKey
    /// вычисляется детерминированно (sourceKey:summon:abilityIndex:stepIndex:seq),
    /// поэтому активный и пассивный клиенты создают одну и ту же сущность с
    /// одинаковым ключом.
    /// </summary>
    public sealed class ApplySummonSystem : IEcsRunSystem
    {
        const int Cols = 5;
        const int CenterCol = 2;

        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<HitComponent, SummonEffectComponent, EffectAbilityRefComponent>> _filter = default;

        readonly EcsPoolInject<SummonEffectComponent> _summonPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<MatchCounterComponent> _counterPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        readonly EcsFilterInject<Inc<BoardTag, BoardPositionComponent>> _boardFilter = default;

        // Чередование: offset 0 = на самой клетке источника (используется только для
        // аватарного призыва), затем -1, +1, -2, +2, ...
        static readonly int[] AvatarOffsets = { 0, -1, +1, -2, +2, -3, +3, -4, +4 };
        static readonly int[] SourceOffsets = { -1, +1, -2, +2, -3, +3, -4, +4 };

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _summonPool.Value.Get(effectEntity);
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                int stepIndex = _refPool.Value.Get(effectEntity).StepIndex;
                int abilityIndex = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).AbilityIndex
                    : 0;
                int sourceCard = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity
                    : -1;

                if (sourceCard < 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                int ownerId = _ownerPool.Value.Has(sourceCard)
                    ? _ownerPool.Value.Get(sourceCard).OwnerId : -1;
                bool isOwn = _ownCardPool.Value.Has(sourceCard);

                bool sourceOnBoard = _boardPosPool.Value.Has(sourceCard);
                int row;
                int sideOwnerId;
                int[] offsets;
                int anchorCol;
                if (sourceOnBoard)
                {
                    ref var spos = ref _boardPosPool.Value.Get(sourceCard);
                    row = spos.Row;
                    sideOwnerId = spos.OwnerId;
                    anchorCol = spos.Col;
                    offsets = SourceOffsets;
                }
                else
                {
                    // Чары/заклинание: от центра ряда 0 на стороне владельца.
                    row = 0;
                    sideOwnerId = ownerId;
                    anchorCol = CenterCol;
                    offsets = AvatarOffsets;
                }

                int dynamicCount = data.CountFromCounterModelId >= 0
                    ? GetCounter(ownerId, data.CountFromCounterModelId)
                    : System.Math.Max(0, data.Count);
                int desiredCount = data.FillRow ? Cols : dynamicCount;
                int summoned = 0;
                string baseKey = _netKeyPool.Value.Has(sourceCard)
                    ? _netKeyPool.Value.Get(sourceCard).NetworkEntityKey
                    : ("local_" + sourceCard);

                var freeCols = CollectFreeColumns(row, sideOwnerId, anchorCol, offsets);

                int placed = 0;
                for (int i = 0; i < freeCols.Count && placed < desiredCount; i++, placed++)
                {
                    PublishCreate(data, baseKey, abilityIndex, stepIndex, placed,
                                  ownerId, isOwn, row, freeCols[i], sideOwnerId,
                                  inBoard: true, inGrave: false);
                    summoned++;
                }

                // Оставшиеся: обычные карты → grave; токены просто игнорируем.
                int overflow = desiredCount - placed;
                if (overflow > 0 && !data.IsToken)
                {
                    for (int i = 0; i < overflow; i++)
                    {
                        PublishCreate(data, baseKey, abilityIndex, stepIndex, placed + i,
                                      ownerId, isOwn, 0, 0, sideOwnerId,
                                      inBoard: false, inGrave: true);
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

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

        int GetCounter(int ownerPlayerId, int modelId)
        {
            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != ownerPlayerId) continue;
                if (!_counterPool.Value.Has(pe)) return 0;
                ref var c = ref _counterPool.Value.Get(pe);
                if (c.CountsByModelId == null) return 0;
                c.CountsByModelId.TryGetValue(modelId, out int n);
                return n;
            }
            return 0;
        }

        void PublishCreate(in SummonEffectComponent data, string baseKey, int abilityIndex, int stepIndex,
                           int seq, int ownerId, bool isOwn, int row, int col, int sideOwnerId,
                           bool inBoard, bool inGrave)
        {
            string netKey = $"{baseKey}:summon:{abilityIndex}:{stepIndex}:{seq}";
            GameEventBus.Publish(new CreateCardEvent
            {
                ExpansionId      = data.ExpansionId,
                CardId           = data.CardId,
                NetworkEntityKey = netKey,
                OwnerId          = ownerId,
                IsEnemy          = !isOwn,
                IsCommander      = false,
                InHand           = false,
                InBoard          = inBoard,
                BoardRow         = row,
                BoardCol         = col,
                BoardOwnerId     = sideOwnerId,
                InGrave          = inGrave,
            });
        }
    }
}
