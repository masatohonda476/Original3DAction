using UnityEngine;

public class LockOnCamera : MonoBehaviour
{
    [SerializeField] private Transform player;
    private LockOnSystem lockOnSystem;

    public float rotationSpeed = 360f; // 1秒間に360度回転する速度

    void Start()
    {
        lockOnSystem = player.GetComponent<LockOnSystem>();
    }

    void LateUpdate()
    {
        if (lockOnSystem.Target == null)
        {
            return;
        }
        Vector3 center = (player.position + lockOnSystem.Target.position) / 2f;
        Vector3 direction = center - transform.position;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );

        Debug.DrawLine(player.position, lockOnSystem.Target.position, Color.green);
        Debug.DrawRay(center, Vector3.up, Color.red);
    }
}
