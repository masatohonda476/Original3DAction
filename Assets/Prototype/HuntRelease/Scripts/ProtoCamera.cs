using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntProto
{
    /// <summary>
    /// 電脳斬禅の DuelCamera にならったカメラを、機体の大きさに合わせて拡大したもの。
    /// ロックオン中:自機の肩越しに相手を捉える(相手と自機の間を見る)。
    /// ロックオンなし:右スティック/マウスで自由に回す(操作に素早く付いてくる)。
    /// </summary>
    public class ProtoCamera : MonoBehaviour
    {
        public static ProtoCamera I;

        public Transform target;
        [Tooltip("人間サイズ(1.8m)の電脳斬禅の値に掛ける倍率。機体の高さから自動で決まる")]
        public float scale = 4.3f;

        // 電脳斬禅の値(人間サイズ)
        const float Distance = 5.4f, Height = 2.9f, Shoulder = 1.2f, Smooth = 7f, FreeSmooth = 16f;
        const float StickYawSpeed = 320f, StickPitchSpeed = 190f, MouseSens = 0.24f;
        const float MinPitch = -5f, MaxPitch = 55f;
        /// <summary>ふだんの見下ろし角度。ロックオンなしの射撃はこの角度を「水平」とみなす。</summary>
        public const float DefaultPitch = 20f;

        float yaw, pitch = DefaultPitch;
        Vector3 fwd = Vector3.forward;
        Vector3 lookPoint;
        float shake;
        bool initialized;

        [Tooltip("画面の揺れ(足音・被弾・爆発)。3D酔い対策で既定は切る")]
        public bool shakeEnabled = false;

        public static void Shake(float amount)
        {
            if (I != null && I.shakeEnabled) I.shake = Mathf.Max(I.shake, amount);
        }

        void Awake() { I = this; }

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

            var p = ProtoPlayer.I;
            if (target == null || p == null) return;
            float dt = Time.unscaledDeltaTime;
            float s = scale;
            Vector3 chest = p.ChestPosition;
            var enemy = p.Target;

            Vector3 desired, look;
            float follow;
            if (enemy != null && !p.Aiming)   // 照準中(△長押し)はロックオン中でも自由に回せる
            {
                Vector3 to = enemy.transform.position - target.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) fwd = Vector3.Slerp(fwd, to.normalized, 1f - Mathf.Exp(-6f * dt));
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                desired = target.position - fwd * Distance * s + Vector3.up * Height * s + right * Shoulder * s;
                Vector3 enemyChest = enemy.transform.position + Vector3.up * p.MechHeight * 0.6f * enemy.Scale;
                look = Vector3.Lerp(chest, enemyChest, 0.68f) + Vector3.down * 0.35f * s;
                yaw = Quaternion.LookRotation(fwd).eulerAngles.y;   // ロックオン中の向きを自由カメラへ引き継ぐ
                pitch = Mathf.MoveTowards(pitch, DefaultPitch, 30f * dt);
                follow = Smooth;
            }
            else
            {
                Vector2 stick = ProtoInput.RightStick;
                Vector2 m = ProtoInput.MouseDelta;
                yaw += stick.x * StickYawSpeed * dt + m.x * MouseSens;
                pitch = Mathf.Clamp(pitch - stick.y * StickPitchSpeed * dt - m.y * MouseSens, MinPitch, MaxPitch);
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                Vector3 dir = rot * Vector3.forward;
                fwd = new Vector3(dir.x, 0f, dir.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 pivot = chest + Vector3.up * 0.5f * s + right * Shoulder * 0.6f * s;
                desired = pivot - dir * Distance * s;
                look = pivot + dir * 4f * s;
                follow = FreeSmooth;
            }
            desired.y = Mathf.Max(desired.y, target.position.y + 1.5f);

            float k = 1f - Mathf.Exp(-follow * dt);
            if (!initialized) { k = 1f; initialized = true; }
            Vector3 pos = Vector3.Lerp(transform.position, desired, k);
            lookPoint = Vector3.Lerp(lookPoint, look, k);

            // 壁・天井にめり込まないよう、機体の胸から見て地形より手前に寄せる(閉所用)
            Vector3 pivotC = chest + Vector3.up * 0.3f * s;
            Vector3 toCam = pos - pivotC;
            float want = toCam.magnitude;
            if (want > 0.01f)
            {
                float hit = ProtoStage.CastDistance(pivotC, toCam / want, want, 1.2f);
                if (hit < want) pos = pivotC + toCam / want * Mathf.Max(1f, hit - 0.3f);
            }

            Vector3 shakeOffset = Vector3.zero;
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 35f;
                shakeOffset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * shake * 2f;
                shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            }
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(lookPoint - pos);
            transform.position += transform.rotation * shakeOffset;
        }
    }
}
