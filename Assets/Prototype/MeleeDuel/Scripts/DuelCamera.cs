using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作のカメラ。武装ハントのカメラ(電脳斬禅の人間サイズの値を機体の大きさへ拡大)を、壁よけなしで小さくしたもの。
    /// ロックオン中は自機の肩越しに敵を見る。それ以外は右スティック・マウスで回す。画面の揺れは入れない。
    /// </summary>
    public class DuelCamera : MonoBehaviour
    {
        [Tooltip("人間サイズ(1.8m)の値に掛ける倍率。機体の高さから自動で決まる")]
        public float scale = 4.3f;

        const float Distance = 5.4f, Height = 2.9f, Shoulder = 1.2f, Smooth = 7f, FreeSmooth = 16f;
        const float StickYawSpeed = 320f, StickPitchSpeed = 190f, MouseSens = 0.24f;
        const float MinPitch = -5f, MaxPitch = 55f, DefaultPitch = 20f;

        float yaw, pitch = DefaultPitch;
        Vector3 fwd = Vector3.forward, pos, lookPoint;
        bool initialized;

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            var p = DuelPlayer.I;
            if (p == null) return;
            float dt = Time.unscaledDeltaTime;
            float s = scale;
            Vector3 chest = p.LockPoint;   // 注視点の高さはロックオンの印と同じ(機体の高さの7割)
            var enemy = p.Target;

            Vector3 desired, look;
            float follow;
            if (enemy != null)
            {
                Vector3 to = enemy.transform.position - p.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) fwd = Vector3.Slerp(fwd, to.normalized, 1f - Mathf.Exp(-6f * dt));
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                desired = p.transform.position - fwd * Distance * s + Vector3.up * Height * s + right * Shoulder * s;
                look = Vector3.Lerp(chest, enemy.LockPoint, 0.6f) + Vector3.down * 0.35f * s;
                yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
                pitch = Mathf.MoveTowards(pitch, DefaultPitch, 30f * dt);
                follow = Smooth;
            }
            else
            {
                Vector2 stick = ProtoInput.RightStick;
                Vector2 m = ProtoInput.MouseDelta;
                yaw += stick.x * StickYawSpeed * dt + m.x * MouseSens;
                pitch = Mathf.Clamp(pitch - stick.y * StickPitchSpeed * dt - m.y * MouseSens, MinPitch, MaxPitch);
                Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                fwd = new Vector3(dir.x, 0f, dir.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 pivot = chest + Vector3.up * 0.5f * s + right * Shoulder * 0.6f * s;
                desired = pivot - dir * Distance * s;
                look = pivot + dir * 4f * s;
                follow = FreeSmooth;
            }
            desired.y = Mathf.Max(desired.y, p.transform.position.y + 1.5f);

            float k = initialized ? 1f - Mathf.Exp(-follow * dt) : 1f;
            initialized = true;
            pos = Vector3.Lerp(pos, desired, k);
            lookPoint = Vector3.Lerp(lookPoint, look, k);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(lookPoint - pos);
        }
    }
}
