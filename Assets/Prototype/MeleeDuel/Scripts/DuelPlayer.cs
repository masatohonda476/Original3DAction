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
    /// 部屋のモード:R1 で剣(素の剣の技)、L1 でガード(下の値)。
    /// ステージ(武装あり、DuelArms):R1 / L1 はその手の武装を使う(剣なら攻撃、盾なら押している間ガード、手の銃なら撃つ)。
    ///   技は剣ごとの表(DuelWeapon.moves → DuelMoveSet)。強攻撃(△+R1/L1)・回避斬りは押したボタンの手の剣で出す。
    ///   弾倉のある銃のボタンでは △+ボタンはリロード(DuelArms.TryManualReload。剣の手のボタンは横取りしない)。
    ///   ガードの値は構えている盾のもの。盾がなければガードできない。近接攻撃の間と回避の間は撃てない(CanShoot)。
    ///   両手の近接:R1 で連撃、L1 で別の技(回避の後も R1 は回避斬り、L1 は別の技)、△+R1/L1 で強攻撃。右手のクリップで振り、左右反転しない。
    ///   両手の銃・砲は DuelArms が撃つ(R1 が主の撃ち方、L1 が別の撃ち方)。両手持ちには盾がないのでガードできない。
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

        [Header("ガード(部屋のモードの値。ステージでは盾の値を使う)")]
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
        /// <summary>EN。回避・攻撃・エネルギー武器の射撃で足りない分はマイナスになる。</summary>
        public float Energy { get; private set; }
        public float Stagger { get; private set; }
        /// <summary>ENが0以下(回復待ち)。この間は回避も攻撃も、エネルギー武器の射撃もできない。</summary>
        public bool Overheated => Energy <= 0f;
        public DuelEnemy Target { get; private set; }
        public bool Guarding => Current is State.Guard or State.GuardStun;
        /// <summary>ブーストがオン(○で切り替え。ENが0以下になると自動でオフ)。</summary>
        public bool BoostOn { get; private set; }
        /// <summary>このフレームはブーストで移動している(見た目用)。</summary>
        public bool Dashing { get; private set; }
        /// <summary>噴射炎:飛行中・ブースト移動中・回避中。</summary>
        public override bool Thrusting => flying || Dashing || Current == State.Dodge;
        /// <summary>武装(ステージだけ。部屋のモードでは null)。強くなるのは武装だけで、素の性能(HP・EN・怯み)は変えない。</summary>
        public DuelArms Arms { get; private set; }
        /// <summary>手の銃・肩の武装を撃てる状態か(近接攻撃の振りかぶり〜戻り・回避・怯み・ガード崩れ・撃墜の間は撃てない)。</summary>
        public bool CanShoot => Current is State.Free or State.Guard;
        /// <summary>
        /// ガードのゲージの最大(部屋のモードは maxGuard、ステージは持っている盾のうち大きいほう。盾がなければ0)。
        /// 盾が2枚でもゲージは1本(作者に未確認の仮)。削れ方・通す傷などは構えている盾(guardShield)の値。
        /// </summary>
        public float MaxGuard => Arms != null ? Arms.GuardMax : maxGuard;

        // 背中の武装(スラスター)・盾・重さを掛けた値。素の値はインスペクターの値のまま
        // 重さ(DuelArms.Mobility)はENを使う移動(ブースト移動・回避の距離・飛ぶときの上昇)だけに掛かり、歩き・走り(RunMul)には掛からない
        float RunMul => Arms != null ? Arms.RunMul : 1f;
        float DashMul => Arms != null ? Arms.DashMul : 1f;
        float EnCostMul => Arms != null ? Arms.EnCostMul : 1f;
        float DodgeDistance => dodgeDistance * (Arms != null ? Arms.DodgeMul : 1f);
        float DashSpeed => dashSpeed * DashMul;

        /// <summary>踏み込むのは振りかぶりのうち、この割合(前半)だけ。</summary>
        const float LungePart = 0.5f;

        float stateT, stateDur;
        DuelSwing cur;
        /// <summary>今の攻撃を振っている手(0=右 / 1=左)と、攻撃ボタンを押した手。</summary>
        int atkHand, pressHand;
        /// <summary>今の攻撃を左右反転して振るか(左手の片手の剣。両手の近接は反転しない)。</summary>
        bool atkMirror;
        /// <summary>構えている盾(部屋のモードは null で、素のガードの値)と、その手(0=右 / 1=左)。</summary>
        DuelWeapon guardShield;
        int guardHand = 1;
        int phase;   // 攻撃:0=振りかぶり 1=振り抜き 2=戻り
        int combo;
        float energyUsedAt = -10f, lastStaggerHitAt = -10f;
        /// <summary>空中の横の速さ(慣性)。地上では毎フレームの速さと同じ。</summary>
        Vector3 airVel;
        bool flyHeldPrev;
        /// <summary>R1を押したときに△も押されていたか(強攻撃)。</summary>
        bool pressHeavy;
        /// <summary>今の強攻撃が何段目か(素の剣は 0=右→左 / 1=左→右)。</summary>
        int heavyStep;
        float lightPressedAt = -10f, dodgePressedAt = -10f, dodgeEndAt = -10f, lastGuardHitAt = -10f;
        Vector3 dodgeDir, attackDir;
        float stepPhase;

        public void Init(DuelGame g)
        {
            I = this;
            Setup(g, 100f);
            Guard = maxGuard;
            Energy = maxEnergy;
            gameObject.AddComponent<DuelThruster>().Init(this, rig);   // 飛行中の噴射炎
            if (rig != null && rig.swordL != null) rig.swordL.gameObject.SetActive(false);
            if (g.stage != null)
            {
                // ステージだけ:敵が落とす武装を拾って付けられる(最初は右手に素の剣、左手は空)
                Arms = gameObject.AddComponent<DuelArms>();
                Arms.Init(this, g, rig);
                Guard = MaxGuard;
            }
        }

        /// <summary>
        /// 盾を付けた・外した(DuelArms から)。新しい盾を付けたらゲージを満タンに、外したら最大までに切る。
        /// 構えている盾はその場で引き直す(構えていた手にまだ盾があればそれ、なければもう片方の手の盾)。
        /// 盾がなくなったらガード(受けて固まっている間も)を解く。
        /// </summary>
        public void OnShieldsChanged(bool refill)
        {
            Guard = refill ? MaxGuard : Mathf.Min(Guard, MaxGuard);
            if (Arms == null) return;
            guardShield = null;
            if (Arms.Hand(guardHand) is { kind: DuelKind.Shield } same) guardShield = same;
            else if (Arms.Hand(1 - guardHand) is { kind: DuelKind.Shield } other)
            {
                guardShield = other;
                guardHand = 1 - guardHand;
            }
            if (Guarding && guardShield == null) Enter(State.Free);
        }

        /// <summary>
        /// その手(0=右 / 1=左)の剣を捨てた(DuelArms.Drop。装備の確認の画面から)。その手で振っている途中なら振りをやめ
        /// (剣のない手の振りで当たらないように)、その手で押して覚えている攻撃も忘れる。
        /// 剣のない手では、通常攻撃・強攻撃・回避斬り・△との同時押しは出ない(TryAttack・TryChord などが SwordIn で止める)。
        /// </summary>
        public void OnSwordDropped(int hand)
        {
            if (pressHand == hand) lightPressedAt = -10f;
            if (Current == State.Attack && atkHand == hand)
            {
                combo = 0;
                Enter(State.Free);
            }
        }

        /// <summary>撃墜からのやり直し:その場所に、HP・EN・ガードを満タンにして出し直す(ロックオンは外す)。</summary>
        public void Respawn(Vector3 pos, Quaternion rot)
        {
            Revive();
            Teleport(pos, rot);
            Guard = MaxGuard;
            Energy = maxEnergy;
            Stagger = 0f;
            BoostOn = false;
            combo = 0;
            airVel = Vector3.zero;
            Target = null;
            stepTravel = 0f;
            FootstepRadius = 0f;
            Enter(State.Free);
        }

        /// <summary>
        /// ENを使う(エネルギー武器の射撃。DuelArms から)。残りが0以下かは呼ぶ側が Overheated で確かめる。
        /// 足りない分はマイナスになり、回避・攻撃と同じく回復を待つ。
        /// </summary>
        public void SpendEnergy(float amount)
        {
            if (amount <= 0f) return;
            Energy -= amount;
            energyUsedAt = Time.time;
        }

        /// <summary>HP を amount だけ回復する(武装の分解。最大まで)。撃墜中は何もしない。</summary>
        public void Heal(float amount)
        {
            if (Dead || amount <= 0f) return;
            HP = Mathf.Min(MaxHP, HP + amount);
        }

        /// <summary>HPとガードを満タンにする(セーブポイント)。</summary>
        public void RestoreHP()
        {
            if (Dead) return;
            HP = MaxHP;
            Guard = MaxGuard;
        }

        // ───────── 足音(敵が聞く。音の大きさの設定には関係なく、動いた距離から出す) ─────────

        /// <summary>一歩ごとに増える番号(敵は変わったときだけ聞く)・鳴った所・届く範囲(m)・鳴った時刻。</summary>
        public int FootstepSequence { get; private set; }
        public Vector3 FootstepPosition { get; private set; }
        public float FootstepRadius { get; private set; }
        public float FootstepTime { get; private set; } = -100f;
        float stepTravel;

        /// <summary>
        /// 動いた距離から足音を出す(ステージだけ)。歩き・走りはスティックの倒し具合(速さ)で届く範囲が変わり、
        /// ブースト移動・回避・飛行は遠くまで届く。飛行は止まって浮いていても鳴る。空中を流れているだけ・押されただけ・瞬間移動は数えない。
        /// </summary>
        void RecordFootsteps(Vector3 before, float dt, bool moveInput)
        {
            var s = game.stage != null ? game.stage.senses : null;
            if (s == null || dt <= 0f) return;
            Vector3 d = transform.position - before;
            float flat = new Vector2(d.x, d.z).magnitude;
            bool dodge = Current == State.Dodge;
            bool walking = (Current is State.Free or State.Guard) && moveInput && !Airborne;
            if (d.magnitude > Mathf.Max(6f, dt * 80f) || !(flying || dodge || walking))
            {
                stepTravel = 0f;
                return;
            }
            if (!flying && flat / dt < 0.5f) { stepTravel = 0f; return; }
            bool hard = flying || dodge || Dashing;
            stepTravel += flying ? Mathf.Max(d.magnitude, 12f * dt) : flat;
            if (stepTravel < (hard ? s.strideHard : s.strideWalk)) return;
            stepTravel = 0f;
            FootstepSequence++;
            FootstepPosition = transform.position;
            FootstepTime = Time.time;
            FootstepRadius = flying ? s.flyStep : dodge ? s.dodgeStep : Dashing ? s.boostStep
                : Mathf.Lerp(s.walkMin, s.walkMax, Mathf.InverseLerp(0.15f, 1f, flat / dt / runSpeed));
        }

        void Update()
        {
            if (DuelGame.Paused) return;   // 装備の確認を開いている間は止める
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            if (Arms != null) Arms.Tick();   // 武装(拾う・付け先を選ぶ・撃つ)。ボタンの横取りを自機が読むより先に決める
            if (Current == State.Dead) { flying = false; Move(Vector3.zero, dt); return; }

            UpdateLock();
            ReadButtons(now);
            if (ProtoInput.BoostToggle) BoostOn = !BoostOn && Energy > 0f;
            // ✗:押したら自動でブーストオン。押している間は飛ぶ(ENを使い続け、0以下なら落ちる)
            // 付け先を選ぶ表示の間と、それを終えた時に押していた ✗ を離すまでは飛ばない(✗ は背中に付けるボタンでもある。DuelArms.FlyBlocked)
            bool flyHeld = ProtoInput.FlyHeld && (Arms == null || !Arms.FlyBlocked);
            if (flyHeld && !flyHeldPrev && Energy > 0f) BoostOn = true;
            flyHeldPrev = flyHeld;
            flying = flyHeld && Energy > 0f && Current is State.Free or State.Guard or State.Attack or State.Dodge;
            riseSpeed = flyRiseSpeed * DashMul;
            riseAccel = flyRiseAccel;
            if (flying)
            {
                Energy -= flyCost * EnCostMul * dt;
                energyUsedAt = now;
            }
            if (BoostOn && Energy <= 0f) BoostOn = false;   // ENが0以下になったら自動でオフ

            Vector2 mv = ProtoInput.Move;
            Vector3 camFwd = Flat(Camera.main.transform.forward);
            if (camFwd == Vector3.zero) camFwd = transform.forward;
            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
            Vector3 worldMove = Vector3.ClampMagnitude(camFwd * mv.y + camRight * mv.x, 1f);

            // ガードのゲージ:最後に受けてから少しして戻る(構えている間は半分の速さ)
            Guard = Mathf.Min(Guard, MaxGuard);
            if (Current != State.GuardBroken && now - lastGuardHitAt >= guardRegenDelay)
                Guard = Mathf.Min(MaxGuard, Guard + guardRegen * (Guarding ? 0.5f : 1f) * dt);

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
                    bool wantGuard = WantGuard();
                    if (wantGuard != (Current == State.Guard)) Enter(wantGuard ? State.Guard : State.Free);
                    // ブースト:オンの間に移動すると速く走り、ENを使い続ける(ガード中は効かない)
                    Dashing = BoostOn && Current == State.Free && worldMove.sqrMagnitude > 0.04f && Energy > 0f;
                    float spd = Dashing ? DashSpeed : (Target != null ? lockedSpeed : runSpeed) * RunMul * (Current == State.Guard ? GuardMoveMul : 1f);
                    vel = worldMove * spd;
                    if (Dashing)
                    {
                        Energy -= dashCost * EnCostMul * dt;
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
                    vel = dodgeDir * (3f * DodgeDistance / dodgeDuration) * (1f - t) * (1f - t);
                    // 回避の後半に攻撃を押したら、回避斬りへ
                    if (stateT >= dodgeDuration * dodgeSlashFrom && now - lightPressedAt <= attackBuffer && !pressHeavy && !Overheated && SwordIn(pressHand) != null)
                    {
                        dodgeEndAt = now;
                        atkHand = pressHand;   // 押したボタンの手の剣で斬る
                        StartDodgeSlash();
                        break;
                    }
                    if (stateT >= dodgeDuration) { dodgeEndAt = now; Enter(State.Free); }
                    break;
                }

                case State.GuardStun:
                    if (stateT >= stateDur) Enter(WantGuard() ? State.Guard : State.Free);
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

            Vector3 before = transform.position;
            Move(vel, dt);
            RecordFootsteps(before, dt, worldMove.sqrMagnitude > 0.02f);
            Animate(dt, vel);
        }

        void ReadButtons(float now)
        {
            // 攻撃:部屋のモードは R1(素の剣)。ステージは剣を持つ手のボタン(R1=右手 / L1=左手。付け先を選んでいる間は数えない)
            if (Arms == null)
            {
                if (ProtoInput.Attack) PressSword(0, now);
            }
            else
            {
                if (Arms.IsSword(0) && Arms.Pressed(DuelSlot.RightHand)) PressSword(0, now);
                if (Arms.IsSword(1) && Arms.Pressed(DuelSlot.LeftHand)) PressSword(1, now);
            }
            if (ProtoInput.Dodge) dodgePressedAt = now;
        }

        /// <summary>先行入力(押してからの受付時間内の攻撃・回避)を捨てる。装備の確認を開いたとき(閉じた直後に出ないように)。</summary>
        public void ClearBufferedInput()
        {
            lightPressedAt = -10f;
            dodgePressedAt = -10f;
            pressHeavy = false;
        }

        void PressSword(int hand, float now)
        {
            lightPressedAt = now;
            pressHeavy = ProtoInput.AimHeld;
            pressHand = hand;
        }

        // ───────── ガード ─────────

        /// <summary>
        /// ガードしたいか:部屋のモードは L1(素のガード)。ステージは盾を持つ手のボタンを押している間(構えている盾を覚える)。
        /// </summary>
        bool WantGuard()
        {
            if (Arms == null)
            {
                guardShield = null;
                guardHand = 1;
                return ProtoInput.LeftHandHeld;
            }
            if (!Arms.GuardHeld(out var shield, out int hand)) return false;
            guardShield = shield;
            guardHand = hand;
            return true;
        }

        float GuardChip => guardShield != null ? guardShield.guardChip : guardChip;
        float GuardCostMul => guardShield != null ? guardShield.guardCostMul : 1f;
        float GuardMoveMul => guardShield != null ? guardShield.guardMoveMul : guardMoveMul;
        float GuardStunMul => guardShield != null ? guardShield.guardStunMul : 1f;

        void Enter(State s, float dur = 0f)
        {
            Current = s;
            stateT = 0f;
            stateDur = dur;
        }

        // ───────── ロックオン ─────────

        /// <summary>ロックオンできる距離(ステージだけ。0 なら制限なし)。</summary>
        float LockRange => game.stage != null ? game.stage.lockRange : 0f;

        /// <summary>ロックオンしてよい敵か(ステージでは、ロックオンできる距離の内側で、壁・床ごしでない敵だけ)。</summary>
        bool Lockable(DuelEnemy e)
        {
            if (e == null || e.Dead) return false;
            float r = LockRange;
            if (r <= 0f) return true;
            return (e.transform.position - transform.position).sqrMagnitude <= r * r && DuelSenses.ClearSight(LockPoint, e.LockPoint);
        }

        void UpdateLock()
        {
            // ステージ:ロックオンできる距離の1.5倍より離れたら外す
            if (Target != null && LockRange > 0f && (Target.transform.position - transform.position).sqrMagnitude > LockRange * LockRange * 2.25f) Target = null;
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
                if (e == null || e.Dead || e == Target || !Lockable(e)) continue;
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
                if (d < bestD && Lockable(e)) { bestD = d; best = e; }
            }
            return best != null ? best : except != null && !except.Dead ? except : null;
        }

        /// <summary>敵が出てきた・こちらに気づいたとき、ロックオンしていなければその敵に合わせる。</summary>
        public void AutoLock(DuelEnemy e)
        {
            if ((Target == null || Target.Dead) && Lockable(e)) Target = e;
        }

        // ───────── 回避 ─────────

        bool TryDodge(float now, Vector3 worldMove)
        {
            if (now - dodgePressedAt > attackBuffer || now - dodgeEndAt < dodgeCooldown) return false;
            if (Overheated) return false;   // ENが0以下の間は避けられない(押したことは少しの間覚えておく)
            dodgePressedAt = -10f;
            Energy -= dodgeCost * EnCostMul;   // 足りなければマイナスになり、その分だけ回復を待つ
            energyUsedAt = now;
            dodgeDir = worldMove.sqrMagnitude > 0.01f ? worldMove.normalized : -transform.forward;
            Enter(State.Dodge);
            return true;
        }

        bool Invincible => Current == State.Dodge && stateT < dodgeInvincible;

        // ───────── 攻撃 ─────────

        /// <summary>その手(0=右 / 1=左)の剣(部屋のモードは右手が素の剣。剣でなければ null)。</summary>
        DuelWeapon SwordIn(int hand)
        {
            if (Arms == null) return hand == 0 ? DuelWeapon.BaseSword : null;
            var w = Arms.Hand(hand);
            return w != null && w.IsBlade ? w : null;   // 両手の近接は R1 でも L1 でも振る
        }

        /// <summary>
        /// その手(ボタン)の剣の技の表(剣がなければ素の剣の表)。両手の近接は、R1 が連撃、L1 が別の技(回避の後の L1 も別の技)。
        /// 強攻撃(△+R1/L1)はどちらのボタンでも同じ表。
        /// </summary>
        DuelSwing[] LightOf(int hand) => (SwordIn(hand) ?? DuelWeapon.BaseSword).LightFor(hand);
        DuelSwing[] HeavyOf(int hand) => (SwordIn(hand) ?? DuelWeapon.BaseSword).HeavyMoves;
        DuelSwing DodgeOf(int hand) => (SwordIn(hand) ?? DuelWeapon.BaseSword).DodgeFor(hand);

        bool TryAttack(float now)
        {
            if (now - lightPressedAt > attackBuffer) return false;
            if (Overheated) return false;   // ENが0以下の間は攻撃できない(押したことは少しの間覚えておく)
            if (SwordIn(pressHand) == null) { lightPressedAt = -10f; return false; }   // 押した後に剣を持ち替えた
            atkHand = pressHand;
            if (pressHeavy) { StartHeavy(0); return true; }   // △+攻撃は回避の直後でも強攻撃
            // 回避が終わってすぐなら回避斬り
            if (now - dodgeEndAt <= dodgeSlashWindow) { StartDodgeSlash(); return true; }
            combo = 1;
            StartLight(LightOf(atkHand)[0]);
            return true;
        }

        void StartLight(DuelSwing m)
        {
            lightPressedAt = -10f;
            StartAttack(m);
        }

        /// <summary>強攻撃の step 段目を始める(表の長さで回す)。この後の通常攻撃は連撃の1段目から。</summary>
        void StartHeavy(int step)
        {
            lightPressedAt = -10f;
            pressHeavy = false;
            var h = HeavyOf(atkHand);
            heavyStep = step % h.Length;
            combo = 0;
            StartAttack(h[heavyStep]);
        }

        void StartDodgeSlash()
        {
            lightPressedAt = -10f;
            combo = 1;   // 次は連撃の2段目へつながる
            StartAttack(DodgeOf(atkHand));
        }

        /// <summary>
        /// 同時押しのずれを許す:通常攻撃の振りかぶりの出だし(heavyChordWindow 秒)に△が押されたら、強攻撃の1段目に切り替える
        /// (足りないENを足して払う)。
        /// </summary>
        bool TryChord()
        {
            if (!cur.chargeable || stateT > heavyChordWindow || !ProtoInput.AimHeld || Overheated) return false;
            if (Arms != null && Time.time - Arms.ReloadChordAt <= heavyChordWindow) return false;   // その△は銃のリロードに使った(△+撃つボタン)
            if (SwordIn(atkHand) == null) return false;   // 振っている手に剣がない(素の剣の強攻撃に化けないように)
            float poseNow = Mathf.Lerp(cur.pose0, cur.pose1, Mathf.Clamp01(stateT / cur.windup));
            var h = Armed(HeavyOf(atkHand)[0], atkHand);
            Energy -= h.cost - cur.cost;
            energyUsedAt = Time.time;
            h.pose0 = Mathf.Min(poseNow, h.pose1);
            cur = h;
            heavyStep = 0;
            stateT = 0f;
            combo = 0;
            return true;
        }

        /// <summary>
        /// その手の剣の倍率を掛けた技:威力・怯ませ・届く範囲・吹き飛ばし・扇の広さ(片側180°まで)、振りの速さ(振りかぶり・振り抜き・戻りが短くなる)。
        /// 踏み込み・ENの量・姿勢は変えない(振り方の違いは技の表で出す)。
        /// </summary>
        DuelSwing Armed(DuelSwing m, int hand)
        {
            var w = SwordIn(hand);
            if (w == null) return m;
            m.damage *= w.damageMul;
            m.stagger *= w.staggerMul;
            m.reach *= w.reachMul;
            m.knock *= w.knockMul;
            m.arc = Mathf.Min(180f, m.arc + w.arcAdd);
            float sp = Mathf.Max(0.3f, w.speedMul);
            m.windup /= sp;
            m.active /= sp;
            m.recover /= sp;
            return m;
        }

        void StartAttack(DuelSwing m)
        {
            // 左手の片手の剣だけ左右反転して振る(両手の近接は L1 の技も右手のクリップのまま、両手で振る)
            atkMirror = atkHand == 1 && SwordIn(1) is not { IsTwoHand: true };
            m = Armed(m, atkHand);
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
                    // 攻撃とほぼ同時に△を押していたら強攻撃へ
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
                    if (stateT >= DuelSwing.ChainAfter && !Overheated && now - lightPressedAt <= attackBuffer && SwordIn(pressHand) != null)
                    {
                        // 押した手で次を振る(両手に剣なら、R1/L1 を交互に押すと左右の剣で交互に振る)
                        bool sameHand = pressHand == atkHand;
                        // 両手の近接は R1/L1 で同じ武装(強攻撃の段は、ボタンが替わっても続ける)
                        bool sameBlade = sameHand || (SwordIn(atkHand) is { IsTwoHand: true } tw && SwordIn(pressHand) == tw);
                        atkHand = pressHand;
                        // △+攻撃:強攻撃。同じ剣の強攻撃の後なら次の段、それ以外は1段目
                        if (pressHeavy) { StartHeavy(cur.heavy && sameBlade ? heavyStep + 1 : 0); return Vector3.zero; }
                        // 攻撃:連撃の表を順に(何回でも続く)。強攻撃の後・手が替わったら、その剣の1段目から
                        // (両手の近接は R1 の連撃と L1 の技が別の表なので、ボタンが替われば替えた側の1段目から)
                        var light = LightOf(atkHand);
                        if (!sameHand) combo = 0;
                        combo = combo % light.Length + 1;
                        StartLight(light[combo - 1]);
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
            bool any = false, big = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if (TooFarVertically(e)) continue;   // 上下に離れすぎていれば届かない
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (GapTo(e) > cur.reach) continue;
                if (to.magnitude > Radius + e.Radius + 0.5f && Vector3.Angle(transform.forward, to) > cur.arc) continue;
                bool wasBroken = e.Flinching;   // 崩れた敵を斬る(大ダメージ)
                e.TakeHit(cur.damage, cur.stagger, Flat(to) * cur.knock, cur.flinch);
                bool broke = !wasBroken && e.Flinching;   // この一太刀で崩した
                // 当たった所(敵の胸の、こちらを向いた面)から火花。強攻撃は多く大きく。崩した・崩れた敵を斬ったときはさらに多く大きく
                Vector3 hitAt = e.Chest - Flat(to) * e.Radius;
                float fx = wasBroken || broke ? 1.6f : 1f;
                DuelSpark.Burst(hitAt, -Flat(to), new Color(1f, 0.88f, 0.55f), Mathf.RoundToInt((cur.heavy ? 26 : 14) * fx), (cur.heavy ? 1.4f : 1f) * fx);
                ProtoFx.Blast(hitAt, (cur.heavy ? 2f : 1.2f) * fx, Color.white, 0.08f);
                any = true;
                big |= wasBroken || broke;
            }
            // ヒットストップ(画面は揺らさない)。崩した・崩れた敵を斬ったときは少し長く
            if (any) game.HitStop((cur.heavy ? 0.12f : 0.06f) + (big ? 0.04f : 0f));
        }

        // ───────── 被弾 ─────────

        /// <summary>敵の攻撃が当たった。回避の無敵中は当たらない。ガードできる攻撃を正面で受ければガード。</summary>
        public void ReceiveHit(DuelHit h)
        {
            if (Dead || Invincible) return;
            Vector3 away = Flat(transform.position - h.from);
            if (away == Vector3.zero) away = -transform.forward;
            bool front = Vector3.Angle(transform.forward, -away) < 110f;
            if (Guarding && h.guardable && front && MaxGuard > 0f)
            {
                // 受け方は構えている盾の値(部屋のモードは素のガードの値)
                lastGuardHitAt = Time.time;
                Guard -= h.guardCost * GuardCostMul;
                HP -= h.damage * GuardChip;
                ProtoDamageText.Show(Chest, h.damage * GuardChip);
                Knockback(away * guardPush * GuardStunMul);
                if (Guard <= 0f)
                {
                    // ガードが崩れた:しばらく無防備。ゲージは半分まで戻してから再開
                    Guard = MaxGuard * 0.5f;
                    StopRising();   // 空中で崩れたら上昇が止まり、そのまま落ちる
                    Flash(new Color(1f, 0.4f, 0.2f), 0.4f);
                    game.Log("ガードが崩れた");
                    Enter(State.GuardBroken, guardBreakTime);
                }
                else
                {
                    Flash(new Color(0.4f, 0.7f, 1f), 0.12f);
                    Enter(State.GuardStun, guardStun * GuardStunMul);
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
                Vector3 local = transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z)) / Mathf.Max(1f, DashSpeed);
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
                {
                    // 技ごとの横のひねり・前傾・沈み(武器ごとの振り方の違い)。戻りで0へ。左手で振るときはひねりを左右反転
                    float a = phase == 0 ? Mathf.Clamp01(stateT / cur.windup) : phase == 1 ? Mathf.Clamp01(stateT / cur.active) : Mathf.Clamp01(stateT / cur.recover);
                    float side = atkMirror ? -1f : 1f;
                    tTwist = side * (phase == 0 ? Mathf.Lerp(cur.twist0, cur.twist1, a) : phase == 1 ? Mathf.Lerp(cur.twist1, cur.twist2, a) : Mathf.Lerp(cur.twist2, 0f, a));
                    tLean += phase == 0 ? Mathf.Lerp(cur.lean0, cur.lean1, a) : phase == 1 ? Mathf.Lerp(cur.lean1, cur.lean2, a) : Mathf.Lerp(cur.lean2, 0f, a);
                    tLift = phase == 0 ? 0f : phase == 1 ? -cur.drop * a : -cur.drop * (1f - a);
                    rate = 40f;
                    break;
                }
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

            // 斬撃クリップ:左手で振るときは左右反転のクリップ(あれば)
            string clip = atkMirror && rig != null && rig.Has(MechRig.AttackL) ? MechRig.AttackL : MechRig.Attack;
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
                    // 仮:振りかぶった途中の姿勢を、構えに見立てる(部屋のモードは右手の剣で。ステージは盾を持つ手を上げる)
                    bool leftGuard = Arms != null && guardHand == 1 && rig != null && rig.Has(MechRig.AttackL);
                    Pose(leftGuard ? MechRig.AttackL : MechRig.Attack, MechRig.AttackRaised * 0.55f, 18f);
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
            // 右手の銃(両手の銃・砲も)を撃った直後・L1 を溜めている間に止まっていれば、右腕を前に出した射撃の構え(左手用の構えのクリップはないので右手だけ)
            if (sp <= 0.5f && Arms != null && Arms.AimPose(0.4f) && rig != null && rig.Has(MechRig.Aim))
            {
                Pose(MechRig.Aim, 0f, 20f);
                return;
            }
            if (sp > 0.5f)
            {
                stepPhase += sp * dt / 6f;
                string walk = rig != null && rig.Has(MechRig.Run) && sp > 15f ? MechRig.Run : MechRig.Walk;
                Pose(walk, stepPhase * 0.5f * (rig != null ? rig.Length(walk) : 1f), 10f);
            }
            else
            {
                // 待機は微動だにしない(作者「待機アニメーションは微動だにしないようにしてください」)。待機のクリップの最初の姿勢で止める
                Pose(MechRig.Idle, 0f, 6f);
            }
        }
    }
}
