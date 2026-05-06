namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// View обычной карты в руке.
    /// После розыгрыша скрывается и может быть переиспользована для следующего добора.
    /// </summary>
    public class SimpleCardView : PlayCardView
    {
        public override void OnClick()
        {
            if (!IsOccupied) return;
            OnActive();
        }
    }
}
