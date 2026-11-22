using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    /// <summary>
    /// プロトタイプのカメラ。武装ハントのカメラ(電脳斬禅の人間サイズの値を機体の大きさへ拡大)を小さくしたもの。壁の処理は avoidWalls でオン(ステージのシーンではオン)。
    /// 壁の処理(wallMode):既定は「透かす」。自機とカメラの間に入った壁・柱は半透明にし(DuelWallFader)、カメラは寄せない。
    ///   寄せるのは、床の下へ潜る・天井を抜けるときだけ(向きは変えず前後に動くだけ、戻るのは待ってからゆっくり)。
    ///   作者「まだカメラがたまに勝手に動きます」(壁・狭い所の近く)→ 透かす形の案に「Aで」。「寄せる」は前の形(比べる用)。
    /// 画面の揺れは入れない(作者が 3D 酔いする)。
    /// 作者「やっぱりカメラが動きすぎで酔うので」→「ロックオンにカメラ操作は完全に委ねられますか?」→「これでやってみてください」(仮の形):
    ///   ロックオン中:ソウルライクのように敵を追い続ける。カメラを回すのはロックオンだけで、右スティック・マウスでは回さない。
    ///     向き直る速さは間合いで変えず一定(lockTurnRate)。角速度に上限(lockMaxTurnSpeed)と、速さの変わり方に上限(lockTurnAccel)を付け、
    ///     相手を切り替えたときも急に振られない。上下(敵の高さ)もゆっくり追い、見える向きの回り方にも上限(lockViewMaxSpeed)を付ける。
    ///   ロックオンしていない:右スティック・マウスで今までどおり回す。
    ///   照準を動かせるとき(DuelArmsRules.cursorMovable。既定はオフ)の、遠距離を構えている間(DuelArms.CursorActive):
    ///     右スティック・マウスは画面の上の照準を動かし(DuelArms)、カメラは回さない。
    ///     ロックオンしていないときだけ、照準を可動範囲の端へ押し付けている間、その方向へゆっくり回す(edgeYawSpeed / edgePitchSpeed)。
    ///     ロックオン中は端でも回さない。撃っても(反動でも)カメラは動かない。
    /// 前の形(撃っている間ロックオンのカメラを止める「構え」・終わったら戻る回転・ロックオン中のずらし・距離と画面の端で速さを変える)は外した。
    /// ジャンプで敵の攻撃を跳び越えた間(DuelPlayer.JumpCameraHold。成功した瞬間から着地・被弾・飛行に移るまで)は、ロックオンのカメラの向きを固定する
    ///   (holdOnJumpSuccess。作者「成功したら専用のカメラ演出などを入れればカメラ問題も解決しそうです」の、一番軽い形。専用の画角は作っていない)。
    ///   相手を追って回るのを止め(左右の角速度は上限つきで 0 へ落とす)、上下は固定し始めたときに見えていた角度のまま動かさない(LateUpdate の heldPitch。
    ///   上下を決める値を止めるだけでは、注視点の補間の残りで少しずつ動いた)。位置は今までどおり自機に付いていく。
    ///   解けたら、いつもの上限つきの追従(lockTurnRate・lockMaxTurnSpeed・lockTurnAccel・lockLookSmooth・lockViewMaxSpeed)で相手へ戻る(止まった状態から回り始める)。
    /// </summary>
    public class DuelCamera : MonoBehaviour
    {
        /// <summary>自機とカメラの間に入った壁・柱の扱い。</summary>
        public enum WallMode
        {
            [InspectorName("透かす(寄せない)")] Fade,
            [InspectorName("寄せる(前の形)")] PullIn,
        }

        [Tooltip("人間サイズ(1.8m)の値に掛ける倍率。機体の高さから自動で決まる")]
        public float scale = 4.3f;
        [Tooltip("壁の処理:自機とカメラの間の壁・柱・床・天井を扱う(ステージのシーンではオン)。扱い方は wallMode")]
        public bool avoidWalls;
        [Tooltip("透かす:壁・柱は半透明にしてカメラは寄せない(寄せるのは床・天井だけ。下の pullOnFloors / pullOnCeilings)。\n" +
                 "寄せる:前の形(壁の手前まで寄る)")]
        public WallMode wallMode = WallMode.Fade;
        [Tooltip("床・地面の下へ潜りそうなときは寄せる(透かすときも。床の上面に当たったら、その手前まで)")]
        public bool pullOnFloors = true;
        [Tooltip("天井を抜けそうなときは寄せる(透かすときも。足場の裏・張り出しの下面も天井に入る)。オフにすると天井も半透明にする")]
        public bool pullOnCeilings = true;
        [Tooltip("床・天井とみなす面の傾き(面の向きの上下の成分)。0.6 なら、水平から約53°までの面が床・天井、それより立った面が壁")]
        [Range(0.3f, 0.95f)] public float floorNormalY = 0.6f;

        [Header("透かす(wallMode = 透かす)。値は仮")]
        [Tooltip("半透明にしたときの濃さ(0 = 見えない、1 = 元のまま)。薄いほど、向こうの敵の色が変わらない")]
        [Range(0f, 1f)] public float fadeAlpha = 0.2f;
        [Tooltip("間に入ってから薄くなりきるまでの秒数")]
        public float fadeInTime = 0.15f;
        [Tooltip("間から外れてから、元の濃さへ戻りきるまでの秒数(戻り始めてから)")]
        public float fadeOutTime = 0.25f;
        [Tooltip("間から外れてから、戻り始めるまでの秒数(狭い所で薄くなったり戻ったりを細かく繰り返さないように)")]
        public float fadeHoldTime = 0.3f;
        [Tooltip("自機の体の幅のどこまでを見るか(機体の高さに対する割合。左右の肩のあたり)")]
        public float fadeBodyWidth = 0.2f;

        [Header("寄せるとき(寄せるモード、または透かすモードで床・天井)")]
        [Tooltip("壁よけで寄るときの速さ(大きいほど速い。1フレームで飛ばないよう、短い補間で寄る)")]
        public float wallPullIn = 25f;
        [Tooltip("壁が無くなって、元の距離へ戻るときの速さ(大きいほど速い)")]
        public float wallRelease = 3f;
        [Tooltip("壁が無くなってから、元の距離へ戻り始めるまでの秒数(狭い所で寄ったり離れたりを細かく繰り返さないように)")]
        public float wallReleaseDelay = 0.6f;
        [Tooltip("オンなら、壁よけで寄っても向きは変えない(寄る前の位置から見た向きのまま、前後に動くだけ。酔い対策)")]
        public bool wallKeepRotation = true;

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
        [Tooltip("オンなら、ジャンプで敵の攻撃を跳び越えた瞬間から着地(被弾・飛行に移る)まで、ロックオンのカメラが相手を追って回るのを止めて向きを固定する。" +
                 "解けたら上の上限つきの追従で相手へ戻る。オフなら、その間も今までどおり相手を追う")]
        public bool holdOnJumpSuccess = true;

        [Header("ロックオンしていないカメラ")]
        [Tooltip("見上げられる角度(度。マイナスが上)。作者「もっとカメラは上を向けられるようにしてください」")]
        public float freeMinPitch = -55f;

        [Header("遠距離を構えている間、照準を画面の端へ押し付けたとき(ロックオンしていないときだけ)")]
        [Tooltip("左右へ回る速さの上限(度/秒)。照準の動きは DuelArmsRules(ステージのインスペクター)")]
        public float edgeYawSpeed = 60f;
        [Tooltip("上下へ回る速さの上限(度/秒)")]
        public float edgePitchSpeed = 35f;
        [Tooltip("回り始め・止まりのなめらかさ(大きいほどすぐ応える)")]
        public float edgeSmooth = 6f;

        float wallDist = -1f, wallClearFor;
        // MaterialPropertyBlock を持つので、フィールドの初期化では作らない(MonoBehaviour の構築中は作れない)。最初に使うときに作る
        DuelWallFader fader;
        DuelWallFader Fader => fader ??= new DuelWallFader();
        readonly RaycastHit[] pullHits = new RaycastHit[16];
        // ロックオン中の左右の角速度(度/秒)と、上下を決める敵までの水平距離・高さの差(ゆっくり追う)
        float lockYawVel, lookDh, lookDy;
        // 照準を端へ押し付けているときの回る速さ(度/秒。x=左右 y=上下。上が+)
        Vector2 edgeVel;
        bool wasLocked;
        Quaternion lastRot;
        // ジャンプで攻撃を跳び越えた間の固定:固定しているか・固定し始めたときに見えていた上下の角度(度。Quaternion.Euler の x)
        bool pitchHeld;
        float heldPitch;

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
            pitchHeld = false;
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
            // 照準を動かせるときの、遠距離を構えている間:右スティック・マウスは照準を動かす(DuelArms)。カメラは回さない
            bool aiming = p.Arms.CursorActive;

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

            bool fade = avoidWalls && wallMode == WallMode.Fade;
            Vector3 shown = !avoidWalls ? pos : AvoidWalls(chest, pos, dt, fade);
            transform.position = shown;
            // 壁よけで寄っても向きは変えない(寄る前の位置から見た向き。寄った位置から見直すと、狭い所で向きまで回って酔う)
            Vector3 lookDir = lookPoint - (wallKeepRotation ? pos : shown);
            Quaternion rot = lookDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDir) : transform.rotation;
            // ジャンプで攻撃を跳び越えた間(UpdateLockTurn が相手を追うのを止めている間)は、上下を固定し始めたときの角度のまま動かさない。
            // 左右は UpdateLockTurn が角速度を落として止める分だけ。解けたら、すぐ下の上限つきの回り方で、いつもの向きへ戻る
            bool hold = enemy != null && initialized && wasLocked && holdOnJumpSuccess && p.JumpCameraHold;
            if (hold)
            {
                if (!pitchHeld) heldPitch = lastRot.eulerAngles.x;
                rot = Quaternion.Euler(heldPitch, rot.eulerAngles.y, 0f);
            }
            pitchHeld = hold;
            // ロックオン中は、見える向きの回り方に上限(付け始め・自機の素早い動きでも急に回らない)
            if (enemy != null && initialized && wasLocked) rot = Quaternion.RotateTowards(lastRot, rot, Mathf.Max(1f, lockViewMaxSpeed) * dt);
            if (hold) rot = Quaternion.Euler(heldPitch, rot.eulerAngles.y, 0f);   // 回る途中でも上下は動かさない
            transform.rotation = rot;
            lastRot = rot;
            // 透かす:自機の体の数点からカメラまでの間に入った壁・柱を半透明にする(外れたら少し待ってから戻す)。
            // 切ったとき・寄せるモードのときは新しく薄くせず、薄くしていた物はいつもの速さで戻す
            if (fade) Fader.Mark(p, shown, transform.right, fadeBodyWidth, floorNormalY, !pullOnCeilings);
            Fader.Tick(dt, fadeAlpha, fadeInTime, fadeOutTime, fadeHoldTime);
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
            if (holdOnJumpSuccess && p.JumpCameraHold)
            {
                // 攻撃を跳び越えた間は向きを固定:相手を追わない。回っている途中だったら、角速度を上限つきで 0 へ落として止める(急に止めない)。
                // 上下を決める値(lookDh / lookDy)も今のまま。解けたら、下のいつもの追従が止まった状態から回り始める
                lockYawVel = Mathf.MoveTowards(lockYawVel, 0f, Mathf.Max(1f, lockTurnAccel) * 2f * dt);
                if (lockYawVel != 0f) fwd = (Quaternion.AngleAxis(lockYawVel * dt, Vector3.up) * fwd).normalized;
                return;
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

        void OnDisable() => fader?.RestoreAll();
        void OnDestroy() => fader?.Dispose();

        /// <summary>
        /// 自機の注視点からカメラまでの間に壁があれば、壁の手前まで寄せる。寄るのは短い補間で速く、離れるのはゆっくり(急に飛ばないように)。
        /// 機体と見えない壁は Ignore Raycast の層なので当たらない(見えている壁・柱・床だけをよける)。
        /// floorsOnly(透かすモード):壁・柱では寄せず、床の上面(床の下へ潜る)・天井の下面(天井を抜ける)に当たったときだけ寄せる。
        /// </summary>
        Vector3 AvoidWalls(Vector3 from, Vector3 to, float dt, bool floorsOnly)
        {
            Vector3 d = to - from;
            float full = d.magnitude;
            if (full < 0.01f) return to;
            Vector3 dir = d / full;
            float allowed = full;
            if (floorsOnly) allowed = FloorCeilingLimit(from, dir, full);
            else if (Physics.SphereCast(from, 0.35f * scale, dir, out var hit, full, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(1f, hit.distance);
            // 壁が入ったら短い補間で寄る(数フレームだけ壁の向こうが見えることがある)。
            // 壁が無くなっても wallReleaseDelay 秒は今の距離で待ち、それからゆっくり元の距離へ戻す(狭い所で寄ったり離れたりを繰り返さない)
            if (wallDist < 0f) { wallDist = allowed; wallClearFor = 0f; }
            else if (allowed < wallDist)
            {
                wallClearFor = 0f;
                wallDist = Mathf.Lerp(wallDist, allowed, 1f - Mathf.Exp(-wallPullIn * dt));
            }
            else
            {
                wallClearFor += dt;
                if (wallClearFor >= wallReleaseDelay) wallDist = Mathf.Lerp(wallDist, allowed, 1f - Mathf.Exp(-wallRelease * dt));
            }
            return from + dir * Mathf.Min(wallDist, full);
        }

        /// <summary>
        /// 透かすモードの寄せる距離:注視点からカメラへの線が、床の上面(上向きの面)・天井の下面(下向きの面)に当たったら、
        /// 面からカメラの半径(0.35 × scale)だけ離れた所まで。立った面(壁・柱の側面)は無視する(透かすので寄せない)。
        /// 線はカメラの少し先まで伸ばし、面のすぐ手前にカメラが来るときも離す。
        /// </summary>
        float FloorCeilingLimit(Vector3 from, Vector3 dir, float full)
        {
            if (!pullOnFloors && !pullOnCeilings) return full;
            float gap = 0.35f * scale;
            int n = Physics.RaycastNonAlloc(from, dir, pullHits, full + gap * 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float allowed = full;
            for (int i = 0; i < n; i++)
            {
                var h = pullHits[i];
                float ny = h.normal.y;
                bool floor = ny >= floorNormalY && pullOnFloors, ceiling = ny <= -floorNormalY && pullOnCeilings;
                if (!floor && !ceiling) continue;
                float cos = Mathf.Max(0.2f, Mathf.Abs(Vector3.Dot(dir, h.normal)));
                allowed = Mathf.Min(allowed, h.distance - gap / cos);
            }
            return Mathf.Max(1f, allowed);
        }
    }
}
