using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 敵(人型の剣士メカ)。中距離で回り込みながら間合いを測り、距離に応じて技を選ぶ。
    /// 横薙ぎ(ガード可・2連まで)/ 叩きつけ(ガード不可)/ 踏み込み突き(離れた所から一気に詰める)。
    /// どの技かは予備動作の姿勢だけで分かるようにする(色・技名・地面の印は出さない)。
    ///   横薙ぎ:剣を上げつつ上半身を横へひねって溜める / 叩きつけ:剣を高く掲げ、のけぞって浮き上がる(一番大きく長い)/
    ///   突き:低く前かがみに構え、少し後ろへ下がって溜める /
    ///   跳び上がり斬り(対空):自機が頭上を飛んでいると、深く沈み込んで上を見上げるようにのけぞって溜め、自機へ跳び上がる
    /// 怯みゲージが攻撃でたまり、満タンになると怯む(技の途中でも止まる)。攻撃の後の隙に当てると多くたまる。
    /// 1発ごとには怯まない。
    /// 種類(Spec)で性格を変える:剣士(中型・1.5倍の大きさ・技3つ)/ 兵卒(雑魚・技2つ・すぐ怯む・攻めがゆっくり)。
    /// 同時に攻撃してくる数は DuelGame が絞る(順番待ちの間は回り込んで待つ)。
    /// </summary>
    public class DuelEnemy : DuelFighter
    {
        /// <summary>敵の種類ごとの性格。</summary>
        public class Spec
        {
            public string name;
            public float size = 1f, hp = 133f, maxStagger = 25f;
            /// <summary>与えるダメージ・自機の怯みゲージへの量・予備動作の長さに掛ける倍率。</summary>
            public float damageMul = 1f, playerStaggerMul = 1f, windupMul = 1f;
            /// <summary>技を選び直す間隔(秒)。</summary>
            public float thinkMin = 0.7f, thinkMax = 1.5f;
            public float approachSpeed = 15f, strafeSpeed = 7f, keepDistance = 12f;
            /// <summary>横薙ぎが2発目へ続く確率。</summary>
            public float sweepChain = 0.5f;
            /// <summary>叩きつけ(ガード不可)を使うか。</summary>
            public bool canSlam = true;
            /// <summary>ダッシュの速さ(離れているときに詰める・横へ回り込む)と、間合いを測っている間に横へダッシュする頻度(1秒あたり)。</summary>
            public float dashSpeed = 34f, sideDashRate = 0.25f;
            /// <summary>自機が頭上を飛んでいるとき、跳び上がり斬りを選ぶ確率と、跳べる高さ(m)。</summary>
            public float antiAirChance = 0.7f, antiAirHeight = 45f;
        }

        /// <summary>剣士(中型):1対1で読み合う相手。</summary>
        public static Spec Duelist(float hp) => new() { name = "剣士", size = 1.5f, hp = hp, keepDistance = 15f };

        /// <summary>兵卒(雑魚):複数で来る前提。技は横薙ぎと突きだけ、2発で怯み、攻めはゆっくり。</summary>
        public static Spec Grunt(float hp) => new()
        {
            name = "兵卒", size = 1f, hp = hp, maxStagger = 10f, damageMul = 0.65f, playerStaggerMul = 0.7f, windupMul = 1.15f,
            thinkMin = 1.5f, thinkMax = 2.8f, approachSpeed = 12f, strafeSpeed = 6f, keepDistance = 14f, sweepChain = 0f, canSlam = false,
            dashSpeed = 26f, sideDashRate = 0.1f, antiAirChance = 0.35f, antiAirHeight = 30f,
        };

        public Spec Kind { get; private set; }
        public string KindName => Kind != null ? Kind.name : "";
        [Header("動き")]
        public float approachSpeed = 15f;
        public float strafeSpeed = 7f;
        [Tooltip("この距離(すき間)より離れていれば近づく")]
        public float keepDistance = 12f;

        [Header("怯みゲージ")]
        public float maxStagger = 25f;
        [Tooltip("攻撃の後の隙に当てたとき、怯みゲージへの量に掛ける倍率")]
        public float recoverStaggerMul = 1.6f;
        public float staggerDecay = 3f;
        [Tooltip("最後に当てられてから、ゲージが減り始めるまでの秒数")]
        public float staggerDecayDelay = 2f;

        public enum State { Approach, Strafe, Windup, Active, Recover, Flinch, Dead }
        /// <summary>技を出している途中(予備動作か、出ている間)。同時に攻撃する数を数えるのに使う。</summary>
        public bool Attacking => Current is State.Windup or State.Active;
        public enum Skill { Sweep, Slam, Thrust, AntiAir }
        public State Current { get; private set; }
        public float Stagger { get; private set; }
        public bool Flinching => Current == State.Flinch;


        DuelPlayer player;
        Skill skill;
        float stateT, stateDur, thinkAt, lastStaggerHitAt = -10f, strafeSign = 1f, idleT, stepPhase;
        int chain;           // 横薙ぎの何発目か
        bool dashing;        // このフレームはダッシュで動いている(見た目用)
        Vector3 jumpVel;     // 跳び上がり斬りの横の速さ
        float sideDashT;     // 横ダッシュの残り時間
        Vector3 commitDir;   // 振りかぶりの最後に決めた向き(そこからは向きを変えない=横へ避けられる)
        Vector3 thrustStart;
        bool hitDone;

        public void Init(DuelGame g, DuelPlayer p, Spec spec)
        {
            player = p;
            Kind = spec;
            Setup(g, spec.hp, spec.size);
            maxStagger = spec.maxStagger;
            approachSpeed = spec.approachSpeed;
            strafeSpeed = spec.strafeSpeed;
            keepDistance = spec.keepDistance;
            Enter(State.Approach);
            thinkAt = Time.time + 1f;
            if (rig != null && rig.swordL != null) rig.swordL.gameObject.SetActive(false);
        }

        void Enter(State s, float dur = 0f)
        {
            Current = s;
            stateT = 0f;
            stateDur = dur;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            if (Current == State.Dead) { Move(Vector3.zero, dt); return; }

            if (now - lastStaggerHitAt >= staggerDecayDelay && Current != State.Flinch)
                Stagger = Mathf.Max(0f, Stagger - staggerDecay * dt);

            Vector3 toP = player != null ? player.transform.position - transform.position : transform.forward;
            Vector3 dir = Flat(toP);
            float gap = player != null ? GapTo(player) : 99f;
            bool playerAlive = player != null && !player.Dead;
            Vector3 vel = Vector3.zero;
            dashing = false;
            if (Current != State.Strafe) sideDashT = 0f;

            switch (Current)
            {
                case State.Approach:
                    Face(dir, 8f, dt);
                    // 離れていればダッシュで一気に詰める
                    dashing = gap > keepDistance + 14f;
                    vel = dir * (dashing ? Kind.dashSpeed : approachSpeed);
                    if (gap <= keepDistance) Enter(State.Strafe);
                    if (playerAlive && now >= thinkAt && gap <= 26f) Think(gap);
                    break;

                case State.Strafe:
                    // 間合いを保ちながら横へ回る。ときどき回る向きを変える
                    Face(dir, 8f, dt);
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * strafeSign;
                    float keep = gap > keepDistance + 3f ? 1f : gap < keepDistance - 4f ? -0.7f : 0f;
                    vel = side * strafeSpeed + dir * keep * strafeSpeed;
                    if (Random.value < dt * 0.4f) strafeSign = -strafeSign;
                    // ときどき横へダッシュして回り込む
                    if (sideDashT <= 0f && Random.value < dt * Kind.sideDashRate)
                    {
                        sideDashT = 0.35f;
                        strafeSign = Random.value < 0.5f ? 1f : -1f;
                    }
                    if (sideDashT > 0f)
                    {
                        sideDashT -= dt;
                        dashing = true;
                        vel = Vector3.Cross(Vector3.up, dir) * strafeSign * Kind.dashSpeed;
                    }
                    if (gap > keepDistance + 8f) Enter(State.Approach);
                    if (playerAlive && now >= thinkAt) Think(gap);
                    break;

                case State.Windup:
                    UpdateWindup(dt, dir);
                    // 突きの溜め:少し後ろへ下がる
                    if (Current == State.Windup && skill == Skill.Thrust) vel = -transform.forward * 5f;
                    break;

                case State.Active:
                    vel = UpdateActive(dt);
                    break;

                case State.Recover:
                    if (stateT >= stateDur) EndAttack();
                    break;

                case State.Flinch:
                    if (stateT >= stateDur) EndAttack();
                    break;
            }

            Move(vel, dt);
            Animate(dt, vel);
        }

        // ───────── 技を選ぶ ─────────

        void Think(float gap)
        {
            // 同時に攻撃している敵が多ければ、順番を待つ(回り込みながら少しして考え直す)
            if (!game.CanStartAttack(this)) { thinkAt = Time.time + Random.Range(0.3f, 0.6f); return; }
            thinkAt = Time.time + Random.Range(Kind.thinkMin, Kind.thinkMax);
            // 自機が頭上を飛んでいる:跳び上がり斬りで落とす(届かない高さ・選ばなかったときは見上げて待つ)
            float above = player.Altitude - Altitude;
            if (player.Airborne && above > game.mechHeight * Size * 0.8f)
            {
                if (above <= Kind.antiAirHeight && gap <= 30f && Random.value < Kind.antiAirChance) StartSkill(Skill.AntiAir);
                return;
            }
            float r = Random.value;
            if (gap <= SweepReach)
            {
                if (!Kind.canSlam) StartSkill(r < 0.7f ? Skill.Sweep : Skill.Thrust);
                else if (r < 0.5f) StartSkill(Skill.Sweep);
                else if (r < 0.8f) StartSkill(Skill.Slam);
                else StartSkill(Skill.Thrust);
            }
            else if (gap <= 26f && r < (Kind.canSlam ? 0.6f : 0.5f)) StartSkill(Skill.Thrust);
        }

        float WindupTime => Kind.windupMul * skill switch
        {
            Skill.Sweep => chain > 0 ? 0.35f : 0.55f,
            Skill.Slam => 1.0f,
            Skill.AntiAir => 0.7f,
            _ => 0.6f,
        };

        /// <summary>横薙ぎの届くすき間の距離(大きさに比例)。</summary>
        float SweepReach => 10f * Size;

        void StartSkill(Skill s)
        {
            skill = s;
            if (s != Skill.Sweep) chain = 0;
            hitDone = false;
            Enter(State.Windup, WindupTime);
        }

        Vector3 SlamCenter => transform.position + transform.forward * 7f * Size;
        float SlamRadius => 6.5f * Size;

        void UpdateWindup(float dt, Vector3 dir)
        {
            // 最後の0.15秒は向きを変えない(そこで横へ避ければ外れる)
            if (stateT < stateDur - 0.15f) { Face(dir, 10f, dt); commitDir = transform.forward; }
            if (stateT < stateDur) return;
            commitDir = transform.forward;
            thrustStart = transform.position;
            if (skill == Skill.AntiAir) { Jump(); return; }
            Enter(State.Active, skill == Skill.Thrust ? 0.3f : 0.12f);
            if (skill != Skill.Thrust) DoHit();
        }

        /// <summary>
        /// 跳び上がり斬り:予備動作の終わりの自機の胸の高さへ、ちょうど頂点で届くように跳ぶ(横も同じ時間で詰める)。
        /// 跳んだ後に自機が流れて位置がずれれば外れる。
        /// </summary>
        void Jump()
        {
            Vector3 target = player.Chest;
            float h = Mathf.Clamp(target.y - Chest.y, 2f, Kind.antiAirHeight);
            float vUp = Mathf.Sqrt(2f * Gravity * h);
            float tApex = vUp / Gravity;
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            jumpVel = Vector3.ClampMagnitude(flat / Mathf.Max(0.2f, tApex), 70f);
            SetVerticalSpeed(vUp);
            Enter(State.Active, tApex + 0.1f);
        }

        /// <summary>技が出ている間。突きは前へ一気に詰め、途中で自機に触れたら当てる。</summary>
        Vector3 UpdateActive(float dt)
        {
            Vector3 vel = Vector3.zero;
            if (skill == Skill.Thrust)
            {
                vel = commitDir * 80f;
                if (!hitDone && player != null && GapTo(player) < 3f) DoHit();
                if (Vector3.Distance(thrustStart, transform.position) > 28f) stateT = stateDur;
            }
            else if (skill == Skill.Sweep) vel = commitDir * 10f;
            else if (skill == Skill.AntiAir)
            {
                // 跳んでいる間、自機の胸に近づいたら当てる
                vel = jumpVel;
                if (!hitDone && player != null && Vector3.Distance(Chest, player.Chest) < Radius + player.Radius + 4f * Size) DoHit();
            }
            if (stateT >= stateDur)
            {
                // 横薙ぎは半分の確率で2発目へ。それ以外は隙(叩きつけが一番長い)
                if (skill == Skill.Sweep && chain == 0 && Random.value < Kind.sweepChain) { chain = 1; StartSkill(Skill.Sweep); }
                else Enter(State.Recover, skill switch { Skill.Slam => 1.1f, Skill.Thrust => 0.8f, Skill.AntiAir => 1.2f, _ => 0.6f });   // 跳び上がり斬りは落ちてくる間も隙
            }
            return vel;
        }

        void DoHit()
        {
            hitDone = true;
            if (player == null || player.Dead) return;
            var h = new DuelHit { from = transform.position };
            bool inRange;
            switch (skill)
            {
                case Skill.Slam:
                    Vector3 c = SlamCenter;
                    Vector3 d = player.transform.position - c;
                    d.y = 0f;
                    inRange = d.magnitude - player.Radius <= SlamRadius;
                    h.damage = 30f; h.guardable = false; h.stagger = 25f; h.hurtTime = 0.45f; h.knock = 22f;
                    ProtoFx.Blast(c + Vector3.up, SlamRadius, new Color(1f, 0.5f, 0.3f), 0.3f);
                    break;
                case Skill.Thrust:
                    inRange = true;   // 触れた時点で呼ばれる
                    h.damage = 20f; h.guardable = true; h.guardCost = 35f; h.stagger = 15f; h.hurtTime = 0.3f; h.knock = 18f;
                    break;
                case Skill.AntiAir:
                    inRange = true;   // 跳んでいる間に近づいた時点で呼ばれる
                    h.damage = 18f; h.guardable = true; h.guardCost = 30f; h.stagger = 18f; h.hurtTime = 0.35f; h.knock = 12f;
                    break;
                default:
                    Vector3 to = player.transform.position - transform.position;
                    to.y = 0f;
                    inRange = GapTo(player) <= SweepReach && (to.magnitude < Radius + player.Radius + 1f || Vector3.Angle(commitDir, to) <= 90f);
                    h.damage = 12f; h.guardable = true; h.guardCost = 25f; h.stagger = 10f; h.hurtTime = 0.2f; h.knock = 10f;
                    break;
            }
            h.damage *= Kind.damageMul;
            h.stagger *= Kind.playerStaggerMul;
            if (skill != Skill.AntiAir && TooFarVertically(player)) inRange = false;   // 自機が高く飛んでいれば届かない(対空は別)
            if (inRange) player.ReceiveHit(h);
        }

        void EndAttack()
        {
            chain = 0;
            Enter(State.Strafe);
            thinkAt = Mathf.Max(thinkAt, Time.time + Random.Range(0.4f, 1f));
        }

        // ───────── 被弾 ─────────

        /// <summary>
        /// 自機の攻撃が当たった。怯みゲージをため、満タンになったら怯む(技の予備動作・出ている途中でも止まる)。
        /// 怯む時間は、いっぱいにした攻撃で決まる(flinchTime)。怯んでいる間はゲージはたまらない。
        /// </summary>
        public void TakeHit(float dmg, float stagger, Vector3 knock, float flinchTime)
        {
            if (Dead) return;
            HP -= dmg;
            ProtoDamageText.Show(Chest, dmg);
            Flash(Color.white, 0.08f);
            Jolt(-7f);
            if (HP <= 0f) { Die(); return; }
            if (Current == State.Flinch) return;

            float mul = Current == State.Recover ? recoverStaggerMul : 1f;   // 攻撃の後の隙に当てると多くたまる
            Stagger += stagger * mul;
            lastStaggerHitAt = Time.time;
            if (Stagger < maxStagger) return;
            Stagger = 0f;
            chain = 0;
            Knockback(knock * 1.5f);
            Enter(State.Flinch, flinchTime);
        }

        void Die()
        {
            HP = 0f;
            StopRising();   // 跳び上がり斬りの途中で倒されたら、そのまま落ちる
            Enter(State.Dead);
            PlayDeath();   // 火花 → 爆発 → 黒焦げ
            game.OnEnemyDied(this);
            Destroy(gameObject, 3f);   // 黒焦げの残骸を少し見せてから消す
        }

        // ───────── 見た目 ─────────

        void Animate(float dt, Vector3 vel)
        {
            const string clip = MechRig.Attack;
            const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;
            float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, stateDur));
            // 機体全体の傾き(前が正)・ひねり(右が正)・浮き。技ごとに予備動作の形を変える
            float tLean = 0f, tTwist = 0f, tLift = 0f, rate = 12f;
            switch (Current)
            {
                case State.Windup:
                    switch (skill)
                    {
                        case Skill.Slam:
                            // 剣を早めに掲げきり、のけぞりながら浮き上がって溜める
                            Pose(clip, R * Mathf.Clamp01(k * 1.6f), 20f);
                            tLean = -16f * k;
                            tLift = 2f * k;
                            break;
                        case Skill.Thrust:
                            // 低く前かがみに構える(後ろへ下がるのは Update で)
                            Pose(MechRig.Dash, 0f, 12f);
                            tLean = 20f * Mathf.Clamp01(k * 2f);
                            tLift = -0.7f * k;
                            break;
                        case Skill.AntiAir:
                            // 深く沈み込み、上を見上げるようにのけぞって溜める
                            Pose(MechRig.Dash, 0f, 12f);
                            tLean = -24f * Mathf.Clamp01(k * 1.5f);
                            tLift = -1.8f * k;
                            break;
                        default:
                            // 剣を上げながら、上半身を右へひねって溜める
                            Pose(clip, R * k, 20f);
                            tTwist = 35f * k;
                            break;
                    }
                    break;
                case State.Active:
                    rate = 30f;
                    if (skill == Skill.Thrust) { Pose(MechRig.Dash, 0f, 20f); tLean = 24f; break; }
                    if (skill == Skill.AntiAir) { Pose(clip, Mathf.Lerp(R, S, k), 25f); tLean = -32f; break; }   // 上へ斬り上げながら跳ぶ
                    Pose(clip, Mathf.Lerp(R, S, k), 25f);
                    if (skill == Skill.Slam) { tLean = Mathf.Lerp(-16f, 24f, k); tLift = Mathf.Lerp(2f, 0f, k); }
                    else tTwist = Mathf.Lerp(35f, -30f, k);
                    break;
                case State.Recover:
                    Pose(clip, Mathf.Lerp(S, E, k), 20f);
                    if (skill == Skill.AntiAir) tLean = -32f * (1f - k);
                    if (skill == Skill.Slam) tLean = 24f * (1f - k);
                    else if (skill == Skill.Sweep) tTwist = -30f * (1f - k);
                    break;
                case State.Flinch:
                    // 怯み:大きくのけぞり、沈んで、よろめきながら戻る
                    Pose(MechRig.Idle, 0f, 10f);
                    FlinchPose(k, out tLean, out tTwist, out tLift);
                    rate = 30f;
                    break;
                case State.Dead:
                    Pose(MechRig.Idle, 0f, 10f);
                    tLean = -30f;
                    tLift = -1.5f;
                    rate = 6f;
                    break;
                default:
                    float sp = new Vector3(vel.x, 0f, vel.z).magnitude;
                    if (dashing) { Pose(MechRig.Dash, 0f, 12f); tLean = 8f; }
                    else if (sp > 0.5f)
                    {
                        stepPhase += sp * dt / 6f;
                        Pose(MechRig.Walk, stepPhase * 0.5f * (rig != null ? rig.Length(MechRig.Walk) : 1f), 10f);
                    }
                    else
                    {
                        idleT += dt;
                        Pose(MechRig.Idle, idleT, 6f);
                    }
                    break;
            }
            BodyOffset(tLean, tTwist, tLift, rate, dt);
        }
    }
}
