using UnityEngine;

public class LockOnCamera : MonoBehaviour
{
    private LockOnSystem lockOnSystem;
    private Transform player;

    public float rotationSpeed = 360f; // 1秒間に360度回転する速度

    void Start()
    {
        player = GameObject.FindWithTag("Player").transform;
        lockOnSystem = player.GetComponent<LockOnSystem>();
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (lockOnSystem.Target != null)
        {
            return;
        }
    }
}
