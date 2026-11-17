using System.Collections.Generic;
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
        float raise;   // 背後が壁のとき、カメラを上へ逃がす角度(なめらかに変える)
        bool playerHidden;
        readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> hiddenModes = new();

        [Tooltip("背後が壁のとき、カメラを上へ逃がす最大の角度")]
        public float maxRaise = 40f;
        [Tooltip("後ろがこの割合(本来の距離に対して)だけ空けば、それ以上は上げない")]
        public float clearRatio = 0.5f;
        [Tooltip("上へ逃がしたとき、注視点を自機へ寄せる強さ(0〜1)")]
        public float raiseLookToPlayer = 0.5f;
        [Tooltip("カメラが機体の胸にこれより近づいたら、自機を消して(影だけ残す)視界をふさがないようにする")]
        public float hidePlayerDistance = 7f;

        /// <summary>
        /// プレイヤーが操作しているカメラの上下の角度(下向きが正)。壁よけでカメラを上げた分は含まない。
        /// ロックオンなしの射撃の高さはこれで決める(壁際でカメラが上がっても弾が下へそれないように)。
        /// </summary>
        public float AimPitch => pitch;
        Vector3 fwd = Vector3.forward;
        Vector3 lookPoint;
        Vector3 freePos;   // 壁で寄せる前のカメラ位置(寄せた位置を次のフレームの出発点にしない)
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
            if (!initialized) { k = 1f; initialized = true; freePos = desired; }
            freePos = Vector3.Lerp(freePos, desired, k);
            Vector3 pos = freePos;
            lookPoint = Vector3.Lerp(lookPoint, look, k);

            // 背後が壁のとき:そのまま寄せると機体に埋もれるので、まずカメラを上へ回して(見下ろして)逃がす。
            // それでも足りない分だけ、地形より手前に寄せる(閉所用)
            Vector3 pivotC = chest + Vector3.up * 0.3f * s;
            Vector3 toCam = pos - pivotC;
            float want = toCam.magnitude;
            if (want > 0.01f)
            {
                Vector3 dir0 = toCam / want;
                float targetRaise = FindRaise(pivotC, dir0, want);
                // 上げるときは素早く、戻すときはゆっくり(行ったり来たりしないように)
                float rate = targetRaise > raise ? 10f : 2.5f;
                raise = Mathf.Lerp(raise, targetRaise, 1f - Mathf.Exp(-rate * dt));
                Vector3 dir = RaiseDir(dir0, raise);
                float hit = ProtoStage.CastDistance(pivotC, dir, want, 1.2f);
                pos = pivotC + dir * Mathf.Max(1f, Mathf.Min(want, hit - 0.3f));
            }
            UpdatePlayerHidden(p, Vector3.Distance(pos, chest));

            Vector3 shakeOffset = Vector3.zero;
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 35f;
                shakeOffset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * shake * 2f;
                shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            }
            transform.position = pos;
            // 上へ逃がしたぶん、注視点を自機の少し前へ寄せる(見下ろしても自機が画面から外れないように)
            Vector3 lookAt = Vector3.Lerp(lookPoint, chest + fwd * 1f * s, Mathf.Clamp01(raise / Mathf.Max(1f, maxRaise)) * raiseLookToPlayer);
            transform.rotation = Quaternion.LookRotation(lookAt - pos);
            transform.position += transform.rotation * shakeOffset;
        }

        /// <summary>dir を上へ deg 度回した向き(真上の手前で止める)。</summary>
        static Vector3 RaiseDir(Vector3 dir, float deg)
        {
            if (deg <= 0.01f) return dir;
            Vector3 axis = Vector3.Cross(dir, Vector3.up);
            if (axis.sqrMagnitude < 1e-4f) return dir;
            float elev = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            deg = Mathf.Min(deg, 80f - elev);
            return deg <= 0f ? dir : Quaternion.AngleAxis(deg, axis.normalized) * dir;
        }

        /// <summary>後ろが十分に空く(本来の距離の clearRatio 以上)まで、少しずつ上へ回した角度。空かなければ一番空く角度。</summary>
        float FindRaise(Vector3 pivot, Vector3 dir, float want)
        {
            const float Step = 6f;
            float best = 0f, bestHit = -1f;
            for (float a = 0f; a <= maxRaise + 0.01f; a += Step)
            {
                float hit = ProtoStage.CastDistance(pivot, RaiseDir(dir, a), want, 1.2f);
                if (hit >= want * clearRatio) return a;
                if (hit > bestHit + 0.5f) { bestHit = hit; best = a; }
            }
            return best;
        }

        /// <summary>カメラが機体に近すぎるときは自機を消す(影は残す)。出入りでちらつかないよう少し幅を持たせる。</summary>
        void UpdatePlayerHidden(ProtoPlayer p, float dist)
        {
            bool hide = playerHidden ? dist < hidePlayerDistance + 1.5f : dist < hidePlayerDistance;
            if (hide)
            {
                // 付け替えで増えた部位も消えるよう、隠している間は毎回確かめる
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                {
                    if (hiddenModes.ContainsKey(r)) continue;
                    hiddenModes[r] = r.shadowCastingMode;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
            }
            else if (playerHidden)
            {
                foreach (var kv in hiddenModes)
                    if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
                hiddenModes.Clear();
            }
            playerHidden = hide;
        }
    }
}
