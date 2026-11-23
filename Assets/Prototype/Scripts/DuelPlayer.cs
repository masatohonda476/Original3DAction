using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 自機の移動・回避・ジャンプ・近接攻撃。ふだんは R1 で通常攻撃、R2 で強攻撃。
    /// L2 を押している間は遠距離を構え、DuelArms が射撃を受け付ける。構えている間も移動・回避・ジャンプができる。
    /// 地上で跳べる攻撃が当たる瞬間に跳ぶと成功し、そのジャンプ中の近接 R1 が落下攻撃になる。
    /// 崩れた敵の近くの地上では、近接 R1 で致命攻撃を出す。
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
        [Tooltip("回避の出だしの無敵時間(秒)。背中のスラスターの倍率が掛かる")]
        public float dodgeInvincible = 0.26f;
        public float dodgeCooldown = 0.12f;

        [Header("EN(回避と攻撃で使う。攻撃の量は攻撃ごと)")]
        public float maxEnergy = 150f;
        public float dodgeCost = 30f;
        public float energyRegen = 60f;
        [Tooltip("最後に回避・攻撃・射撃でENを使ってから、回復が始まるまでの秒数。ブースト移動・飛ぶのは待たず、やめた直後から回復する(ENを使い切ったときだけこの秒数を待つ)")]
        public float energyRegenDelay = 0.6f;

        [Header("攻撃")]
        public float attackBuffer = 0.25f;
        [Tooltip("振り抜いてから(戻りに入ってから)この秒数は、回避で取り消せない")]
        public float dodgeLockAfterSwing = 0.1f;
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
        [Tooltip("ロックオン中の敵が、壁・床ごし、またはロックオン距離(DuelStage.lockOnRange)の1.5倍より遠い状態がこの秒数続いたら外して、次の敵を探す(電脳斬禅は 1.5 秒)")]
        public float lockLoseAfter = 1.5f;
        [Tooltip("付け始めに選ぶ敵の点数 = 画面の中央からのずれ(0=中央〜0.7=角)× 10 + 距離(m)× この値。小さいほど先に選ぶ。" +
                 "電脳斬禅は人間の大きさで 0.5/m(機体は約4.3倍なので 0.12/m)。0 なら画面の中央に近い敵、大きくするほど近い敵を選ぶ。" +
                 "選ぶのは、体が照準枠(ロックオンがオンの間、画面の縁寄りに出ている四つの角の内側)に掛かっている敵だけ(作者「サイトの枠内に入った場合のみロックオン開始する」。" +
                 "枠の大きさは DuelGame.Hud.cs の LockCornerHalfW / LockCornerHalfH。前の「画面の端から内側へ空ける割合 lockPickScreenMargin」はやめた)。ロックオン中の切り替え先は画面の中ならよい")]
        public float lockPickDistanceWeight = 0.12f;

        public enum State { Free, Attack, Dodge, Hurt, Dead }
        public State Current { get; private set; }
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
        /// <summary>ブーストがオン(○で切り替え。ENが0以下になると自動でオフ)。</summary>
        public bool BoostOn { get; private set; }
        /// <summary>このフレームはブーストで移動している(見た目用)。</summary>
        public bool Dashing { get; private set; }
        /// <summary>噴射炎:飛行中・ブースト移動中・回避中・ジャンプで上がっている間・落下攻撃で降りている間。</summary>
        public override bool Thrusting => flying || Dashing || Current == State.Dodge || JumpRising || DropDiving;
        /// <summary>武装(Init で必ず付く。null にはならない)。強くなるのは武装だけで、素の性能(HP・EN・怯み)は変えない。</summary>
        public DuelArms Arms { get; private set; }
        /// <summary>移動中やジャンプ中も撃てる。回避・怯み・近接攻撃の間は撃てない。</summary>
        public bool CanShoot => Current == State.Free;
        /// <summary>近接攻撃のうち、動作を最後まで続けてから持ち替える区間。</summary>
        public bool MeleeCommitted => Current == State.Attack &&
            (dropping || FatalCommitted || phase == 1 || (phase == 2 && stateT < dodgeLockAfterSwing));

        // 背中のスラスターによる機動の補正。
        float DashMul => Arms.DashMul;
        float EnCostMul => Arms.EnCostMul;
        float DodgeDistance => dodgeDistance * Arms.DodgeDistanceMul;
        float DodgeInvincibleTime => dodgeInvincible * Arms.DodgeInvincibleMul;
        float DashSpeed => dashSpeed * DashMul;

        /// <summary>踏み込むのは振りかぶりのうち、この割合(前半)だけ。</summary>
        const float LungePart = 0.5f;

        float stateT, stateDur;
        DuelSwing cur;
        int phase;   // 攻撃:0=振りかぶり 1=振り抜き 2=戻り
        int combo;
        float energyUsedAt = -10f, lastStaggerHitAt = -10f;
        /// <summary>空中の横の速さ(慣性)。地上では毎フレームの速さと同じ。</summary>
        Vector3 airVel;
        bool flyHeldPrev;
        /// <summary>落下攻撃の間に押していた ✗ は、一度離すまで飛行にもジャンプにも使わない(着地の隙の後、押したままで浮き上がらないように)。</summary>
        bool flyNeedRelease;
        bool heavyPressed;
        float meleePressedAt = -10f, dodgePressedAt = -10f, dodgeEndAt = -10f;
        Vector3 dodgeDir, attackDir;
        float stepPhase;

        public void Init(DuelGame g)
        {
            I = this;
            Setup(g, 100f);
            Energy = maxEnergy;
            LockOn = lockOnMemory ?? lockOnAtStart;
            gameObject.AddComponent<DuelThruster>().Init(this, rig);   // 飛行中の噴射炎
            // 近接・遠距離・背中の武装。落とし物を拾うと同じ種類の武装を入れ替えられる。
            Arms = gameObject.AddComponent<DuelArms>();
            Arms.Init(this, g, rig);
        }

        /// <summary>撃墜からのやり直し。その場所に HP・EN を満タンにして出し直す。</summary>
        public void Respawn(Vector3 pos, Quaternion rot)
        {
            Revive();
            Teleport(pos, rot);
            Energy = maxEnergy;
            Stagger = 0f;
            BoostOn = false;
            ClearBufferedInput();
            Arms.ClearBufferedInput();
            Arms.StopBurst();
            airVel = Vector3.zero;
            flyNeedRelease = true;   // 出し直した瞬間に ✗ を押していても跳ばない(押していなければ、次のフレームに解ける)
            slideOff = Vector3.zero;
            Target = null;   // ロックオンのオン・オフはそのまま(オンなら、出し直した所で次の敵を探す)
            lockLostFor = 0f;
            stepTravel = 0f;
            FootstepRadius = 0f;
            EndJump();
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
            bool walking = Current == State.Free && moveInput && !Airborne;
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
            Arms.ReleaseSupportGrip();
            if (DuelGame.Paused)
            {
                Arms.CheckPausedBurstRelease();
                return;
            }
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            Arms.Tick();   // 持ち替え・弾倉・落とし物を、自機のボタン入力より先に更新する
            if (Current == State.Dead) { flying = false; Move(Vector3.zero, dt); Arms.UpdateWeaponVisibility(); return; }   // 構えたまま撃墜されても剣の見た目に戻す

            // 振り抜き・落下攻撃・致命攻撃は、取り消せる区間まで待ってから持ち替える。
            if (Arms.RangedHeld && Current == State.Attack && !MeleeCommitted) Enter(State.Free);
            UpdateLock();
            ReadButtons(now);
            // 落とし物の選択中と閉じたフレームは、ブーストを切り替えない(○ は選ぶ表示の取り消し。DuelArms.ChoiceLocksInput)。
            // オンのまま選ぶ表示に入ったら、オンのまま(速い移動はそのまま効く)
            if (!Arms.ChoiceLocksInput && ProtoInput.BoostToggle) BoostOn = !BoostOn && Energy > 0f;
            // ✗:押したら自動でブーストオン。地上で押していればジャンプ(UpdateJump。地上では飛ばない・上がっている間は飛行にしない)。
            // 空中で押している間・ジャンプの頂点を過ぎても押している間は飛ぶ(ENを使い続け、0以下なら落ちる)
            // 落とし物の選択中と、それを終えた時に押していた ✗ を離すまでは跳ばない・飛ばない(DuelArms.FlyBlocked)
            bool flyRaw = ProtoInput.FlyHeld && !Arms.FlyBlocked;
            if (flyRaw && !flyHeldPrev && Energy > 0f) BoostOn = true;
            flyHeldPrev = flyRaw;
            // 落下攻撃の間(降下・振り・着地の隙)に押していた ✗ は、一度離すまで効かない(着地の隙の後に、押したままで跳んだり浮いたりしない)
            if (!flyRaw) flyNeedRelease = false;
            else if (dropping) flyNeedRelease = true;
            bool flyHeld = flyRaw && !flyNeedRelease;
            bool mayFly = UpdateJump(now, flyHeld);
            flying = mayFly && flyHeld && Energy > 0f && Current is State.Free or State.Attack or State.Dodge;
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
                    // 回避 → 近接攻撃の順に受け付ける。射撃は移動後に扱う。
                    if (TryDodge(now, worldMove)) break;
                    if (TryAttack(now)) break;
                    Dashing = BoostOn && worldMove.sqrMagnitude > 0.04f && Energy > 0f;
                    float spd = Dashing ? DashSpeed : (Target != null ? lockedSpeed : runSpeed);
                    vel = worldMove * spd;
                    if (Dashing)
                    {
                        // 空中の横移動は半分(作者「空中のブースト移動(上昇ではない)はエネルギー消費を半減」)。✗ で上昇する分(flyCost)はそのまま
                        Energy -= dashCost * (Airborne ? airDashCostMul : 1f) * EnCostMul * dt;
                        if (Energy <= 0f) energyUsedAt = now;   // 使い切ったときだけ回復を待たせる(飛ぶのと同じ)
                    }
                    // 遠距離を構えていれば、LateUpdate でこのフレームのカメラに向きを合わせる。
                    if (!Arms.Aiming)
                    {
                        if (Target != null) Face(Target.transform.position - transform.position, 14f, dt);
                        else if (Dashing) Face(worldMove, 14f, dt);
                        else if (worldMove.sqrMagnitude > 0.01f) Face(worldMove, 12f, dt);
                    }
                    break;

                case State.Attack:
                    vel = UpdateAttack(dt, now, worldMove);
                    break;

                case State.Dodge:
                {
                    float t = Mathf.Clamp01(stateT / dodgeDuration);
                    vel = dodgeDir * (3f * DodgeDistance / dodgeDuration) * (1f - t) * (1f - t);
                    // 回避の後半に、崩れた敵の近く(地上)で攻撃を押したら、致命攻撃へ(回避斬りより先。EN が0以下でも出る)
                    if (stateT >= dodgeDuration * dodgeSlashFrom && TryFatal(now))
                    {
                        dodgeEndAt = now;
                        break;
                    }
                    // 回避の後半に攻撃を押したら、回避斬りへ
                    if (stateT >= dodgeDuration * dodgeSlashFrom && LightInputReady(now) && !Overheated)
                    {
                        dodgeEndAt = now;
                        if (CanDrop) StartDrop();   // 成功しているジャンプ中なら落下攻撃
                        else StartDodgeSlash();
                        break;
                    }
                    if (stateT >= dodgeDuration) { dodgeEndAt = now; Enter(State.Free); }
                    break;
                }

                case State.Hurt:
                    if (stateT >= stateDur) Enter(State.Free);
                    break;
            }

            // 空中の慣性:横の速さは少しずつしか変わらない(地上はすぐ止まり、すぐ曲がる)
            if (Airborne)
            {
                if (Current == State.Dodge || DropDiving) airVel = vel;   // 回避・落下攻撃の降下はすぐその向きへ
                else if (Current == State.Free) airVel = Vector3.MoveTowards(airVel, vel, airAccel * dt);
                else airVel = Vector3.MoveTowards(airVel, vel, airDrag * dt);   // 攻撃・怯みの間は勢いがゆるやかに抜ける
                vel = airVel;
            }
            else airVel = vel;

            vel = WithSlideOff(vel);   // ジャンプ中に敵の体の上に乗ったら、水平に押し出して滑り落とす(UpdateJump。慣性には入れない)
            Vector3 before = transform.position;
            Move(vel, dt);
            RecordFootsteps(before, dt, worldMove.sqrMagnitude > 0.02f);
            Animate(dt, vel);
        }

        void LateUpdate()
        {
            if (DuelGame.Paused || Dead) return;
            // 表示に使うカメラの向きが決まってから、機体の向き・銃口・射撃方向をそろえる。
            var cam = Camera.main;
            if (Arms.Aiming && cam != null)
            {
                Vector3 facing = Flat(cam.transform.forward);
                if (facing != Vector3.zero) transform.rotation = Quaternion.LookRotation(facing);
            }
            Arms.UpdateCombat(CanShoot);
        }

        void ReadButtons(float now)
        {
            if (Arms.RangedHeld || Arms.ChoiceLocksInput)
                meleePressedAt = -10f;
            else if (Arms.HeavyPressed)
            {
                meleePressedAt = now;
                heavyPressed = true;
            }
            else if (Arms.LightPressed)
            {
                meleePressedAt = now;
                heavyPressed = false;
            }
            // 落とし物の選択中と閉じたフレームには回避を先行入力しない。
            if (!Arms.ChoiceLocksInput && ProtoInput.Dodge) dodgePressedAt = now;
        }

        /// <summary>メニューや落とし物の選択を開いたとき、先行入力を捨てる。</summary>
        public void ClearBufferedInput()
        {
            meleePressedAt = -10f;
            dodgePressedAt = -10f;
            heavyPressed = false;
        }

        void Enter(State s, float dur = 0f)
        {
            if (s is State.Dodge or State.Hurt) Arms.StopBurst();
            if (s != State.Attack)
            {
                dropping = fatal = false;
                combo = 0;
            }
            Current = s;
            stateT = 0f;
            stateDur = dur;
        }

        // ───────── 被弾 ─────────

        /// <summary>敵の攻撃が当たった。回避や致命攻撃の無敵中は当たらない。</summary>
        public void ReceiveHit(DuelHit h)
        {
            if (Dead || Invincible) return;
            EndJump();   // 被弾で「ジャンプ中」は終わる。成功も消える
            if (DropDiving) airVel = Vector3.zero;
            Vector3 away = Flat(transform.position - h.from);
            if (away == Vector3.zero) away = -transform.forward;
            Vector3 hurtAt = Chest - away * Radius;

            if (HeavyGuardPoint)
            {
                float reduced = h.damage * GuardPointDamageMul;
                HP -= reduced;
                ProtoDamageText.Show(Chest, reduced);
                Color tint = new(0.55f, 0.85f, 1f);
                Flash(tint, 0.12f);
                DuelSpark.Burst(hurtAt, -away, tint, 18, 1.1f);
                if (HP <= 0f) Die();
                return;
            }

            HP -= h.damage;
            ProtoDamageText.Show(Chest, h.damage);
            Flash(new Color(1f, 0.25f, 0.2f), 0.18f);
            Knockback(away * h.knock);
            Jolt(-8f);
            DuelSpark.Burst(hurtAt, -away, new Color(1f, 0.55f, 0.25f), 16, 1.1f);
            game.HitStop(0.08f);
            if (HP <= 0f) { Die(); return; }
            // 怯みゲージ:いっぱいになったときだけ怯む(時間はいっぱいにした攻撃で決まる)。怯んでいる間はたまらない
            if (Current == State.Hurt) return;
            Stagger += h.stagger;
            lastStaggerHitAt = Time.time;
            if (Stagger < maxStagger) return;
            Stagger = 0f;
            StopRising();   // 空中で怯んだら上昇の勢いが止まり、怯んでいる間は落ちる(✗を押し続けていれば、怯みが終わるとまた上がる)
            Enter(State.Hurt, h.hurtTime);
        }

        void Die()
        {
            HP = 0f;
            ClearBufferedInput();
            Arms.ClearBufferedInput();
            Arms.StopBurst();
            EndJump();
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
            // 機体全体の姿勢:怯みはのけぞって沈みよろめく
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
                    // 技ごとの横のひねり・前傾・沈み(武器ごとの振り方の違い)。戻りで0へ
                    float a = phase == 0 ? Mathf.Clamp01(stateT / cur.windup) : phase == 1 ? Mathf.Clamp01(stateT / cur.active) : Mathf.Clamp01(stateT / cur.recover);
                    tTwist = phase == 0 ? Mathf.Lerp(cur.twist0, cur.twist1, a) : phase == 1 ? Mathf.Lerp(cur.twist1, cur.twist2, a) : Mathf.Lerp(cur.twist2, 0f, a);
                    tLean += phase == 0 ? Mathf.Lerp(cur.lean0, cur.lean1, a) : phase == 1 ? Mathf.Lerp(cur.lean1, cur.lean2, a) : Mathf.Lerp(cur.lean2, 0f, a);
                    tLift = phase == 0 ? 0f : phase == 1 ? -cur.drop * a : -cur.drop * (1f - a);
                    rate = 40f;
                    break;
                }
                case State.Hurt:
                    FlinchPose(k, out tLean, out tTwist, out tLift);
                    rate = 30f;
                    break;
                case State.Dead:
                    tLean = -30f;
                    tLift = -1.5f;
                    rate = 6f;
                    break;
            }
            BodyOffset(tLean, tTwist, tLift, rate, dt, tRoll);

            switch (Current)
            {
                case State.Attack:
                    float t = phase == 0 ? Mathf.Lerp(cur.pose0, cur.pose1, Mathf.Clamp01(stateT / cur.windup))
                            : phase == 1 ? Mathf.Lerp(cur.pose1, cur.pose2, Mathf.Clamp01(stateT / cur.active))
                            : Mathf.Lerp(cur.pose2, cur.pose3, Mathf.Clamp01(stateT / cur.recover));
                    Pose(MechRig.Attack, t, 25f);
                    return;
                case State.Dodge:
                    Pose(MechRig.Dash, 0f, 15f);
                    return;
                case State.Hurt:
                case State.Dead:
                    Pose(MechRig.Idle, 0f, 10f);
                    return;
            }
            float sp = new Vector3(vel.x, 0f, vel.z).magnitude;
            // ブースト移動中・空中。足もとの高さは機体の中心から真下へ測るので、足場の角に立つと中心が外へ出て高く出る。
            // 接地しているかも見る(Airborne)ことで、角に立っても空中の姿勢にしない
            if (Dashing || (Airborne && Altitude > 1.5f)) { Pose(MechRig.Dash, 0f, 12f); return; }
            // 停止中に L2 で遠距離を構えている間は、右腕を前に出す。
            if (sp <= 0.5f && Arms.Aiming && rig != null && rig.Has(MechRig.Aim))
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
