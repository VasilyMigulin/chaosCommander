namespace Game.Core.Ecs.Components
{
    public struct InvokeEvent 
    {
        public bool NotCast;    // для исключения публикации CardCastEvent при резолве способностей, которые вызывают призыв существ (SummonEffect.InvokeSummon)
    }
}