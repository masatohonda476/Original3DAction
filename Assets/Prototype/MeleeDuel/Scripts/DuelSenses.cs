using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 敵が自機に気づく仕組み(発覚)の数値。ステージ(DuelStage)の Inspector で触れ、全部の敵が同じものを見る。
    /// 電脳斬禅(人間サイズ)の値を、機体の大きさ(機体の高さ 9.06m / 人間 2.1m ≒ 4.3倍)へ拡大したものを既定にしている。
    /// 敵ごとに警戒度(0〜1)を持ち、1 で発覚(戦う)。見た目の表示(ゲージ・マーク・文字・色)は出さず、向き・歩き方だけで分からせる。
    /// </summary>
    [System.Serializable]
    public class DuelSenses
    {
        [Header("視覚")]
        [Tooltip("見える距離(m)。電脳斬禅 12m × 4.3")]
        public float sightRange = 52f;
        [Tooltip("前方の視野の全角(度)。この外は見えない(近くの横を除く)")]
        public float viewAngle = 160f;
        [Tooltip("中央の視野の全角(度)。ここで体の3点のうち2点以上が見えたら即発覚")]
        public float centerAngle = 90f;
        [Tooltip("近くの横:この距離(m)までは横も見え、速く気づく。電脳斬禅 4.5m × 4.3")]
        public float closeSideRange = 19f;
        [Tooltip("近くの横で見える角度(正面から左右それぞれ、度)")]
        public float closeSideAngle = 110f;
        [Tooltip("近くの横で全身が見えたときの溜まる速さ(1秒あたり。4 なら約0.25秒で発覚)")]
        public float closeSideRate = 4f;
        [Tooltip("視野の中で見えたときの溜まる速さ(1秒あたり):見える距離ぎりぎり → すぐ近く。視野の端ではさらに edgeMul 倍")]
        public float farRate = 0.45f, nearRate = 2.8f;
        [Tooltip("視野の端での溜まる速さの倍率(中央の視野の縁で1)")]
        public float edgeMul = 0.3f;
        [Tooltip("すき間がこの距離(m)より近ければ、向きに関係なくすぐ気づく")]
        public float touchGap = 3f;

        [Header("聴覚(足音。音だけでは発覚しない)")]
        [Tooltip("一歩ごとに「届いた強さ(0〜1)× これ」だけ警戒度が上がる")]
        public float hearingGain = 0.6f;
        [Tooltip("音だけで上がる警戒度の上限(1 未満なので、音だけでは発覚しない)")]
        public float hearingCap = 0.75f;
        [Tooltip("壁・床ごしの足音は、届く範囲がこの割合に縮む")]
        public float muffled = 0.3f;
        [Tooltip("歩きの足音が届く範囲(m):そっと(スティックを浅く) → 普通に走る。電脳斬禅 0.8〜2.3m × 4.3")]
        public float walkMin = 3.5f, walkMax = 10f;
        [Tooltip("ブースト移動の足音が届く範囲(m)。電脳斬禅のダッシュ 11m × 4.3")]
        public float boostStep = 47f;
        [Tooltip("回避の足音が届く範囲(m)。電脳斬禅 8m × 4.3")]
        public float dodgeStep = 34f;
        [Tooltip("飛行(噴射)の音が届く範囲(m)。止まって浮いていても鳴る")]
        public float flyStep = 40f;
        [Tooltip("一歩の長さ(m):歩き / ブースト・回避・飛行")]
        public float strideWalk = 3f, strideHard = 4.3f;

        [Header("警戒")]
        [Tooltip("手掛かり(見えた・聞こえた)が途切れてから、警戒度が減り始めるまでの秒数")]
        public float quietDelay = 1.25f;
        [Tooltip("警戒度が減る速さ(1秒あたり)")]
        public float decayRate = 0.3f;
        [Tooltip("最後の手掛かりから、その方向を向き続ける秒数")]
        public float attentionTime = 2.5f;
        [Tooltip("警戒度がこれ以上なら、最後に見聞きした所へゆっくり近づく")]
        public float investigateFrom = 0.35f;
        [Tooltip("近づく速さ(敵の歩く速さに掛ける倍率)")]
        public float investigateSpeed = 0.35f;
        [Tooltip("近づくのは、持ち場からこの距離(m)の内側まで。電脳斬禅 7m × 4.3")]
        public float investigateRadius = 30f;
        [Tooltip("上下にこれ以上(m)離れた所へは近づかない(上下階へは追わない)")]
        public float investigateMaxHeight = 10f;

        [Header("仲間への伝達(連鎖しない・壁ごしは無し)")]
        [Tooltip("発覚した敵から、この距離(m)の内側で見通せる仲間に伝える。電脳斬禅 11m × 4.3")]
        public float alertRadius = 47f;
        [Tooltip("伝えられた仲間が自機を見えていないときの警戒度(不審から)")]
        public float alertAwareness = 0.55f;

        [Header("見失う")]
        [Tooltip("発覚した後、自機が見えない状態がこの秒数続いたら、発覚を切って持ち場へ帰る")]
        public float loseSightTime = 6f;
        [Tooltip("発覚した後に『見えている』とみなす距離(m)。これより遠いと見えていない扱い")]
        public float keepSightRange = 80f;
        [Tooltip("自機が持ち場からこの距離(m)より離れたら、発覚を切って帰る(0 で無し)。電脳斬禅 24m × 4.3")]
        public float leashRange = 100f;

        public float ViewEdgeCos => Mathf.Cos(viewAngle * 0.5f * Mathf.Deg2Rad);
        public float ViewCenterCos => Mathf.Cos(centerAngle * 0.5f * Mathf.Deg2Rad);
        public float CloseSideCos => Mathf.Cos(closeSideAngle * Mathf.Deg2Rad);

        /// <summary>a から b が見通せるか(地形の壁・床・柱でさえぎる。機体と見えない壁は Ignore Raycast なので当たらない)。</summary>
        public static bool ClearSight(Vector3 a, Vector3 b) =>
            !Physics.Linecast(a, b, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }
}
