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

//========================================================
//初期化
//========================================================
    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x  = angles.y;
        y  = angles.x;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        //ロックオン対象を取得
        lockOnSystem = target.GetComponent<LockOnSystem>();
    }

//========================================================
//カメラ更新
//========================================================
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

        //カメラ入力
        Vector2 lookInput = GameInput.Instance.Look;

        //通常時のみマウス入力でカメラを回転させる
        if (lockOnSystem.Target == null)
        {
            if (lookInput != Vector2.zero)
            {
                x += lookInput.x * xSpeed * Time.deltaTime;
                y -= lookInput.y * ySpeed * Time.deltaTime;

                y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
            }
        }

        //ロックオン中は敵の方向へYaw(x)を徐々に向ける
        if (lockOnSystem.Target != null)
        {
            Vector3 direction = lockOnSystem.Target.position - target.position;
            direction.y = 0f; //上下方向は無視して水平方向(Yaw)のみ敵を向く

            float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

            x = Mathf.LerpAngle(
                x,
                targetYaw,
                10f * Time.deltaTime
            );
        }

        //x(Yaw)とy(Pitch)の回転角度を元にカメラの回転を計算
        Quaternion rotation = Quaternion.Euler(y, x, 0);

        //プレイヤーを中心にしてカメラの位置を計算
        Vector3 focusPosition = target.position;
        Vector3 cameraDirection = rotation * Vector3.back;
        Vector3 position = focusPosition + cameraDirection * distance;
        Vector3 headPosition = focusPosition + Vector3.up * headHeight;
        Vector3 chestPosition = focusPosition + Vector3.up * chestHeight;


//========================================================
//壁回避
//========================================================
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

        //カメラがめり込んでいたら少しずつ押し出す
        for (int i = 0; i < 20; i++)
        {
            //カメラの位置に球を置き、衝突判定を行う
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

        //カメラ更新
        transform.rotation = rotation;
        transform.position = position;

        Collider[] hits = Physics.OverlapSphere(
            adjustedPosition,
            collisionRadius,
            collisionMask,
            QueryTriggerInteraction.Ignore);
    }

    //ギズモ
    void OnDrawGizmos()
{
    Gizmos.color = Color.red;
    Gizmos.DrawWireSphere(transform.position, collisionRadius);
}
}
