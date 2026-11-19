using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作のカメラ。武装ハントのカメラ(電脳斬禅の人間サイズの値を機体の大きさへ拡大)を小さくしたもの。壁よけは avoidWalls でオン(ステージだけ)。
    /// ロックオン中は自機の肩越しに敵を見る。それ以外は右スティック・マウスで回す。画面の揺れは入れない。
    /// ロックオン中のカメラ(作者「敵が遠いとゆっくり、近いと速くカメラが追尾するようにするのはどうですか?」。仮の形):
    ///   敵へ向き直る速さを間合いで変える(近いは今までどおり速く、遠いほどゆっくり)。ロックオン中も右スティック・マウスで狙いをずらせ、
    ///   ずらした分は引き戻す(近いほど強く、遠いほど弱い)。敵が画面の端へ寄ったら向き直り・引き戻しを強めて見失わないようにする。
    ///   銃は画面の中央へ飛ぶ(DuelArms)ので、遠い敵には自分で狙いを合わせる。近接の向き合わせはカメラでなくロックオンの相手で決まる(DuelPlayer)。
    /// 射撃の構え(作者「あんまりエイムしてる感がないです」→ 撃っている間はカメラの追いかけを止める。仮の形):
    ///   狙って撃つ銃・砲のボタンを押している間と、撃ってから少しの間(DuelArms.Aiming)は、ロックオン中でも向き直り・引き戻しを止め、
    ///   右スティック・マウスだけで向く(ずらせる上限なし。感度は aimStick* / aimMouseSens)。ロックオンの相手は覚えたまま。
    ///   構え始めは今の画面をそのまま引き継ぎ(押した瞬間に画面が動かない)、終わったら aimReturnTime 秒でゆっくりロックオンのカメラへ戻る。
    ///   寄る・ズーム(aimPullIn / aimFovMul)は既定でオフ。反動でカメラを跳ね上げることはしない(照準の広がりだけで表す)。
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

        [Header("ロックオン中のカメラ(敵との距離で、向き直る速さと引き戻す強さを変える。値は仮)")]
        [Tooltip("敵との間合い(体と体の間。m)がこれまでなら「近い」:今までどおり速く向き直り、右スティックでずらしてもすぐ戻る(ソウルライク)")]
        public float lockNear = 10f;
        [Tooltip("間合いがこれから先なら「遠い」:ゆっくり向き直り、ずらした狙いが戻りにくい(自分で狙う)。近い〜遠いの間はなめらかにつなぐ")]
        public float lockFar = 40f;
        [Tooltip("近い敵へ向き直る速さ(大きいほど速い。前の値は 6)")]
        public float nearTurnRate = 6f;
        [Tooltip("遠い敵へ向き直る速さ(一番ゆっくり)")]
        public float farTurnRate = 1.2f;
        [Tooltip("右スティック・マウスでずらした狙いを戻す強さ:近い敵(大きいほど強く速く戻る)")]
        public float nearPull = 10f;
        [Tooltip("ずらした狙いを戻す強さ:遠い敵(小さいほど、ずらしたまま狙える)")]
        public float farPull = 0.6f;
        [Tooltip("ずらせる角度の上限(度。左右):近い敵 / 遠い敵")]
        public float nearMaxYaw = 6f, farMaxYaw = 35f;
        [Tooltip("ずらせる角度の上限(度。上下):近い敵 / 遠い敵")]
        public float nearMaxPitch = 4f, farMaxPitch = 20f;

        [Header("ロックオン中に見失わない(敵が画面の端へ寄ったら、向き直り・引き戻しを強める)")]
        [Tooltip("敵が画面の中央からこの角度(度)を超えたら強め始める(視野の縦の半分は 27.5°、横の半分は約 44°)")]
        public float edgeStartDeg = 14f;
        [Tooltip("この角度(度)で一番強くする")]
        public float edgeFullDeg = 24f;
        [Tooltip("一番強くしたときの向き直る速さ")]
        public float edgeTurnRate = 7f;
        [Tooltip("一番強くしたときの、ずらした狙いを戻す強さ")]
        public float edgePull = 8f;

        [Header("ロックオン中の右スティック・マウス(狙いをずらす)")]
        [Tooltip("オンなら、ロックオン中(撃っていないとき)も右スティック・マウスで狙いをずらせる。オフ(既定)ならずらせず、右スティックを左右に弾くとロックオンの相手を切り替える(DuelPlayer)。オンにすると両方が効いてしまうので、比べるときだけ")]
        public bool lockStickAims = false;
        [Tooltip("右スティックを倒しきったときの、左右・上下にずらす速さ(度/秒)。ロックオンしていないときは今までどおり 320 / 190")]
        public float lockStickYawSpeed = 140f, lockStickPitchSpeed = 90f;
        [Tooltip("マウスの1画素あたりにずらす角度(度)。ロックオンしていないときは 0.24")]
        public float lockMouseSens = 0.12f;

        [Header("射撃の構え(狙って撃つ銃・砲のボタンを押している間と、撃ってから少しの間。DuelArms.Aiming)")]
        [Tooltip("オンなら、構えの間はロックオンのカメラの向き直り・引き戻しを止め、右スティック・マウスだけで向く(ずらせる上限もなし)。" +
                 "オフなら今までのロックオンのカメラのまま(比べる用)")]
        public bool aimHoldsCamera = true;
        [Tooltip("構えの間に右スティックを倒しきったときの、左右・上下に回す速さ(度/秒)。自由なカメラは 320 / 190")]
        public float aimStickYawSpeed = 220f, aimStickPitchSpeed = 140f;
        [Tooltip("構えの間のマウスの1画素あたりの角度(度)。自由なカメラは 0.24")]
        public float aimMouseSens = 0.18f;
        [Tooltip("構えの間に見上げられる角度(度。マイナスが上)/ 見下ろせる角度")]
        public float aimMinPitch = -70f, aimMaxPitch = 60f;
        [Tooltip("ロックオンしていない自由なカメラで見上げられる角度(度。マイナスが上)。作者「もっとカメラは上を向けられるようにしてください」")]
        public float freeMinPitch = -55f;
        [Tooltip("構えの間、カメラが自機の動きに付いていく速さ(大きいほどぴったり付く)")]
        public float aimFollow = 14f;
        [Tooltip("構えが終わってから、ロックオンのカメラへ戻りきるまでの秒数(ゆっくり動き出して、ゆっくり止まる)。敵へ60°より大きく向き直るときは、その分長くなる")]
        public float aimReturnTime = 0.7f;
        [Tooltip("構えで自機へ寄る割合(0 でオフ。0.1 で1割寄る)。酔いやすいので控えめに")]
        [Range(0f, 0.4f)] public float aimPullIn = 0f;
        [Tooltip("構えで画角を狭める倍率(1 でオフ。0.9 で少しズーム)。酔いやすいので控えめに")]
        [Range(0.6f, 1f)] public float aimFovMul = 1f;
        [Tooltip("寄る・ズームに掛ける秒数(戻るときも同じ)")]
        public float aimZoomTime = 0.4f;

        float wallDist = -1f;
        // 構えのカメラ:向き(度)と、構え始めたときのカメラの位置(自機の胸から、カメラの向きで見た向き)。胸に付いていく点を中心に回す
        bool aimCam, aimReturning;
        float aimCamYaw, aimCamPitch, aimReturnT, aimReturnDur = 0.7f, zoomK;
        Vector3 aimOffset, aimAnchor, lastRawPos;
        Camera cam;
        float baseFov = -1f;
        bool fovChanged;
        // ロックオン中に右スティック・マウスでずらした狙い(度。左右は右が+、上下は上が+)。自機の胸を中心にカメラごと回す
        float aimYaw, aimPitch;
        // 遠さ(0=近い〜1=遠い)と、画面の端への寄り(0〜1)。急に切り替わらないよう、少しずつ追う
        float farK, edgeK;
        bool wasLocked;
        Vector3 lastShownPos, lastShownLook;

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
            aimYaw = aimPitch = 0f;
            farK = edgeK = 0f;
            wasLocked = false;
            aimCam = aimReturning = false;
            zoomK = 0f;
            ApplyZoom();
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

            // 射撃の構え:ロックオン中に構えたら、構えのカメラにする(構えている間にロックオンが外れても、構え終わるまで続ける)。
            // ロックオンしていない構えは自由なカメラのまま(感度だけ構えの値)
            bool aiming = p.Arms != null && p.Arms.Aiming;
            bool useAim = aimHoldsCamera && aiming && initialized && (enemy != null || aimCam);
            if (useAim && !aimCam) BeginAim(chest);
            else if (!useAim && aimCam)
            {
                // 構えが終わった:今の向きから、ロックオン(または自由)のカメラへなめらかに戻る
                aimCam = false;
                aimReturning = true;
                aimReturnT = 0f;
                // 大きく向きを変えて戻るときは、そのぶん時間を掛ける(60°ごとに aimReturnTime。後ろの敵へ 0.7 秒で振り向かないように)
                float turnDeg = 0f;
                if (enemy != null)
                {
                    Vector3 to = enemy.transform.position - p.transform.position;
                    to.y = 0f;
                    Vector3 f = transform.forward;
                    f.y = 0f;
                    if (to.sqrMagnitude > 0.01f && f.sqrMagnitude > 0.0001f) turnDeg = Vector3.Angle(f, to);
                }
                aimReturnDur = aimReturnTime * Mathf.Max(1f, turnDeg / 60f);
            }
            zoomK = Mathf.MoveTowards(zoomK, aiming && aimHoldsCamera ? 1f : 0f, dt / Mathf.Max(0.01f, aimZoomTime));

            Vector3 desired, look;
            float follow;
            if (enemy != null)
            {
                // 遠さと画面の端への寄りで、向き直る速さ・引き戻す強さ・ずらせる上限を決める(どれもなめらかに変わる)
                UpdateLockFactors(p, enemy, dt);
                float turn = Mathf.Lerp(nearTurnRate, farTurnRate, farK);
                turn = Mathf.Lerp(turn, Mathf.Max(turn, edgeTurnRate), edgeK);
                Vector3 to = enemy.transform.position - p.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) fwd = Vector3.Slerp(fwd, to.normalized, 1f - Mathf.Exp(-turn * dt));
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                desired = p.transform.position - fwd * Distance * s + Vector3.up * Height * s + right * Shoulder * s;
                look = Vector3.Lerp(chest, enemy.LockPoint, 0.6f) + Vector3.down * 0.35f * s;
                UpdateLockAim(dt, !aimCam);   // 構えの間は右スティック・マウスを構えのカメラが使う(隠れているロックオンのカメラは読まない)
                yaw = Quaternion.LookRotation(fwd).eulerAngles.y + aimYaw;
                pitch = Mathf.MoveTowards(pitch, DefaultPitch, 30f * dt);
                follow = Smooth;
            }
            else
            {
                if (wasLocked)
                {
                    // ロックオンを外したとき:ずらしていた狙いのまま自由なカメラへ引き継ぐ(画面が飛ばないように)
                    pos = lastShownPos;
                    lookPoint = lastShownLook;
                    pitch = Mathf.Clamp(pitch - aimPitch, MinPitch, MaxPitch);
                    aimYaw = aimPitch = 0f;
                    farK = edgeK = 0f;
                }
                if (aimCam)
                {
                    // 構えのカメラの後ろで、自由なカメラを構えの向きにそろえておく(構えが終わったら、その向きのまま自由なカメラへ)
                    yaw = aimCamYaw;
                    pitch = Mathf.Clamp(aimCamPitch, MinPitch, MaxPitch);
                }
                else
                {
                    Vector2 stick = ProtoInput.RightStick;
                    Vector2 m = ProtoInput.MouseDelta;
                    float ys = aiming ? aimStickYawSpeed : StickYawSpeed, ps = aiming ? aimStickPitchSpeed : StickPitchSpeed, ms = aiming ? aimMouseSens : MouseSens;
                    yaw += stick.x * ys * dt + m.x * ms;
                    pitch = Mathf.Clamp(pitch - stick.y * ps * dt - m.y * ms, MinPitch, MaxPitch);
                }
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
            Vector3 camPos = pos, camLook = lookPoint;
            if (enemy != null)
            {
                // ずらした狙い:自機の胸を中心に、カメラと注視点をまとめて回す(向きがちょうどその角度だけ変わる)。補間の後に掛けて、操作にすぐ応える
                if (aimYaw != 0f || aimPitch != 0f)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    Quaternion q = Quaternion.AngleAxis(aimYaw, Vector3.up) * Quaternion.AngleAxis(-aimPitch, right);
                    camPos = chest + q * (pos - chest);
                    camLook = chest + q * (lookPoint - chest);
                    camPos.y = Mathf.Max(camPos.y, p.transform.position.y + 1.5f);
                }
                lastShownPos = camPos;
                lastShownLook = camLook;
            }
            wasLocked = enemy != null;

            // 構えのカメラ(構えている間は1、戻る途中は1→0):胸に付いていく点を中心に、構えの向きで回したカメラ
            float blend = 0f;
            Quaternion aimRot = Quaternion.identity;
            Vector3 raw = camPos;
            if (aimCam || aimReturning)
            {
                if (aimCam)
                {
                    Vector2 stick = ProtoInput.RightStick;
                    Vector2 m = ProtoInput.MouseDelta;
                    aimCamYaw += stick.x * aimStickYawSpeed * dt + m.x * aimMouseSens;
                    aimCamPitch = Mathf.Clamp(aimCamPitch - stick.y * aimStickPitchSpeed * dt - m.y * aimMouseSens, aimMinPitch, aimMaxPitch);
                    blend = 1f;
                }
                else
                {
                    aimReturnT += dt / Mathf.Max(0.01f, aimReturnDur);
                    if (aimReturnT >= 1f) aimReturning = false;
                    blend = 1f - Mathf.SmoothStep(0f, 1f, aimReturnT);
                }
                aimAnchor = Vector3.Lerp(aimAnchor, chest, 1f - Mathf.Exp(-aimFollow * dt));
                aimRot = Quaternion.Euler(aimCamPitch, aimCamYaw, 0f);
                Vector3 aimPos = aimAnchor + aimRot * (aimOffset * PullIn);
                aimPos.y = Mathf.Max(aimPos.y, p.transform.position.y + 1.5f);
                // 胸の周りを回って戻る(まっすぐ結ぶと、敵が後ろにいるときに機体を突き抜けるので)
                if (blend > 0f) raw = chest + Vector3.Slerp(camPos - chest, aimPos - chest, blend);
            }
            lastRawPos = raw;
            Vector3 shown = avoidWalls ? AvoidWalls(chest, raw, dt) : raw;
            transform.position = shown;
            Vector3 lookDir = camLook - shown;
            Quaternion rot = lookDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDir) : transform.rotation;
            transform.rotation = blend > 0f ? Quaternion.Slerp(rot, aimRot, blend) : rot;
            ApplyZoom();
        }

        /// <summary>構えで寄る割合を掛けた、カメラの位置の倍率(1 = 寄らない)。</summary>
        float PullIn => 1f - aimPullIn * Mathf.SmoothStep(0f, 1f, zoomK);

        /// <summary>
        /// 構えのカメラを始める:今の画面の向きと位置をそのまま引き継ぐ(押した瞬間に画面が動かないように)。
        /// 位置は胸からの向き(カメラの向きで見た向き)で覚え、以後は右スティック・マウスでその向きごと胸の周りを回す。
        /// </summary>
        void BeginAim(Vector3 chest)
        {
            aimCam = true;
            aimReturning = false;
            Vector3 e = transform.rotation.eulerAngles;
            aimCamYaw = e.y;
            aimCamPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), aimMinPitch, aimMaxPitch);
            var r = Quaternion.Euler(aimCamPitch, aimCamYaw, 0f);
            aimOffset = Quaternion.Inverse(r) * (lastRawPos - chest) / Mathf.Max(0.1f, PullIn);
            aimAnchor = chest;
            // 隠れているロックオンのカメラのずらしは捨てる(戻るときは、敵へ向いたロックオンのカメラへ戻る)
            aimYaw = aimPitch = 0f;
        }

        /// <summary>構えのズーム(画角)を掛ける。倍率が 1 の間は画角に触らない(インスペクターで変えた画角を上書きしない)。</summary>
        void ApplyZoom()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            float mul = Mathf.Lerp(1f, aimFovMul, Mathf.SmoothStep(0f, 1f, zoomK));
            if (!fovChanged) baseFov = cam.fieldOfView;
            if (Mathf.Abs(mul - 1f) < 0.0001f)
            {
                if (fovChanged) cam.fieldOfView = baseFov;
                fovChanged = false;
                return;
            }
            cam.fieldOfView = baseFov * mul;
            fovChanged = true;
        }

        /// <summary>
        /// ロックオン中の遠さ(farK。0=近い〜1=遠い)と、画面の端への寄り(edgeK。0〜1)を求める。
        /// どちらも急に変わらないよう少しずつ追う(相手を切り替えて距離が飛んでも、速さがカクッと変わらない)。
        /// 端への寄りは、前のフレームのカメラの向きと敵の印の向きの角度で見る。
        /// </summary>
        void UpdateLockFactors(DuelPlayer p, DuelEnemy enemy, float dt)
        {
            float gap = Vector3.Distance(p.LockPoint, enemy.LockPoint) - p.Radius - enemy.Radius;
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lockNear, Mathf.Max(lockNear + 0.01f, lockFar), gap));
            float off = initialized ? Vector3.Angle(transform.forward, enemy.LockPoint - transform.position) : 0f;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edgeStartDeg, Mathf.Max(edgeStartDeg + 0.01f, edgeFullDeg), off));
            if (!wasLocked) { farK = far; edgeK = edge; return; }
            farK = Mathf.Lerp(farK, far, 1f - Mathf.Exp(-4f * dt));
            edgeK = Mathf.Lerp(edgeK, edge, 1f - Mathf.Exp(-10f * dt));
        }

        /// <summary>
        /// ロックオン中の右スティック・マウスで狙いをずらす。ずらした分は毎フレーム引き戻す(近い敵ほど強く、遠い敵ほど弱く。画面の端へ寄るほど強く)。
        /// ずらせる上限も近い敵ほど小さい。上限は遠さに合わせてなめらかに動く。
        /// </summary>
        void UpdateLockAim(float dt, bool readInput)
        {
            if (readInput && lockStickAims)   // 既定はオフ:撃っていないロックオン中は右スティックで相手を切り替える(作者「ロックオン中に銃を撃たずに動かせるのは消して」)
            {
                Vector2 stick = ProtoInput.RightStick;
                Vector2 m = ProtoInput.MouseDelta;
                aimYaw += stick.x * lockStickYawSpeed * dt + m.x * lockMouseSens;
                aimPitch += stick.y * lockStickPitchSpeed * dt + m.y * lockMouseSens;
            }
            float pull = Mathf.Lerp(nearPull, farPull, farK);
            pull = Mathf.Lerp(pull, Mathf.Max(pull, edgePull), edgeK);
            float keep = Mathf.Exp(-pull * dt);
            float maxYaw = Mathf.Lerp(nearMaxYaw, farMaxYaw, farK), maxPitch = Mathf.Lerp(nearMaxPitch, farMaxPitch, farK);
            aimYaw = Mathf.Clamp(aimYaw * keep, -maxYaw, maxYaw);
            aimPitch = Mathf.Clamp(aimPitch * keep, -maxPitch, maxPitch);
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
