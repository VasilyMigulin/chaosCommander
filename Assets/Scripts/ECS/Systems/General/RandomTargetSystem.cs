using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает карты-заклинания с RequiresRandomTargetTag.
    ///
    /// Алгоритм (только для локальных карт — OwnCardTag):
    ///   1. Читает TargetRequirementComponent чтобы понять тип случайной цели.
    ///   2. Собирает список валидных существ на доске.
    ///   3. Выбирает случайное существо.
    ///   4. Заполняет CastEvent.TargetEntity.
    ///   5. Снимает RequiresRandomTargetTag — CastCardSystem продолжит обработку.
    ///
    /// Если подходящих целей нет — карта остаётся заблокированной (каст не произойдёт).
    ///
    /// Детерминизм: выбор делает только владелец карты (OwnCardTag).
    /// Результат передаётся по сети через уже существующий NetworkCardCastSystem
    /// (он отправляет TargetEntity ключом). RemoteCastSystem на другом клиенте
    /// воспроизводит тот же CastEvent с той же целью.
    /// </summary>
    public sealed class RandomTargetSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<
            Inc<CastEvent, RequiresRandomTargetTag, OwnCardTag, TargetRequirementComponent>>
            _filter = default;

        readonly EcsPoolInject<CastEvent>                   _castPool      = default;
        readonly EcsPoolInject<RequiresRandomTargetTag>     _randTagPool   = default;
        readonly EcsPoolInject<TargetRequirementComponent>  _reqPool       = default;
        readonly EcsPoolInject<OwnerComponent>              _ownerPool     = default;
        readonly EcsPoolInject<BoardPositionComponent>      _posPool       = default;

        readonly EcsFilterInject<
            Inc<CreatureTag, BoardTag, BoardPositionComponent, OwnerComponent>,
            Exc<DeadTag>>
            _creaturesFilter = default;

        // Переиспользуемый список кандидатов (нет аллокаций каждый фрейм)
        readonly List<int> _candidates = new List<int>(16);

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var castEvent = ref _castPool.Value.Get(cardEntity);
                var mask = _reqPool.Value.Get(cardEntity).RequiredTarget;

                int ownerPlayerId = -1;
                if (_ownerPool.Value.Has(cardEntity))
                    ownerPlayerId = _ownerPool.Value.Get(cardEntity).OwnerId;

                _candidates.Clear();
                CollectCandidates(mask, ownerPlayerId);

                if (_candidates.Count == 0)
                    continue; // нет валидных целей — ждём (или карта не сыграется)

                int chosen = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
                castEvent.TargetEntity = chosen;

                _randTagPool.Value.Del(cardEntity);
            }
        }

        private void CollectCandidates(TargetMask mask, int ownerPlayerId)
        {
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var owner = ref _ownerPool.Value.Get(ce);
                bool isAlly = owner.OwnerId == ownerPlayerId;

                if (mask.MatchesCreature(isAlly))
                    _candidates.Add(ce);
            }
        }
    }
}
