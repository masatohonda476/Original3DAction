using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機:移動・ロックオン・通常攻撃(2つの振りを交互にくり返す)・強攻撃(△+R1。右→左と左→右の薙ぎ払いを交互に)・
    /// 回避・回避斬り・ブースト(○で切り替え)・ブースト飛行(✗長押し。押すと自動でブーストオン)・ガード。
    /// 守りは回避が軸。回避は出だしが無敵で、避けた後もすぐ動ける(敵の隙に差し込める)。
    /// ガードは押している間ダメージを大きく減らすが、受けるたびに押し戻されて少し固まり、ゲージが尽きると崩れる。
    /// 敵の叩きつけ・跳躍叩きつけの直撃はガードできない(DuelHit.guardable)。回避と攻撃はENを使う。ENが少しでもあれば出せ、足りない分はマイナスになり、
    /// 0を超えるまで回復を待つ(その間は回避も攻撃もできない)。
    /// 武装(DuelArms。いつも付いている):R1 / L1 はその手の武装を使う(剣なら攻撃、盾なら押している間ガード、手の銃なら撃つ)。
    ///   技は剣ごとの表(DuelWeapon.moves → DuelMoveSet)。強攻撃(△+R1/L1)・回避斬りは押したボタンの手の剣で出す。
    ///   弾倉のある銃のボタンでは △+ボタンはリロード(DuelArms.TryManualReload。剣の手のボタンは横取りしない)。
    ///   ガードの値は構えている盾のもの。盾がなければガードできない。近接攻撃の間と回避の間は撃てない(CanShoot)。
    ///   両手の近接:R1 で連撃、L1 で別の技(回避の後も R1 は回避斬り、L1 は別の技)、△+R1/L1 で強攻撃。右手のクリップで振り、左右反転しない。
    ///   両手の銃・砲は DuelArms が撃つ(R1 が主の撃ち方、L1 が別の撃ち方)。両手持ちには盾がないのでガードできない。
    /// ファイルは役割ごとに分けている:DuelPlayer.LockOn.cs(ロックオン)/ DuelPlayer.Attack.cs(回避・攻撃)。
    /// </summary>
    public partial class DuelPlayer : DuelFighter
    {
        public static DuelPlayer I;

        [Header("移動")]
        public float runSpeed = 22f;
        public float lockedSpeed = 17f;
        [Tooltip("ブーストがオンの間に移動したときの速さ")]
        public float dashSpeed = 35f;
        [Tooltip("ブーストで移動している間に1秒あたり使うEN(止まっている間は使わない)")]
        public float dashCost = 30f;
        [Tooltip("空中でブースト移動している間の、dashCost への倍率(0.5 で半分)。上昇(flyCost)には掛からない")]
        public float airDashCostMul = 0.5f;
        [Tooltip("✗長押しで飛んでいる間に1秒あたり使うEN")]
        public float flyCost = 35f;
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
        [Tooltip("最後に回避・攻撃・射撃でENを使ってから、回復が始まるまでの秒数。ブースト移動・飛ぶのは待たず、やめた直後から回復する(ENを使い切ったときだけこの秒数を待つ)")]
        public float energyRegenDelay = 0.6f;

        [Header("ガード(どの盾でも同じ値。ゲージの量・通す傷・構え歩きの速さなどは盾ごと=DuelWeapon)")]
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

        [Header("ロックオンのオン・オフ(電脳斬禅の PlayerTargeting と同じ作り。値は仮)")]
        [Tooltip("始めたときにロックオンをオンにするか(電脳斬禅は新しいデータではオフ)。やり直し(F1)・撃墜のあとは、最後に切り替えた状態のまま(Play を止めるまで覚えている)")]
        public bool lockOnAtStart = true;
        [Tooltip("ロックオン中の敵が、壁・床ごし、またはロックオンできる距離(DuelStage.lockRange)の1.5倍より遠い状態がこの秒数続いたら外して、次の敵を探す(電脳斬禅は 1.5 秒)")]
        public float lockLoseAfter = 1.5f;
        [Tooltip("自動で選ぶ敵の点数 = 画面の中央からのずれ(0=中央〜0.7=角)× 10 + 距離(m)× この値。小さいほど先に選ぶ。" +
                 "電脳斬禅は人間の大きさで 0.5/m(機体は約4.3倍なので 0.12/m)。0 なら画面の中央に近い敵、大きくするほど近い敵を選ぶ")]
        public float lockPickDistanceWeight = 0.12f;
        [Tooltip("自動で選ぶのは、画面の端からこの割合より内側に映っている敵だけ(電脳斬禅は 0.06)。切り替え先は画面の中ならよい")]
        [Range(0f, 0.3f)] public float lockPickScreenMargin = 0.06f;

        public enum State { Free, Attack, Dodge, Guard, GuardStun, Hurt, GuardBroken, Dead }
        public State Current { get; private set; }
        public float Guard { get; private set; }
        /// <summary>EN。回避・攻撃・エネルギー武器の射撃で足りない分はマイナスになる。</summary>
        public float Energy { get; private set; }
        public float Stagger { get; private set; }
        /// <summary>ENが0以下(回復待ち)。この間は回避も攻撃も、エネルギー武器の射撃もできない。</summary>
        public bool Overheated => Energy <= 0f;
        public DuelEnemy Target { get; private set; }
        /// <summary>
        /// ロックオンのオン・オフ(電脳斬禅のオートロックオン)。オンの間は、狙う敵がいなければ自動で選び、倒した・見失ったら次へ移る。
        /// オフの間は敵を狙わない(カメラは自由に回せる)。表示は DuelGame の照準枠(オン・オフの切り替わりで動く)。
        /// </summary>
        public bool LockOn { get; private set; }
        public bool Guarding => Current is State.Guard or State.GuardStun;
        /// <summary>構えている盾の手(0=右 / 1=左)。右下の武装の欄で、構えている盾のセルを見分けるのに使う。</summary>
        public int GuardHand => guardHand;
        /// <summary>ブーストがオン(○で切り替え。ENが0以下になると自動でオフ)。</summary>
        public bool BoostOn { get; private set; }
        /// <summary>このフレームはブーストで移動している(見た目用)。</summary>
        public bool Dashing { get; private set; }
        /// <summary>噴射炎:飛行中・ブースト移動中・回避中。</summary>
        public override bool Thrusting => flying || Dashing || Current == State.Dodge;
        /// <summary>武装(Init で必ず付く。null にはならない)。強くなるのは武装だけで、素の性能(HP・EN・怯み)は変えない。</summary>
        public DuelArms Arms { get; private set; }
        /// <summary>手の銃・肩の武装を撃てる状態か(近接攻撃の振りかぶり〜戻り・回避・怯み・ガード崩れ・撃墜の間は撃てない)。</summary>
        public bool CanShoot => Current is State.Free or State.Guard;
        /// <summary>
        /// ガードのゲージの最大(持っている盾のうち大きいほう。盾がなければ0)。
        /// 盾が2枚でもゲージは1本(作者に未確認の仮)。削れ方・通す傷などは構えている盾(guardShield)の値。
        /// </summary>
        public float MaxGuard => Arms.GuardMax;

        // 背中の武装(スラスター)・盾・重さを掛けた値。素の値はインスペクターの値のまま
        // 重さ(DuelArms.Mobility)はENを使う移動(ブースト移動・回避の距離・飛ぶときの上昇)だけに掛かり、歩き・走り(RunMul)には掛からない
        float RunMul => Arms.RunMul;
        float DashMul => Arms.DashMul;
        float EnCostMul => Arms.EnCostMul;
        float DodgeDistance => dodgeDistance * Arms.DodgeMul;
        float DashSpeed => dashSpeed * DashMul;

        /// <summary>踏み込むのは振りかぶりのうち、この割合(前半)だけ。</summary>
        const float LungePart = 0.5f;

        float stateT, stateDur;
        DuelSwing cur;
        /// <summary>今の攻撃を振っている手(0=右 / 1=左)と、攻撃ボタンを押した手。</summary>
        int atkHand, pressHand;
        /// <summary>今の攻撃を左右反転して振るか(左手の片手の剣。両手の近接は反転しない)。</summary>
        bool atkMirror;
        /// <summary>
        /// 構えている盾と、その手(0=右 / 1=左)。ガード中(Guarding)は必ず入っている:ガードに入るのは WantGuard が盾を覚えたときだけで、
        /// 盾を外したら OnShieldsChanged が引き直し、なければガードを解く。
        /// </summary>
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
            Energy = maxEnergy;
            LockOn = lockOnMemory ?? lockOnAtStart;
            gameObject.AddComponent<DuelThruster>().Init(this, rig);   // 飛行中の噴射炎
            if (rig != null && rig.swordL != null) rig.swordL.gameObject.SetActive(false);
            // 敵が落とす武装を拾って付けられる(最初は右手に素の剣、左手は空)
            Arms = gameObject.AddComponent<DuelArms>();
            Arms.Init(this, g, rig);
            Guard = MaxGuard;
        }

        /// <summary>
        /// 盾を付けた・外した(DuelArms から)。新しい盾を付けたらゲージを満タンに、外したら最大までに切る。
        /// 構えている盾はその場で引き直す(構えていた手にまだ盾があればそれ、なければもう片方の手の盾)。
        /// 盾がなくなったらガード(受けて固まっている間も)を解く。
        /// </summary>
        public void OnShieldsChanged(bool refill)
        {
            Guard = refill ? MaxGuard : Mathf.Min(Guard, MaxGuard);
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
            Target = null;   // ロックオンのオン・オフはそのまま(オンなら、出し直した所で次の敵を探す)
            lockLostFor = 0f;
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
        /// 動いた距離から足音を出す(届く範囲などは DuelStage.senses)。歩き・走りはスティックの倒し具合(速さ)で届く範囲が変わり、
        /// ブースト移動・回避・飛行は遠くまで届く。飛行は止まって浮いていても鳴る。空中を流れているだけ・押されただけ・瞬間移動は数えない。
        /// </summary>
        void RecordFootsteps(Vector3 before, float dt, bool moveInput)
        {
            var s = game.stage.senses;
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
            if (DuelGame.Paused) return;   // メニュー(装備の確認・操作方法)を開いている間は止める
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            Arms.Tick();   // 武装(拾う・付け先を選ぶ・撃つ)。ボタンの横取りを自機が読むより先に決める
            if (Current == State.Dead) { flying = false; Move(Vector3.zero, dt); return; }

            UpdateLock();
            ReadButtons(now);
            if (ProtoInput.BoostToggle) BoostOn = !BoostOn && Energy > 0f;
            // ✗:押したら自動でブーストオン。押している間は飛ぶ(ENを使い続け、0以下なら落ちる)
            // 付け先を選ぶ表示の間と、それを終えた時に押していた ✗ を離すまでは飛ばない(✗ は背中に付けるボタンでもある。DuelArms.FlyBlocked)
            bool flyHeld = ProtoInput.FlyHeld && !Arms.FlyBlocked;
            if (flyHeld && !flyHeldPrev && Energy > 0f) BoostOn = true;
            flyHeldPrev = flyHeld;
            flying = flyHeld && Energy > 0f && Current is State.Free or State.Guard or State.Attack or State.Dodge;
            riseSpeed = flyRiseSpeed * DashMul;
            riseAccel = flyRiseAccel;
            if (flying)
            {
                Energy -= flyCost * EnCostMul * dt;
                // 移動は回復を待たせない。使い切ったときだけ待たせる(待たせないと、✗ を押したままENが0のあたりで飛び続けられる)
                if (Energy <= 0f) energyUsedAt = now;
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

            // EN:回避・攻撃・射撃は、最後に使ってから少しして戻る(マイナスからでも同じ速さで戻る)。
            // 移動(ブースト移動・飛ぶ)は、している間は戻らず、やめた直後から戻る(作者「移動は直後からENの回復が始まるようにしたい」)。
            // Dashing はここではまだ前のフレームの値(すぐ下で決め直す)
            if (!flying && !Dashing && now - energyUsedAt >= energyRegenDelay) Energy = Mathf.Min(maxEnergy, Energy + energyRegen * dt);

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
                        // 空中の横移動は半分(作者「空中のブースト移動(上昇ではない)はエネルギー消費を半減」)。✗ で上昇する分(flyCost)はそのまま
                        Energy -= dashCost * (Airborne ? airDashCostMul : 1f) * EnCostMul * dt;
                        if (Energy <= 0f) energyUsedAt = now;   // 使い切ったときだけ回復を待たせる(飛ぶのと同じ)
                    }
                    // 向き:ロックオン中は敵を向いたまま(ブーストで動くときは進む方向へ傾くだけ)。していなければ進む向きを向く(止まっていれば今の向きのまま)。
                    // 既定の形(弾は自機の向いている方へ飛ぶ。DuelArmsRules.FacingAim)では、射撃の構えで向き直らない
                    // (作者「ノーロック射撃時自機とカメラの向きを変えないようにしたいです」→ 動いたときは「撃たないときと同じ」。
                    //  前は、ロックオンしていない構えの間はカメラの向きへ向き直っていた。カメラは射撃では回さない)。
                    // 照準を動かせる形だけ、構えの間は照準の先(画面の上の照準を通る向き。DuelArms.AimFacing)を向く(銃口と弾の向きがずれないように)
                    if (Arms.Aiming && !DuelArmsRules.Current.FacingAim)
                    {
                        Vector3 af = Arms.AimFacing;
                        Face(af != Vector3.zero ? af : camFwd, 14f, dt);
                    }
                    else if (Target != null) Face(Target.transform.position - transform.position, 14f, dt);
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
            // 攻撃:剣を持つ手のボタン(R1=右手 / L1=左手。付け先を選んでいる間は数えない)
            if (Arms.IsSword(0) && Arms.Pressed(DuelSlot.RightHand)) PressSword(0, now);
            if (Arms.IsSword(1) && Arms.Pressed(DuelSlot.LeftHand)) PressSword(1, now);
            if (ProtoInput.Dodge) dodgePressedAt = now;
        }

        /// <summary>先行入力(押してからの受付時間内の攻撃・回避)を捨てる。メニューを開いたとき(閉じた直後に出ないように)。</summary>
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
        /// ガードしたいか:盾を持つ手のボタンを押している間(構えている盾を覚える)。盾がなければガードできない。
        /// 剣(L1)での素のガードは、部屋のモードと一緒にやめた。
        /// </summary>
        bool WantGuard()
        {
            if (!Arms.GuardHeld(out var shield, out int hand)) return false;
            guardShield = shield;
            guardHand = hand;
            return true;
        }

        // 構えている盾の値。ガード中(Guarding)にしか読まないので、guardShield は必ず入っている
        float GuardChip => guardShield.guardChip;
        float GuardCostMul => guardShield.guardCostMul;
        float GuardMoveMul => guardShield.guardMoveMul;
        float GuardStunMul => guardShield.guardStunMul;

        /// <summary>最後に近接(剣・両手の近接・回避斬り)か回避を始めた時刻。射撃の構え(DuelArms.Aiming)はこれで終わる。</summary>
        public float MeleeOrDodgeAt { get; private set; } = -10f;

        void Enter(State s, float dur = 0f)
        {
            if (s is State.Attack or State.Dodge) MeleeOrDodgeAt = Time.time;
            Current = s;
            stateT = 0f;
            stateDur = dur;
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
                // 受け方は構えている盾の値
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
                    // 仮:振りかぶった途中の姿勢を、構えに見立てる(盾を持つ手を上げる)
                    bool leftGuard = guardHand == 1 && rig != null && rig.Has(MechRig.AttackL);
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
            // ブースト移動中・空中。足もとの高さは機体の中心から真下へ測るので、足場の角に立つと中心が外へ出て高く出る。
            // 接地しているかも見る(Airborne)ことで、角に立っても空中の姿勢にしない
            if (Dashing || (Airborne && Altitude > 1.5f)) { Pose(MechRig.Dash, 0f, 12f); return; }
            // 右手の銃(両手の銃・砲も)を撃った直後・L1 を溜めている間に止まっていれば、右腕を前に出した射撃の構え(左手用の構えのクリップはないので右手だけ)
            if (sp <= 0.5f && Arms.AimPose(0.4f) && rig != null && rig.Has(MechRig.Aim))
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
