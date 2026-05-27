using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float moveForce = 10f;           // 加える力の大きさ
    public float maxSpeed = 10f;            // 最大速度

    private InputAction moveAction;
    private Rigidbody rb;
    private Camera mainCamera;

    void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;
    }

    void OnEnable()
    {
        moveAction?.Enable();
    }

    void OnDisable()
    {
        moveAction?.Disable();
    }

    void Update()
    {
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
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

        // 現在の水平速度を取得
        Vector3 currentVelocity = rb.linearVelocity;
        float currentSpeed = new Vector3(currentVelocity.x, 0, currentVelocity.z).magnitude;

        // 最大速度に達していない場合のみ力を加える
        if (currentSpeed < maxSpeed)
        {
            rb.AddForce(moveDirection * moveForce, ForceMode.Force);
        }
    }
}
