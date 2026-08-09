namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Держит сущность карты, которую владелец ЗАПОМНИЛ, чтобы разыграть ПОЗЖЕ (Королевский шут: раскопка
    /// на розыгрыше запоминает выбранную «шутку» на себе, а не кладёт её в руку — RememberCardForLaterPlayEffect
    /// снимает у неё HandTag ДО регистрации в списке руки, тем же путём, что и PlayTargetCardEffect у
    /// «Приглашения»; PlayRememberedCardEffect на хрипе её форс-разыгрывает и снимает этот компонент).
    /// </summary>
    public struct RememberedPlayTargetComponent
    {
        public int Entity;
    }
}
