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

    private float x = 0.0f;
    private float y = 0.0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x  = angles.x;
        y  = angles.y;
    }

    void LateUpdate()
    {
        Vector2 lookInput = GameInput.Instance.Look;

        if (lookInput != Vector2.zero && Mouse.current.leftButton.isPressed) // 右クリック中
        {
            x += lookInput.x * xSpeed * Time.deltaTime;
            y -= lookInput.y * ySpeed * Time.deltaTime;

            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }

        Quaternion rotation;

        if (lockOnSystem.Target != null)
        {
            Vector3 center = (target.position + lockOnSystem.Target.position) * 0.5f;
            Vector3 direction = center - transform.position;
            rotation = Quaternion.LookRotation(direction);
        }
        else
        {
            rotation = Quaternion.Euler(y, x, 0);
        }

        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;

        transform.rotation = rotation;
        transform.position = position;
    }
}
