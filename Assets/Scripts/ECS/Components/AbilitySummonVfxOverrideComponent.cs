namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>ОДНОРАЗОВЫЙ оверрайд появления от способности призыва (SpawnOnBoardEffect.AbilitySummonVfx) —
    /// ставит OverrideSummonVfxEffect (SummonModifiers) сразу после материализации сущности. НЕ путать с
    /// SummonVfxComponent (интринзик самого существа, живёт всю жизнь сущности): этот компонент читает и
    /// СРАЗУ УДАЛЯЕТ SpawnCreatureViewSystem при первом спауне вида — иначе он пережил бы баунс/воскрешение
    /// (те снимают только ViewSpawnedTag, не трогая VFX-компоненты) и подменял бы вид существу навсегда,
    /// даже когда оно возвращается на стол уже НЕ через эту способность (обычный розыгрыш из руки, подъём
    /// с кладбища). Одноразовость гарантирует: свой/дефолтный SummonVfx существа не портится безвозвратно.</summary>
    public struct AbilitySummonVfxOverrideComponent
    {
        public Game.Core.Shared.Interface.SummonVfxSpec Spec;
    }
}
