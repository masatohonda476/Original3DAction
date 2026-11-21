using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 「敵の攻撃をジャンプで越えて、落下攻撃で大きく崩す」を試す版の決まり(数値はどれも仮)。DuelGame.jump のインスペクターで直せる。
    /// 作者「敵の攻撃に合わせて上昇すると上から回り込んで落下攻撃でき、崩しが大きく溜まるとか考えました」「地上からのジャンプの時だけそうするのはどうですか?」
    /// 「1はジャンプ中だけにするのはどうですか?」「2敵の遠距離攻撃などや上段の攻撃は不可能にします」。
    /// 2 つ目の形(最初の形は「高さで外せば成功」で、作者が触って成功しなかった):
    ///   作者「当たる瞬間にジャンプがいいです　あと、ただのジャンプでは落下攻撃は出ないようにしてください　致命攻撃も必要ですね」。
    ///   受付の幅は「当たる 0.2 秒前まで」、受付より早く跳んだときは「当たらないが成功でもない」、ただのジャンプ中の R1 は「空中のふつうの振り」。
    /// 流れ:地上で ✗ を押すと跳ぶ(DuelPlayer.Jump.cs)→ 跳べる技(下段)の当たりが出る瞬間に、踏み切ってから justWindow 秒以内なら成功(DuelEnemy.DoHit)
    ///   → 成功している間(そのジャンプの着地・被弾・飛行に移るまで)だけ、近接のボタンが落下攻撃になり、跳び越えた敵の怯みゲージを大きくためる
    ///   → 崩れた敵の近くで近接のボタンを押すと致命攻撃(DuelPlayer.Attack.cs。数値は DuelGame の「致命攻撃」)。
    /// 技ごとの区分(跳べる=下段 / 跳べない=上段)は下の表(Jumpable)。敵の遠距離攻撃はまだ無いので表にない(足すときは「跳べない」にする)。
    /// </summary>
    [System.Serializable]
    public class DuelJumpRules
    {
        [Header("ジャンプ(地上で ✗ を押した瞬間に跳ぶ。✗ を押し続けると今までの飛行に移り、そこからは「ジャンプ中」ではない)")]
        [Tooltip("踏み切りの上向きの速さ(m/s)。重力は 40 m/s² なので、頂点の高さ = 速さ² ÷ 80、頂点まで = 速さ ÷ 40 秒。\n" +
                 "30 なら頂点 11.25 m(0.75 秒)、滞空 1.5 秒。武装の重さは掛けない(重くても同じ高さ)。0 でジャンプなし(今までの上昇だけ)")]
        public float jumpSpeed = 30f;
        [Tooltip("ジャンプで使う EN(回避は 30)。EN が少しでもあれば跳べ、足りない分はマイナスになる。スラスターなどの EN の倍率は掛かる")]
        public float jumpCost = 25f;
        [Tooltip("踏み切ってから ✗ を押し続けた秒数がこれに達したら、頂点を待たずに飛行(ブースト上昇)に移る。上向きの速さはそのまま引き継ぐ。\n" +
                 "飛行に移ると「ジャンプ中」は終わる(成功していても、成功と落下攻撃は消える)。0 以下なら、押し続けても頂点を過ぎるまで待つ(前の形)。\n" +
                 "jumpSpeed 30 なら、0.3 秒の時点で高さ 7.2 m・上向き 18 m/s(飛行の上昇は 20 m/s)")]
        public float jumpToFlyHold = 0.3f;
        [Tooltip("ジャンプ中に敵の体の上に乗った・乗りそうなとき、水平に押し出す速さ(m/s)。頭の上に着地せず、滑り落ちる(落ちるまではジャンプ中のまま)。0 で押し出さない")]
        public float slideOffSpeed = 20f;

        [Header("成功の受付(当たる瞬間に跳ぶ)")]
        [Tooltip("敵の跳べる技の当たりが出る瞬間に、踏み切ってからこの秒数以内なら成功(高さに関係なく当たらない。水平の間合いは当たる範囲であること)。\n" +
                 "= 当たる瞬間の、この秒数前から当たる瞬間までに ✗ を押す。これより早く跳んでいたときは、下の clearHeight で越えていれば当たらないだけ(成功ではない)")]
        public float justWindow = 0.2f;

        [Header("跳べる技・跳べない技(ジャンプ中だけの決まり。飛行中・地上は今までどおり、高さの差が機体の高さを超えたら届かない)")]
        [Tooltip("受付より早く跳んでいたとき:跳べる技(下段)は、ジャンプ中の自機の足もとが敵の足もとよりこの高さ(m)以上高ければ外れる(成功ではない)。\n" +
                 "敵の大きさは掛けない(ボスでも同じ)。jumpSpeed 30 なら、踏み切ってから 5 m まで 0.19 秒、5 m より上にいるのは 1.12 秒")]
        public float clearHeight = 5f;
        [Tooltip("跳べない技(上段)は、ジャンプ中の自機が敵の足もとよりこの高さ(m)まで上にいても届く(ジャンプの頂点より大きくしておく)")]
        public float highReach = 16f;
        [Tooltip("横薙ぎ(兵卒・剣士・ボス。2発目も)")] public bool sweep = true;
        [Tooltip("踏み込み突き(兵卒・剣士・ボス)")] public bool thrust = true;
        [Tooltip("ボスの大薙ぎ(1回転して全周)")] public bool wideSweep = true;
        [Tooltip("ボスの突進")] public bool charge = true;
        [Tooltip("ボスの三段斬りの1・2段目(横の斬り)")] public bool flurry = true;
        [Tooltip("ボスの三段斬りの3段目(縦の振り下ろし)。見た目は上段だが、今回の仮の区分では跳べる。オフにすると3段目だけ跳べない")]
        public bool flurryLast = true;
        [Tooltip("叩きつけ(剣士・ボス。上段)")] public bool slam = false;
        [Tooltip("ボスの跳躍叩きつけの直撃(上段)。広がる衝撃波は今までどおり、空中にいれば当たらない(成功にはならない)")]
        public bool leapSlam = false;
        [Tooltip("跳び上がり斬り(対空。上段)")] public bool antiAir = false;

        [Header("落下攻撃(成功しているジャンプ中だけ、近接のボタン。相手へ向かって降り、着地した瞬間に斬る。技の威力・届く範囲は DuelMoveSet.Drop)")]
        [Tooltip("落下攻撃が、跳び越えた敵の怯みゲージへためる量(兵卒の最大 10・剣士 25・ボス 70)。武装の倍率・「ふつうの殴り」の倍率(DuelGame.meleeStaggerMul)は掛からない。\n" +
                 "敵が攻撃の後の隙の間に当てると、さらに 1.6 倍(DuelEnemy.recoverStaggerMul)")]
        public float dropSuccessStagger = 30f;
        [Tooltip("降りる速さ(m/s。相手の手前へまっすぐ向かう線の上の速さ)")]
        public float dropSpeed = 50f;
        [Tooltip("下向きの速さの下限(m/s)。低い所から遠い相手へ出したときも、これより遅くは降りない(届かなければ手前に着地して空振り)。\n" +
                 "成功した直後はまだ低い(0.2 秒で 5 m 弱)ので、遠い相手には少し上がってから出さないと届かない。0 にすると、低くても相手の手前まで必ず届く")]
        public float dropMinFall = 25f;
        [Tooltip("相手とのすき間がこれ(m)以内なら、相手へ向かって降りる。遠ければ、向いている方の斜め下へ降りる")]
        public float dropRange = 26f;
        [Tooltip("相手の手前、このすき間(m)の所へ降りる(相手の上には降りない。真上・近すぎる所から出したときも、ここまで水平に出てから・下がりながら降りる)")]
        public float dropStopGap = 1.5f;
        [Tooltip("当てたときの着地の隙(秒)。この間は回避・次の攻撃・ジャンプで取り消せない")]
        public float dropRecoverHit = 0.35f;
        [Tooltip("外したときの着地の隙(秒)。この間は回避・次の攻撃・ジャンプで取り消せない")]
        public float dropRecoverMiss = 0.8f;
        [Tooltip("降り始めてからこの秒数たっても着地しなければ、その場で振る(引っかかったときの保険)")]
        public float dropMaxTime = 1.2f;

        [Header("成功の合図(画面は揺らさない。自機の足もとの火花・自機が一瞬光る・短いヒットストップ)")]
        [Tooltip("成功した瞬間のヒットストップ(秒)。0 で止めない。成功した瞬間の自機はまだ低く、敵の剣が体を通り抜けて見えるので、「かわした」間として少し長め(前の形は 0.07)")]
        public float successHitStop = 0.12f;

        /// <summary>
        /// その技が跳べる(下段)か。跳べる技は、当たる瞬間に跳べば成功、早く跳んでいたときは clearHeight 以上高ければ外れるだけ。
        /// 跳べない技(上段)は highReach まで届く。flurryIndex は三段斬りの何段目か(0〜2)。
        /// </summary>
        public bool Jumpable(DuelEnemy.Skill s, int flurryIndex) => s switch
        {
            DuelEnemy.Skill.Sweep => sweep,
            DuelEnemy.Skill.Thrust => thrust,
            DuelEnemy.Skill.WideSweep => wideSweep,
            DuelEnemy.Skill.Charge => charge,
            DuelEnemy.Skill.Flurry => flurryIndex >= 2 ? flurryLast : flurry,
            DuelEnemy.Skill.Slam => slam,
            DuelEnemy.Skill.LeapSlam => leapSlam,
            DuelEnemy.Skill.AntiAir => antiAir,
            _ => false,
        };
    }
}
