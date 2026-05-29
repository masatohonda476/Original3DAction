using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public Transform target;
    public float distance = 5.0f;
    public float xSpeed = 120.0f;
    public float ySpeed = 80.0f;
    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;
    private InputSystem_Actions inputActions;
    private float x = 0.0f;
    private float y = 0.0f;

    void Awake()
    {
        inputActions = new InputSystem_Actions();
    }

    void OnEnable()
    {
        inputActions.Player.Enable();
    }

    void OnDisable()
    {
        inputActions.Player.Disable();
    }

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x  = angles.x;
        y  = angles.y;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LateUpdate()
    {
        Vector2 lookInput = inputActions.Player.Look.ReadValue<Vector2>();

        if (lookInput != Vector2.zero && Mouse.current.leftButton.isPressed) // 右クリック中
        {
            x += lookInput.x * xSpeed * Time.deltaTime;
            y -= lookInput.y * ySpeed * Time.deltaTime;

            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;

        transform.rotation = rotation;
        transform.position = position;
    }
}
