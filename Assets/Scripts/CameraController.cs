using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public Transform target;
    public float distance = 5.0f;
    public float xSpeed = 400.0f;
    public float ySpeed = 400.0f;
    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;
    public float collisionRadius = 0.25f;
    public float minDistance = 0.5f;
    public float collisionOffset = 0.1f;
    public LayerMask collisionMask = ~0;

    private float x = 0.0f;
    private float y = 0.0f;

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
        if (target == null)
        {
            return;
        }

        Vector2 lookInput = GameInput.Instance.Look;

        if (lookInput != Vector2.zero && Mouse.current.leftButton.isPressed) // 右クリック中
        {
            x += lookInput.x * xSpeed * Time.deltaTime;
            y -= lookInput.y * ySpeed * Time.deltaTime;

            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 focusPosition = target.position;
        Vector3 cameraDirection = rotation * Vector3.back;
        Vector3 position = focusPosition + cameraDirection * distance;

        if (Physics.SphereCast(
            focusPosition,
            collisionRadius,
            cameraDirection,
            out RaycastHit hit,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore))
        {
            float adjustedDistance = Mathf.Clamp(hit.distance - collisionOffset, minDistance, distance);
            position = focusPosition + cameraDirection * adjustedDistance;
        }

        transform.rotation = rotation;
        transform.position = position;
    }
}
