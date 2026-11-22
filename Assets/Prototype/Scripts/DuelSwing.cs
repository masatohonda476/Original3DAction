using HuntProto;
using UnityEngine;

namespace Prototype
{
    public enum DuelGripMotion { Swing, Thrust, Sweep, Slam }

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
        public DuelGripMotion gripMotion;
        public float pose0, pose1, pose2, pose3;
        /// <summary>機体全体の横のひねり(右が正):振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり。</summary>
        public float twist0, twist1, twist2;
        /// <summary>機体全体の前傾(前が正。負はのけぞり):振りかぶりの始め / 振り抜きの始め / 振り抜きの終わり。</summary>
        public float lean0, lean1, lean2;
        /// <summary>振り抜きで腰を落とす深さ(m)。重い武器ほど深い。</summary>
        public float drop;

        /// <summary>戻りに入ってからこの秒数たてば、次の段へつなげられる。</summary>
        public const float ChainAfter = 0.08f;
    }

    /// <summary>近接武器の通常攻撃と回避斬り。R2 の強攻撃は各武器が技を選ぶ。</summary>
    [System.Serializable]
    public partial class DuelMoveSet
    {
        [Tooltip("連撃(攻撃ボタンを続けて押すと順にくり返す)")]
        public DuelSwing[] light;
        [Tooltip("回避斬り(回避の後半か、回避の直後に攻撃)")]
        public DuelSwing dodgeSlash;

        const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;
        /// <summary>振り切り(25フレーム)。重い武器は振り抜いた先まで振り切る。</summary>
        const float F = 25f / 30f;
        /// <summary>戻りの途中(S から E へ t の割合)。</summary>
        static float Mid(float t) => S + (E - S) * t;

        /// <summary>素の剣と片手武器で共用する表。</summary>
        public static readonly DuelMoveSet Base = Standard();

        /// <summary>
        /// 斬り下ろしと、返す刀の切り返しを交互に。
        /// 回避斬りは回避の勢いのまま大きく踏み込み、広く薙ぐ(この後は切り返しへつながる)。
        /// </summary>
        public static DuelMoveSet Standard() => new()
        {
            light = new DuelSwing[]
            {
                new() { windup = 0.32f, active = 0.16f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 3f, knock = 3f, cost = 10f,
                        pose0 = R * 0.5f, pose1 = R, pose2 = S, pose3 = Mid(0.6f) },
                new() { windup = 0.24f, active = 0.18f, recover = 0.6f, damage = 9f, stagger = 7f, flinch = 0.5f, reach = 9f, arc = 75f, lunge = 2.5f, knock = 3f, cost = 10f,
                        pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.5f },
            },
            dodgeSlash = new() { windup = 0.14f, active = 0.14f, recover = 0.5f, damage = 12f, stagger = 9f, flinch = 0.5f, reach = 9.5f, arc = 100f, lunge = 6f, knock = 6f, cost = 10f,
                                 pose0 = R * 0.7f, pose1 = R, pose2 = S, pose3 = Mid(0.6f) },
        };

        // 強攻撃は通常攻撃より崩しを大きくし、EN を多く使う。数値と振り方は仮。
        public static readonly DuelSwing CommonHeavy = new() // 仮
        {
            windup = 0.4f, active = 0.14f, recover = 0.6f, damage = 12f, stagger = 14f, flinch = 0.7f, reach = 10f, arc = 90f, lunge = 4f, knock = 5f, cost = 25f, heavy = true,
            pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.6f), lean1 = -12f, lean2 = 20f, drop = 0.6f,
        };

        public static readonly DuelSwing KnockbackHeavy = new() // 仮
        {
            windup = 0.37f, active = 0.18f, recover = 0.65f, damage = 13f, stagger = 17f, flinch = 0.7f, reach = 10f, arc = 105f, lunge = 4f, knock = 18f, cost = 32f, heavy = true,
            pose0 = R * 0.3f, pose1 = R, pose2 = S, pose3 = Mid(0.6f), twist0 = 10f, twist1 = 55f, twist2 = -55f, lean2 = 12f,
        };

        public static readonly DuelSwing GuardPointHeavy = new() // 仮
        {
            windup = 0.46f, active = 0.17f, recover = 0.7f, damage = 14f, stagger = 18f, flinch = 0.75f, reach = 10f, arc = 95f, lunge = 2.5f, knock = 7f, cost = 34f, heavy = true,
            pose0 = R * 0.55f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = 18f, twist2 = -24f, lean0 = 6f, lean1 = 15f, lean2 = 20f, drop = 1f,
        };

        public static readonly DuelSwing WaveHeavy = new() // 仮
        {
            windup = 0.52f, active = 0.2f, recover = 0.72f, damage = 16f, stagger = 24f, flinch = 0.8f, reach = 10f, arc = 120f, lunge = 4f, knock = 9f, cost = 45f, heavy = true,
            pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.8f, pose3 = R * 0.5f, twist0 = -20f, twist1 = -65f, twist2 = 65f, lean1 = -7f, lean2 = 18f,
        };

        /// <summary>
        /// 落下攻撃(成功しているジャンプ中に近接のボタン。DuelPlayer.Jump.cs。試す版で、値は仮)。どの近接武器でも同じ技。書き換えないこと。
        /// 新しいモーションは作っていない(斬撃クリップの流用):降りる間に剣を掲げて前へ傾き(windup の秒数で掲げきる)、着地した瞬間に振り切りまで振り下ろして腰を落とす。
        /// windup は「出してから着地して斬るまでの最短の秒数」(低い所から出しても、これより早くは斬らない)。踏み込み(lunge)は使わない(降りる動きは DuelJumpRules)。
        /// recover(着地の隙)は DuelJumpRules の dropRecoverHit / dropRecoverMiss で上書きする。
        /// stagger は、跳び越えていない敵(落下攻撃に巻き込んだほかの敵)への量(ふつうの殴りと同じく DuelGame.meleeStaggerMul が掛かる)。
        /// 跳び越えた敵へは DuelJumpRules.dropSuccessStagger。heavy は手応え(火花・ヒットストップ)を強攻撃なみにするため。
        /// </summary>
        public static readonly DuelSwing Drop = new()
        {
            windup = 0.18f, active = 0.14f, recover = 0.8f, damage = 16f, stagger = 10f, flinch = 1.2f, reach = 9.5f, arc = 100f, knock = 12f, cost = 15f, heavy = true,
            pose0 = R * 0.5f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = 8f, lean1 = 20f, lean2 = 28f, drop = 1.2f,
        };

        /// <summary>
        /// 致命攻撃(崩れた敵の近くで、地上で近接のボタン。DuelPlayer.Attack.cs。試す版で、値は仮)。どの近接武器でも同じ技。書き換えないこと。
        /// 新しいモーションは作っていない(斬撃クリップの流用。大剣の強攻撃の兜割りと同じ姿勢):のけぞって大きく掲げ、前へ倒れ込みながら振り切りまで叩きつけて腰を落とす。
        /// windup の間に相手の手前へ踏み込み(速さの上限は DuelGame.fatalLungeSpeed)、振り抜いた瞬間に相手 1 体だけに当たる。reach は振り抜いたときに届くすき間。
        /// ダメージは DuelGame の fatalMinDamage / fatalHpPart(damage・stagger は使わない)。EN は使わない(cost = 0)。
        /// windup〜active の間は無敵で取り消せず、recover(戻り)はふつうの近接と同じく少し後から取り消せる。
        /// </summary>
        public static readonly DuelSwing Fatal = new()
        {
            windup = 0.3f, active = 0.16f, recover = 0.55f, reach = 6f, knock = 20f, cost = 0f,
            pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -14f, lean1 = -18f, lean2 = 26f, drop = 1.2f,
        };

    }
}
