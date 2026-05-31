using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveForce = 10f;           
    public float maxSpeed = 10f;   
    public float rotationSpeed = 720f;

    private Rigidbody rb;
    private Camera mainCamera;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;
    }


    void Update()
    {
        Vector2 moveInput = GameInput.Instance.Move;
        float moveX = moveInput.x;
        float moveZ = moveInput.y;

        // カメラの向きに基づいた移動方向を計算
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;

        // Y軸は無視して水平面での移動に限定
        cameraForward.y = 0;
        cameraRight.y = 0;

        // 移動方向のベクトル
        Vector3 moveDirection = (cameraForward.normalized * moveZ + cameraRight.normalized * moveX).normalized;

        // // 現在の水平速度を取得
        // Vector3 currentVelocity = rb.linearVelocity;
        // float currentSpeed = new Vector3(currentVelocity.x, 0, currentVelocity.z).magnitude;

        Vector3 desiredVelocity = moveDirection * maxSpeed;
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 currentHorizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

        Vector3 newHorizontalVelocity = Vector3.MoveTowards(
            currentHorizontalVelocity,
            desiredVelocity,
            moveForce * Time.deltaTime
        );

        rb.linearVelocity = new Vector3(
            newHorizontalVelocity.x,
            currentVelocity.y,
            newHorizontalVelocity.z
        );

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
    }
}
