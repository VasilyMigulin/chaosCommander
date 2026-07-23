using Game.Core.Backend;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Хедер-виджет баланса валют (Gold/Gems). Кладётся в любую мета-панель (магазин/рынок/аукцион/
    /// бустеры/задачи). Сам подписывается на PlayerWallet.OnChanged и обновляет числа.
    ///
    /// Префаб: _goldText (TMP), _gemsText (TMP) — оба опциональны.
    /// </summary>
    public class WalletView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _goldText;
        [SerializeField] private TextMeshProUGUI _gemsText;

        void OnEnable()
        {
            PlayerWallet.OnChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            PlayerWallet.OnChanged -= Refresh;
        }

        void Refresh()
        {
            if (_goldText != null) _goldText.text = PlayerWallet.Gold.ToString();
            if (_gemsText != null) _gemsText.text = PlayerWallet.Gems.ToString();
        }
    }
}
