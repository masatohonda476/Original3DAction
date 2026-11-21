using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 剣の技1つの中身(自機の通常攻撃・強攻撃・回避斬り)。stagger は敵の怯みゲージへの量。
    /// pose0〜3 は斬撃クリップの再生位置(振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり / 戻りの終わり)。
    /// 斬撃クリップは 0 構え → 16 振りかぶり → 21 振り抜き → 25 振り切り → 40 構え(30fps。MechRig.AttackRaised など)。
    /// 振り抜きから振りかぶりへ逆に再生すると、返す刀の切り上げになる。
    /// 機体全体のひねり・前傾・沈みをクリップの上に重ねて、武器ごとの振りの違いを出す(戻りで0へ戻る)。
    /// </summary>
    [System.Serializable]
    public struct DuelSwing
    {
        public float windup, active, recover, damage, stagger, flinch, reach, arc, lunge, knock, cost;
        public bool heavy;
        /// <summary>振りかぶりの出だしに△が押されたら強攻撃に切り替わる(通常攻撃だけ。同時押しのずれを許すため)。</summary>
        public bool chargeable;
        public float pose0, pose1, pose2, pose3;
        /// <summary>機体全体の横のひねり(右が正):振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり。左手で振るときは左右を反転する。</summary>
        public float twist0, twist1, twist2;
        /// <summary>機体全体の前傾(前が正。負はのけぞり):振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり。</summary>
        public float lean0, lean1, lean2;
        /// <summary>振り抜きで腰を落とす深さ(m)。重い武器ほど深い。</summary>
        public float drop;

        /// <summary>戻りに入ってからこの秒数たてば、次の段へつなげられる。</summary>
        public const float ChainAfter = 0.08f;
        /// <summary>この技だけ、次の段へつなげられるまでの秒数を変える(0 なら ChainAfter)。素手のパンチが使う。</summary>
        public float chainDelay;
        /// <summary>戻りに入ってから次の段へつなげられるまでの秒数。</summary>
        public readonly float ChainAt => chainDelay > 0f ? chainDelay : ChainAfter;
    }

    /// <summary>
    /// 剣1本の技の表:連撃(続けて押すと順にくり返す)・強攻撃(△+攻撃。続けて出すと順に)・回避斬り。
    /// 剣の武装(DuelWeapon.moves)が持つ。空なら素の剣の表(Standard)を使う。数値は仮置き。
    /// 届く範囲・威力などには、さらに武装の倍率(damageMul・reachMul など)が掛かる。
    /// </summary>
    [System.Serializable]
    public class DuelMoveSet
    {
        [Tooltip("連撃(攻撃ボタンを続けて押すと順にくり返す)")]
        public DuelSwing[] light;
        [Tooltip("強攻撃(△+攻撃ボタン。続けて出すと順に)")]
        public DuelSwing[] heavy;
        [Tooltip("回避斬り(回避の後半か、回避の直後に攻撃)")]
        public DuelSwing dodgeSlash;
        [Tooltip("両手の近接だけ:L1 の別の技(続けて押すと順にくり返す。回避の後に L1 でもこれ)。片手の剣は空")]
        public DuelSwing[] alt;

        const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;
        /// <summary>振り切り(25フレーム)。重い武器は振り抜いた先まで振り切る。</summary>
        const float F = 25f / 30f;
        /// <summary>戻りの途中(S から E へ t の割合)。</summary>
        static float Mid(float t) => S + (E - S) * t;

        /// <summary>素の剣の表(最初から右手に持っている素の剣の技)。書き換えないこと。</summary>
        public static readonly DuelMoveSet Base = Standard();

        /// <summary>
        /// 素の剣(鋼の剣も):斬り下ろし(A)と、返す刀の切り返し(B)を交互に。強攻撃は右→左と左→右の薙ぎ払いを交互に。
        /// 回避斬りは回避の勢いのまま大きく踏み込み、広く薙ぐ(この後は切り返しへつながる)。
        /// </summary>
        public static DuelMoveSet Standard() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.32f, active = 0.16f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 3f, knock = 3f, cost = 10f, chargeable = true,
                        pose0 = R * 0.5f, pose1 = R, pose2 = S, pose3 = Mid(0.6f) },
                new() { windup = 0.24f, active = 0.18f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 2.5f, knock = 3f, cost = 10f, chargeable = true,
                        pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.5f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.4f, active = 0.14f, recover = 0.6f, damage = 24f, stagger = 8.4f, flinch = 0.7f, reach = 10f, arc = 110f, lunge = 5f, knock = 16f, cost = 25f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = S, pose3 = Mid(0.6f), twist0 = 10f, twist1 = 55f, twist2 = -55f },
                new() { windup = 0.32f, active = 0.14f, recover = 0.6f, damage = 24f, stagger = 8.4f, flinch = 0.7f, reach = 10f, arc = 110f, lunge = 4f, knock = 16f, cost = 25f, heavy = true,
                        pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.5f, twist0 = -55f, twist1 = -60f, twist2 = 50f },
            },
            dodgeSlash = new() { windup = 0.14f, active = 0.14f, recover = 0.5f, damage = 12f, stagger = 9f, flinch = 0.5f, reach = 9.5f, arc = 100f, lunge = 6f, knock = 6f, cost = 10f,
                                 pose0 = R * 0.7f, pose1 = R, pose2 = S, pose3 = Mid(0.6f) },
        };

        /// <summary>素手の表(右手・左手が両方とも空のとき。DuelArms.Unarmed)。書き換えないこと。</summary>
        public static readonly DuelMoveSet Bare = Unarmed();

        /// <summary>
        /// 素手のパンチ(両手持ちの素手。R1 = 右手 / L1 = 左手で、左は左右反転)。武装ではないので、武装の倍率は掛からない。
        /// 強攻撃(△+R1/L1)・回避斬りに当たる入力でも、この表のパンチが出る(DuelPlayer.Attack.cs。使うのは light だけ)。
        /// 作者「武装を付けていないときは左右の手でパンチが出るようにして、ヒットでHPが2づつ減るようにしてください」→ 最初の版(1発 2・約4発/秒)を見て
        /// 「DPSと怯ませDPSはそのままにモーション速度を半分にしてください」→ 振りかぶり・振り抜き・戻り・次へつなげるまでの秒数を全部 2 倍にし、
        /// 1発の威力・怯み・EN を 2 倍(2 → 4、2 → 4、3 → 6)にした。続けて殴ると 0.5 秒に1発(2発/秒)で、毎秒の威力 8・怯み 8・EN 12 は前と同じ。
        /// 届く範囲・扇の広さ・踏み込み・吹き飛ばしは仮置き(剣より短く・軽く)。
        /// 姿勢(仮。新しいモーションは作っていない):斬撃クリップの、腕が前へ伸びきる所(19.5フレーム)の前後だけを使う。
        ///   振りかぶりで拳を頭の横まで引き(19 → 17.5フレーム)、振り抜きで前へ突き出す(→ 19.5フレーム)。戻りは腕を伸ばしたあたりで止める(→ 19フレーム)。
        ///   機体のひねり(引くときに振る手の側へ、突くときに逆へ)と前傾を重ねて、突き出す動きに寄せる。
        /// </summary>
        public static DuelMoveSet Unarmed() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.2f, active = 0.14f, recover = 0.52f, chainDelay = DuelSwing.ChainAfter * 2f, damage = 4f, stagger = 4f, flinch = 0.3f, reach = 5f, arc = 50f, lunge = 2f, knock = 1.5f, cost = 6f,
                        pose0 = 19f / 30f, pose1 = 17.5f / 30f, pose2 = 19.5f / 30f, pose3 = 19f / 30f, twist0 = 4f, twist1 = 12f, twist2 = -15f, lean0 = -1f, lean1 = -3f, lean2 = 8f },
            },
        };

        /// <summary>
        /// 軽剣:振りかぶりが浅く、小さく速い3段の連撃(斬り下ろし → 切り上げ → 手首を返した横払い)。一振りは軽く、ENも少ない。
        /// 強攻撃は体ごと素早く回す横薙ぎ。回避斬りはとても出が速い。
        /// </summary>
        public static DuelMoveSet Quick() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.17f, active = 0.09f, recover = 0.4f, damage = 6f, stagger = 4.5f, flinch = 0.4f, reach = 8.5f, arc = 65f, lunge = 2.5f, knock = 2f, cost = 6f, chargeable = true,
                        pose0 = R * 0.7f, pose1 = R * 0.92f, pose2 = S, pose3 = Mid(0.35f), twist1 = -10f, twist2 = 15f, lean2 = 5f },
                new() { windup = 0.14f, active = 0.09f, recover = 0.4f, damage = 6f, stagger = 4.5f, flinch = 0.4f, reach = 8.5f, arc = 65f, lunge = 2f, knock = 2f, cost = 6f, chargeable = true,
                        pose0 = Mid(0.35f), pose1 = Mid(0.55f), pose2 = R * 0.95f, pose3 = R * 0.75f, twist0 = 10f, twist1 = 15f, twist2 = -15f },
                new() { windup = 0.16f, active = 0.1f, recover = 0.45f, damage = 7f, stagger = 5.5f, flinch = 0.45f, reach = 8.5f, arc = 80f, lunge = 3f, knock = 3f, cost = 6f, chargeable = true,
                        pose0 = R * 0.75f, pose1 = R * 0.9f, pose2 = S, pose3 = Mid(0.4f), twist0 = -20f, twist1 = -40f, twist2 = 40f, lean2 = 8f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.26f, active = 0.12f, recover = 0.5f, damage = 15f, stagger = 7f, flinch = 0.6f, reach = 9f, arc = 120f, lunge = 4f, knock = 10f, cost = 16f, heavy = true,
                        pose0 = R * 0.4f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = 30f, twist1 = 70f, twist2 = -70f, lean2 = 6f },
                new() { windup = 0.22f, active = 0.12f, recover = 0.5f, damage = 15f, stagger = 7f, flinch = 0.6f, reach = 9f, arc = 120f, lunge = 3.5f, knock = 10f, cost = 16f, heavy = true,
                        pose0 = Mid(0.5f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.6f, twist0 = -60f, twist1 = -75f, twist2 = 60f },
            },
            dodgeSlash = new() { windup = 0.09f, active = 0.1f, recover = 0.4f, damage = 9f, stagger = 6f, flinch = 0.45f, reach = 9f, arc = 100f, lunge = 7f, knock = 4f, cost = 6f,
                                 pose0 = R * 0.75f, pose1 = R, pose2 = S, pose3 = Mid(0.4f), twist0 = -15f, twist1 = -25f, twist2 = 25f, lean2 = 6f },
        };

        /// <summary>
        /// 大剣:構えから大きく振りかぶり(のけぞる)、振り切りまで振り抜いて腰を落とす。2段(袈裟斬り → 返しの横薙ぎ)。遅いが一振りが重く広い。
        /// 強攻撃は体ごと叩きつける兜割りと、大きく回る薙ぎ払い。回避斬りも重い。
        /// </summary>
        public static DuelMoveSet Great() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.5f, active = 0.2f, recover = 0.75f, damage = 15f, stagger = 12f, flinch = 0.7f, reach = 10f, arc = 95f, lunge = 4f, knock = 9f, cost = 15f, chargeable = true,
                        pose0 = 0.05f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -10f, twist1 = -25f, twist2 = 30f, lean0 = -6f, lean1 = -10f, lean2 = 16f, drop = 0.7f },
                new() { windup = 0.42f, active = 0.2f, recover = 0.75f, damage = 15f, stagger = 12f, flinch = 0.7f, reach = 10f, arc = 110f, lunge = 3.5f, knock = 10f, cost = 15f, chargeable = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 35f, twist1 = 55f, twist2 = -60f, lean0 = 4f, lean1 = -4f, lean2 = 10f, drop = 0.4f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.75f, active = 0.18f, recover = 0.9f, damage = 34f, stagger = 16f, flinch = 0.9f, reach = 11f, arc = 70f, lunge = 6f, knock = 22f, cost = 30f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -14f, lean1 = -18f, lean2 = 26f, drop = 1.2f },
                new() { windup = 0.6f, active = 0.22f, recover = 0.9f, damage = 30f, stagger = 14f, flinch = 0.9f, reach = 11f, arc = 150f, lunge = 4f, knock = 22f, cost = 30f, heavy = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = -50f, twist1 = -95f, twist2 = 90f, lean2 = 12f, drop = 0.6f },
            },
            dodgeSlash = new() { windup = 0.24f, active = 0.18f, recover = 0.65f, damage = 18f, stagger = 13f, flinch = 0.7f, reach = 10.5f, arc = 120f, lunge = 8f, knock = 12f, cost = 15f,
                                 pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -20f, twist1 = -40f, twist2 = 45f, lean2 = 14f, drop = 0.6f },
        };

        /// <summary>
        /// 高周波ブレード:速く鋭い4段の連撃(斬り下ろし → 切り上げ → 横一文字 → 返しの横薙ぎ)。
        /// 強攻撃は大きく踏み込む突き(狭いが遠くまで届く)と、のけぞりながらの斬り上げ。
        /// </summary>
        public static DuelMoveSet Blade() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.2f, active = 0.1f, recover = 0.45f, damage = 8f, stagger = 6f, flinch = 0.45f, reach = 9f, arc = 70f, lunge = 3f, knock = 3f, cost = 7f, chargeable = true,
                        pose0 = R * 0.6f, pose1 = R, pose2 = S, pose3 = Mid(0.45f), twist0 = -5f, twist1 = -15f, twist2 = 20f, lean2 = 6f },
                new() { windup = 0.16f, active = 0.1f, recover = 0.45f, damage = 8f, stagger = 6f, flinch = 0.45f, reach = 9f, arc = 70f, lunge = 2.5f, knock = 3f, cost = 7f, chargeable = true,
                        pose0 = Mid(0.45f), pose1 = Mid(0.8f), pose2 = R * 0.9f, pose3 = R * 0.65f, twist0 = 10f, twist1 = 20f, twist2 = -20f },
                new() { windup = 0.18f, active = 0.1f, recover = 0.45f, damage = 8f, stagger = 6f, flinch = 0.45f, reach = 9f, arc = 90f, lunge = 3f, knock = 4f, cost = 7f, chargeable = true,
                        pose0 = R * 0.7f, pose1 = R, pose2 = S, pose3 = Mid(0.45f), twist0 = 40f, twist1 = 60f, twist2 = -55f, lean2 = 6f },
                new() { windup = 0.18f, active = 0.11f, recover = 0.5f, damage = 10f, stagger = 8f, flinch = 0.5f, reach = 9f, arc = 90f, lunge = 4f, knock = 6f, cost = 7f, chargeable = true,
                        pose0 = Mid(0.45f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.6f, twist0 = -55f, twist1 = -65f, twist2 = 60f, lean2 = 8f },
            },
            heavy = new DuelSwing[]
            {
                // 突き:剣を構えたまま、肩を入れて大きく踏み込む
                new() { windup = 0.3f, active = 0.1f, recover = 0.6f, damage = 22f, stagger = 12f, flinch = 0.7f, reach = 12f, arc = 30f, lunge = 10f, knock = 18f, cost = 22f, heavy = true,
                        pose0 = R * 0.5f, pose1 = R * 0.75f, pose2 = R * 0.8f, pose3 = R * 0.6f, twist0 = -25f, twist1 = -30f, twist2 = 10f, lean0 = -8f, lean1 = -10f, lean2 = 30f, drop = 0.3f },
                // 斬り上げ:のけぞりながら下から上へ
                new() { windup = 0.26f, active = 0.12f, recover = 0.6f, damage = 20f, stagger = 11f, flinch = 0.7f, reach = 10f, arc = 80f, lunge = 4f, knock = 14f, cost = 22f, heavy = true,
                        pose0 = Mid(0.45f), pose1 = E, pose2 = R * 0.6f, pose3 = R * 0.4f, twist1 = -10f, twist2 = 10f, lean0 = 10f, lean1 = 12f, lean2 = -15f },
            },
            dodgeSlash = new() { windup = 0.1f, active = 0.1f, recover = 0.4f, damage = 11f, stagger = 8f, flinch = 0.5f, reach = 10f, arc = 90f, lunge = 9f, knock = 5f, cost = 7f,
                                 pose0 = R * 0.7f, pose1 = R, pose2 = S, pose3 = Mid(0.45f), twist0 = -15f, twist1 = -25f, twist2 = 30f, lean2 = 10f },
        };

        /// <summary>
        /// 斬艦刀:長大な刃で、体ごと大きくひねって周りを薙ぎ払う2段の大薙ぎ。出は遅いが、ほぼ全周に届く。
        /// 強攻撃は大回転と、全身で叩きつける兜割り。
        /// </summary>
        public static DuelMoveSet Giant() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.55f, active = 0.24f, recover = 0.8f, damage = 20f, stagger = 14f, flinch = 0.8f, reach = 10f, arc = 150f, lunge = 3f, knock = 14f, cost = 16f, chargeable = true,
                        pose0 = 0.1f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 40f, twist1 = 85f, twist2 = -95f, lean0 = -4f, lean1 = -8f, lean2 = 12f, drop = 0.8f },
                new() { windup = 0.5f, active = 0.24f, recover = 0.8f, damage = 20f, stagger = 14f, flinch = 0.8f, reach = 10f, arc = 150f, lunge = 3f, knock = 14f, cost = 16f, chargeable = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = -90f, twist1 = -100f, twist2 = 95f, lean2 = 10f, drop = 0.6f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.8f, active = 0.3f, recover = 1f, damage = 40f, stagger = 20f, flinch = 1f, reach = 11f, arc = 180f, lunge = 3f, knock = 28f, cost = 34f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 120f, twist2 = -150f, lean2 = 10f, drop = 0.8f },
                new() { windup = 0.85f, active = 0.2f, recover = 1f, damage = 44f, stagger = 22f, flinch = 1f, reach = 12f, arc = 90f, lunge = 7f, knock = 30f, cost = 34f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -18f, lean1 = -22f, lean2 = 30f, drop = 1.5f },
            },
            dodgeSlash = new() { windup = 0.28f, active = 0.24f, recover = 0.7f, damage = 24f, stagger = 15f, flinch = 0.8f, reach = 10.5f, arc = 170f, lunge = 8f, knock = 16f, cost = 16f,
                                 pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 50f, twist1 = 90f, twist2 = -100f, lean2 = 12f, drop = 0.7f },
        };

        // ───────── 両手の近接(R1 = 連撃 / L1 = 別の技 alt)。振りは右手のクリップで、左右反転しない ─────────
        // 表の値は片手の剣と同じ桁にし、強さは武装の倍率(damageMul・reachMul・staggerMul)で上げる(届く範囲の倍率は見た目の長さにも効く)。

        /// <summary>突き(槍・薙刀・突撃槍):剣を構えたまま肩を入れて踏み込む姿勢。</summary>
        static DuelSwing Thrust(float windup, float active, float recover, float dmg, float stagger, float flinch, float reach, float arc, float lunge, float knock, float cost,
                                float twist, float lean, float drop, bool heavy = false) => new()
        {
            windup = windup, active = active, recover = recover, damage = dmg, stagger = stagger, flinch = flinch, reach = reach, arc = arc, lunge = lunge, knock = knock, cost = cost,
            heavy = heavy, chargeable = !heavy,
            pose0 = R * 0.5f, pose1 = R * 0.75f, pose2 = R * 0.8f, pose3 = R * 0.6f,
            twist0 = -twist * 0.8f, twist1 = -twist, twist2 = twist * 0.4f, lean0 = -lean * 0.3f, lean1 = -lean * 0.4f, lean2 = lean, drop = drop,
        };

        /// <summary>
        /// 両手剣:袈裟斬り → 返しの横薙ぎ → 兜割りの3段。L1 は大きく踏み込んで斬り抜ける突進斬り。
        /// 強攻撃は大上段の叩き割りと大薙ぎ。片手の大剣より出が速く、届く範囲が長い。
        /// </summary>
        public static DuelMoveSet TwoHandSword() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.36f, active = 0.18f, recover = 0.65f, damage = 14f, stagger = 10f, flinch = 0.6f, reach = 9.5f, arc = 95f, lunge = 4f, knock = 7f, cost = 13f, chargeable = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.6f), twist0 = -10f, twist1 = -25f, twist2 = 30f, lean0 = -4f, lean1 = -6f, lean2 = 12f, drop = 0.5f },
                new() { windup = 0.3f, active = 0.18f, recover = 0.65f, damage = 14f, stagger = 10f, flinch = 0.6f, reach = 9.5f, arc = 110f, lunge = 3.5f, knock = 8f, cost = 13f, chargeable = true,
                        pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 35f, twist1 = 55f, twist2 = -60f, lean1 = -4f, lean2 = 8f, drop = 0.3f },
                new() { windup = 0.42f, active = 0.18f, recover = 0.75f, damage = 20f, stagger = 14f, flinch = 0.75f, reach = 10f, arc = 80f, lunge = 5f, knock = 14f, cost = 15f, chargeable = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -10f, lean1 = -14f, lean2 = 22f, drop = 0.9f },
            },
            alt = new DuelSwing[]
            {
                // 突進斬り:前のめりに大きく踏み込み、体ごと斬り抜ける
                new() { windup = 0.3f, active = 0.16f, recover = 0.6f, damage = 22f, stagger = 14f, flinch = 0.7f, reach = 10f, arc = 70f, lunge = 14f, knock = 16f, cost = 18f, chargeable = true,
                        pose0 = R * 0.5f, pose1 = R, pose2 = F, pose3 = Mid(0.6f), twist0 = -20f, twist1 = -35f, twist2 = 40f, lean0 = 6f, lean1 = 10f, lean2 = 20f, drop = 0.5f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.7f, active = 0.18f, recover = 0.85f, damage = 38f, stagger = 20f, flinch = 0.9f, reach = 10.5f, arc = 75f, lunge = 6f, knock = 24f, cost = 30f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -16f, lean1 = -20f, lean2 = 28f, drop = 1.3f },
                new() { windup = 0.55f, active = 0.22f, recover = 0.85f, damage = 32f, stagger = 17f, flinch = 0.9f, reach = 10.5f, arc = 160f, lunge = 4f, knock = 22f, cost = 30f, heavy = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = -55f, twist1 = -100f, twist2 = 95f, lean2 = 12f, drop = 0.6f },
            },
            dodgeSlash = new() { windup = 0.2f, active = 0.18f, recover = 0.6f, damage = 20f, stagger = 13f, flinch = 0.7f, reach = 10f, arc = 120f, lunge = 8f, knock = 12f, cost = 13f,
                                 pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -20f, twist1 = -40f, twist2 = 45f, lean2 = 14f, drop = 0.6f },
        };

        /// <summary>
        /// 長槍:狭く速く遠くまで届く突きの3段。L1 は柄ごと体を回す、ほぼ全周の薙ぎ払い(囲まれたとき用)。
        /// 強攻撃は大きく踏み込む突きと、上からの叩き下ろし。
        /// </summary>
        public static DuelMoveSet Spear() => new()
        {
            light = new DuelSwing[]
            {
                Thrust(0.24f, 0.1f, 0.45f, 10f, 7f, 0.5f, 9f, 35f, 3.5f, 6f, 9f, 25f, 20f, 0.2f),
                Thrust(0.2f, 0.1f, 0.45f, 10f, 7f, 0.5f, 9f, 35f, 3f, 6f, 9f, 20f, 18f, 0.2f),
                Thrust(0.3f, 0.12f, 0.5f, 14f, 10f, 0.6f, 9.5f, 35f, 6f, 10f, 11f, 30f, 26f, 0.3f),
            },
            alt = new DuelSwing[]
            {
                // 薙ぎ払い:柄を大きく回して周りをまとめて払う
                new() { windup = 0.4f, active = 0.24f, recover = 0.7f, damage = 16f, stagger = 12f, flinch = 0.7f, reach = 8.5f, arc = 170f, lunge = 2f, knock = 14f, cost = 16f, chargeable = true,
                        pose0 = 0.1f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 50f, twist1 = 90f, twist2 = -110f, lean1 = -4f, lean2 = 10f, drop = 0.5f },
            },
            heavy = new DuelSwing[]
            {
                Thrust(0.4f, 0.12f, 0.7f, 30f, 16f, 0.8f, 10f, 25f, 12f, 22f, 26f, 35f, 32f, 0.4f, true),
                new() { windup = 0.5f, active = 0.18f, recover = 0.8f, damage = 28f, stagger = 18f, flinch = 0.85f, reach = 9.5f, arc = 60f, lunge = 4f, knock = 18f, cost = 26f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -12f, lean1 = -16f, lean2 = 24f, drop = 1f },
            },
            dodgeSlash = Thrust(0.12f, 0.1f, 0.45f, 15f, 10f, 0.55f, 9.5f, 35f, 10f, 10f, 9f, 25f, 24f, 0.2f),
        };

        /// <summary>
        /// 薙刀:広い弧を描いて流れるように振る4段。L1 は長い突き → 斬り上げ。強攻撃は大回転と兜割り。
        /// </summary>
        public static DuelMoveSet Glaive() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.3f, active = 0.16f, recover = 0.55f, damage = 12f, stagger = 9f, flinch = 0.55f, reach = 9f, arc = 110f, lunge = 3f, knock = 6f, cost = 11f, chargeable = true,
                        pose0 = R * 0.5f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -15f, twist1 = -35f, twist2 = 45f, lean2 = 8f, drop = 0.3f },
                new() { windup = 0.26f, active = 0.16f, recover = 0.55f, damage = 12f, stagger = 9f, flinch = 0.55f, reach = 9f, arc = 120f, lunge = 3f, knock = 6f, cost = 11f, chargeable = true,
                        pose0 = Mid(0.5f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.6f, twist0 = 40f, twist1 = 60f, twist2 = -65f, lean2 = 6f, drop = 0.2f },
                new() { windup = 0.28f, active = 0.16f, recover = 0.55f, damage = 13f, stagger = 10f, flinch = 0.55f, reach = 9f, arc = 110f, lunge = 4f, knock = 8f, cost = 11f, chargeable = true,
                        pose0 = R * 0.6f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -40f, twist1 = -70f, twist2 = 70f, lean0 = 2f, lean2 = 10f, drop = 0.4f },
                new() { windup = 0.34f, active = 0.18f, recover = 0.65f, damage = 16f, stagger = 12f, flinch = 0.65f, reach = 9.5f, arc = 150f, lunge = 3f, knock = 12f, cost = 13f, chargeable = true,
                        pose0 = Mid(0.5f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 60f, twist1 = 95f, twist2 = -110f, lean1 = -4f, lean2 = 12f, drop = 0.5f },
            },
            alt = new DuelSwing[]
            {
                Thrust(0.26f, 0.1f, 0.5f, 16f, 11f, 0.6f, 10f, 30f, 8f, 12f, 14f, 30f, 26f, 0.3f),
                // 斬り上げ:のけぞりながら下から上へ
                new() { windup = 0.24f, active = 0.14f, recover = 0.6f, damage = 18f, stagger = 14f, flinch = 0.7f, reach = 9.5f, arc = 80f, lunge = 3f, knock = 14f, cost = 14f, chargeable = true,
                        pose0 = Mid(0.45f), pose1 = E, pose2 = R * 0.6f, pose3 = R * 0.4f, twist1 = -10f, twist2 = 10f, lean0 = 10f, lean1 = 12f, lean2 = -15f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.6f, active = 0.3f, recover = 0.9f, damage = 30f, stagger = 18f, flinch = 0.9f, reach = 10f, arc = 180f, lunge = 3f, knock = 22f, cost = 30f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 120f, twist2 = -150f, lean2 = 10f, drop = 0.8f },
                new() { windup = 0.6f, active = 0.18f, recover = 0.85f, damage = 34f, stagger = 20f, flinch = 0.9f, reach = 10.5f, arc = 70f, lunge = 6f, knock = 24f, cost = 30f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -16f, lean1 = -20f, lean2 = 28f, drop = 1.2f },
            },
            dodgeSlash = new() { windup = 0.14f, active = 0.16f, recover = 0.5f, damage = 16f, stagger = 11f, flinch = 0.6f, reach = 9.5f, arc = 140f, lunge = 8f, knock = 10f, cost = 11f,
                                 pose0 = R * 0.6f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -30f, twist1 = -60f, twist2 = 70f, lean2 = 10f, drop = 0.4f },
        };

        /// <summary>
        /// 戦鎚:重い振り回しの2段(一振りで剣士の怯みゲージをほぼ満たす)。L1 は全身で床を叩く、周りごとの叩きつけ。
        /// 強攻撃は大振りの叩き下ろしと、回転しての叩き。
        /// </summary>
        public static DuelMoveSet Hammer() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.48f, active = 0.2f, recover = 0.75f, damage = 20f, stagger = 13f, flinch = 0.75f, reach = 9.5f, arc = 90f, lunge = 4f, knock = 12f, cost = 16f, chargeable = true,
                        pose0 = 0.05f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -10f, twist1 = -30f, twist2 = 35f, lean0 = -6f, lean1 = -10f, lean2 = 18f, drop = 0.8f },
                new() { windup = 0.42f, active = 0.2f, recover = 0.75f, damage = 20f, stagger = 13f, flinch = 0.75f, reach = 9.5f, arc = 110f, lunge = 3.5f, knock = 12f, cost = 16f, chargeable = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 35f, twist1 = 60f, twist2 = -65f, lean0 = 4f, lean1 = -4f, lean2 = 12f, drop = 0.5f },
            },
            alt = new DuelSwing[]
            {
                // 叩きつけ:振りかぶって床ごと叩き、周りの敵をまとめて崩す
                new() { windup = 0.6f, active = 0.2f, recover = 0.85f, damage = 26f, stagger = 18f, flinch = 0.9f, reach = 9f, arc = 180f, lunge = 2f, knock = 26f, cost = 22f, chargeable = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -18f, lean1 = -22f, lean2 = 32f, drop = 1.6f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.8f, active = 0.2f, recover = 1f, damage = 44f, stagger = 24f, flinch = 1f, reach = 10f, arc = 80f, lunge = 6f, knock = 30f, cost = 34f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -18f, lean1 = -24f, lean2 = 32f, drop = 1.5f },
                new() { windup = 0.7f, active = 0.26f, recover = 1f, damage = 36f, stagger = 22f, flinch = 1f, reach = 10f, arc = 180f, lunge = 3f, knock = 28f, cost = 34f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 120f, twist2 = -150f, lean2 = 10f, drop = 0.8f },
            },
            dodgeSlash = new() { windup = 0.26f, active = 0.2f, recover = 0.7f, damage = 22f, stagger = 15f, flinch = 0.8f, reach = 9.5f, arc = 120f, lunge = 8f, knock = 16f, cost = 16f,
                                 pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -20f, twist1 = -40f, twist2 = 45f, lean2 = 16f, drop = 0.7f },
        };

        /// <summary>
        /// 斬機大太刀:長大な刃を両手で回す、ほぼ全周の大薙ぎの3段。L1 は大回転 → 回転斬り上げ。強攻撃は兜割りと大旋風。
        /// </summary>
        public static DuelMoveSet GreatTachi() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.42f, active = 0.22f, recover = 0.7f, damage = 16f, stagger = 11f, flinch = 0.75f, reach = 9.5f, arc = 150f, lunge = 3f, knock = 12f, cost = 16f, chargeable = true,
                        pose0 = 0.1f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 40f, twist1 = 85f, twist2 = -95f, lean0 = -4f, lean1 = -8f, lean2 = 12f, drop = 0.7f },
                new() { windup = 0.38f, active = 0.22f, recover = 0.7f, damage = 16f, stagger = 11f, flinch = 0.75f, reach = 9.5f, arc = 150f, lunge = 3f, knock = 12f, cost = 16f, chargeable = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = -90f, twist1 = -100f, twist2 = 95f, lean2 = 10f, drop = 0.6f },
                new() { windup = 0.5f, active = 0.24f, recover = 0.8f, damage = 20f, stagger = 14f, flinch = 0.8f, reach = 10f, arc = 170f, lunge = 4f, knock = 16f, cost = 18f, chargeable = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 110f, twist2 = -130f, lean1 = -6f, lean2 = 14f, drop = 0.9f },
            },
            alt = new DuelSwing[]
            {
                new() { windup = 0.5f, active = 0.3f, recover = 0.8f, damage = 22f, stagger = 15f, flinch = 0.85f, reach = 9.5f, arc = 180f, lunge = 4f, knock = 18f, cost = 20f, chargeable = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 70f, twist1 = 130f, twist2 = -170f, lean2 = 10f, drop = 0.8f },
                new() { windup = 0.45f, active = 0.3f, recover = 0.85f, damage = 24f, stagger = 16f, flinch = 0.9f, reach = 9.5f, arc = 180f, lunge = 4f, knock = 20f, cost = 20f, chargeable = true,
                        pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.6f, pose3 = R * 0.4f, twist0 = -70f, twist1 = -130f, twist2 = 170f, lean0 = 8f, lean1 = 10f, lean2 = -12f, drop = 0.4f },
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.8f, active = 0.2f, recover = 1f, damage = 46f, stagger = 24f, flinch = 1f, reach = 11f, arc = 90f, lunge = 8f, knock = 32f, cost = 34f, heavy = true,
                        pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -18f, lean1 = -22f, lean2 = 30f, drop = 1.5f },
                new() { windup = 0.85f, active = 0.35f, recover = 1f, damage = 40f, stagger = 22f, flinch = 1f, reach = 11f, arc = 180f, lunge = 4f, knock = 30f, cost = 34f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 70f, twist1 = 140f, twist2 = -180f, lean2 = 12f, drop = 0.9f },
            },
            dodgeSlash = new() { windup = 0.24f, active = 0.26f, recover = 0.7f, damage = 24f, stagger = 15f, flinch = 0.8f, reach = 10f, arc = 170f, lunge = 9f, knock = 16f, cost = 16f,
                                 pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 50f, twist1 = 90f, twist2 = -100f, lean2 = 12f, drop = 0.7f },
        };

        /// <summary>
        /// 突撃槍:とても遠くまで届く速い突きの3段。L1 は敵まで一気に詰める突撃(長い踏み込み)。強攻撃は全周の薙ぎ払いと貫き。
        /// </summary>
        public static DuelMoveSet Lance() => new()
        {
            light = new DuelSwing[]
            {
                Thrust(0.22f, 0.1f, 0.45f, 12f, 8f, 0.55f, 9f, 40f, 3.5f, 8f, 10f, 25f, 20f, 0.2f),
                Thrust(0.2f, 0.1f, 0.45f, 12f, 8f, 0.55f, 9f, 40f, 3f, 8f, 10f, 20f, 18f, 0.2f),
                Thrust(0.28f, 0.12f, 0.5f, 16f, 11f, 0.65f, 9.5f, 40f, 6f, 12f, 12f, 30f, 26f, 0.3f),
            },
            alt = new DuelSwing[]
            {
                // 突撃:構えたまま敵まで一気に踏み込んで貫く(相手に密着したら踏み込みは止まる)
                Thrust(0.34f, 0.14f, 0.7f, 30f, 20f, 0.9f, 10f, 45f, 18f, 30f, 24f, 30f, 34f, 0.3f),
            },
            heavy = new DuelSwing[]
            {
                new() { windup = 0.55f, active = 0.3f, recover = 0.9f, damage = 34f, stagger = 20f, flinch = 0.9f, reach = 9.5f, arc = 180f, lunge = 3f, knock = 26f, cost = 30f, heavy = true,
                        pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 120f, twist2 = -150f, lean2 = 10f, drop = 0.8f },
                Thrust(0.6f, 0.14f, 0.9f, 44f, 24f, 1f, 10.5f, 25f, 14f, 34f, 30f, 35f, 36f, 0.5f, true),
            },
            dodgeSlash = Thrust(0.12f, 0.1f, 0.45f, 18f, 12f, 0.6f, 9.5f, 40f, 12f, 12f, 10f, 25f, 26f, 0.2f),
        };
    }
}
