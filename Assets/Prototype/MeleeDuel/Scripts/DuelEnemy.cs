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
    /// 種類(Spec)で性格を変える:剣士(中型・1.5倍の大きさ・技3つ・ブースト飛行)/ 兵卒(雑魚・技2つ・すぐ怯む・攻めがゆっくり)。
    /// 剣士は自機が頭上を飛ぶと、噴射炎を出して同じ高さまで飛び上がり、空中で斬りかかる(続けて飛べるのは maxFlyTime 秒まで)。
    /// 同時に攻撃してくる数は DuelGame が絞る(順番待ちの間は回り込んで待つ)。
    /// ボス(大剣士):剣士を大きく硬くし、固有の技を3つ足したもの(大薙ぎ・突進・跳躍叩きつけ)。数値は BossMoves(ステージの DuelStage で触れる)。
    ///   大薙ぎ:深く沈み、上半身を左へ大きくひねって剣を担ぐ(普通の横薙ぎは右へひねる)→ その場で1回転して全周を斬る /
    ///   突進:剣を担いで深く前かがみになり、じりじり下がって溜める → 遠くから一直線に突っ込む(壁にぶつかると長くよろける) /
    ///   跳躍叩きつけ:剣を掲げたまま深くしゃがみ込んで溜める → 自機のいた所へ跳び、落ちた所を叩いて衝撃波を広げる(空中にいれば衝撃波は当たらない) /
    ///   三段斬り(HPが半分を切ってから):剣を正面に構えたまま左右に揺れてリズムを刻む → 踏み込みながら右・左・縦の3連撃(最後が重い)
    /// ステージでは発覚の仕組み(DuelSenses)を使う:持ち場(home)で決まった向きに立ち番し、見る・聞くで警戒度(0〜1)が溜まり、
    /// 1 で発覚して戦う。不審な間はその方向を向き、ゆっくり近づく。発覚したら見通せる近くの仲間に伝える。斬られたら発覚。
    /// 自機を見失って loseSightTime 秒たつか、自機が持ち場から leashRange より離れたら、発覚を切って持ち場へ帰る。
    /// 部屋のモード(stealth なし)では、最初から発覚している。持ち場から歩いて出ない敵(leashRadius)も置ける。
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

        /// <summary>兵卒(雑魚):複数で来る前提。技は横薙ぎと突きだけ、2発で怯み、攻めはゆっくり。</summary>
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

        // ───────── 発覚(ステージ) ─────────

        /// <summary>発覚の数値(null なら発覚の仕組みを使わず、最初から戦う=部屋のモード)。</summary>
        DuelSenses senses;
        /// <summary>自機に気づいて戦っている。</summary>
        public bool Engaged { get; private set; } = true;
        /// <summary>警戒度(0〜1)。1 で発覚。画面には出さない。</summary>
        public float Awareness { get; private set; }
        /// <summary>不審(気づいてはいないが、何かを見聞きした)。</summary>
        public bool Suspicious => !Engaged && Awareness >= 0.12f;
        float quietFor, attentionFor, unseenFor, sightCheckAt, stuckFor;
        int heardStep;
        bool seesPlayer;
        Vector3 lastKnown, stuckFrom;

        /// <summary>ステージ:発覚の仕組みを使い、持ち場で立ち番を始める(まだ気づいていない)。</summary>
        public void UseSenses(DuelSenses s)
        {
            senses = s;
            Engaged = false;
            ResetAwareness();
            heardStep = player != null ? player.FootstepSequence : 0;   // 置く前の足音は聞かない
        }

        /// <summary>目の高さ(機体の高さの85%)。</summary>
        Vector3 Eye => transform.position + Vector3.up * game.mechHeight * Size * 0.85f;

        void ResetAwareness()
        {
            Awareness = 0f;
            quietFor = attentionFor = unseenFor = 0f;
        }

        /// <summary>
        /// 発覚する(戦い始める)。alert なら、見通せる近くの仲間(まだ気づいていない敵)に伝える。伝えられた側からは連鎖しない。
        /// </summary>
        public void Engage(bool alert = true)
        {
            if (Engaged || Dead || player == null || player.Dead) return;
            Engaged = true;
            Awareness = 1f;
            unseenFor = 0f;
            lastKnown = player.transform.position;
            if (Current is State.Approach or State.Strafe) Enter(State.Approach);
            thinkAt = Mathf.Max(thinkAt, Time.time + Random.Range(0.6f, 1.2f));   // 気づいてから少しして技を考え始める
            player.AutoLock(this);
            if (!alert || senses == null) return;
            foreach (var other in game.Enemies)
            {
                if (other == null || other == this || other.Dead || other.Engaged || other.senses == null) continue;
                if ((other.transform.position - transform.position).sqrMagnitude > senses.alertRadius * senses.alertRadius) continue;
                if (DuelSenses.ClearSight(Eye, other.Eye)) other.ReceiveAlert(lastKnown);
            }
        }

        /// <summary>仲間から伝えられた:自機が見えていれば発覚、見えていなければ不審から(その場所のほうを向く)。</summary>
        void ReceiveAlert(Vector3 knownPos)
        {
            if (SightRate(out _) > 0f) { Engage(false); return; }
            Awareness = Mathf.Max(Awareness, senses.alertAwareness);
            lastKnown = knownPos;
            quietFor = 0f;
            attentionFor = senses.attentionTime;
        }

        /// <summary>発覚を切る(見失った・持ち場から離れすぎた):警戒度を0に戻し、持ち場へ歩いて帰る。</summary>
        void Disengage()
        {
            Engaged = false;
            ResetAwareness();
            chain = 0;
            Enter(State.Approach);
        }

        /// <summary>
        /// やり直し(セーブポイントから再開):HPを満タンにして持ち場へ戻し、発覚・警戒を切る。
        /// 発覚の仕組みを使わない敵(部屋のモード)は、発覚したままにする。
        /// </summary>
        public void ResetToHome()
        {
            if (Dead) return;
            Revive();
            Stagger = 0f;
            chain = 0;
            flyTime = 0f;
            Teleport(home, homeRotation);
            Enter(State.Approach);
            ResetAwareness();
            stuckFor = 0f;
            if (senses != null)
            {
                Engaged = false;
                heardStep = player != null ? player.FootstepSequence : 0;
            }
        }

        /// <summary>
        /// 見えたときの警戒度の溜まる速さ(1秒あたり)。0 なら見えていない。inFront は中央の視野で体の3点のうち2点以上が見えた(即発覚)。
        /// 視野:前方 viewAngle、近くは横 closeSideAngle まで。体の3点(腰・胸・頭)のうち見えた数で速さが変わる。
        /// </summary>
        float SightRate(out bool inFront)
        {
            inFront = false;
            if (player == null || player.Dead) return 0f;
            var s = senses;
            Vector3 to = player.transform.position - transform.position;
            float distance = to.magnitude;
            if (distance >= s.sightRange) return 0f;
            to.y = 0f;
            float facing = to.sqrMagnitude > 0.001f ? Vector3.Dot(transform.forward, to.normalized) : 1f;
            bool touching = GapTo(player) < s.touchGap && !TooFarVertically(player);
            bool closeSide = distance <= s.closeSideRange && facing >= s.CloseSideCos;
            if (!touching && !closeSide && facing < s.ViewEdgeCos) return 0f;
            Vector3 eye = Eye, p = player.transform.position;
            float h = game.mechHeight * player.Size;
            int visible = 0;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.6f)) visible++;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.88f)) visible++;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.3f)) visible++;
            if (visible == 0) return 0f;
            // 即発覚は、体の3点のうち2点以上が見えるとき(物陰から頭だけ・足だけがのぞく程度なら、徐々に警戒する)
            inFront = facing >= s.ViewCenterCos && visible >= 2;
            if (touching) return 8f;
            if (closeSide) return s.closeSideRate * (visible / 3f);
            float central = Mathf.Lerp(s.edgeMul, 1f, Mathf.InverseLerp(s.ViewEdgeCos, s.ViewCenterCos, facing));
            float near = 1f - Mathf.Clamp01(distance / s.sightRange);
            return Mathf.Lerp(s.farRate, s.nearRate, near * near) * central * (visible / 3f);
        }

        /// <summary>自機の最新の足音が届いた強さ(0〜1)。壁・床ごしは届く範囲が縮む。</summary>
        float HearingStrength()
        {
            if (player == null || player.Dead || player.FootstepRadius <= 0f || Time.time - player.FootstepTime > 0.8f) return 0f;
            Vector3 sound = player.FootstepPosition + Vector3.up;
            Vector3 ear = Eye;
            float distance = Vector3.Distance(ear, sound);
            float radius = player.FootstepRadius;
            if (distance >= radius) return 0f;   // 届かない足音は、遮蔽を調べる前に打ち切る
            if (!DuelSenses.ClearSight(ear, sound)) radius *= senses.muffled;
            return Mathf.Clamp01(1f - distance / radius);
        }

        /// <summary>まだ気づいていない間の、見る・聞くで警戒度を上げ下げする。1 になったら発覚。</summary>
        void TickAwareness(float dt)
        {
            if (player == null || player.Dead) { ResetAwareness(); return; }
            var s = senses;
            // 見えも聞こえもしない遠さなら、レイを撃たずに減らすだけ
            float far = Mathf.Max(s.sightRange, s.boostStep, s.flyStep);
            bool inReach = (player.transform.position - transform.position).sqrMagnitude < far * far;
            float sight = 0f;
            bool inFront = false;
            if (inReach) sight = SightRate(out inFront);
            if (inFront) { Engage(); return; }
            bool heard = false;
            if (heardStep != player.FootstepSequence)
            {
                heardStep = player.FootstepSequence;
                float strength = inReach ? HearingStrength() : 0f;
                if (strength > 0f)
                {
                    Awareness = Mathf.Max(Awareness, Mathf.Min(s.hearingCap, Awareness + strength * s.hearingGain));
                    lastKnown = player.FootstepPosition;
                    heard = true;
                }
            }
            if (sight > 0f)
            {
                Awareness = Mathf.Min(1f, Awareness + sight * dt);
                lastKnown = player.transform.position;
            }
            if (sight > 0f || heard) { quietFor = 0f; attentionFor = s.attentionTime; }
            else
            {
                quietFor += dt;
                attentionFor = Mathf.Max(0f, attentionFor - dt);
                if (quietFor > s.quietDelay) Awareness = Mathf.MoveTowards(Awareness, 0f, s.decayRate * dt);
            }
            if (Awareness >= 1f) Engage();
        }

        /// <summary>
        /// まだ気づいていない間の動き:不審ならその方向を向き、見通せて持ち場の範囲内ならゆっくり近づく(壁ごし・上下階・持ち場の外へは追わない)。
        /// 何もなければ持ち場へ歩いて戻り、決まった向きで立ち番する。
        /// </summary>
        Vector3 UpdateUnaware(float dt)
        {
            TickAwareness(dt);
            if (Engaged) return Vector3.zero;
            var s = senses;
            Vector3 vel = Vector3.zero;
            if (Suspicious && attentionFor > 0f)
            {
                Vector3 to = lastKnown - transform.position;
                to.y = 0f;
                Face(to, 5f, dt);
                Vector3 fromHome = lastKnown - home;
                fromHome.y = 0f;
                bool approach = Awareness >= s.investigateFrom && to.magnitude > Radius + 6f &&
                                Mathf.Abs(lastKnown.y - transform.position.y) < s.investigateMaxHeight &&
                                fromHome.magnitude < s.investigateRadius &&
                                DuelSenses.ClearSight(Eye, lastKnown + Vector3.up * game.mechHeight * 0.6f);
                if (approach) vel = to.normalized * approachSpeed * s.investigateSpeed;
                stuckFor = 0f;
                return vel;
            }
            Vector3 toHome = home - transform.position;
            float vertical = Mathf.Abs(toHome.y);
            toHome.y = 0f;
            if (toHome.magnitude > 2f || vertical > 4f)
            {
                Face(toHome, 6f, dt);
                vel = toHome.magnitude > 0.5f ? toHome.normalized * approachSpeed * 0.6f : Vector3.zero;
                // 持ち場へ帰れない(段差の下へ落ちた・何かに引っかかった)まま数秒たったら、持ち場へ戻す
                if (stuckFor <= 0f) stuckFrom = transform.position;
                stuckFor += dt;
                if (stuckFor > 4f)
                {
                    if ((transform.position - stuckFrom).magnitude < 3f) Teleport(home, homeRotation);
                    stuckFor = 0f;
                }
            }
            else
            {
                stuckFor = 0f;
                Face(homeRotation * Vector3.forward, 4f, dt);
            }
            return vel;
        }

        /// <summary>
        /// 発覚している間:自機が見えているかを数フレームおきに確かめ、見えない状態が loseSightTime 秒続いたら発覚を切る。
        /// 自機が持ち場から leashRange より離れても切る。技の途中では切らない。
        /// </summary>
        void UpdateEngaged(float dt)
        {
            if (senses == null || player == null || player.Dead) return;
            if (Time.time >= sightCheckAt)
            {
                sightCheckAt = Time.time + 0.15f;
                Vector3 p = player.transform.position;
                float h = game.mechHeight * player.Size;
                seesPlayer = (p - transform.position).sqrMagnitude < senses.keepSightRange * senses.keepSightRange &&
                             (DuelSenses.ClearSight(Eye, p + Vector3.up * h * 0.6f) || DuelSenses.ClearSight(Eye, p + Vector3.up * h * 0.88f));
            }
            if (seesPlayer) { unseenFor = 0f; lastKnown = player.transform.position; }
            else unseenFor += dt;
            Vector3 ph = player.transform.position - home;
            ph.y = 0f;
            bool leashed = senses.leashRange > 0f && ph.magnitude > senses.leashRange;
            if ((unseenFor >= senses.loseSightTime || leashed) && Current is State.Approach or State.Strafe) Disengage();
        }

        /// <summary>自機がいる間ずっと発覚させておく(ボスの部屋に入った)。見失いの時計も戻す。</summary>
        public void KeepEngaged()
        {
            if (!Engaged) Engage(false);
            unseenFor = 0f;
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
            float dt = Time.deltaTime;
            float now = Time.time;
            stateT += dt;
            UpdateTint(dt);
            if (Current == State.Dead) { Move(Vector3.zero, dt); return; }

            if (now - lastStaggerHitAt >= staggerDecayDelay && Current != State.Flinch)
                Stagger = Mathf.Max(0f, Stagger - staggerDecay * dt);

            if (!Engaged && Current is State.Approach or State.Strafe)
            {
                // まだ気づいていない:立ち番・不審・持ち場へ帰る
                Vector3 calm = UpdateUnaware(dt);
                if (!Engaged)
                {
                    flying = false;
                    dashing = false;
                    calm = LimitToLeash(calm);
                    // 持ち場で落ち着いて立っている間は、移動の計算を止める(姿勢だけ)
                    if (calm != Vector3.zero || !cc.isGrounded || Airborne) Move(calm, dt);
                    Animate(dt, calm);
                    return;
                }
            }
            if (Engaged) UpdateEngaged(dt);

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

        /// <summary>持ち場の半径(leashRadius)の外へ向かう分の速さを消す。</summary>
        Vector3 LimitToLeash(Vector3 vel)
        {
            if (leashRadius <= 0f) return vel;
            Vector3 off = transform.position - home;
            off.y = 0f;
            if (off.magnitude <= leashRadius || Vector3.Dot(vel, off) <= 0f) return vel;
            return vel - off.normalized * Vector3.Dot(vel, off.normalized);
        }

        // ───────── 技を選ぶ ─────────

        void Think(float gap)
        {
            // 同時に攻撃している敵が多ければ、順番を待つ(回り込みながら少しして考え直す)
            if (!game.CanStartAttack(this)) { thinkAt = Time.time + Random.Range(0.3f, 0.6f); return; }
            // 持ち場のある敵は、自機が持ち場の近くへ来るまで技を出さない(段差の下へ突きで飛び降りてこない)
            if (leashRadius > 0f)
            {
                Vector3 ph = player.transform.position - home;
                ph.y = 0f;
                if (ph.magnitude > leashRadius + 6f) { thinkAt = Time.time + 0.3f; return; }
            }
            thinkAt = Time.time + Random.Range(Kind.thinkMin, Kind.thinkMax);
            // 自機が頭上を飛んでいる:跳び上がり斬りで落とす(届かない高さ・選ばなかったときは見上げて待つ)
            float above = player.transform.position.y - transform.position.y;
            if (player.Airborne && above > game.mechHeight * Size * 0.8f)
            {
                if (CanFlyNow) return;   // 飛べる剣士は、飛び上がって高さを合わせてから斬る
                if (above <= Kind.antiAirHeight && gap <= 30f && Random.value < Kind.antiAirChance) StartSkill(Skill.AntiAir);
                return;
            }
            float r = Random.value;
            if (IsBoss) { ThinkBoss(gap, r); return; }
            // 自機と高さが大きく違う(段差の上と下)ときは突きを選ばない(台の上から突きで飛び降りない)
            bool thrustOk = Mathf.Abs(above) < ThrustMaxHeightGap;
            if (gap <= SweepReach)
            {
                if (!Kind.canSlam) StartSkill(r < 0.7f || !thrustOk ? Skill.Sweep : Skill.Thrust);
                else if (r < 0.5f) StartSkill(Skill.Sweep);
                else if (r < 0.8f) StartSkill(Airborne ? Skill.Sweep : Skill.Slam);   // 空中では叩きつけは使わない
                else StartSkill(thrustOk ? Skill.Thrust : Skill.Sweep);
            }
            else if (thrustOk && gap <= 26f && r < (Kind.canSlam ? 0.6f : 0.5f)) StartSkill(Skill.Thrust);
        }

        /// <summary>
        /// ボスの技選び。遠ければ突進か跳躍叩きつけで詰め、近ければ横薙ぎ・叩きつけ・大薙ぎを混ぜる。
        /// HPが半分を切ったら、近い間合いで三段斬りも混ぜる。
        /// 固有の技(大薙ぎ・突進・跳躍叩きつけ・三段斬り)は続けて同じものを出さない。
        /// </summary>
        void ThinkBoss(float gap, float r)
        {
            bool enraged = HP < MaxHP * B.flurryFromHP;
            bool flurryOk = enraged && gap <= B.flurryReach * Size;   // 三段斬りは1段目が届く間合いだけ
            Skill s;
            if (gap > SweepReach)
            {
                // 遠い:詰める技だけから選ぶ(前回と同じ固有の技・空中で使えない技は除く)。どれも使えなければ歩いて詰める
                bool thrustOk = gap <= 26f && Mathf.Abs(player.transform.position.y - transform.position.y) < ThrustMaxHeightGap;
                bool chargeOk = gap <= B.chargeDistance - 10f && lastSkill != Skill.Charge;
                bool leapOk = gap <= B.leapMaxDistance && lastSkill != Skill.LeapSlam && !Airborne;
                if (thrustOk && r < 0.2f) s = Skill.Thrust;
                else if (chargeOk && (!leapOk || r < 0.6f)) s = Skill.Charge;
                else if (leapOk) s = Skill.LeapSlam;
                else if (thrustOk) s = Skill.Thrust;
                else return;
                StartSkill(s);
                return;
            }
            if (flurryOk && Random.value < B.flurryChance) s = Skill.Flurry;
            else if (r < 0.25f) s = Skill.WideSweep;
            else if (r < 0.55f) s = Skill.Sweep;
            else if (r < 0.75f) s = Skill.Slam;
            else if (r < 0.87f) s = Skill.LeapSlam;
            else s = Skill.Thrust;
            // 近い間合いでの置き換えは横薙ぎ(届く)
            if (s == lastSkill && s is Skill.WideSweep or Skill.Charge or Skill.LeapSlam or Skill.Flurry) s = Skill.Sweep;
            if (Airborne && s is Skill.Slam or Skill.LeapSlam) s = Skill.Sweep;   // 空中では叩きつけ・跳躍は使わない
            StartSkill(s);
        }

        float WindupTime => skill switch
        {
            Skill.WideSweep => B.wideWindup,
            Skill.Charge => B.chargeWindup,
            Skill.LeapSlam => B.leapWindup,
            Skill.Flurry => flurryIndex == 0 ? B.flurryWindup : B.flurryStepWindup,
            _ => Kind.windupMul * skill switch
            {
                Skill.Sweep => chain > 0 ? 0.35f : 0.55f,
                Skill.Slam => 1.0f,
                Skill.AntiAir => 0.7f,
                _ => 0.6f,
            },
        };

        /// <summary>突きを選んでよい、自機との高さの差(m)。これより大きいと突きで段差を飛び降りてしまう。</summary>
        const float ThrustMaxHeightGap = 2.5f;

        /// <summary>予備動作の最後に向きを固める時間(そこからは向きを変えない=横へ避ければ外れる)。</summary>
        float CommitTime => skill == Skill.Charge ? B.chargeCommit : 0.15f;

        /// <summary>横薙ぎの届くすき間の距離(大きさに比例)。</summary>
        float SweepReach => 10f * Size;

        void StartSkill(Skill s)
        {
            skill = s;
            lastSkill = s;
            if (s != Skill.Sweep) chain = 0;
            hitDone = false;
            chargeWall = false;
            flurryIndex = 0;
            Enter(State.Windup, WindupTime);
        }

        Vector3 SlamCenter => transform.position + transform.forward * 7f * Size;
        float SlamRadius => 6.5f * Size;

        void UpdateWindup(float dt, Vector3 dir)
        {
            // 最後の0.15秒(突進は長め)は向きを変えない(そこで横へ避ければ外れる)
            if (stateT < stateDur - CommitTime) { Face(dir, 10f, dt); commitDir = transform.forward; }
            if (stateT < stateDur) return;
            commitDir = transform.forward;
            thrustStart = transform.position;
            if (skill == Skill.AntiAir) { Jump(); return; }
            if (skill == Skill.LeapSlam) { Leap(); return; }
            if (skill == Skill.Charge) { Enter(State.Active, B.chargeDistance / Mathf.Max(1f, B.chargeSpeed) + 0.2f); return; }
            if (skill == Skill.WideSweep) { spinFrom = transform.rotation; Enter(State.Active, B.wideSpin); return; }
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

        /// <summary>跳躍叩きつけ:予備動作の終わりに自機のいた所へ、leapAirTime 秒で落ちるように跳ぶ。</summary>
        void Leap()
        {
            Vector3 flat = player.transform.position - transform.position;
            flat.y = 0f;
            flat = Vector3.ClampMagnitude(flat, B.leapMaxDistance);
            float t = Mathf.Max(0.4f, B.leapAirTime);
            leapVel = flat / t;
            SetVerticalSpeed(Gravity * t * 0.5f);   // ちょうど t 秒で同じ高さへ落ちる
            Enter(State.Active, t + 1.5f);          // 着地で終わる(落ちきらないときの保険)
        }

        Vector3 leapVel;

        /// <summary>跳躍叩きつけの着地:落ちた所を叩き(直撃)、衝撃波を広げる。</summary>
        void Land()
        {
            DoHit();   // 直撃の範囲にいれば impactInRange が立つ
            Vector3 at = transform.position;
            ProtoFx.Blast(at + Vector3.up, B.leapImpactRadius * Size, new Color(1f, 0.6f, 0.3f), 0.35f);
            DuelSpark.Burst(at + Vector3.up, Vector3.up, new Color(1f, 0.75f, 0.45f), 40, 1.6f * Size);
            var wave = new DuelHit
            {
                damage = B.waveDamage * Kind.damageMul, guardable = true, guardCost = B.waveGuardCost,
                stagger = B.waveStagger * Kind.playerStaggerMul, hurtTime = 0.3f, knock = B.waveKnock, from = at,
            };
            DuelShockwave.Spawn(at, B.leapImpactRadius * Size, B.waveMaxRadius, B.waveSpeed, B.waveWidth, wave, impactInRange);   // 直撃の範囲にいた相手には衝撃波は当てない
            Enter(State.Recover, B.leapRecover);
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
            else if (skill == Skill.Charge)
            {
                // 突進:一直線に突っ込み、途中で自機に触れたら当てる。進めなくなったら(壁・自機に阻まれた)止まる
                vel = commitDir * B.chargeSpeed;
                dashing = true;
                if (!hitDone && player != null && GapTo(player) < 3f && !TooFarVertically(player)) DoHit();
                float movedLast = (transform.position - lastPos).magnitude;
                if (stateT > 0.12f && movedLast < B.chargeSpeed * lastDt * 0.3f) { chargeWall = !hitDone; stateT = stateDur; }
                if (Vector3.Distance(thrustStart, transform.position) > B.chargeDistance) stateT = stateDur;
            }
            else if (skill == Skill.WideSweep)
            {
                // 大薙ぎ:その場で1回転(終わりはゆっくり)。半分回ったところで全周に当てる
                float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, stateDur));
                float turn = 1f - (1f - k) * (1f - k);
                transform.rotation = spinFrom * Quaternion.Euler(0f, 360f * turn, 0f);
                if (!hitDone && k >= 0.5f) DoHit();
                if (k >= 1f) transform.rotation = spinFrom;
            }
            else if (skill == Skill.LeapSlam)
            {
                // 跳んでいる間は噴射炎。落ちたら叩きつける
                dashing = true;
                if ((stateT > 0.25f && !Airborne) || stateT >= stateDur) { Land(); return Vector3.zero; }
                return leapVel;
            }
            if (stateT >= stateDur && skill == Skill.Flurry && flurryIndex < 2)
            {
                // 三段斬り:次の段へ(踏み込みながら振りかぶる)
                flurryIndex++;
                hitDone = false;
                Enter(State.Windup, WindupTime);
                return vel;
            }
            if (stateT >= stateDur)
            {
                // 横薙ぎは半分の確率で2発目へ。それ以外は隙(叩きつけが一番長い)
                if (skill == Skill.Sweep && chain == 0 && Random.value < Kind.sweepChain) { chain = 1; StartSkill(Skill.Sweep); }
                else Enter(State.Recover, skill switch
                {
                    Skill.Slam => 1.1f, Skill.Thrust => 0.8f, Skill.AntiAir => 1.2f,   // 跳び上がり斬りは落ちてくる間も隙
                    Skill.WideSweep => B.wideRecover,
                    Skill.Flurry => B.flurryRecover,
                    Skill.Charge => chargeWall ? B.chargeWallRecover : B.chargeRecover,   // 壁にぶつかったら長くよろける
                    _ => 0.6f,
                });
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
                case Skill.Flurry:
                {
                    Vector3 tf = player.transform.position - transform.position;
                    tf.y = 0f;
                    inRange = GapTo(player) <= B.flurryReach * Size && (tf.magnitude < Radius + player.Radius + 1f || Vector3.Angle(commitDir, tf) <= B.flurryArc);
                    bool last = flurryIndex >= 2;
                    h.damage = last ? B.flurryLastDamage : B.flurryDamage; h.guardable = true;
                    h.guardCost = last ? B.flurryLastGuardCost : B.flurryGuardCost;
                    h.stagger = last ? B.flurryLastStagger : B.flurryStagger;
                    h.hurtTime = last ? 0.5f : 0.25f;
                    h.knock = last ? B.flurryLastKnock : B.flurryKnock;
                    break;
                }
                case Skill.WideSweep:
                    inRange = GapTo(player) <= B.wideReach * Size;   // 全周
                    h.damage = B.wideDamage; h.guardable = true; h.guardCost = B.wideGuardCost; h.stagger = B.wideStagger; h.hurtTime = 0.5f; h.knock = B.wideKnock;
                    break;
                case Skill.Charge:
                    inRange = true;   // 触れた時点で呼ばれる
                    h.damage = B.chargeDamage; h.guardable = true; h.guardCost = B.chargeGuardCost; h.stagger = B.chargeStagger; h.hurtTime = 0.5f; h.knock = B.chargeKnock;
                    break;
                case Skill.LeapSlam:
                    Vector3 dl = player.transform.position - transform.position;
                    dl.y = 0f;
                    inRange = dl.magnitude - player.Radius <= B.leapImpactRadius * Size;
                    impactInRange = inRange && !TooFarVertically(player);
                    h.damage = B.leapDamage; h.guardable = false; h.stagger = B.leapStagger; h.hurtTime = 0.55f; h.knock = B.leapKnock;
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
            Engage();   // 斬られたら発覚(見通せる近くの仲間にも伝わる)
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
            Knockback(knock * 1.5f * Kind.knockMul);
            StopRising();   // 空中で怯んだら上昇が止まり、怯んでいる間は落ちる
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
                        case Skill.Flurry:
                            if (flurryIndex == 0)
                            {
                                // 三段斬り:剣を正面に半ば掲げたまま、左右に揺れてリズムを刻む。最後に右へひねって1段目へ
                                Pose(clip, R * 0.6f, 20f);
                                tTwist = Mathf.Lerp(12f * Mathf.Sin(k * Mathf.PI * 4f), 30f, Mathf.Clamp01((k - 0.7f) / 0.3f));
                                tLift = -1f * k;
                            }
                            else if (flurryIndex == 1)
                            {
                                // 2段目:剣を振り上げ直しながら左へひねる
                                Pose(clip, Mathf.Lerp(S, R, k), 25f);
                                tTwist = Mathf.Lerp(-30f, -35f, k);
                                tLean = 8f;
                            }
                            else
                            {
                                // 3段目:剣を頭上へ高く掲げ、のけぞる
                                Pose(clip, Mathf.Lerp(S, R, k), 25f);
                                tTwist = Mathf.Lerp(30f, 0f, k);
                                tLean = -12f * k;
                            }
                            rate = 20f;
                            break;
                        case Skill.WideSweep:
                            // 大薙ぎ:深く沈み、上半身を左へ大きくひねって剣を担ぐ(普通の横薙ぎは右へひねる)
                            Pose(clip, R * Mathf.Clamp01(k * 1.4f), 20f);
                            tTwist = -70f * k;
                            tLean = 12f * k;
                            tLift = -2.2f * k;
                            break;
                        case Skill.Charge:
                            // 突進:剣を担いで深く前かがみになり、じりじり下がる(後ろへ下がるのは Update で)
                            Pose(clip, R * Mathf.Clamp01(k * 2f), 20f);
                            tTwist = 45f * k;
                            tLean = 28f * Mathf.Clamp01(k * 2f);
                            tLift = -1.6f * k;
                            break;
                        case Skill.LeapSlam:
                            // 跳躍叩きつけ:剣を掲げたまま、膝を深く折ってしゃがみ込む
                            Pose(clip, R * Mathf.Clamp01(k * 1.5f), 20f);
                            tLean = 18f * k;
                            tLift = -3.2f * k;
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
                    if (skill == Skill.Charge) { Pose(clip, hitDone ? S : R, 25f); tLean = 30f; tTwist = hitDone ? -20f : 45f; break; }   // 担いだまま突っ込む
                    if (skill == Skill.Flurry)
                    {
                        // 右→左 / 左→右 / 縦の振り下ろし
                        Pose(clip, Mathf.Lerp(R, S, k), 25f);
                        if (flurryIndex == 0) tTwist = Mathf.Lerp(30f, -30f, k);
                        else if (flurryIndex == 1) tTwist = Mathf.Lerp(-35f, 30f, k);
                        else tLean = Mathf.Lerp(-12f, 24f, k);
                        break;
                    }
                    if (skill == Skill.WideSweep) { Pose(clip, Mathf.Lerp(R, S, k), 25f); tTwist = Mathf.Lerp(-70f, 0f, k); tLift = Mathf.Lerp(-2.2f, -1f, k); break; }   // 機体ごと回る
                    if (skill == Skill.LeapSlam)
                    {
                        // 上がる間は剣を掲げてのけぞり、落ちる間は前へ振りかぶる
                        bool rising = stateT < B.leapAirTime * 0.5f;
                        Pose(clip, R, 20f);
                        tLean = rising ? -14f : 20f;
                        rate = 10f;
                        break;
                    }
                    Pose(clip, Mathf.Lerp(R, S, k), 25f);
                    if (skill == Skill.Slam) { tLean = Mathf.Lerp(-16f, 24f, k); tLift = Mathf.Lerp(2f, 0f, k); }
                    else tTwist = Mathf.Lerp(35f, -30f, k);
                    break;
                case State.Recover:
                    Pose(clip, Mathf.Lerp(S, E, k), 20f);
                    if (skill == Skill.AntiAir) tLean = -32f * (1f - k);
                    if (skill == Skill.WideSweep) tLift = -1f * (1f - k);
                    if (skill == Skill.Flurry) { tLean = 24f * (1f - k); tLift = -1.2f * (1f - k); }   // 振り下ろしたまま沈んで、ゆっくり起き上がる
                    if (skill == Skill.Charge)
                    {
                        // 壁にぶつかったら、のけぞって左右によろける
                        tLean = chargeWall ? -20f * (1f - k) : 24f * (1f - k);
                        tTwist = chargeWall ? 14f * Mathf.Sin(k * Mathf.PI * 3f) * (1f - k) : 0f;
                    }
                    if (skill == Skill.LeapSlam) { tLean = 28f * (1f - k); tLift = -2f * (1f - k); }   // 剣を叩きつけたまま、深く沈んで起き上がる
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
                    if (dashing || Airborne) { Pose(MechRig.Dash, 0f, 12f); tLean = 8f; }   // ダッシュ中・空中
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
