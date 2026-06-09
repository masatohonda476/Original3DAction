using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController characterController;
    private Camera mainCamera;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    public float acceleration = 20f;
    public float maxSpeed = 5f;
    public float rotationSpeed = 720f;
    public float gravity = -25f;
    public float groundedGravity = -2f;
    public float myMaxHP = 100f;
    public float myCurrentHP;



    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;
        myCurrentHP = myMaxHP;
    }

    void Update()
    {
        Vector2 moveInput = GameInput.Instance.Move;

        // カメラの向きに基づいた移動方向を計算
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;

        // Y軸は無視して水平面での移動に限定
        cameraForward.y = 0;
        cameraRight.y = 0;

        // 移動方向のベクトル
        Vector3 moveDirection = (cameraForward.normalized * moveInput.y + cameraRight.normalized * moveInput.x).normalized;
        Vector3 desiredVelocity = moveDirection * maxSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            desiredVelocity,
            acceleration * Time.deltaTime
        );

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedGravity;
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);

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
