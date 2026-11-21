using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 自機の武装(DuelPlayer.Init でいつも付く)。付ける所は5つ:右手(R1)/ 左手(L1)/ 背中 / 右肩(R2)/ 左肩(L2)。どれも1つずつ。
    ///   手:剣・盾・手の銃のどれでも(左右どちらにも)。剣ならそのボタンで攻撃(技は剣ごとの表)、盾なら押している間ガード、銃なら撃つ。
    ///       最初は右手に素の剣、左手は空(盾を拾うまでガードできない)。
    ///   背中:スラスター。走り・ブースト・回避の距離が伸び、回避・ブースト・飛行のENが減る。
    ///   肩:銃・砲・ミサイルのどれでも(左右どちらにも)。R2 で右肩、L2 で左肩を撃つ。撃ち方(押している間 / 押すたび)は武装ごと。
    /// 近接攻撃の間(振りかぶり〜戻り)と回避の間は撃てない(DuelPlayer.CanShoot)。
    /// 狙いの既定(DuelArmsRules.cursorMovable オフ・aimByFacing オン。作者「照準は動かせないようにしましょう」「自機の向いている方に弾が飛ぶようにしてください」):
    ///   照準は動かさず、弾は自機の向いている方へ飛ぶ(上下はロックオンの相手の胸、いなければカメラの上下の向き。照準の表示はその先へ動く。UpdateCursor)。
    ///   射撃では自機は向き直らない(ロックオン中は相手を向き、していなければ撃たないときと同じ。作者「ノーロック射撃時自機とカメラの向きを変えないようにしたいです」)。
    ///   反動で照準はずれない(ばらつきの広がりは残る)。弾が速さを持つ撃ち方にも、枠の中の敵への吸い付きが効く(AssistDeg)。
    /// 照準を動かせるとき(cursorMovable オン。作者「ロックオンにカメラ操作は完全に委ねられますか?」→「これでやってみてください」の形):
    ///   照準(Cursor)は画面の上を動くカーソル。銃・砲(ミサイル以外)のボタンを押している間と撃ってから少しの間(構え。Aiming = CursorActive)は、
    ///   右スティック・マウスで動かす(カメラは回さない。DuelCamera)。可動範囲は画面の中央の楕円(DuelArmsRules.cursorRangeX / Y)。
    ///   構えていない間はゆっくり画面の中央へ戻る(表示が動くだけ)。照準が敵の上にある間は、右スティックでの動きを少し遅くする(cursorSlowOnEnemy)。
    ///   銃・砲は、カメラから照準を通る向きの先(最初に当たる地形・敵)へ撃つ。照準の枠は照準の位置を中心にし、枠に掛かっている射程の内・見通せる敵へ
    ///   狙いを少しだけ寄せる(吸い付き。最大で「枠の大きさ × assistPart」度)。弾が速さを持つ武装は既定で吸い付かない(projectileAssist。
    ///   照準を先に置いた偏差射撃を、吸い付きが敵の今の位置へ引き戻してしまうため)。照準が体に掛かる・寄せた狙いが掛かるときだけ枠を橙にして敵に印。
    ///   ミサイルは今までどおり枠(照準の位置が中心)の中の敵に割り振る(ロックオン中の敵は枠を lockFrameMul 倍に広くみなす)。
    /// 弾の速さ:実弾の銃・砲は見える弾が DuelWeapon.shotSpeed(m/s)で飛び、当たった時に火花・ヒットマーカーを出す。横に動く・避ける敵には
    ///   照準を先に置かないと当たらない(偏差の自動補正はしない)。shotSpeed が 0 の撃ち方(照射し続けるレーザー)は今までどおりの光線。
    /// 反動:照準を動かせるときは、撃つたびに照準そのものが上(と少し左右ランダム)へずれる(DuelWeapon.aimKick / aimKickSide。全体の倍率 kickMul)。押さえ込まないと上がっていく。
    ///   ばらつきの広がり(DuelWeapon.recoilPerShot / recoilMax。枠と十字が開く)も今までどおり。カメラは一切動かさない。
    ///   自機は構えの間、照準の先を向く(DuelPlayer)。近接・回避で構えは終わる。
    ///   手応え:当たったら照準の位置にヒットマーカー(HitMarkAt / HitMarkKind。崩せる・崩れた・倒したで色を変える)。
    /// 共通の弾:実弾の銃・砲・ミサイルは1種類の弾(Ammo)を使う。弾倉のある銃はリロードのときに詰めた分だけ、弾倉のない撃ち方は撃つたびに使う
    ///   (DuelWeapon.ammoCost。エネルギー武器は 0 で使わない)。弾は武装を分解して得る。
    ///   弾倉のある銃を分解すると、弾倉の残りの分も共通の弾へ戻る(弾にしても回復にしても。落ちたばかりの満タンの弾倉も数える。MagAmmo)。
    /// EN:エネルギー武器(ビーム・レーザー。DuelWeapon.enCost)は弾の代わりに自機のENを使う(回避・攻撃と同じEN)。ENが0以下の間は撃てず、
    ///   少しでも残っていれば撃てて足りない分はマイナスになる。押している間撃ち続ける武装は1秒あたり、ほかは1発ごと。
    ///   溜め撃ちは溜め始めにENが残っていれば溜められ、離して撃つときに使う(溜めている間に0以下になっても撃てる)。
    /// 所持:付けずに持っておける(Stash)。付けている武装と所持品の重さの合計で、持てる上限と推力(ENを使う移動:ブースト移動・回避の距離・
    ///   飛ぶときの上昇の速さ)が決まる(Mobility)。歩き・走りには効かない(作者「歩きには効かないようにして、ENを使った移動に効くようにします」)。
    /// 拾う:落ちている武装の近くで、十字下(G)で所持品へ、十字上(F)で分解(選ぶ表示で十字左(←)が弾・十字右(→)が回復・十字上(F)でやめる)、十字右(V)で付ける。
    ///   付け先を選ぶ表示が出て(付け先が空きでもふさがっていても同じ手順。作者「枠が空きのときと装備済みのときの動作は変えないでほしい」)、
    ///   R1/L1(手)・R2/L2(肩)・✗(背中)で決める。
    ///   選ぶ表示(付け先・分解)の間に効くのは、移動・カメラ・選ぶ表示の操作だけ(作者「ドロップした武装の操作中に移動とカメラ以外の操作が
    ///   できないようにし、○でキャンセルできるようにしてください」。ChoiceOpen / ChoiceLocksInput)。攻撃・射撃・ガード・溜め撃ち・リロード・飛行は
    ///   ここで、回避・ブーストの切り替え・ロックオンのボタンは DuelPlayer で止める。終えた時に押していた R1/L1/R2/L2/✗ は、離すまで効かない。
    ///   拾う案内のカードが出ているだけ(Nearby があるだけ)の間は何も止めない。
    ///   付け先を選ぶ・分解を選ぶ・背中に付けるのは、自機が動ける(Free)かガード中だけ(入ると盾の構えは解ける)。怯み・ガード崩れなどで動けなくなったら取り消し。
    ///   ○(Backspace)・入ったのと同じボタンをもう一度・離れるでも取り消し。
    ///   同じ所の今の武装は、拾った武装のあった所に置く(置いた直後から拾い直せる。素の剣も。ただし拾う案内の優先度は低く、
    ///   近くにふつうの武装があればそちらが先に対象になる。自機が拾える範囲から一度出ると戻る。DuelLoot.LowerPriority)。装備の確認の画面から、外して持つ・所持品を付ける・
    ///   足もとの床に捨てる(Drop)・分解する。
    /// 撃墜されても武装・所持品・弾は残る。ただしボス戦の間(入口の扉が閉じている間。DuelStage)にやられて再開したときは、共通の弾と弾倉の残りだけを
    ///   扉が閉じたときに戻し、付けている銃を共通の弾でリロード済みにする(KeepAmmo / RestoreKeptAmmo。武装・所持品はやられた時点のまま)。
    /// ボスを倒すと爆発で全部消し飛ぶ(LoseAll。両手持ちも消え、右手は素の剣、左手は空に戻る。所持品も消え、弾は0)。自機の素の性能(HP・EN・怯み)は変えない。画面は揺らさない。
    /// 両手持ち(両手の近接・両手の銃・砲):右手と左手の両方の所に同じ武装を入れる。R1 が主の技、L1 が別の技(近接は別の振り、射撃は別の撃ち方)。
    ///   付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付き、それまでの右手・左手の武装は拾った所に置く(素の剣も)。
    ///   両手持ちのまま片手の武装を付けると、両手持ちは外れて置かれ、もう片方の手は空になる。捨てると両手が空く。盾がないのでガードはできない。
    ///   両手の銃・砲の撃つ状態は右手の所で持ち、L1 の撃ち方は別の状態(AltIndex)で持つ。溜め撃ちは L1 を押して溜め、溜まってから離すと撃つ。
    /// リロード:弾倉のある銃は、撃ち切ると自動でリロードする。撃ちかけでも △ を押しながらその銃を撃つボタンを押すとリロードする
    ///   (作者「△+射撃ボタンで手動リロードできるようにして、待てば自動リロードは消してください」。TryManualReload)。
    ///   リロード中はその銃だけ撃てない(ほかの武装は使える)。弾倉のない銃・砲・ミサイル・剣・盾では △+ボタンは今までどおり(剣なら強攻撃)。
    /// 毎フレームの処理は DuelPlayer.Update の始めに Tick() で呼ぶ(ボタンの横取りを、自機が読むより先に決めるため)。
    /// ファイルは役割ごとに分けている:DuelArms.Equip.cs(付ける・外す・所持品・見た目)/ DuelArms.Aim.cs(狙い・照準・照準の枠)/
    /// DuelArms.Firing.cs(銃・砲・ミサイルを撃つ)。
    /// </summary>
    public partial class DuelArms : MonoBehaviour
    {
        const int SlotCount = 5;
        /// <summary>撃つ状態の6つ目:両手の銃・砲の L1 の撃ち方。</summary>
        const int AltIndex = 5, StateCount = 6;
        const int R = (int)DuelSlot.RightHand, L = (int)DuelSlot.LeftHand;
        /// <summary>押すたびに撃つ武装で、撃てない間に押したのを覚えておく秒数。</summary>
        const float PressBuffer = 0.2f;

        DuelPlayer player;
        DuelGame game;
        MechRig rig;
        readonly DuelWeapon[] slots = new DuelWeapon[SlotCount];
        readonly Transform[] visuals = new Transform[SlotCount];
        Vector3 swordBaseScale = Vector3.one, swordLBaseScale = Vector3.one;
        // 撃つ武装の、所ごとの状態(6つ目は両手の銃・砲の L1 の撃ち方)
        readonly float[] cd = new float[StateCount], cdMax = new float[StateCount], reload = new float[StateCount];
        readonly float[] lastFire = new float[StateCount], pressedAt = new float[StateCount];
        /// <summary>次に撃てるまでの待ちを装填のゲージに出すか(撃った後と、外したときの装填待ちを持ち越して付け直した後。付けた直後の 0.3 秒の待ちは出さない)。</summary>
        readonly bool[] cdShown = new bool[StateCount];
        /// <summary>弾が受け継ぐ自機の速度(今の速度へ DuelArmsRules.inheritSmoothTime 秒ほどで追いつく。Fly と十字の見込み PredictShot で同じ値を使う)。</summary>
        Vector3 inheritVel;
        readonly int[] mag = new int[StateCount];
        /// <summary>撃つ状態ごとの、付けている武装の個体の番号(Memo.id。0 = まだ付けていない)。</summary>
        readonly int[] uid = new int[StateCount];
        int nextUid;
        /// <summary>撃つ状態ごとの反動の広がり(度。spread に足す)。撃つたびに広がり、撃つのをやめると戻る。</summary>
        readonly float[] bloom = new float[StateCount];

        /// <summary>
        /// 最後に射撃(銃・砲。ミサイルは入れない)が敵に当たった時刻(実時間)と、その印の種類(照準の真ん中に出すヒットマーカー)。
        /// 0 = 当たった / 1 = 崩せる(怯みゲージが射撃の上限まで溜まっている。斬りにいけ)/ 2 = 崩れている敵に当たった / 3 = 倒した。
        /// </summary>
        public float HitMarkAt { get; private set; } = -10f;
        public int HitMarkKind { get; private set; }
        /// <summary>リロード中に取り置いた共通の弾(詰める数と弾の量)。リロードを始めたときに弾から引き、終わったら弾倉へ。やめたら返す。</summary>
        readonly int[] resN = new int[StateCount];
        readonly float[] resAmmo = new float[StateCount];
        /// <summary>実弾の溜め撃ち(L1)の、溜め始めに取り置いた1回分の弾(撃ったら使い、取り消したら返す)。</summary>
        float chargeAmmo;
        /// <summary>L1 の溜め撃ちを溜めている秒数(0なら溜めていない)。</summary>
        float charge;
        /// <summary>
        /// ボタン(0=R1 1=L1 2=R2 3=L2 4=✗)を、離すまで使わない(付け先を決めたボタンや、選ぶ表示(付け先・分解)の間に押し続けていたボタンが、
        /// 表示を終えた瞬間に攻撃・射撃・ガード・飛行にならないように。EndChoice が立て、Tick が離したら下ろす)。△+撃つボタンのリロードでも立てる。
        /// ✗(FlyBtn)は付け先を選ぶ表示で背中に付けるボタン。ふだんは飛行(DuelPlayer が FlyBlocked を見る)。
        /// </summary>
        readonly bool[] suppress = new bool[5];
        const int FlyBtn = 4;
        /// <summary>✗ を前のフレームに押していたか / このフレームに押したか(押し始めの判定。Tick の始めで決める)。</summary>
        bool flyPrev, flyPressed;

        /// <summary>拾える範囲にある一番近い武装(なければ null)。</summary>
        public DuelLoot Nearby { get; private set; }
        /// <summary>付け先を選んでいる武装(選んでいなければ null)。</summary>
        public DuelLoot Choosing { get; private set; }
        /// <summary>分解(弾にするか回復にするか)を選んでいる武装(選んでいなければ null)。十字左(←)で弾、十字右(→)で回復、十字上(F)でやめる。</summary>
        public DuelLoot Breaking { get; private set; }
        /// <summary>分解を選ぶ表示を抜けた後、「弾にする」のボタン(十字左 / ←)を離すまで立てておく(押しっぱなしの十字左でロックオンが切り替わらないように)。</summary>
        bool breakLeftHold;
        /// <summary>
        /// 十字左を分解の「弾にする」に使っている(分解を選んでいる間と、抜けた後に離すまで)。この間、DuelPlayer は十字左でロックオンを切り替えない。
        /// 選ぶ表示の間と閉じたフレームは、ロックオンのボタンそのものが効かない(ChoiceLocksInput)ので、これが効くのは抜けた後、十字左を離すまでの間
        /// (その間も Q・中クリックでは切り替わる)。
        /// </summary>
        public bool LockDpadTaken => Breaking != null || breakLeftHold;

        /// <summary>選ぶ表示(付け先を選ぶ表示・分解を選ぶ表示)を開いている。拾う案内のカードが出ているだけ(Nearby があるだけ)の間は入らない。</summary>
        public bool ChoiceOpen => Choosing != null || Breaking != null;
        /// <summary>選ぶ表示を最後に終えたフレーム(EndChoice)。</summary>
        int choiceEndFrame = -1;
        /// <summary>
        /// 選ぶ表示が戦闘の入力を止めている:開いている間と、閉じたそのフレーム。この間に効くのは、移動・カメラ・選ぶ表示の操作だけ
        /// (作者「ドロップした武装の操作中に移動とカメラ以外の操作ができないようにし、○でキャンセルできるようにしてください」)。
        /// 手・肩のボタンと ✗ は Held / Pressed / FlyBlocked が止める。DuelPlayer はこれを見て、回避・ブーストの切り替え・ロックオンのボタンを読まない。
        /// 閉じたフレームも入れるのは、Tick が DuelPlayer の入力より先に走るため(取り消しの ○ がそのフレームにブーストの切り替えにならないように。
        /// 選ぶ表示を終えたのと同じフレームに押したボタンも出さない)。押した瞬間だけ読む入力は、閉じた後に押しっぱなしでも出ない。
        /// </summary>
        public bool ChoiceLocksInput => ChoiceOpen || choiceEndFrame == Time.frameCount;

        // ───────── 所持品・共通の弾 ─────────

        /// <summary>
        /// 外した武装が覚えておく撃つ状態(所持品・床に置いた武装)。付け直したときに戻す(外して付け直すと装填待ちが縮む・弾倉が満ちる、を防ぐ)。
        /// mag:弾倉の残り(-1=満タン)。ready / altReady:主の撃ち方・両手の銃・砲の L1 の撃ち方が次に撃てる時刻(Time.time。0 なら待ちなし)。
        /// 時刻で覚えるので、装備の確認(時間が止まっている)の中で付け外ししても待ちは減らず、持ち歩いている間・床にある間は進む。
        /// </summary>
        public struct Memo
        {
            public int mag;
            public float ready, altReady;
            /// <summary>
            /// 個体の番号(0 = まだ付けていない)。同じ種類の武装は同じ DuelWeapon を指すので、個体を見分けるのはこの番号だけ。
            /// ボス戦の前の弾倉の残りを控えるとき(KeepAmmo)に付け、付ける・外す・持つ・床に置くの間ついて回る(付けている間は uid)。
            /// </summary>
            public int id;
            /// <summary>まだ撃っていない武装(敵が落とした武装など。弾倉は満タン)。</summary>
            public static readonly Memo Fresh = new() { mag = -1 };
        }

        /// <summary>所持品の1つ(付けていない武装)と、外したときの撃つ状態(Memo)。</summary>
        public class Carried
        {
            public DuelWeapon w;
            public Memo memo = Memo.Fresh;
        }

        readonly List<Carried> stash = new();
        /// <summary>所持品(付けずに持っている武装。拾った順)。</summary>
        public IReadOnlyList<Carried> Stash => stash;
        /// <summary>共通の弾(実弾の銃・砲・ミサイルが使う)。</summary>
        public float Ammo { get; private set; }
        /// <summary>照準の枠・重さ・弾・分解の決まり(ステージのインスペクター)。</summary>
        public DuelArmsRules Rules => DuelArmsRules.Of(game);
        /// <summary>弾が足りなくて撃てなかった・リロードできなかった時刻(右下の弾の表示を赤く光らせる)。</summary>
        public float NoAmmoAt { get; private set; } = -10f;
        /// <summary>ENが0以下でエネルギー武器を撃てなかった時刻(左下のENの表示を赤く光らせる)。</summary>
        public float NoEnergyAt { get; private set; } = -10f;
        /// <summary>重すぎて持てなかった・付けられなかった時刻(実時間。重さの表示を赤く光らせる)。</summary>
        public float TooHeavyAt { get; private set; } = -10f;
        float noAmmoLogAt = -10f, noEnergyLogAt = -10f;

        public void Init(DuelPlayer p, DuelGame g, MechRig r)
        {
            player = p;
            game = g;
            rig = r;
            if (rig != null && rig.sword != null) swordBaseScale = rig.sword.localScale;
            if (rig != null && rig.swordL != null) swordLBaseScale = rig.swordL.localScale;
            for (int i = 0; i < StateCount; i++) lastFire[i] = pressedAt[i] = -10f;
            slots[(int)DuelSlot.RightHand] = DuelWeapon.BaseSword;
            for (int i = 0; i < SlotCount; i++) Refresh((DuelSlot)i);
            Ammo = Mathf.Clamp(Rules.ammoStart, 0f, Rules.ammoMax);
        }

        // ───────── 重さ ─────────

        /// <summary>付けている武装の重さ(両手持ちは1つとして数える)。</summary>
        public float EquippedWeight
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < SlotCount; i++)
                {
                    var w = slots[i];
                    if (w == null || (i == L && w.IsTwoHand)) continue;
                    s += w.weight;
                }
                return s;
            }
        }

        /// <summary>所持品の重さ(そのまま。重さの合計には stockWeightRate 倍で入る)。</summary>
        public float StashWeight
        {
            get
            {
                float s = 0f;
                foreach (var c in stash) s += c.w.weight;
                return s;
            }
        }

        /// <summary>重さの合計(付けている武装 + 所持品 × stockWeightRate)。持てる上限(CarryMax)と速さ(Mobility)はこれで決まる。</summary>
        public float TotalWeight => EquippedWeight + StashWeight * Rules.stockWeightRate;
        public float CarryMax => Rules.carryMax;
        /// <summary>重さによる推力の倍率(ブースト移動・回避の距離・上昇の速さ。歩き・走りには掛けない)。</summary>
        public float Mobility => Rules.MobilityAt(TotalWeight);
        /// <summary>その重さの合計が、持てる上限に収まるか(拾う案内で、押す前に重すぎる所を赤くするのにも使う)。</summary>
        public bool Fits(float total) => total <= CarryMax + 0.001f;

        /// <summary>その所に w を付けたときに外れる武装の重さ(両手持ちが絡むときは両手の分。DuelArms.Mount と同じ決まり)。</summary>
        float RemovedWeight(DuelWeapon w, DuelSlot slot)
        {
            int i = (int)slot;
            if ((i == R || i == L) && (w.IsTwoHand || TwoHanded))
            {
                float s = slots[R] != null ? slots[R].weight : 0f;
                if (slots[L] != null && !TwoHanded) s += slots[L].weight;
                return s;
            }
            return slots[i] != null ? slots[i].weight : 0f;
        }

        /// <summary>落ちている武装をその所に付けたときの重さの合計(外れた武装は床に置かれる)。</summary>
        public float WeightIfEquipped(DuelWeapon w, DuelSlot slot) => TotalWeight + w.weight - RemovedWeight(w, slot);
        /// <summary>落ちている武装を所持品に入れたときの重さの合計。</summary>
        public float WeightIfStashed(DuelWeapon w) => TotalWeight + w.weight * Rules.stockWeightRate;

        void TooHeavy(float after, string what)
        {
            TooHeavyAt = Time.unscaledTime;
            game.Log($"重すぎて{what}(重さ {after:0}/{CarryMax:0})― いらない武装を分解か捨てると軽くなる");
        }

        public DuelWeapon Get(DuelSlot s) => slots[(int)s];

        // ───────── 自機から見る値 ─────────

        /// <summary>その手(0=右 / 1=左)の武装(空なら null)。</summary>
        public DuelWeapon Hand(int hand) => slots[hand == 0 ? R : L];
        /// <summary>その手で振れる(片手の剣、または両手の近接。両手の近接は R1 も L1 も振る)。</summary>
        public bool IsSword(int hand) => Hand(hand) is { IsBlade: true };
        /// <summary>両手持ちを付けている(右手と左手に同じ武装)。</summary>
        public bool TwoHanded => slots[R] is { IsTwoHand: true };
        /// <summary>付けている両手の銃・砲(なければ null)。</summary>
        DuelWeapon TwoHandGun => slots[R] is { kind: DuelKind.TwoHandGun } w ? w : null;
        /// <summary>両手の銃・砲の L1 の撃ち方(なければ null)。</summary>
        public DuelWeapon AltFire => TwoHandGun?.alt;
        /// <summary>撃つ状態の番号の武装(0〜4 は所、5 は両手の銃・砲の L1 の撃ち方)。</summary>
        DuelWeapon StateWeapon(int i) => i < SlotCount ? slots[i] : AltFire;
        /// <summary>L1 の溜め撃ちの溜まり具合(0〜1。溜め撃ちでなければ 0)。</summary>
        public float Charge01 => AltFire is { charge: > 0f } a ? Mathf.Clamp01(charge / a.charge) : 0f;
        /// <summary>L1 の撃ち方が次に撃てるまでの残り(0〜1。0で撃てる)。</summary>
        public float AltCooldown01 => AltFire != null && cd[AltIndex] > 0f ? Mathf.Clamp01(cd[AltIndex] / Mathf.Max(0.01f, cdMax[AltIndex])) : 0f;
        /// <summary>
        /// 射撃の構え:狙って撃つ武装(手の銃・肩の銃・砲・両手の銃・砲と、その L1 の撃ち方。ミサイルは入らない)のボタンを押している間と、
        /// それを最後に撃ってから DuelArmsRules.aimHoldAfterShot 秒の間(L1 の溜め撃ちを溜めている間も)。
        /// 照準を動かせるとき(CursorActive)は、構えの間、自機は照準の先を向き(DuelPlayer)、右スティック・マウスが画面の上の照準を動かす(カメラは回さない。DuelCamera)。
        /// 既定(弾が自機の向いている方へ飛ぶ形)では、構えでも自機は向き直らない。
        /// ロックオンの相手を自動で選ぶのは構えの間はしない。右スティックの弾きでの切り替えは、照準を動かしている間だけしない(DuelPlayer)。
        /// 近接・回避を出したら終わる(それより前に撃った分は数えない。ボタンを押したままなら、近接・回避が終わったらまた構える)。
        /// </summary>
        public bool Aiming
        {
            get
            {
                if (player == null || player.Dead || ChoiceOpen) return false;   // 選ぶ表示の間は構えない(右スティック・マウスはカメラのまま)
                if (player.Current is DuelPlayer.State.Attack or DuelPlayer.State.Dodge) return false;
                float now = Time.time, cut = player.MeleeOrDodgeAt, hold = Rules.aimHoldAfterShot;
                for (int i = 0; i < StateCount; i++)
                {
                    var w = StateWeapon(i);
                    if (w == null || !w.IsShooter || w.IsMissile || (i == L && w.IsTwoHand)) continue;
                    if (Held(i == AltIndex ? DuelSlot.LeftHand : (DuelSlot)i)) return true;
                    if (lastFire[i] > cut && now - lastFire[i] < hold) return true;
                }
                return charge > 0f && AltFire is { IsMissile: false };
            }
        }

        /// <summary>右スティック・マウスが画面の上の照準を動かしている(= 構え。DuelCamera はこの間カメラを回さない)。</summary>
        public bool CursorActive => Aiming && Rules.cursorMovable;   // 作者「照準は動かせないようにしましょう」で既定は動かさない(照準は画面の中央のまま)

        /// <summary>右手の銃(手の銃・両手の銃・砲)を最近撃ったか、L1 を溜めている(射撃の構えの見た目用)。</summary>
        public bool AimPose(float sec) => slots[R] is { kind: DuelKind.HandGun or DuelKind.TwoHandGun }
                                           && (ShotRecently(DuelSlot.RightHand, sec) || (TwoHandGun != null && (Time.time - lastFire[AltIndex] < sec || charge > 0f)));

        public DuelWeapon BackW => slots[(int)DuelSlot.Back];
        /// <summary>走りの倍率:スラスター × 持っている盾の重さ。重さの合計による倍率(Mobility)は掛けない(ENを使わない移動なので)。</summary>
        public float RunMul
        {
            get
            {
                float m = BackW != null ? BackW.runMul : 1f;
                for (int h = 0; h < 2; h++)
                    if (Hand(h) is { kind: DuelKind.Shield } s) m *= s.runMul;
                return m;
            }
        }
        /// <summary>ブースト移動と飛ぶときの上昇の速さの倍率(スラスター × 重さ)。</summary>
        public float DashMul => (BackW != null ? BackW.dashMul : 1f) * Mobility;
        /// <summary>回避で動く距離の倍率(スラスター × 重さ)。</summary>
        public float DodgeMul => (BackW != null ? BackW.dodgeMul : 1f) * Mobility;
        public float EnCostMul => BackW != null ? BackW.enCostMul : 1f;

        /// <summary>
        /// ガードのゲージの最大(持っている盾のうち大きいほう。盾がなければ0=ガードできない)。
        /// 盾が2枚でもゲージは1本を共有する(作者に未確認の仮)。最大はこの値で、削れ方・通す傷・構え歩き・固まる長さは構えている盾の値
        /// (DuelPlayer.guardShield。両手で構えたら大きい盾)。
        /// </summary>
        public float GuardMax
        {
            get
            {
                float m = 0f;
                for (int h = 0; h < 2; h++)
                    if (Hand(h) is { kind: DuelKind.Shield } s) m = Mathf.Max(m, s.guardMax);
                return m;
            }
        }

        /// <summary>盾を持つ手のボタンを押しているか。押していれば、その盾と手(0=右 / 1=左)を返す(両手なら大きい盾)。</summary>
        public bool GuardHeld(out DuelWeapon shield, out int hand)
        {
            shield = null;
            hand = 0;
            for (int h = 0; h < 2; h++)
            {
                var w = Hand(h);
                if (w == null || w.kind != DuelKind.Shield || !Held(h == 0 ? DuelSlot.RightHand : DuelSlot.LeftHand)) continue;
                if (shield != null && w.guardMax <= shield.guardMax) continue;
                shield = w;
                hand = h;
            }
            return shield != null;
        }

        // ───────── ボタン(R1=右手 / L1=左手 / R2=右肩 / L2=左肩) ─────────

        static int Btn(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => 0,
            DuelSlot.LeftHand => 1,
            DuelSlot.RightShoulder => 2,
            DuelSlot.LeftShoulder => 3,
            _ => -1,
        };

        /// <summary>
        /// 付け先を選ぶ表示・装備の確認で所持品を付けるときに、その所に決めるボタン(0=R1 1=L1 2=R2 3=L2 4=✗。手・肩はその所のボタン、背中は ✗)。
        /// 番号は ProtoInput.MenuSlotPressed と同じ。
        /// </summary>
        internal static int ChoiceBtn(DuelSlot s) => s == DuelSlot.Back ? FlyBtn : Btn(s);

        static bool RawHeld(int b) => b switch
        {
            0 => ProtoInput.RightHandHeld,
            1 => ProtoInput.LeftHandHeld,
            2 => ProtoInput.RightShoulderHeld,
            3 => ProtoInput.LeftShoulderHeld,
            _ => ProtoInput.FlyHeld,
        };

        bool RawPressed(int b) => b switch
        {
            0 => ProtoInput.Attack,
            1 => ProtoInput.LeftHandPressed,
            2 => ProtoInput.RightShoulderPressed,
            3 => ProtoInput.LeftShoulderPressed,
            _ => flyPressed,
        };

        /// <summary>
        /// ✗ を飛行に使わない(選ぶ表示(付け先・分解)の間と、選ぶ表示を終えた時に押していた ✗ を離すまで)。DuelPlayer の飛行が見る。
        /// 空中では選ぶ表示を開かない(Nearby が null)。
        /// </summary>
        public bool FlyBlocked => ChoiceOpen || suppress[FlyBtn];

        /// <summary>その所のボタンを押している(選ぶ表示(付け先・分解)の間と、終えた時に押していたボタンを離すまでは数えない)。攻撃・射撃・ガードはこれを読む。</summary>
        public bool Held(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && !ChoiceOpen && !suppress[b] && RawHeld(b);
        }

        /// <summary>その所のボタンをこのフレームに押した(選ぶ表示(付け先・分解)の間と、終えた時に押していたボタンを離すまでは数えない)。</summary>
        public bool Pressed(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && !ChoiceOpen && !suppress[b] && RawPressed(b);
        }

        // ───────── 画面に出す情報 ─────────

        /// <summary>その所の銃の弾倉の残り(弾倉のない銃・銃でなければ -1)。</summary>
        public int MagLeft(DuelSlot s) => Get(s) is { IsGun: true, magazine: > 0 } ? mag[(int)s] : -1;
        /// <summary>その所の銃のリロードの残り(秒。0ならリロードしていない)。</summary>
        public float ReloadLeft(DuelSlot s) => Mathf.Max(0f, reload[(int)s]);
        /// <summary>その所の銃のリロードの残り(0〜1。1でリロードを始めたところ、0でリロードしていない)。</summary>
        public float Reload01(DuelSlot s) => Get(s) is { IsGun: true, magazine: > 0 } w && reload[(int)s] > 0f
            ? Mathf.Clamp01(reload[(int)s] / Mathf.Max(0.01f, w.reload)) : 0f;
        /// <summary>その所の砲・ミサイルが次に撃てるまでの残り(0〜1。0で撃てる)。</summary>
        public float Cooldown01(DuelSlot s) => cd[(int)s] > 0f ? Mathf.Clamp01(cd[(int)s] / Mathf.Max(0.01f, cdMax[(int)s])) : 0f;
        /// <summary>その所の砲・ミサイルが次に撃てるまでの残り(秒)。</summary>
        public float CooldownLeft(DuelSlot s) => cdShown[(int)s] ? Mathf.Max(0f, cd[(int)s]) : 0f;   // 付けた直後の待ちは出さない
        /// <summary>L1 の撃ち方が次に撃てるまでの残り(秒)。</summary>
        public float AltCooldownLeft => AltFire != null && cdShown[AltIndex] ? Mathf.Max(0f, cd[AltIndex]) : 0f;
        /// <summary>手動でリロードできる銃(弾倉のある銃)をどこかに付けている(メニューの操作方法のページに「△+撃つボタン リロード」を出すか)。</summary>
        public bool AnyReloadable
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (slots[i] is { IsGun: true, magazine: > 0 }) return true;
                return false;
            }
        }
        /// <summary>その所で、最近(秒)撃ったか(手の銃の構えの見た目用)。</summary>
        public bool ShotRecently(DuelSlot s, float sec) => Time.time - lastFire[(int)s] < sec;

        /// <summary>
        /// その所の武装が、共通の弾が足りなくて撃てない(弾倉の銃:弾倉が空でリロードもしておらず、1発も詰められない /
        /// 撃つたびに使う砲・ミサイル:1回分に足りない)。右下の武装の欄に「弾切れ」を出す。
        /// </summary>
        public bool Starved(DuelSlot s)
        {
            int i = (int)s;
            var w = slots[i];
            if (w == null || !w.IsShooter || !w.UsesAmmo) return false;
            if (w.IsGun && w.magazine > 0) return mag[i] <= 0 && reload[i] <= 0f && Loadable(w, w.magazine) <= 0;
            return Ammo < w.ammoCost - 0.0001f;
        }

        /// <summary>両手の銃・砲の L1 の撃ち方が、共通の弾が足りなくて撃てない。</summary>
        public bool AltStarved => AltFire is { } a && a.PaysPerShot && Ammo < a.ammoCost - 0.0001f;

        /// <summary>その所のエネルギー武器が、ENが0以下で撃てない(右下の武装の欄に「EN切れ」を出す)。</summary>
        public bool EnergyStarved(DuelSlot s) => slots[(int)s] is { IsShooter: true, UsesEnergy: true } && player != null && player.Overheated;
        /// <summary>両手の銃・砲の L1 の撃ち方がエネルギー武器で、ENが0以下で撃てない(溜めている途中は撃てるので数えない)。</summary>
        public bool AltEnergyStarved => AltFire is { UsesEnergy: true } && charge <= 0f && player != null && player.Overheated;
        /// <summary>撃ち方 w がENを使うのに、ENが0以下で撃てない。</summary>
        bool NoEnergy(DuelWeapon w) => w.UsesEnergy && player.Overheated;

        /// <summary>弾倉へ詰められる数(必要な数 need と、共通の弾で足りる数の少ないほう。弾を使わない銃は need)。</summary>
        int Loadable(DuelWeapon w, int need) =>
            need <= 0 ? 0 : w.ammoCost <= 0f ? need : Mathf.Min(need, Mathf.FloorToInt(Ammo / w.ammoCost + 0.0001f));

        /// <summary>
        /// リロードを始める(弾倉のある銃で、満タンでもリロード中でもなく、共通の弾で1発以上詰められるとき)。
        /// 詰める分(足りなければ詰められる分だけ)を、始めたときに共通の弾から取り置く(終わったら弾倉へ。外す・捨てるなどでやめたら返す。ResetState)。
        /// 始めたら true。quiet なら、共通の弾が1発分もなくても知らせない(再開のときのリロード。RestoreKeptAmmo)。
        /// </summary>
        bool StartReload(int i, DuelWeapon w, bool quiet = false)
        {
            if (w == null || w.magazine <= 0 || reload[i] > 0f || mag[i] >= w.magazine) return false;
            int n = Loadable(w, w.magazine - mag[i]);
            if (n <= 0)
            {
                if (!quiet) OutOfAmmo();
                return false;
            }
            resN[i] = n;
            resAmmo[i] = w.ammoCost > 0f ? n * w.ammoCost : 0f;
            Ammo = Mathf.Max(0f, Ammo - resAmmo[i]);
            reload[i] = w.reload;
            return true;
        }

        /// <summary>
        /// リロードを終える:始めたときに取り置いた分を弾倉へ詰める(2つの銃が同時にリロードしても、先に終わった方が弾を使い切らないように、
        /// 共通の弾は始めたときに引いてある)。
        /// </summary>
        void EndReload(int i, DuelWeapon w)
        {
            if (w != null) mag[i] = Mathf.Min(w.magazine, mag[i] + resN[i]);
            resN[i] = 0;
            resAmmo[i] = 0f;
            reload[i] = 0f;
        }

        /// <summary>取り置いた弾を返す(上限まで)。</summary>
        void Refund(float amount)
        {
            if (amount > 0f) Ammo = Mathf.Min(Rules.ammoMax, Ammo + amount);
        }

        /// <summary>L1 の溜めをやめる(撃たずにやめたなら、取り置いた1回分の弾を返す)。</summary>
        void StopCharge()
        {
            Refund(chargeAmmo);
            chargeAmmo = 0f;
            charge = 0f;
        }

        /// <summary>共通の弾が足りなかった:右下の弾の表示を光らせ、知らせを出す(続けて押しても2秒に1回)。</summary>
        void OutOfAmmo()
        {
            NoAmmoAt = Time.time;
            if (Time.unscaledTime - noAmmoLogAt < 2f) return;
            noAmmoLogAt = Time.unscaledTime;
            game.Log($"弾が足りない ― 落ちている武装を分解すると弾になる({ProtoInput.L(ProtoInput.Btn.Break)})");
        }

        /// <summary>ENが0以下でエネルギー武器を撃てなかった:左下のENの表示を光らせ、知らせを出す(続けて押しても2秒に1回)。</summary>
        void OutOfEnergy()
        {
            NoEnergyAt = Time.time;
            if (Time.unscaledTime - noEnergyLogAt < 2f) return;
            noEnergyLogAt = Time.unscaledTime;
            game.Log("ENが足りない ― ビーム・レーザーはENを使う。回復を待つ");
        }

        /// <summary>共通の弾を使う(足りていれば減らして true)。</summary>
        bool PayAmmo(DuelWeapon w)
        {
            if (!w.PaysPerShot) return true;
            if (Ammo < w.ammoCost - 0.0001f) return false;
            Ammo = Mathf.Max(0f, Ammo - w.ammoCost);
            return true;
        }

        // ───────── 毎フレーム(DuelPlayer.Update の始めから) ─────────

        public void Tick()
        {
            float dt = Time.deltaTime, now = Time.time;
            if (player != null)
            {
                // 弾が受け継ぐ速度は、今の速度へなめらかに追いつかせる(動き始め・止まり・回避で、十字と弾の流れが一瞬で跳ばないように)
                float st = Rules.inheritSmoothTime;
                inheritVel = st <= 0f ? player.Velocity : Vector3.Lerp(inheritVel, player.Velocity, 1f - Mathf.Exp(-3f * dt / st));
            }
            bool fh = ProtoInput.FlyHeld;
            flyPressed = fh && !flyPrev;
            flyPrev = fh;
            bool two = TwoHanded;
            float recoilDelay = Rules.recoilDelay;
            for (int i = 0; i < StateCount; i++)
            {
                cd[i] -= dt;
                var w = StateWeapon(i);
                // 反動の広がり:撃つのをやめて少したったら戻す(連射の間は戻らない)
                if (bloom[i] > 0f && now - lastFire[i] >= recoilDelay)
                    bloom[i] = w == null ? 0f : Mathf.MoveTowards(bloom[i], 0f, Mathf.Max(0f, w.recoilRecover) * dt);
                if (w == null || !w.IsGun || (i == L && two)) continue;   // 両手持ちの状態は右手の所で持つ
                if (reload[i] > 0f)
                {
                    reload[i] -= dt;
                    if (reload[i] <= 0f) EndReload(i, w);
                }
                // 待てば満タンに戻る自動リロードはない(作者「待てば自動リロードは消してください」)。撃ちかけの弾倉は △+撃つボタンで詰め直す
            }
            for (int b = 0; b < suppress.Length; b++)
                if (suppress[b] && !RawHeld(b)) suppress[b] = false;
            if (breakLeftHold && Breaking == null && !ProtoInput.BreakAmmoHeld) breakLeftHold = false;
            UpdateCursor();   // 画面の上の照準(撃つ向きと枠の中心。撃つ前に決める)

            if (player == null || player.Dead)
            {
                Nearby = null;
                StopCharge();
                if (ChoiceOpen) EndChoice();
                Frames.Clear();
                Marks.Clear();
                return;
            }
            // ボスを倒した後(武装が消し飛んだ後)は拾えない
            // 空中では拾う案内も付け先を選ぶ表示も出さない(作者「空中では拾うメニューが開かないようにしてください」)。
            // 選んでいる途中で空中に出たら、Choosing != Nearby で取り消しになる
            // 付け替えで置いた武装の、下げた案内の優先度を戻すか(拾える範囲から出たときだけ。空中にいる間も数える)
            DuelLoot.RestorePriorities(player);
            // 付け先を選ぶ表示・分解の選択の途中は、その武装を拾える間は対象を替えない
            // (敵が近くへ別の武装を落とした・別の武装へ近づいた、で表示が黙って閉じないように。作者の指示)。
            // 範囲を出る・壁の陰に入る・空中に出ると対象から外れ、今までどおり取り消しになる
            var keep = Choosing != null ? Choosing : Breaking;
            Nearby = game.stage.Cleared || player.Airborne ? null
                : keep != null && keep.CanPick(player) ? keep
                : DuelLoot.Nearest(player);
            UpdateChoice();
            TryManualReload();
            UpdateFrames();   // 照準の枠と当てる相手(撃つときもこの結果を使う。同じ探索をし直さない)

            bool can = player.CanShoot;
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (w == null || !w.IsShooter) continue;
                if (i == L && w.IsTwoHand) continue;   // 両手の銃・砲の L1 は別の撃ち方(TickAlt)
                var s = (DuelSlot)i;
                if (Pressed(s)) pressedAt[i] = now;
                if (!can) pressedAt[i] = -10f;   // 斬っている間・回避の間に押したのは覚えておかない
                // 両手の銃・砲は、L1 を溜めている間は主の撃ち方を撃たない
                bool want = can && !(w.IsTwoHand && charge > 0f) && (w.hold ? Held(s) : now - pressedAt[i] <= PressBuffer);
                if (!want)
                {
                    cd[i] = Mathf.Max(cd[i], 0f);   // 撃っていない間に端数を溜めない(押し始めにまとめて出ないように)
                    continue;
                }
                if (Fire(w, i, s)) pressedAt[i] = -10f;
            }
            TickAlt(can, now, dt);
        }

        /// <summary>
        /// 撃つ(爆発なら砲・ミサイル、それ以外は銃)。i は撃つ状態の番号、at は撃ち出す所。撃ったら true。
        /// charged は溜め撃ちを離したとき(ENは溜め始めに確かめてあるので、0以下でも撃ってENを使う)。
        /// </summary>
        bool Fire(DuelWeapon w, int i, DuelSlot at, bool charged = false) => w.Explosive ? FireHeavy(w, i, at, charged) : FireGun(w, i, at, charged);

        /// <summary>
        /// 両手の銃・砲の L1 の撃ち方。溜め撃ち(charge > 0)は、撃てる間に L1 を押している間溜め、溜まってから離すと撃つ
        /// (溜まる前に離す・撃てない状態になると溜めをやめる。撃った後の間隔の間は溜め始めない)。溜め撃ちでなければ主の撃ち方と同じ決まり。
        /// </summary>
        void TickAlt(bool can, float now, float dt)
        {
            var a = AltFire;
            const int i = AltIndex;
            if (a == null)
            {
                StopCharge();
                return;
            }
            if (a.charge > 0f)
            {
                if (!can || ChoiceOpen)   // 付け先・分解を選び始めたら溜めをやめる(Held が数えなくなり、離したことになって撃たないように)
                {
                    StopCharge();
                    return;
                }
                if (Held(DuelSlot.LeftHand))
                {
                    if (charge <= 0f && cd[i] > 0f) return;
                    // 共通の弾が1回分ないなら溜め始めない(溜めてから撃てないことに気づかないように)
                    if (charge <= 0f && a.PaysPerShot && Ammo < a.ammoCost - 0.0001f)
                    {
                        if (Pressed(DuelSlot.LeftHand)) OutOfAmmo();
                        return;
                    }
                    // ENを使う溜め撃ちは、ENが0以下なら溜め始めない(溜め始めたら、溜めている間に0以下になっても離せば撃てる)
                    if (charge <= 0f && NoEnergy(a))
                    {
                        if (Pressed(DuelSlot.LeftHand)) OutOfEnergy();
                        return;
                    }
                    // 実弾の溜め撃ちは、溜め始めに1回分を取り置く(溜めている間にほかの銃のリロードで使い切られないように。取り消したら返す)
                    if (charge <= 0f && a.PaysPerShot)
                    {
                        chargeAmmo = a.ammoCost;
                        Ammo = Mathf.Max(0f, Ammo - chargeAmmo);
                    }
                    float before = charge;
                    charge += dt;
                    // 溜まった合図:銃口に小さな火花(画面は揺らさない)
                    if (before < a.charge && charge >= a.charge) DuelSpark.Burst(Muzzle(DuelSlot.RightHand), player.transform.forward, new Color(1f, 0.9f, 0.6f), 10, 0.7f);
                    return;
                }
                if (charge >= a.charge && Fire(a, i, DuelSlot.RightHand, true)) chargeAmmo = 0f;   // 撃ったら取り置いた弾を使う
                StopCharge();   // 溜まる前に離した・撃てなかったら返す
                return;
            }
            StopCharge();
            if (Pressed(DuelSlot.LeftHand)) pressedAt[i] = now;
            if (!can) pressedAt[i] = -10f;
            bool want = can && (a.hold ? Held(DuelSlot.LeftHand) : now - pressedAt[i] <= PressBuffer);
            if (!want)
            {
                cd[i] = Mathf.Max(cd[i], 0f);
                return;
            }
            if (Fire(a, i, DuelSlot.RightHand)) pressedAt[i] = -10f;
        }

        static readonly DuelSlot[] FireSlots = { DuelSlot.RightHand, DuelSlot.LeftHand, DuelSlot.RightShoulder, DuelSlot.LeftShoulder };

        /// <summary>
        /// そのボタンの所で手動リロードできる銃の、撃つ状態の番号(できなければ -1)。弾倉のある銃(手・肩・両手の銃の主の撃ち方)だけ。
        /// 両手の銃は、L1 の撃ち方に弾倉がないので、R1 でも L1 でも主の銃(右手の所の状態)。
        /// </summary>
        int ReloadState(DuelSlot s)
        {
            int i = (s is DuelSlot.RightHand or DuelSlot.LeftHand) && TwoHanded ? R : (int)s;
            return slots[i] is { IsGun: true, magazine: > 0 } ? i : -1;
        }

        /// <summary>△+撃つボタンをリロードに使った時刻(DuelPlayer の強攻撃の同時押しの救済が、この△を拾わないように)。</summary>
        public float ReloadChordAt { get; private set; } = -10f;

        /// <summary>
        /// △を押しながら、弾倉のある銃を撃つボタンを押した:その銃のリロードを始める(弾倉が満タン・もうリロード中なら何もしない)。
        /// どちらの場合も、押したボタンは離すまで撃つ・溜める・振るのに使わない(この押下で弾が出たり、L1 の溜め撃ちが始まったりしないように)。
        /// 押して覚えている射撃(先行入力)も忘れる。押したのが剣・盾・砲・弾倉のない銃のボタンなら何もしない(△+R1 の強攻撃などは DuelPlayer がそのまま読む)。
        /// 撃てない間(振っている途中・回避中など)でも始められる。選ぶ表示(付け先・分解)の間は Pressed が数えないので始めない。
        /// </summary>
        void TryManualReload()
        {
            if (!ProtoInput.AimHeld) return;
            foreach (var s in FireSlots)
            {
                if (!Pressed(s)) continue;
                int i = ReloadState(s);
                if (i < 0) continue;
                suppress[Btn(s)] = true;
                pressedAt[i] = -10f;
                ReloadChordAt = Time.time;
                StartReload(i, slots[i]);   // 満タン・リロード中なら何もしない。共通の弾が1発分もなければ知らせる
            }
        }
    }
}
