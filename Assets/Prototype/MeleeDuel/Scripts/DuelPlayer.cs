using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機:移動・ロックオン・通常攻撃(2つの振りを交互にくり返す)・強攻撃(△+R1。右→左と左→右の薙ぎ払いを交互に)・
    /// 回避・回避斬り・ブースト(○で切り替え)・ブースト飛行(✗長押し。押すと自動でブーストオン)・ガード。
    /// 守りは回避が軸。回避は出だしが無敵で、避けた後もすぐ動ける(敵の隙に差し込める)。
    /// ガードは押している間ダメージを大きく減らすが、受けるたびに押し戻されて少し固まり、ゲージが尽きると崩れる。
    /// 赤く光る攻撃はガードできない。回避と攻撃はENを使う。ENが少しでもあれば出せ、足りない分はマイナスになり、
    /// 0を超えるまで回復を待つ(その間は回避も攻撃もできない)。
    /// </summary>
    public class DuelPlayer : DuelFighter
    {
        public static DuelPlayer I;

        [Header("移動")]
        public float runSpeed = 22f;
        public float lockedSpeed = 17f;
        public float guardMoveMul = 0.4f;
        [Tooltip("ブーストがオンの間に移動したときの速さ")]
        public float dashSpeed = 35f;
        [Tooltip("ブーストで移動している間に1秒あたり使うEN(止まっている間は使わない)")]
        public float dashCost = 30f;
        [Tooltip("✗長押しで飛んでいる間に1秒あたり使うEN")]
        public float flyCost = 25f;
        [Tooltip("飛ぶときの上昇の速さ")]
        public float flyRiseSpeed = 20f;
        [Tooltip("飛び始めの上昇の加速(小さいほど、ふわっと浮き上がる)")]
        public float flyRiseAccel = 45f;
        [Tooltip("空中で横の速さが変わる速さ(m/s毎秒)。小さいほど慣性が強く、スティックを離しても流れる")]
        public float airAccel = 35f;
        [Tooltip("空中で攻撃・怯みの間に、横の勢いが抜ける速さ(m/s毎秒)")]
        public float airDrag = 20f;

        [Header("回避")]
        public float dodgeDistance = 13f;
        public float dodgeDuration = 0.38f;
        [Tooltip("回避の出だしから、この秒数は攻撃が当たらない")]
        public float dodgeInvincible = 0.26f;
        public float dodgeCooldown = 0.12f;

        [Header("EN(回避と攻撃で使う。攻撃の量は攻撃ごと)")]
        public float maxEnergy = 150f;
        public float dodgeCost = 30f;
        public float energyRegen = 60f;
        [Tooltip("最後にENを使ってから、回復が始まるまでの秒数")]
        public float energyRegenDelay = 0.6f;

        [Header("ガード")]
        public float maxGuard = 100f;
        [Tooltip("ガードで受けたダメージの何割をHPに受けるか")]
        public float guardChip = 0.1f;
        public float guardStun = 0.28f;
        public float guardPush = 12f;
        public float guardBreakTime = 1.2f;
        public float guardRegen = 30f;
        [Tooltip("最後にガードで受けてから、ゲージが戻り始めるまでの秒数")]
        public float guardRegenDelay = 0.8f;

        [Header("攻撃")]
        public float attackBuffer = 0.25f;
        [Tooltip("振り抜いてから(戻りに入ってから)この秒数は、回避で取り消せない")]
        public float dodgeLockAfterSwing = 0.1f;
        [Tooltip("R1を押してからこの秒数のうちに△を押せば、同時押しとみなして強攻撃にする")]
        public float heavyChordWindow = 0.1f;
        [Tooltip("回避のこの割合を過ぎてから、または回避が終わってこの秒数の間に攻撃すると回避斬りになる")]
        [Range(0f, 1f)] public float dodgeSlashFrom = 0.4f;
        public float dodgeSlashWindow = 0.2f;

        [Header("怯みゲージ(敵と同じ作り。いっぱいになるまでは攻撃を受けても怯まない)")]
        public float maxStagger = 25f;
        public float staggerDecay = 3f;
        [Tooltip("最後に当てられてから、ゲージが減り始めるまでの秒数")]
        public float staggerDecayDelay = 2f;

        public enum State { Free, Attack, Dodge, Guard, GuardStun, Hurt, GuardBroken, Dead }
        public State Current { get; private set; }
        public float Guard { get; private set; }
        /// <summary>EN。回避で足りない分はマイナスになる。</summary>
        public float Energy { get; private set; }
        public float Stagger { get; private set; }
        /// <summary>ENが0以下(回復待ち)。この間は回避も攻撃もできない。</summary>
        public bool Overheated => Energy <= 0f;
        public DuelEnemy Target { get; private set; }
        public bool Guarding => Current is State.Guard or State.GuardStun;
        /// <summary>ブーストがオン(○で切り替え。ENが0以下になると自動でオフ)。</summary>
        public bool BoostOn { get; private set; }
        /// <summary>このフレームはブーストで移動している(見た目用)。</summary>
        public bool Dashing { get; private set; }

        /// <summary>
        /// 攻撃1種類の中身。stagger は敵の怯みゲージへの量。
        /// pose0〜3 は斬撃クリップの再生位置(振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり / 戻りの終わり)。
        /// </summary>
        struct AttackMove
        {
            public float windup, active, recover, damage, stagger, flinch, reach, arc, lunge, knock, cost;
            public bool heavy;
            /// <summary>振りかぶりの出だしに△が押されたら強攻撃に切り替わる(通常攻撃だけ。同時押しのずれを許すため)。</summary>
            public bool chargeable;
            public float pose0, pose1, pose2, pose3;
            /// <summary>機体全体の横のひねり(右が正):振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり。戻りで0へ戻る。</summary>
            public float twist0, twist1, twist2;
        }

        /// <summary>踏み込むのは振りかぶりのうち、この割合(前半)だけ。</summary>
        const float LungePart = 0.5f;

        const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;

        /// <summary>
        /// 通常攻撃:斬り下ろし(A)と、返す刀の切り返し(B)を交互にくり返す。
        /// B は斬撃クリップを振り抜いた後ろから逆に再生し、A の振りかぶりへそのままつながる。
        /// </summary>
        static readonly AttackMove[] Light =
        {
            new() { windup = 0.32f, active = 0.16f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 3f, knock = 3f, cost = 10f, chargeable = true,
                    pose0 = R * 0.5f, pose1 = R, pose2 = S, pose3 = S + (E - S) * 0.6f },
            new() { windup = 0.24f, active = 0.18f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 2.5f, knock = 3f, cost = 10f, chargeable = true,
                    pose0 = S + (E - S) * 0.6f, pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.5f },
        };
        /// <summary>回避斬り:回避の勢いのまま大きく踏み込み、広く薙ぐ。出が速い。この後は通常攻撃の切り返しへつながる。</summary>
        static readonly AttackMove DodgeSlash = new() { windup = 0.14f, active = 0.14f, recover = 0.5f, damage = 12f, stagger = 9f, flinch = 0.5f, reach = 9.5f, arc = 100f,
                                                        lunge = 6f, knock = 6f, cost = 10f, pose0 = R * 0.7f, pose1 = R, pose2 = S, pose3 = S + (E - S) * 0.6f };
        /// <summary>
        /// 強攻撃(△+R1):1段目は右から左、2段目は左から右への薙ぎ払い。続けて出すと交互にくり返す。
        /// 斬撃クリップに機体全体の横のひねりを重ねて、横に払う形にする。横に払うので扇は広い。
        /// </summary>
        static readonly AttackMove[] Heavy =
        {
            new() { windup = 0.4f, active = 0.14f, recover = 0.6f, damage = 24f, stagger = 8.4f, flinch = 0.7f, reach = 10f, arc = 110f, lunge = 5f, knock = 16f, cost = 25f, heavy = true,
                    pose0 = R * 0.3f, pose1 = R, pose2 = S, pose3 = S + (E - S) * 0.6f, twist0 = 10f, twist1 = 55f, twist2 = -55f },
            new() { windup = 0.32f, active = 0.14f, recover = 0.6f, damage = 24f, stagger = 8.4f, flinch = 0.7f, reach = 10f, arc = 110f, lunge = 4f, knock = 16f, cost = 25f, heavy = true,
                    pose0 = S + (E - S) * 0.6f, pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.5f, twist0 = -55f, twist1 = -60f, twist2 = 50f },
        };

        float stateT, stateDur;
        AttackMove cur;
        int phase;   // 攻撃:0=振りかぶり 1=振り抜き 2=戻り
        int combo;
        float energyUsedAt = -10f, lastStaggerHitAt = -10f;
        /// <summary>空中の横の速さ(慣性)。地上では毎フレームの速さと同じ。</summary>
        Vector3 airVel;
        bool flyHeldPrev;
        /// <summary>R1を押したときに△も押されていたか(強攻撃)。</summary>
        bool pressHeavy;
        /// <summary>今の強攻撃が何段目か(0=右→左 / 1=左→右)。</summary>
        int heavyStep;
        float lightPressedAt = -10f, dodgePressedAt = -10f, dodgeEndAt = -10f, lastGuardHitAt = -10f;
        Vector3 dodgeDir, attackDir;
        float stepPhase, idleT;

        public void Init(DuelGame g)
        {
            I = this;
            Setup(g, 100f);
            Guard = maxGuard;
            Energy = maxEnergy;
            gameObject.AddComponent<DuelThruster>().Init(this, rig);   // 飛行中の噴射炎
            if (rig != null && rig.swordL != null) rig.swordL.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            if (Current == State.Dead) { flying = false; Move(Vector3.zero, dt); return; }

            UpdateLock();
            ReadButtons(now);
            if (ProtoInput.BoostToggle) BoostOn = !BoostOn && Energy > 0f;
            // ✗:押したら自動でブーストオン。押している間は飛ぶ(ENを使い続け、0以下なら落ちる)
            bool flyHeld = ProtoInput.FlyHeld;
            if (flyHeld && !flyHeldPrev && Energy > 0f) BoostOn = true;
            flyHeldPrev = flyHeld;
            flying = flyHeld && Energy > 0f && Current is State.Free or State.Guard or State.Attack or State.Dodge;
            riseSpeed = flyRiseSpeed;
            riseAccel = flyRiseAccel;
            if (flying)
            {
                Energy -= flyCost * dt;
                energyUsedAt = now;
            }
            if (BoostOn && Energy <= 0f) BoostOn = false;   // ENが0以下になったら自動でオフ

            Vector2 mv = ProtoInput.Move;
            Vector3 camFwd = Flat(Camera.main.transform.forward);
            if (camFwd == Vector3.zero) camFwd = transform.forward;
            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
            Vector3 worldMove = Vector3.ClampMagnitude(camFwd * mv.y + camRight * mv.x, 1f);

            // ガードのゲージ:最後に受けてから少しして戻る(構えている間は半分の速さ)
            if (Current != State.GuardBroken && now - lastGuardHitAt >= guardRegenDelay)
                Guard = Mathf.Min(maxGuard, Guard + guardRegen * (Guarding ? 0.5f : 1f) * dt);

            if (now - lastStaggerHitAt >= staggerDecayDelay && Current != State.Hurt)
                Stagger = Mathf.Max(0f, Stagger - staggerDecay * dt);

            // EN:最後に使ってから少しして戻る(マイナスからでも同じ速さで戻る)
            if (now - energyUsedAt >= energyRegenDelay) Energy = Mathf.Min(maxEnergy, Energy + energyRegen * dt);

            Vector3 vel = Vector3.zero;
            Dashing = false;
            switch (Current)
            {
                case State.Free:
                case State.Guard:
                    // 回避 → 攻撃 → ガード の順に受け付ける
                    if (TryDodge(now, worldMove)) break;
                    if (TryAttack(now)) break;
                    bool wantGuard = ProtoInput.LeftHandHeld;
                    if (wantGuard != (Current == State.Guard)) Enter(wantGuard ? State.Guard : State.Free);
                    // ブースト:オンの間に移動すると速く走り、ENを使い続ける(ガード中は効かない)
                    Dashing = BoostOn && Current == State.Free && worldMove.sqrMagnitude > 0.04f && Energy > 0f;
                    float spd = Dashing ? dashSpeed : (Target != null ? lockedSpeed : runSpeed) * (Current == State.Guard ? guardMoveMul : 1f);
                    vel = worldMove * spd;
                    if (Dashing)
                    {
                        Energy -= dashCost * dt;
                        energyUsedAt = now;
                    }
                    // ロックオン中は敵を向いたまま(ブーストで動くときは進む方向へ傾くだけ)。それ以外のブーストは進む向きを向く
                    if (Target != null) Face(Target.transform.position - transform.position, 14f, dt);
                    else if (Dashing) Face(worldMove, 14f, dt);
                    else if (worldMove.sqrMagnitude > 0.01f) Face(worldMove, 12f, dt);
                    break;

                case State.Attack:
                    vel = UpdateAttack(dt, now, worldMove);
                    break;

                case State.Dodge:
                {
                    float t = Mathf.Clamp01(stateT / dodgeDuration);
                    vel = dodgeDir * (3f * dodgeDistance / dodgeDuration) * (1f - t) * (1f - t);
                    // 回避の後半に攻撃を押したら、回避斬りへ
                    if (stateT >= dodgeDuration * dodgeSlashFrom && now - lightPressedAt <= attackBuffer && !pressHeavy && !Overheated)
                    {
                        dodgeEndAt = now;
                        StartDodgeSlash();
                        break;
                    }
                    if (stateT >= dodgeDuration) { dodgeEndAt = now; Enter(State.Free); }
                    break;
                }

                case State.GuardStun:
                    if (stateT >= stateDur) Enter(ProtoInput.LeftHandHeld ? State.Guard : State.Free);
                    break;

                case State.Hurt:
                case State.GuardBroken:
                    if (stateT >= stateDur) Enter(State.Free);
                    break;
            }

            // 空中の慣性:横の速さは少しずつしか変わらない(地上はすぐ止まり、すぐ曲がる)
            if (Airborne)
            {
                if (Current == State.Dodge) airVel = vel;   // 回避はすぐその向きへ
                else if (Current is State.Free or State.Guard) airVel = Vector3.MoveTowards(airVel, vel, airAccel * dt);
                else airVel = Vector3.MoveTowards(airVel, vel, airDrag * dt);   // 攻撃・怯みの間は勢いがゆるやかに抜ける
                vel = airVel;
            }
            else airVel = vel;

            Move(vel, dt);
            Animate(dt, vel);
        }

        void ReadButtons(float now)
        {
            if (ProtoInput.Attack) { lightPressedAt = now; pressHeavy = ProtoInput.AimHeld; }
            if (ProtoInput.Dodge) dodgePressedAt = now;
        }

        void Enter(State s, float dur = 0f)
        {
            Current = s;
            stateT = 0f;
            stateDur = dur;
        }

        // ───────── ロックオン ─────────

        void UpdateLock()
        {
            if (Target != null && Target.Dead) Target = Nearest();   // 倒したら、残っている一番近い敵へ
            if (ProtoInput.LockToggle) Target = Target == null ? Nearest() : null;
            if (ProtoInput.SwitchTarget) Target = Nearest(Target);
            if (Target == null) { stickArmed = true; mouseFlick = 0f; return; }

            // ロックオン中:右スティックを左右へはじくと、その側の敵へ切り替える(一度戻すまで次は切り替えない)
            float sx = ProtoInput.RightStick.x;
            if (Mathf.Abs(sx) < 0.3f) stickArmed = true;
            else if (stickArmed && Mathf.Abs(sx) > 0.75f) { stickArmed = false; SwitchSide(Mathf.Sign(sx)); }
            // マウス:左右に大きく振ると同じように切り替える
            mouseFlick = mouseFlick * Mathf.Exp(-6f * Time.unscaledDeltaTime) + ProtoInput.MouseDelta.x;
            if (Mathf.Abs(mouseFlick) > 160f) { SwitchSide(Mathf.Sign(mouseFlick)); mouseFlick = 0f; }
        }

        bool stickArmed = true;
        float mouseFlick;

        /// <summary>今の敵から見て side(+1=右 / -1=左)の側にいる、向きの一番近い敵へ切り替える(いなければそのまま)。</summary>
        void SwitchSide(float side)
        {
            Vector3 toCur = Flat(Target.transform.position - transform.position);
            DuelEnemy best = null;
            float bestAngle = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == Target) continue;
                float a = Vector3.SignedAngle(toCur, Flat(e.transform.position - transform.position), Vector3.up);
                if (a * side <= 0f) continue;
                if (Mathf.Abs(a) < bestAngle) { bestAngle = Mathf.Abs(a); best = e; }
            }
            if (best != null) Target = best;
        }

        /// <summary>一番近い敵(except は除く。ほかにいなければ except のまま)。</summary>
        DuelEnemy Nearest(DuelEnemy except = null)
        {
            DuelEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == except) continue;
                float d = (e.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best != null ? best : except;
        }

        /// <summary>敵が出てきたとき、ロックオンしていなければその敵に合わせる。</summary>
        public void AutoLock(DuelEnemy e)
        {
            if (Target == null || Target.Dead) Target = e;
        }

        // ───────── 回避 ─────────

        bool TryDodge(float now, Vector3 worldMove)
        {
            if (now - dodgePressedAt > attackBuffer || now - dodgeEndAt < dodgeCooldown) return false;
            if (Overheated) return false;   // ENが0以下の間は避けられない(押したことは少しの間覚えておく)
            dodgePressedAt = -10f;
            Energy -= dodgeCost;   // 足りなければマイナスになり、その分だけ回復を待つ
            energyUsedAt = now;
            dodgeDir = worldMove.sqrMagnitude > 0.01f ? worldMove.normalized : -transform.forward;
            Enter(State.Dodge);
            return true;
        }

        bool Invincible => Current == State.Dodge && stateT < dodgeInvincible;

        // ───────── 攻撃 ─────────

        bool TryAttack(float now)
        {
            if (now - lightPressedAt > attackBuffer) return false;
            if (Overheated) return false;   // ENが0以下の間は攻撃できない(押したことは少しの間覚えておく)
            if (pressHeavy) { StartHeavy(0); return true; }   // △+R1は回避の直後でも強攻撃
            // 回避が終わってすぐなら回避斬り
            if (now - dodgeEndAt <= dodgeSlashWindow) { StartDodgeSlash(); return true; }
            combo = 1;
            StartLight(Light[0]);
            return true;
        }

        void StartLight(AttackMove m)
        {
            lightPressedAt = -10f;
            StartAttack(m);
        }

        /// <summary>強攻撃の step 段目を始める(0=右→左 / 1=左→右)。この後の通常攻撃は斬り下ろしから。</summary>
        void StartHeavy(int step)
        {
            lightPressedAt = -10f;
            pressHeavy = false;
            heavyStep = step;
            combo = 0;
            StartAttack(Heavy[step]);
        }

        void StartDodgeSlash()
        {
            lightPressedAt = -10f;
            combo = 1;   // 次は切り返し(B)へつながる
            StartAttack(DodgeSlash);
        }

        /// <summary>
        /// 同時押しのずれを許す:通常攻撃の振りかぶりの出だし(heavyChordWindow 秒)に△が押されたら、強攻撃の1段目に切り替える
        /// (足りないENを足して払う)。
        /// </summary>
        bool TryChord()
        {
            if (!cur.chargeable || stateT > heavyChordWindow || !ProtoInput.AimHeld || Overheated) return false;
            float poseNow = Mathf.Lerp(cur.pose0, cur.pose1, Mathf.Clamp01(stateT / cur.windup));
            Energy -= Heavy[0].cost - cur.cost;
            energyUsedAt = Time.time;
            var h = Heavy[0];
            h.pose0 = Mathf.Min(poseNow, h.pose1);
            cur = h;
            heavyStep = 0;
            stateT = 0f;
            combo = 0;
            return true;
        }

        void StartAttack(AttackMove m)
        {
            Energy -= m.cost;   // 足りなければマイナスになり、その分だけ回復を待つ
            energyUsedAt = Time.time;
            cur = m;
            phase = 0;
            Enter(State.Attack);
            // 狙い:ロックオン中はその敵、なければ正面の近い敵、いなければ今の向き
            var aim = Target != null ? Target : SoftAim();
            attackDir = aim != null ? Flat(aim.transform.position - transform.position) : transform.forward;
        }

        DuelEnemy SoftAim()
        {
            DuelEnemy best = null;
            float bestD = 20f;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float d = GapTo(e);
                if (d < bestD && Vector3.Angle(transform.forward, e.transform.position - transform.position) < 100f) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>攻撃中の1フレーム。振りかぶりの間は少し踏み込み、振り抜いた瞬間に当たりを調べる。</summary>
        Vector3 UpdateAttack(float dt, float now, Vector3 worldMove)
        {
            Vector3 vel = Vector3.zero;
            switch (phase)
            {
                case 0:
                    // 振りかぶり:相手の方へ向き直りながら、前半だけ踏み込む(相手に密着していれば踏み込まない)
                    if (Target != null) attackDir = Flat(Target.transform.position - transform.position);
                    Face(attackDir, 20f, dt);
                    var near = Target != null ? Target : SoftAim();
                    float lungeTime = cur.windup * LungePart;
                    if (stateT < lungeTime && (near == null || GapTo(near) > 1.5f)) vel = attackDir * (cur.lunge / Mathf.Max(0.05f, lungeTime));
                    // R1とほぼ同時に△を押していたら強攻撃へ
                    TryChord();
                    // 振りかぶりの間は回避で取り消せる(当たり判定が出ている振り抜きの間だけは取り消せない)
                    if (TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= cur.windup) { phase = 1; stateT = 0f; Strike(); }
                    break;
                case 1:
                    if (stateT >= cur.active) { phase = 2; stateT = 0f; }
                    break;
                case 2:
                    // 戻り:振り抜いてから dodgeLockAfterSwing 秒たったら回避で取り消せる。少し戻ったら次の段・強攻撃へつなげる
                    if (stateT >= dodgeLockAfterSwing && TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= 0.08f && !Overheated && now - lightPressedAt <= attackBuffer)
                    {
                        // △+R1:強攻撃。強攻撃の後なら次の段(右→左と左→右を交互に)、それ以外は1段目
                        if (pressHeavy) { StartHeavy(cur.heavy ? 1 - heavyStep : 0); return Vector3.zero; }
                        // R1:通常攻撃の2つの振りを交互に(何回でも続く)。強攻撃の後は斬り下ろしから
                        combo = combo == 1 ? 2 : 1;
                        StartLight(Light[combo - 1]);
                        return Vector3.zero;
                    }
                    if (stateT >= cur.recover) { combo = 0; Enter(State.Free); }
                    break;
            }
            return vel;
        }

        /// <summary>振り抜いた瞬間:扇の中の敵に当てる。</summary>
        void Strike()
        {
            bool any = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if (TooFarVertically(e)) continue;   // 上下に離れすぎていれば届かない
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (GapTo(e) > cur.reach) continue;
                if (to.magnitude > Radius + e.Radius + 0.5f && Vector3.Angle(transform.forward, to) > cur.arc) continue;
                e.TakeHit(cur.damage, cur.stagger, Flat(to) * cur.knock, cur.flinch);
                // 当たった所(敵の胸の、こちらを向いた面)から火花。強攻撃は多く大きく
                Vector3 hitAt = e.Chest - Flat(to) * e.Radius;
                DuelSpark.Burst(hitAt, -Flat(to), new Color(1f, 0.88f, 0.55f), cur.heavy ? 26 : 14, cur.heavy ? 1.4f : 1f);
                ProtoFx.Blast(hitAt, cur.heavy ? 2f : 1.2f, Color.white, 0.08f);
                any = true;
            }
            if (any) game.HitStop(cur.heavy ? 0.12f : 0.06f);
        }

        // ───────── 被弾 ─────────

        /// <summary>敵の攻撃が当たった。回避の無敵中は当たらない。ガードできる攻撃を正面で受ければガード。</summary>
        public void ReceiveHit(DuelHit h)
        {
            if (Dead || Invincible) return;
            Vector3 away = Flat(transform.position - h.from);
            if (away == Vector3.zero) away = -transform.forward;
            bool front = Vector3.Angle(transform.forward, -away) < 110f;
            if (Guarding && h.guardable && front)
            {
                lastGuardHitAt = Time.time;
                Guard -= h.guardCost;
                HP -= h.damage * guardChip;
                ProtoDamageText.Show(Chest, h.damage * guardChip);
                Knockback(away * guardPush);
                if (Guard <= 0f)
                {
                    // ガードが崩れた:しばらく無防備。ゲージは半分まで戻してから再開
                    Guard = maxGuard * 0.5f;
                    StopRising();   // 空中で崩れたら上昇が止まり、そのまま落ちる
                    Flash(new Color(1f, 0.4f, 0.2f), 0.4f);
                    game.Log("ガードが崩れた");
                    Enter(State.GuardBroken, guardBreakTime);
                }
                else
                {
                    Flash(new Color(0.4f, 0.7f, 1f), 0.12f);
                    Enter(State.GuardStun, guardStun);
                }
                // ガード:受けた所から青白い火花
                Vector3 guardAt = Chest - away * Radius;
                DuelSpark.Burst(guardAt, -away, new Color(0.7f, 0.9f, 1f), 18, 1.1f);
                ProtoFx.Blast(guardAt, 1.6f, new Color(0.5f, 0.8f, 1f), 0.1f);
                Jolt(-4f);
                if (HP <= 0f) Die();
                return;
            }

            HP -= h.damage;
            ProtoDamageText.Show(Chest, h.damage);
            Flash(new Color(1f, 0.25f, 0.2f), 0.18f);
            Knockback(away * h.knock);
            Jolt(-8f);
            Vector3 hurtAt = Chest - away * Radius;
            DuelSpark.Burst(hurtAt, -away, new Color(1f, 0.55f, 0.25f), 16, 1.1f);
            game.HitStop(0.08f);
            if (HP <= 0f) { Die(); return; }
            // 怯みゲージ:いっぱいになったときだけ怯む(時間はいっぱいにした攻撃で決まる)。怯んでいる間はたまらない
            if (Current == State.Hurt) return;
            Stagger += h.stagger;
            lastStaggerHitAt = Time.time;
            if (Stagger < maxStagger) return;
            Stagger = 0f;
            combo = 0;
            StopRising();   // 空中で怯んだら上昇の勢いが止まり、怯んでいる間は落ちる(✗を押し続けていれば、怯みが終わるとまた上がる)
            Enter(State.Hurt, h.hurtTime);
        }

        void Die()
        {
            HP = 0f;
            // 空中で撃墜されたら、飛行と上昇の勢いを止めて落ちる
            flying = false;
            StopRising();
            BoostOn = false;
            Enter(State.Dead);
            PlayDeath();   // 火花 → 爆発 → 黒焦げ
            game.OnPlayerDied();
        }

        // ───────── 見た目 ─────────

        void Animate(float dt, Vector3 vel)
        {
            // 機体全体の姿勢:怯みはのけぞって沈みよろめく / ガードで受けたら少しのけぞる / ガードが崩れたら大きくのけぞって沈む
            float k = stateDur > 0f ? Mathf.Clamp01(stateT / stateDur) : 0f;
            float tLean = 0f, tTwist = 0f, tLift = 0f, tRoll = 0f, rate = 12f;
            if (Airborne)
            {
                // 空中:流れている方向へ傾く(速いほど大きく、最大15°)
                Vector3 local = transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z)) / Mathf.Max(1f, dashSpeed);
                tLean = Mathf.Clamp(local.z, -1f, 1f) * 15f;
                tRoll = Mathf.Clamp(local.x, -1f, 1f) * 15f;
                rate = 6f;
            }
            else if (Dashing && Target != null)
            {
                // ロックオン中のブースト移動:進む方向へ少し傾く(前後・左右)
                Vector3 local = transform.InverseTransformDirection(Flat(vel));
                tLean = local.z * 12f;
                tRoll = local.x * 12f;
                rate = 8f;
            }
            switch (Current)
            {
                case State.Attack:
                    // 攻撃ごとの横のひねり(強攻撃の薙ぎ払い)。戻りで0へ
                    tTwist = phase == 0 ? Mathf.Lerp(cur.twist0, cur.twist1, Mathf.Clamp01(stateT / cur.windup))
                           : phase == 1 ? Mathf.Lerp(cur.twist1, cur.twist2, Mathf.Clamp01(stateT / cur.active))
                           : Mathf.Lerp(cur.twist2, 0f, Mathf.Clamp01(stateT / cur.recover));
                    rate = 40f;
                    break;
                case State.Hurt:
                    FlinchPose(k, out tLean, out tTwist, out tLift);
                    rate = 30f;
                    break;
                case State.GuardStun:
                    tLean = -8f * (1f - k);
                    rate = 30f;
                    break;
                case State.GuardBroken:
                    FlinchPose(Mathf.Min(k * 0.6f, 1f), out tLean, out tTwist, out tLift);
                    tLift -= 0.6f * (1f - k);
                    rate = 20f;
                    break;
                case State.Dead:
                    tLean = -30f;
                    tLift = -1.5f;
                    rate = 6f;
                    break;
            }
            BodyOffset(tLean, tTwist, tLift, rate, dt, tRoll);

            const string clip = MechRig.Attack;
            switch (Current)
            {
                case State.Attack:
                    float t = phase == 0 ? Mathf.Lerp(cur.pose0, cur.pose1, Mathf.Clamp01(stateT / cur.windup))
                            : phase == 1 ? Mathf.Lerp(cur.pose1, cur.pose2, Mathf.Clamp01(stateT / cur.active))
                            : Mathf.Lerp(cur.pose2, cur.pose3, Mathf.Clamp01(stateT / cur.recover));
                    Pose(clip, t, 25f);
                    return;
                case State.Guard:
                case State.GuardStun:
                    // 仮:剣を振りかぶった途中の姿勢を、構えに見立てる
                    Pose(clip, MechRig.AttackRaised * 0.55f, 18f);
                    return;
                case State.Dodge:
                    Pose(MechRig.Dash, 0f, 15f);
                    return;
                case State.GuardBroken:
                case State.Hurt:
                case State.Dead:
                    Pose(MechRig.Idle, 0f, 10f);
                    return;
            }
            float sp = new Vector3(vel.x, 0f, vel.z).magnitude;
            if (Dashing || Altitude > 1.5f) { Pose(MechRig.Dash, 0f, 12f); return; }   // ブースト移動中・空中
            if (sp > 0.5f)
            {
                stepPhase += sp * dt / 6f;
                string walk = rig != null && rig.Has(MechRig.Run) && sp > 15f ? MechRig.Run : MechRig.Walk;
                Pose(walk, stepPhase * 0.5f * (rig != null ? rig.Length(walk) : 1f), 10f);
            }
            else
            {
                idleT += dt;
                Pose(MechRig.Idle, idleT, 6f);
            }
        }
    }
}
