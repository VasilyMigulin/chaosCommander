using UnityEngine.UI;

namespace AwesomeUI.Service
{
    public class InvisibleGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); // ничего не рисуем
        }
    }
}