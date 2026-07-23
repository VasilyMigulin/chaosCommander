namespace Game.Core.Shared
{
    /// <summary>
    /// Мост Mono→UI без обратной сборочной зависимости: AvatarPlayerView (Game.Core.Mono) держит ссылку на
    /// UI-компонент через этот интерфейс, не зная о конкретном классе AuraStatusBarView (AwesomeUI.Feature.
    /// Battle, реализует интерфейс) — та же идея, что у IAbility/IBattleUIContext (Shared.Interface), только
    /// интерфейс лежит в Game.Core.Shared (не в .Interface): метод завязан на CardVisualData, а Interface→
    /// Shared — уже другое направление существующей зависимости (Shared уже ссылается на Interface).
    /// </summary>
    public interface IAuraStatusReceiver
    {
        /// <summary>turnsRemaining[i] соответствует visuals[i]; &lt;0 — чара постоянная (без таймера).
        /// stackCounts[i] — размер стака одноимённых чар (слот показывает ×N при N&gt;1).</summary>
        void SetAuras(CardVisualData[] visuals, int[] turnsRemaining, int[] stackCounts);
    }
}
