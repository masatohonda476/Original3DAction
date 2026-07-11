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
    public LayerMask occlusionMask = ~0;
    public float headHeight = 1.7f;
    public float chestHeight = 1.0f;

    private float x = 0.0f;
    private float y = 0.0f;
    private LockOnSystem lockOnSystem;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x  = angles.x;
        y  = angles.y;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        lockOnSystem = target.GetComponent<LockOnSystem>();
    }

    void LateUpdate()
    {
        if (lockOnSystem.Target != null)
        {
            Debug.Log(lockOnSystem.Target.name);
        }

        if (target == null)
        {
            return;
        }

        Vector2 lookInput = GameInput.Instance.Look;

        if (lookInput != Vector2.zero)
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

            direction.y = 0f; // Y軸の回転を無視

            rotation = Quaternion.LookRotation(direction);
            }
            else
            {
                rotation = Quaternion.Euler(y, x, 0);
                }

        Vector3 focusPosition = target.position;
        Vector3 cameraDirection = rotation * Vector3.back;
        Vector3 position = focusPosition + cameraDirection * distance;
        Vector3 headPosition = focusPosition + Vector3.up * headHeight;
        Vector3 chestPosition = focusPosition + Vector3.up * chestHeight;

        bool headBlocked = Physics.Linecast(
            position,
            headPosition,
            occlusionMask,
            QueryTriggerInteraction.Ignore);

        bool chestBlocked = Physics.Linecast(
            position,
            chestPosition,
            occlusionMask,
            QueryTriggerInteraction.Ignore);

        if (Physics.SphereCast(
            focusPosition,
            collisionRadius,
            cameraDirection,
            out RaycastHit hit,
            distance,
            occlusionMask,
            QueryTriggerInteraction.Ignore))
        {
            float adjustedDistance = Mathf.Clamp(
                hit.distance - collisionOffset,
                minDistance,
                distance);

                position = focusPosition + cameraDirection * adjustedDistance;

        }

        Vector3 adjustedPosition = position;

        for (int i = 0; i < 20; i++)
        {
            if (!Physics.CheckSphere(
                adjustedPosition,
                collisionRadius,
                collisionMask,
                QueryTriggerInteraction.Ignore))
            {
                break;
            }
            //プレイヤー側へ少し押し出す
            adjustedPosition -= cameraDirection * 0.05f;
        }
        position = adjustedPosition;

        transform.rotation = rotation;
        transform.position = position;

        Collider[] hits = Physics.OverlapSphere(
            adjustedPosition,
            collisionRadius,
            collisionMask,
            QueryTriggerInteraction.Ignore);
    }

    void OnDrawGizmos()
{
    Gizmos.color = Color.red;
    Gizmos.DrawWireSphere(transform.position, collisionRadius);
}
}
