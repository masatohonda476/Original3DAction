using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 3, -5);
    void LateUpdate()
    {
        if (target == null) return;
        // プレイヤーの位置にオフセットを加えてカメラ位置を決定
        transform.position = target.position + offset;
        // 常にプレイヤーの方向を向く
        transform.LookAt(target);
    }
}
