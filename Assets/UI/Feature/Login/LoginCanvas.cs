using AwesomeUI.Core.Canvas;

namespace AwesomeUI.Feature.Login
{
    /// <summary>
    /// Канвас входа. РОУТИНГ СТАРТОВОЙ ПАНЕЛИ ЗДЕСЬ НЕ ДЕЛАЕМ — им управляет InitState (единый
    /// контроллер): Start → TitlePanel (заставка), затем OnContinue → язык / туториал / логин.
    /// Раньше InvokeCanvas сам открывал LanguageSelectPanel/LoginPanel и перебивал заставку.
    /// </summary>
    public class LoginCanvas : SourceCanvas
    {
    }
}
