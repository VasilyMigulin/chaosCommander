using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// «+X/+Y за каждую чару под вашим контролем» (Обжора, BuffPerCharmComponent): каждый кадр диффит
    /// желаемый бонус (Per × число чар владельца на поле) с уже навешанным (Applied*) и правит стек
    /// модификаторов статов (AddModifier/RemoveModifier — живой аура-паттерн; изменение видно в тот же
    /// кадр: появление/истечение чары само подтянет статы). Кламп Current при снижении Max делает
    /// HealthComponent.RecalculateValue. Существо вне борда (умерло/забаунсено) — Applied сбрасывается
    /// (мягкие модификаторы чистит DieSystem; RemoveModifier тогда просто no-op).
    /// СИНК ДАРОМ: чары на поле зеркальны у обоих клиентов → бонус одинаков без спец-канала.
    /// Регистрация: _generalSystems, после CharmDieSystem (истёкшие чары выходят из счёта в тот же кадр).
    /// </summary>
    public sealed class BuffPerCharmSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<BuffPerCharmComponent, CreatureTag>> _buffed = default;
        readonly EcsFilterInject<Inc<CharmTag, BoardTag, OwnerComponent>, Exc<DeadTag>> _charms = default;

        readonly EcsPoolInject<BuffPerCharmComponent> _perCharmPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool  = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool     = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<DeadTag>  _deadPool  = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var e in _buffed.Value)
            {
                ref var per = ref _perCharmPool.Value.Get(e);

                // Вне борда/мёртв: мягкие модификаторы уже снял DieSystem (или снимем no-op'ом) — сброс диффа.
                bool onBoard = _boardPool.Value.Has(e) && !_deadPool.Value.Has(e);
                int count = 0;
                if (onBoard && _ownerPool.Value.Has(e))
                {
                    int ownerId = _ownerPool.Value.Get(e).OwnerId;
                    foreach (var ch in _charms.Value)
                        if (_ownerPool.Value.Get(ch).OwnerId == ownerId) count++;
                }

                int wantAtk = onBoard ? per.AttackPerCharm * count : 0;
                int wantHp  = onBoard ? per.HealthPerCharm * count : 0;

                if (wantAtk != per.AppliedAttack && _attackPool.Value.Has(e))
                {
                    ref var atk = ref _attackPool.Value.Get(e);
                    if (per.AppliedAttack != 0) atk.RemoveModifier(per.AppliedAttack);
                    if (wantAtk != 0) atk.AddModifier(wantAtk);
                    per.AppliedAttack = wantAtk;
                }

                if (wantHp != per.AppliedHealth && _hpPool.Value.Has(e))
                {
                    ref var hp = ref _hpPool.Value.Get(e);
                    if (per.AppliedHealth != 0) hp.RemoveModifier(per.AppliedHealth);
                    if (wantHp != 0) hp.AddModifier(wantHp);
                    per.AppliedHealth = wantHp;
                }
            }
        }
    }
}
