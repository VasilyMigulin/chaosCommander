using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Range(1f, 5f)] private float mouseSesitivity;

    private float _yaw;
    private float _pitch;

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
    }

    private void LateUpdate()
    {
        transform.position = target.position;

        _yaw += Input.GetAxis("Mouse X") * mouseSesitivity * Time.deltaTime;
        _pitch += Input.GetAxis("Mouse Y") * mouseSesitivity * Time.deltaTime;

        _pitch = Mathf.Clamp(_pitch, -60f, 80f);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
