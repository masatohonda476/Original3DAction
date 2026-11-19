using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作のカメラ。武装ハントのカメラ(電脳斬禅の人間サイズの値を機体の大きさへ拡大)を小さくしたもの。壁よけは avoidWalls でオン(ステージだけ)。
    /// 画面の揺れは入れない(作者が 3D 酔いする)。
    /// 作者「やっぱりカメラが動きすぎで酔うので」→「ロックオンにカメラ操作は完全に委ねられますか?」→「これでやってみてください」(仮の形):
    ///   ロックオン中:ソウルライクのように敵を追い続ける。カメラを回すのはロックオンだけで、右スティック・マウスでは回さない。
    ///     向き直る速さは間合いで変えず一定(lockTurnRate)。角速度に上限(lockMaxTurnSpeed)と、速さの変わり方に上限(lockTurnAccel)を付け、
    ///     相手を切り替えたときも急に振られない。上下(敵の高さ)もゆっくり追い、見える向きの回り方にも上限(lockViewMaxSpeed)を付ける。
    ///   ロックオンしていない:右スティック・マウスで今までどおり回す。
    ///   銃のボタンを押している間(DuelArms.CursorActive):右スティック・マウスは画面の上の照準を動かし(DuelArms)、カメラは回さない。
    ///     ロックオンしていないときだけ、照準を可動範囲の端へ押し付けている間、その方向へゆっくり回す(edgeYawSpeed / edgePitchSpeed)。
    ///     ロックオン中は端でも回さない。撃っても(反動でも)カメラは動かない。
    /// 前の形(撃っている間ロックオンのカメラを止める「構え」・終わったら戻る回転・ロックオン中のずらし・距離と画面の端で速さを変える)は外した。
    /// </summary>
    public class DuelCamera : MonoBehaviour
    {
        [Tooltip("人間サイズ(1.8m)の値に掛ける倍率。機体の高さから自動で決まる")]
        public float scale = 4.3f;
        [Tooltip("壁よけ:自機とカメラの間に壁があれば、壁の手前まで寄る(ステージで使う。部屋ではオフ)")]
        public bool avoidWalls;
        [Tooltip("壁よけで寄るときの速さ(大きいほど速い。1フレームで飛ばないよう、短い補間で寄る)")]
        public float wallPullIn = 25f;
        [Tooltip("壁が無くなって、元の距離へ戻るときの速さ(大きいほど速い)")]
        public float wallRelease = 3f;

        [Header("ロックオン中のカメラ(敵を追い続ける。距離では変えない。値は仮)")]
        [Tooltip("敵へ向き直る速さ(大きいほど速い。前の「近い敵」の値 6)。ずれの角度 × これ が向き直る角速度の目安")]
        public float lockTurnRate = 6f;
        [Tooltip("向き直る角速度の上限(度/秒)。相手を切り替えた・敵が背中側へ回ったときも、これより速くは回らない")]
        public float lockMaxTurnSpeed = 120f;
        [Tooltip("向き直る角速度の変わり方の上限(度/秒²)。動き出し・止まりをなめらかにする(止まるときはこの倍の速さで減らせる)")]
        public float lockTurnAccel = 720f;
        [Tooltip("敵の高さ・距離の変化(上下の向き)を追う速さ(大きいほど速い)")]
        public float lockLookSmooth = 4f;
        [Tooltip("見える向きが回る速さの上限(度/秒。左右・上下とも)。ロックオンの付け始め・自機の素早い動きでも、これより速くは回らない")]
        public float lockViewMaxSpeed = 120f;

        [Header("ロックオンしていないカメラ")]
        [Tooltip("見上げられる角度(度。マイナスが上)。作者「もっとカメラは上を向けられるようにしてください」")]
        public float freeMinPitch = -55f;

        [Header("銃のボタンを押している間、照準を画面の端へ押し付けたとき(ロックオンしていないときだけ)")]
        [Tooltip("左右へ回る速さの上限(度/秒)。照準の動きは DuelArmsRules(ステージのインスペクター)")]
        public float edgeYawSpeed = 60f;
        [Tooltip("上下へ回る速さの上限(度/秒)")]
        public float edgePitchSpeed = 35f;
        [Tooltip("回り始め・止まりのなめらかさ(大きいほどすぐ応える)")]
        public float edgeSmooth = 6f;

        float wallDist = -1f;
        // ロックオン中の左右の角速度(度/秒)と、上下を決める敵までの水平距離・高さの差(ゆっくり追う)
        float lockYawVel, lookDh, lookDy;
        // 照準を端へ押し付けているときの回る速さ(度/秒。x=左右 y=上下。上が+)
        Vector2 edgeVel;
        bool wasLocked;
        Quaternion lastRot;

        const float Distance = 5.4f, Height = 2.9f, Shoulder = 1.2f, Smooth = 7f, FreeSmooth = 16f;
        const float StickYawSpeed = 320f, StickPitchSpeed = 190f, MouseSens = 0.24f;
        const float MaxPitch = 55f, DefaultPitch = 20f;
        float MinPitch => freeMinPitch;

        float yaw, pitch = DefaultPitch;
        Vector3 fwd = Vector3.forward, pos, lookPoint;
        bool initialized;

        /// <summary>
        /// 補間せずに、次のフレームで自機の後ろへ即座に合わせる(やり直しで自機が瞬間移動したとき。カメラが大きく流れないように)。
        /// 自由に回すときの向きは自機の向きにそろえる。
        /// </summary>
        public void Snap()
        {
            initialized = false;
            wallDist = -1f;
            wasLocked = false;
            lockYawVel = 0f;
            edgeVel = Vector2.zero;
            var p = DuelPlayer.I;
            if (p == null) return;
            Vector3 f = p.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f) fwd = f.normalized;
            yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            pitch = DefaultPitch;
        }

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
            // 銃のボタンを押している間(と撃ってから少しの間):右スティック・マウスは照準を動かす(DuelArms)。カメラは回さない
            bool aiming = p.Arms != null && p.Arms.CursorActive;

            Vector3 desired, look;
            float follow;
            if (enemy != null)
            {
                UpdateLockTurn(p, enemy, chest, dt);
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                desired = p.transform.position - fwd * Distance * s + Vector3.up * Height * s + right * Shoulder * s;
                // 注視点:左右はカメラの向き(上限つきで回る fwd)の先、上下は敵の高さをゆっくり追う(相手を切り替えても注視点が飛ばない)
                look = chest + fwd * (lookDh * 0.6f) + Vector3.up * (lookDy * 0.6f) + Vector3.down * 0.35f * s;
                edgeVel = Vector2.zero;
                follow = Smooth;
            }
            else
            {
                if (wasLocked && initialized)
                {
                    // ロックオンが外れたとき:今見えている向きのまま自由なカメラへ引き継ぐ(画面が飛ばないように)
                    Vector3 e = transform.rotation.eulerAngles;
                    yaw = e.y;
                    pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), MinPitch, MaxPitch);
                }
                Vector2 wantEdge = Vector2.zero;
                if (!aiming)
                {
                    Vector2 stick = ProtoInput.RightStick;
                    Vector2 m = ProtoInput.MouseDelta;
                    yaw += stick.x * StickYawSpeed * dt + m.x * MouseSens;
                    pitch = Mathf.Clamp(pitch - stick.y * StickPitchSpeed * dt - m.y * MouseSens, MinPitch, MaxPitch);
                }
                else
                {
                    // 照準を可動範囲の端へ押し付けている間だけ、その方向へゆっくり回る(上限つき)
                    Vector2 push = p.Arms.EdgePush;
                    wantEdge = new Vector2(push.x * edgeYawSpeed, push.y * edgePitchSpeed);
                }
                edgeVel = Vector2.Lerp(edgeVel, wantEdge, 1f - Mathf.Exp(-Mathf.Max(0.1f, edgeSmooth) * dt));
                yaw += edgeVel.x * dt;
                pitch = Mathf.Clamp(pitch - edgeVel.y * dt, MinPitch, MaxPitch);
                Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                fwd = new Vector3(dir.x, 0f, dir.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 pivot = chest + Vector3.up * 0.5f * s + right * Shoulder * 0.6f * s;
                desired = pivot - dir * Distance * s;
                look = pivot + dir * 4f * s;
                follow = FreeSmooth;
                lockYawVel = 0f;
            }
            desired.y = Mathf.Max(desired.y, p.transform.position.y + 1.5f);

            float k = initialized ? 1f - Mathf.Exp(-follow * dt) : 1f;
            pos = Vector3.Lerp(pos, desired, k);
            lookPoint = Vector3.Lerp(lookPoint, look, k);

            Vector3 shown = avoidWalls ? AvoidWalls(chest, pos, dt) : pos;
            transform.position = shown;
            Vector3 lookDir = lookPoint - shown;
            Quaternion rot = lookDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDir) : transform.rotation;
            // ロックオン中は、見える向きの回り方に上限(付け始め・自機の素早い動きでも急に回らない)
            if (enemy != null && initialized && wasLocked) rot = Quaternion.RotateTowards(lastRot, rot, Mathf.Max(1f, lockViewMaxSpeed) * dt);
            transform.rotation = rot;
            lastRot = rot;
            wasLocked = enemy != null;
            initialized = true;
        }

        /// <summary>
        /// ロックオン中の向き直り:敵への左右のずれ × lockTurnRate を目安の角速度にし、上限(lockMaxTurnSpeed)で切り、
        /// 角速度の変わり方にも上限(lockTurnAccel。減らすときは2倍)を付けて fwd を回す。行き過ぎない。
        /// 上下を決める敵までの水平距離・高さの差は lockLookSmooth でゆっくり追う。
        /// </summary>
        void UpdateLockTurn(DuelPlayer p, DuelEnemy enemy, Vector3 chest, float dt)
        {
            Vector3 toE = enemy.LockPoint - chest;
            float dh = new Vector2(toE.x, toE.z).magnitude, dy = toE.y;
            if (!wasLocked || !initialized)
            {
                // 付け始め:今のカメラの向きから回り始める(止まった状態から)
                Vector3 f = transform.forward;
                f.y = 0f;
                if (initialized && f.sqrMagnitude > 0.0001f) fwd = f.normalized;
                lockYawVel = 0f;
                lookDh = dh;
                lookDy = dy;
            }
            float lk = 1f - Mathf.Exp(-Mathf.Max(0.1f, lockLookSmooth) * dt);
            lookDh = Mathf.Lerp(lookDh, dh, lk);
            lookDy = Mathf.Lerp(lookDy, dy, lk);

            Vector3 to = enemy.transform.position - p.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            float err = Vector3.SignedAngle(fwd, to, Vector3.up);
            float max = Mathf.Max(1f, lockMaxTurnSpeed);
            float want = Mathf.Clamp(err * Mathf.Max(0.1f, lockTurnRate), -max, max);
            bool slowing = Mathf.Abs(want) < Mathf.Abs(lockYawVel) || want * lockYawVel < 0f;
            lockYawVel = Mathf.MoveTowards(lockYawVel, want, Mathf.Max(1f, lockTurnAccel) * (slowing ? 2f : 1f) * dt);
            float step = lockYawVel * dt;
            if (step * err > 0f && Mathf.Abs(step) > Mathf.Abs(err))
            {
                // 行き過ぎない(ちょうど敵の向きで止める)
                step = err;
                lockYawVel = want;
            }
            fwd = (Quaternion.AngleAxis(step, Vector3.up) * fwd).normalized;
        }

        /// <summary>
        /// 自機の注視点からカメラまでの間に壁があれば、壁の手前まで寄せる。寄るのは短い補間で速く、離れるのはゆっくり(急に飛ばないように)。
        /// 機体と見えない壁は Ignore Raycast の層なので当たらない(見えている壁・柱・床だけをよける)。
        /// </summary>
        Vector3 AvoidWalls(Vector3 from, Vector3 to, float dt)
        {
            Vector3 d = to - from;
            float full = d.magnitude;
            if (full < 0.01f) return to;
            Vector3 dir = d / full;
            float allowed = full;
            if (Physics.SphereCast(from, 0.35f * scale, dir, out var hit, full, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(1f, hit.distance);
            // 壁が入ったら短い補間で寄る(数フレームだけ壁の向こうが見えることがある)。壁が無くなったら、ゆっくり元の距離へ戻す
            if (wallDist < 0f) wallDist = allowed;
            else wallDist = Mathf.Lerp(wallDist, allowed, 1f - Mathf.Exp(-(allowed < wallDist ? wallPullIn : wallRelease) * dt));
            return from + dir * Mathf.Min(wallDist, full);
        }
    }
}
