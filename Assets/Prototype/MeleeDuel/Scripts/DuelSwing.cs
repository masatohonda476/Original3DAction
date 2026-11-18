using HuntProto;
using UnityEngine;

namespace MeleeDuel
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

        const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;
        /// <summary>振り切り(25フレーム)。重い武器は振り抜いた先まで振り切る。</summary>
        const float F = 25f / 30f;
        /// <summary>戻りの途中(S から E へ t の割合)。</summary>
        static float Mid(float t) => S + (E - S) * t;

        /// <summary>素の剣の表(武装を持たない・部屋のモードと同じ)。書き換えないこと。</summary>
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
    }
}
