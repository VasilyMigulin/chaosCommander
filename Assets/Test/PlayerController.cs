using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] KeyCode interactKey;
    [SerializeField] Transform firePoint;
    [SerializeField, Range(2f, 10f)] private float moveSpeed;
    [SerializeField, Range(2f, 10f)] private float rotationSpeed;
     
    InteractableObject _interactableObject;

    public const string INTERACTABLE_TAG = "Interactable";

    private void Update()
    {
        Move();

        InputInteractable();
    } 
    void InputInteractable()
    {
        Ray ray = new Ray(firePoint.position, firePoint.forward);

        Debug.DrawRay(firePoint.position, firePoint.forward, Color.red); 

        if (Physics.Raycast(ray, out var hitInfo, 10f))
        {
            if (hitInfo.transform.TryGetComponent<InteractableObject>(out var interactableObject) && interactableObject.tag == INTERACTABLE_TAG)
            {
                if(_interactableObject != interactableObject) _interactableObject?.DisposeHighlight();

                _interactableObject = interactableObject;

                _interactableObject.InvokeHightlight();
            }
        }
        else if(_interactableObject != null)
        {
            _interactableObject.DisposeHighlight();
            _interactableObject = null;
        }

        if (Input.GetKeyDown(interactKey))
        {
            _interactableObject?.InvokeInteractable();
        }
    } 
    void Move()
    {
        float inputHorizontal = Input.GetAxisRaw("Horizontal");
        float inputVertical = Input.GetAxisRaw("Vertical");

        Vector3 direction = new Vector3(inputHorizontal, 0f, inputVertical);

        if (direction.sqrMagnitude > 0.01f)
        {
            transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);

            Quaternion targetRotation = Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    } 
}
