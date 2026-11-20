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
    /// 1発ごとには怯まない。射撃では上限の手前までしかたまらず、最後に崩す(怯ませる)のは近接だけ。崩れた敵を斬ると大ダメージ(DuelGame の「崩し」)。
    /// 種類(Spec)で性格を変える:剣士(中型・1.5倍の大きさ・地上の技3つ・ブースト飛行)/ 兵卒(雑魚・地上の技2つ・すぐ怯む・攻めがゆっくり)。
    /// どちらも自機が頭上を飛ぶと跳び上がり斬りを使う(確率 Spec.antiAirChance。飛べる剣士は先に飛び上がって高さを合わせる)。
    /// 剣士は自機が頭上を飛ぶと、噴射炎を出して同じ高さまで飛び上がり、空中で斬りかかる(続けて飛べるのは maxFlyTime 秒まで)。
    /// 同時に攻撃してくる数は DuelGame が絞る(順番待ちの間は回り込んで待つ)。
    /// ボス(大剣士):剣士を大きく硬くし、固有の技を3つ足したもの(大薙ぎ・突進・跳躍叩きつけ)。数値は BossMoves(ステージの DuelStage で触れる)。
    ///   大薙ぎ:深く沈み、上半身を左へ大きくひねって剣を担ぐ(普通の横薙ぎは右へひねる)→ その場で1回転して全周を斬る /
    ///   突進:剣を担いで深く前かがみになり、じりじり下がって溜める → 遠くから一直線に突っ込む(壁にぶつかると長くよろける) /
    ///   跳躍叩きつけ:剣を掲げたまま深くしゃがみ込んで溜める → 自機のいた所へ跳び、落ちた所を叩いて衝撃波を広げる(空中にいれば衝撃波は当たらない) /
    ///   三段斬り(HPが半分を切ってから):剣を正面に構えたまま左右に揺れてリズムを刻む → 踏み込みながら右・左・縦の3連撃(最後が重い)
    /// 発覚の仕組み(DuelSenses。DuelStage が UseSenses で渡す)を使う:持ち場(home)で決まった向きに立ち番し、見る・聞くで警戒度(0〜1)が溜まり、
    /// 1 で発覚して戦う。不審な間はその方向を向き、ゆっくり近づく。発覚したら見通せる近くの仲間に伝える。斬られたら発覚。
    /// 自機を見失って loseSightTime 秒たつか、自機が持ち場から leashRange より離れたら、発覚を切って持ち場へ帰る。
    /// UseSenses を呼ばれていない敵(今のステージにはいない)は、最初から発覚している。持ち場から歩いて出ない敵(leashRadius)も置ける。
    /// ファイルは役割ごとに分けている:DuelEnemy.Senses.cs(発覚)/ DuelEnemy.Skills.cs(技)/ DuelEnemy.Animate.cs(見た目)。
    /// </summary>
    public partial class DuelEnemy : DuelFighter
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
            /// <summary>ブースト飛行できるか。上昇の速さ・続けて飛べる秒数・使い切った後に飛べない秒数。</summary>
            public bool canFly;
            public float flyRiseSpeed = 18f, maxFlyTime = 4f, flyCooldown = 3f;
            /// <summary>当てられて怯んだときに吹き飛ぶ量の倍率(大きく重い機体ほど小さく)。</summary>
            public float knockMul = 1f;
            /// <summary>ボスの技の数値(null ならボスの技は使わない)。</summary>
            public BossMoves boss;
        }

        /// <summary>
        /// ボスだけの技の数値。届く範囲(Reach・Radius)は機体の大きさの倍率が掛かる前の値。ダメージには Spec.damageMul が掛かる。
        /// 予備動作の長さは、この値そのまま(Spec.windupMul は掛からない)。
        /// </summary>
        [System.Serializable]
        public class BossMoves
        {
            [Header("大薙ぎ(その場で1回転して全周を斬る)")]
            [Tooltip("予備動作:沈んで左へひねり、剣を担ぐ(秒)")] public float wideWindup = 1.1f;
            [Tooltip("1回転にかける時間(秒)。半分回ったところで当たる")] public float wideSpin = 0.45f;
            [Tooltip("届くすき間の距離(大きさの倍率が掛かる)")] public float wideReach = 11f;
            public float wideDamage = 20f, wideGuardCost = 60f, wideStagger = 22f, wideKnock = 26f;
            [Tooltip("振り終わってからの隙(秒)")] public float wideRecover = 1.0f;

            [Header("突進(遠くから一直線に突っ込む)")]
            [Tooltip("予備動作:剣を担いで前かがみになり、じりじり下がる(秒)")] public float chargeWindup = 0.9f;
            [Tooltip("予備動作の最後に向きを固める時間(秒)。この間に横へ避ければ外れる")] public float chargeCommit = 0.3f;
            public float chargeSpeed = 60f, chargeDistance = 70f;
            public float chargeDamage = 22f, chargeGuardCost = 55f, chargeStagger = 22f, chargeKnock = 30f;
            [Tooltip("突っ込み終えた後の隙(秒) / 壁にぶつかって止まったときの隙(秒)")] public float chargeRecover = 1.2f, chargeWallRecover = 2.0f;

            [Header("跳躍叩きつけ(跳んで叩きつけ、衝撃波を広げる)")]
            [Tooltip("予備動作:深くしゃがみ込む(秒)")] public float leapWindup = 0.8f;
            [Tooltip("跳んでから落ちるまで(秒)")] public float leapAirTime = 1.5f;
            [Tooltip("跳べる横の距離の上限(m)")] public float leapMaxDistance = 55f;
            [Tooltip("落ちた所の直撃の半径(大きさの倍率が掛かる)。ガード不可")] public float leapImpactRadius = 5f;
            public float leapDamage = 28f, leapStagger = 30f, leapKnock = 28f;
            [Tooltip("衝撃波の広がる速さ(m/s)・最大の半径(m)・輪の太さ(m)")] public float waveSpeed = 38f, waveMaxRadius = 45f, waveWidth = 3f;
            [Tooltip("衝撃波(ガード可。空中にいれば当たらない)")] public float waveDamage = 12f, waveGuardCost = 30f, waveStagger = 15f, waveKnock = 14f;
            [Tooltip("着地してからの隙(秒)")] public float leapRecover = 1.4f;

            [Header("三段斬り(HPが半分を切ってから使い出す。踏み込みながら右・左・縦の3連撃)")]
            [Tooltip("HPがこの割合を切ったら使い出す")] public float flurryFromHP = 0.5f;
            [Tooltip("近い間合いで技を選ぶとき、三段斬りを選ぶ確率")] public float flurryChance = 0.3f;
            [Tooltip("予備動作:剣を正面に構えたまま左右に揺れてリズムを刻み、最後に右へひねる(秒)")] public float flurryWindup = 1.0f;
            [Tooltip("2段目・3段目の振りかぶり(秒)。この間に前へ踏み込む")] public float flurryStepWindup = 0.45f;
            [Tooltip("2段目・3段目で踏み込む距離(m)")] public float flurryLunge = 6f;
            [Tooltip("届くすき間の距離(大きさの倍率が掛かる)と、正面からの角度(度)")] public float flurryReach = 8f, flurryArc = 70f;
            [Tooltip("1・2段目(横の斬り。ガード可)")] public float flurryDamage = 14f, flurryGuardCost = 25f, flurryStagger = 10f, flurryKnock = 10f;
            [Tooltip("3段目(縦の振り下ろし。ガード可だが重い)")] public float flurryLastDamage = 22f, flurryLastGuardCost = 40f, flurryLastStagger = 20f, flurryLastKnock = 24f;
            [Tooltip("3段目の後の隙(秒)")] public float flurryRecover = 1.6f;
        }

        /// <summary>剣士(中型):1対1で読み合う相手。</summary>
        public static Spec Duelist(float hp) => new() { name = "剣士", size = 1.5f, hp = hp, keepDistance = 15f, canFly = true };

        /// <summary>ボス(大剣士):剣士を大きく硬くし、固有の技を足したもの。怯みにくく、吹き飛びにくい。飛ばない。</summary>
        public static Spec Boss(float hp, BossMoves moves, float size = 2.5f, float maxStagger = 70f) => new()
        {
            name = "大剣士", size = size, hp = hp, maxStagger = maxStagger, damageMul = 1.3f, windupMul = 1.25f,
            thinkMin = 0.9f, thinkMax = 1.6f, approachSpeed = 11f, strafeSpeed = 5f, keepDistance = 16f, sweepChain = 0.5f,
            dashSpeed = 30f, sideDashRate = 0.08f, antiAirChance = 0.6f, antiAirHeight = 40f, knockMul = 0.25f, boss = moves ?? new BossMoves(),
        };

        /// <summary>兵卒(雑魚):複数で来る前提。地上の技は横薙ぎと突きだけ(叩きつけはない)、2発で怯み、攻めはゆっくり。</summary>
        public static Spec Grunt(float hp) => new()
        {
            name = "兵卒", size = 1f, hp = hp, maxStagger = 10f, damageMul = 0.65f, playerStaggerMul = 0.7f, windupMul = 1.15f,
            thinkMin = 1.5f, thinkMax = 2.8f, approachSpeed = 12f, strafeSpeed = 6f, keepDistance = 14f, sweepChain = 0f, canSlam = false,
            dashSpeed = 26f, sideDashRate = 0.1f, antiAirChance = 0.35f, antiAirHeight = 30f,
        };

        public Spec Kind { get; private set; }
        /// <summary>噴射炎:飛行中・ダッシュ中。</summary>
        public override bool Thrusting => flying || dashing;
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
        public enum Skill { Sweep, Slam, Thrust, AntiAir, WideSweep, Charge, LeapSlam, Flurry }
        public bool IsBoss => Kind != null && Kind.boss != null;
        BossMoves B => Kind.boss;

        [Header("ステージ")]
        [Tooltip("持ち場(立ち番する所)と、歩いて出ない半径(0 なら制限なし。段差の上で待ち構える敵など)")]
        public Vector3 home;
        public float leashRadius;
        [Tooltip("持ち場で向く向き")]
        public Quaternion homeRotation = Quaternion.identity;
        /// <summary>自機が持ち場から leashRange より離れていても、発覚を切らない・気づく(ボス。部屋が見えない壁で囲まれている)。</summary>
        [HideInInspector] public bool ignoreLeashRange;

        public State Current { get; private set; }
        public float Stagger { get; private set; }
        public bool Flinching => Current == State.Flinch;

        DuelPlayer player;
        Skill skill;
        float stateT, stateDur, thinkAt, lastStaggerHitAt = -10f, strafeSign = 1f, stepPhase;
        int chain;           // 横薙ぎの何発目か
        bool dashing;        // このフレームはダッシュで動いている(見た目用)
        Vector3 jumpVel;     // 跳び上がり斬りの横の速さ
        float sideDashT;     // 横ダッシュの残り時間
        Vector3 commitDir;   // 振りかぶりの最後に決めた向き(そこからは向きを変えない=横へ避けられる)
        Vector3 thrustStart;
        bool hitDone;
        Skill lastSkill;
        Vector3 lastPos;      // 突進:前のフレームの位置と、そのフレームの時間(壁にぶつかったかを見る)
        float lastDt;
        bool chargeWall;      // 突進が壁で止まった
        Quaternion spinFrom;  // 大薙ぎ:回り始めの向き
        int flurryIndex;      // 三段斬りの何段目か(0〜2)
        bool impactInRange;   // 跳躍叩きつけの直撃の範囲に自機がいた(衝撃波を重ねて当てない)

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
            if (spec.canFly || spec.boss != null) gameObject.AddComponent<DuelThruster>().Init(this, rig);   // 飛行中・ダッシュ中の噴射炎
        }

        // ───────── ブースト飛行(剣士) ─────────

        float flyTime, flyCooldownUntil = -10f;
        bool CanFlyNow => Kind.canFly && Time.time >= flyCooldownUntil;

        /// <summary>
        /// 自機が頭上を飛んでいれば、同じ高さまで飛び上がって留まる。自機が降りたら飛ぶのをやめて落ちる。
        /// 続けて飛べるのは maxFlyTime 秒まで(使い切ったら flyCooldown 秒は飛べない)。怯み・跳び上がり斬りの間は飛ばない。
        /// </summary>
        void UpdateFlight(float dt)
        {
            bool want = false;
            if (CanFlyNow && player != null && !player.Dead && Engaged && Current != State.Flinch && Current != State.Dead &&
                !(Current == State.Active && skill == Skill.AntiAir))
            {
                float above = player.transform.position.y - transform.position.y;
                // 自機が高く飛んだら飛び上がる。すでに飛んでいれば、自機が空中にいる間は高さを合わせ続ける
                if (player.Airborne && (above > game.mechHeight * Size * 0.6f || (flying && Airborne)))
                {
                    want = true;
                    riseSpeed = Mathf.Clamp(above * 1.5f, -12f, Kind.flyRiseSpeed);   // 近づくほどゆっくり、上なら下りる
                    riseAccel = 60f;
                }
            }
            if (want)
            {
                flyTime += dt;
                if (flyTime >= Kind.maxFlyTime) { want = false; flyCooldownUntil = Time.time + Kind.flyCooldown; }
            }
            else if (!Airborne) flyTime = 0f;   // 着地したら燃料が戻る
            flying = want;
        }

        void Enter(State s, float dur = 0f)
        {
            Current = s;
            stateT = 0f;
            stateDur = dur;
        }

        void Update()
        {
            if (DuelGame.Paused) return;   // 装備の確認を開いている間は止める
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            if (Current == State.Dead) { Move(Vector3.zero, dt); return; }

            if (now - lastStaggerHitAt >= staggerDecayDelay && Current != State.Flinch)
                Stagger = Mathf.Max(0f, Stagger - staggerDecay * dt);

            if (Engaged && UpdateEngaged(dt))
            {
                // 発覚を切ったフレームは、技を考えずにここで終える(次のフレームから持ち場へ帰る)
                flying = false;
                dashing = false;
                Move(Vector3.zero, dt);
                Animate(dt, Vector3.zero);
                return;
            }
            if (!Engaged && Current is State.Approach or State.Strafe)
            {
                // まだ気づいていない:立ち番・不審・持ち場へ帰る
                Vector3 calm = UpdateUnaware(dt);
                if (!Engaged)
                {
                    flying = false;
                    dashing = false;
                    calm = LimitToLeash(calm);
                    bool settled = calm == Vector3.zero && cc.isGrounded && !Airborne;
                    // 持ち場で落ち着いて立っている間は、移動の計算を止める。自機から十分遠ければ姿勢の計算も止める(重さ)
                    if (!settled) Move(calm, dt);
                    if (!settled || Awareness > 0f || !PlayerFar()) Animate(dt, calm);
                    return;
                }
            }

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
                    if (playerAlive && now >= thinkAt && (gap <= 26f || IsBoss)) Think(gap);   // ボスは遠くからも突進・跳躍で詰める
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
                    // 突き・突進の溜め:少し後ろへ下がる
                    if (Current == State.Windup && skill == Skill.Thrust) vel = -transform.forward * 5f;
                    if (Current == State.Windup && skill == Skill.Charge) vel = -transform.forward * 6f;
                    // 三段斬りの2段目・3段目:振りかぶりながら前へ踏み込む
                    if (Current == State.Windup && skill == Skill.Flurry && flurryIndex > 0)
                        vel = transform.forward * (B.flurryLunge / Mathf.Max(0.05f, B.flurryStepWindup));
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

            // 持ち場から歩いて出ない(外へ向かう分の速さを消す。技の最中は止めない)
            if (Current is State.Approach or State.Strafe)
            {
                Vector3 limited = LimitToLeash(vel);
                if (limited != vel) dashing = false;
                vel = limited;
            }

            UpdateFlight(dt);
            lastPos = transform.position;
            lastDt = dt;
            Move(vel, dt);
            Animate(dt, vel);
        }

        /// <summary>自機が、姿勢の計算を止めてよいほど遠い(見える距離の2倍より遠い)。</summary>
        bool PlayerFar()
        {
            if (player == null || senses == null) return false;
            float r = senses.sightRange * 2f;
            return (player.transform.position - transform.position).sqrMagnitude > r * r;
        }

        /// <summary>持ち場の半径(leashRadius)の外へ向かう分の速さを消す。</summary>
        Vector3 LimitToLeash(Vector3 vel)
        {
            if (leashRadius <= 0f) return vel;
            Vector3 off = transform.position - home;
            off.y = 0f;
            if (off.magnitude <= leashRadius || Vector3.Dot(vel, off) <= 0f) return vel;
            return vel - off.normalized * Vector3.Dot(vel, off.normalized);
        }

        // ───────── 被弾 ─────────

        /// <summary>射撃(自機の肩の武装)で受けるダメージと怯みの倍率(ボスの厚い装甲。1 なら減らない)。ステージが置くときに決める。</summary>
        [HideInInspector] public float rangedTakenMul = 1f;

        /// <summary>射撃が当たった(rangedTakenMul を掛けてから TakeHit)。</summary>
        public void TakeRangedHit(float dmg, float stagger, Vector3 knock, float flinchTime) =>
            TakeHit(dmg * rangedTakenMul, stagger * rangedTakenMul, knock, flinchTime, true);

        /// <summary>
        /// 自機の攻撃が当たった。怯みゲージをため、満タンになったら怯む(技の予備動作・出ている途中でも止まる)。
        /// 怯む時間は、いっぱいにした攻撃で決まる(flinchTime。近接で崩したときは DuelGame.breakFlinchMin 秒より短くしない)。
        /// 怯んでいる間はゲージはたまらない。
        /// 崩し(DuelGame):射撃(ranged)は怯みゲージを上限(rangedStaggerCap)の手前までしかためず、怯ませない
        /// (knock の大きい射撃は1発ごとに軽く押し返すだけ)。最後に崩すのは近接だけ。
        /// 崩れている(怯んでいる)敵に近接を当てると、ダメージが brokenMeleeDamageMul 倍(数字は橙)。
        /// </summary>
        public void TakeHit(float dmg, float stagger, Vector3 knock, float flinchTime, bool ranged = false)
        {
            if (Dead) return;
            Engage();   // 斬られたら発覚(見通せる近くの仲間にも伝わる)
            if (senses != null) holdEngagedUntil = Time.time + senses.holdAfterHit;   // 斬られた直後は、持ち場から遠くても発覚を切らない
            bool broken = Current == State.Flinch && !ranged;   // 崩れた敵を斬った
            if (broken) dmg *= game.brokenMeleeDamageMul;
            HP -= dmg;
            ProtoDamageText.Show(Chest, dmg, broken);   // 崩れた敵への大ダメージは橙の数字
            Flash(Color.white, 0.08f);
            Jolt(-7f);
            if (HP <= 0f) { Die(); return; }
            if (Current == State.Flinch) return;

            float mul = Current == State.Recover ? recoverStaggerMul : 1f;   // 攻撃の後の隙に当てると多くたまる
            lastStaggerHitAt = Time.time;
            if (ranged && game.rangedStaggerCap < 1f)
            {
                // 射撃:上限の手前までしかためない(近接で上限より上にためてあれば、そのまま)。怯ませない
                float cap = maxStagger * game.rangedStaggerCap;
                if (Stagger < cap) Stagger = Mathf.Min(cap, Stagger + stagger * mul);
                // knock の大きい射撃(散弾・砲など)は、1発ごとに軽く押し返す(技は止めない)
                Vector3 shove = knock * game.rangedPushMul * Kind.knockMul;
                if (knock.magnitude >= game.rangedPushMin && shove.sqrMagnitude > push.sqrMagnitude) Knockback(shove);
                return;
            }
            Stagger += stagger * mul;
            if (Stagger < maxStagger) return;
            Stagger = 0f;
            chain = 0;
            Knockback(knock * 1.5f * Kind.knockMul);
            StopRising();   // 空中で怯んだら上昇が止まり、怯んでいる間は落ちる
            Enter(State.Flinch, ranged ? flinchTime : Mathf.Max(flinchTime, game.breakFlinchMin));
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
    }
}
