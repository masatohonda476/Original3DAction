using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController characterController;
    private Camera mainCamera;
    private Vector3 horizontalVelocity;
    private Vector3 lastMoveDirection = Vector3.forward;
    private float verticalVelocity;
    private bool isDodging;
    private float dodgeTimer;
    private Vector3 dodgeDirection;
    private float currentDodgeSpeed;

    public float acceleration = 20f;
    public float maxSpeed = 5f;
    public float rotationSpeed = 720f;
    public float gravity = -25f;
    public float groundedGravity = -2f;
    public float myMaxHP = 100f;
    public float myCurrentHP;
    public float shortDodgeSpeed = 10f;
    public float shortDodgeDuration = 0.3f;

    public float longDodgeSpeed = 15f;
    public float longDodgeDuration = 0.6f;



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

        //回避
        if (isDodging)
        {
            dodgeTimer -= Time.deltaTime;
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedGravity;
            }
            verticalVelocity += gravity * Time.deltaTime;
            Vector3 dodgeVelocity = dodgeDirection * currentDodgeSpeed + Vector3.up * verticalVelocity;

            dodgeDirection = lastMoveDirection;//回避中も移動方向を更新
            transform.rotation = Quaternion.LookRotation(dodgeDirection);

            characterController.Move(dodgeVelocity * Time.deltaTime);

            if (dodgeTimer <= 0f)
            {
                isDodging = false;
            }
            return;
        }

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
        
        //プレイヤー回転
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            lastMoveDirection = moveDirection;

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        //小回避
        if (GameInput.Instance.ShortDodgePressed() && !isDodging)
        {
            Debug.Log("Short Dodge Pressed!");
            isDodging = true;
            dodgeTimer = shortDodgeDuration;
            currentDodgeSpeed = shortDodgeSpeed;
            dodgeDirection = lastMoveDirection;
        }

        //大回避
        if (GameInput.Instance.LongDodgePressed() && !isDodging)
        {
            Debug.Log("Long Dodge Pressed!");
            isDodging = true;
            dodgeTimer = longDodgeDuration;
            currentDodgeSpeed = longDodgeSpeed;
            dodgeDirection = lastMoveDirection;
        }
    }
}
