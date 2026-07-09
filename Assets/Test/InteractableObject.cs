using System.Runtime.Versioning;
using UnityEngine;

public class InteractableObject : MonoBehaviour
{
    private Material _mat;
    private MeshRenderer _renderer;
    private Color _defaultColor;

    private bool isInteractable;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _mat = _renderer.material;
        _defaultColor = _mat.color;
    }

    public void InvokeHightlight()
    {
        if (isInteractable) return;

       _mat.color = Color.yellow;
    }

    public void InvokeInteractable()
    {
        isInteractable = !isInteractable;

        if(isInteractable) _mat.color = Color.red;
    }

    public void DisposeHighlight()
    {
        _mat.color = _defaultColor;

        isInteractable = false;
    }

    private void OnDestroy()
    {
        _mat.color = _defaultColor;
    }
}
