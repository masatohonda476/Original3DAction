using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機の武装(ステージだけ。部屋のモードでは付かない)。付ける所は5つ:右手(R1)/ 左手(L1)/ 背中 / 右肩(R2)/ 左肩(L2)。どれも1つずつ。
    ///   手:剣・盾・手の銃のどれでも(左右どちらにも)。剣ならそのボタンで攻撃(技は剣ごとの表)、盾なら押している間ガード、銃なら撃つ。
    ///       最初は右手に素の剣、左手は空(盾を拾うまでガードできない)。
    ///   背中:スラスター。走り・ブースト・回避の距離が伸び、回避・ブースト・飛行のENが減る。
    ///   肩:銃・砲・ミサイルのどれでも(左右どちらにも)。R2 で右肩、L2 で左肩を撃つ。撃ち方(押している間 / 押すたび)は武装ごと。
    /// 近接攻撃の間(振りかぶり〜戻り)と回避の間は撃てない(DuelPlayer.CanShoot)。
    /// 狙い(画面の上の照準。作者「ロックオンにカメラ操作は完全に委ねられますか?」→「これでやってみてください」。仮の形):
    ///   照準(Cursor)は画面の上を動くカーソル。銃・砲(ミサイル以外)のボタンを押している間と撃ってから少しの間(構え。Aiming = CursorActive)は、
    ///   右スティック・マウスで動かす(カメラは回さない。DuelCamera)。可動範囲は画面の中央の楕円(DuelArmsRules.cursorRangeX / Y)。
    ///   構えていない間はゆっくり画面の中央へ戻る(表示が動くだけ)。照準が敵の上にある間は、右スティックでの動きを少し遅くする(cursorSlowOnEnemy)。
    ///   銃・砲は、カメラから照準を通る向きの先(最初に当たる地形・敵)へ撃つ。照準の枠は照準の位置を中心にし、枠に掛かっている射程の内・見通せる敵へ
    ///   狙いを少しだけ寄せる(吸い付き。最大で「枠の大きさ × assistPart」度)。弾が速さを持つ武装は既定で吸い付かない(projectileAssist。
    ///   照準を先に置いた偏差射撃を、吸い付きが敵の今の位置へ引き戻してしまうため)。照準が体に掛かる・寄せた狙いが掛かるときだけ枠を橙にして敵に印。
    ///   ミサイルは今までどおり枠(照準の位置が中心)の中の敵に割り振る(ロックオン中の敵は枠を lockFrameMul 倍に広くみなす)。
    /// 弾の速さ:実弾の銃・砲は見える弾が DuelWeapon.shotSpeed(m/s)で飛び、当たった時に火花・ヒットマーカーを出す。横に動く・避ける敵には
    ///   照準を先に置かないと当たらない(偏差の自動補正はしない)。shotSpeed が 0 の撃ち方(照射し続けるレーザー)は今までどおりの光線。
    /// 反動:撃つたびに照準そのものが上(と少し左右ランダム)へずれる(DuelWeapon.aimKick / aimKickSide。全体の倍率 kickMul)。押さえ込まないと上がっていく。
    ///   ばらつきの広がり(DuelWeapon.recoilPerShot / recoilMax。枠と十字が開く)も今までどおり。カメラは一切動かさない。
    ///   自機は構えの間、照準の先を向く(DuelPlayer)。近接・回避で構えは終わる。
    ///   手応え:当たったら照準の位置にヒットマーカー(HitMarkAt / HitMarkKind。崩せる・崩れた・倒したで色を変える)。
    /// 共通の弾:実弾の銃・砲・ミサイルは1種類の弾(Ammo)を使う。弾倉のある銃はリロードのときに詰めた分だけ、弾倉のない撃ち方は撃つたびに使う
    ///   (DuelWeapon.ammoCost。エネルギー武器は 0 で使わない)。弾は武装を分解して得る。
    /// EN:エネルギー武器(ビーム・レーザー。DuelWeapon.enCost)は弾の代わりに自機のENを使う(回避・攻撃と同じEN)。ENが0以下の間は撃てず、
    ///   少しでも残っていれば撃てて足りない分はマイナスになる。押している間撃ち続ける武装は1秒あたり、ほかは1発ごと。
    ///   溜め撃ちは溜め始めにENが残っていれば溜められ、離して撃つときに使う(溜めている間に0以下になっても撃てる)。
    /// 所持:付けずに持っておける(Stash)。付けている武装と所持品の重さの合計で、持てる上限と推力(ENを使う移動:ブースト移動・回避の距離・
    ///   飛ぶときの上昇の速さ)が決まる(Mobility)。歩き・走りには効かない(作者「歩きには効かないようにして、ENを使った移動に効くようにします」)。
    /// 拾う:落ちている武装の近くで、十字下(G)で所持品へ、十字上(F)で分解(十字上で弾・十字下で回復)、十字右(V)で付ける。付け先を選ぶ表示が出て、R1/L1(手)・R2/L2(肩)・✗(背中。選んでいる間は ✗ で飛ばない)で決める
    ///   (選んでいる間、R1/L1/R2/L2 は攻撃・射撃・ガードにならない。決めた・取り消した時に押していたボタンは、離すまで効かない)。
    ///   付け先を選ぶ・背中に付けるのは、自機が動ける(Free)かガード中だけ。攻撃・回避・怯み・ガードで受けた・崩れたら取り消し。もう一度十字右・離れるでも取り消し。
    ///   同じ所の今の武装は、拾った武装のあった所に置く(拾い直せる。素の剣も)。装備の確認の画面から、外して持つ・所持品を付ける・
    ///   足もとの床に捨てる(Drop)・分解する。
    /// 撃墜されても武装・所持品・弾は残る。ボスを倒すと爆発で全部消し飛ぶ(LoseAll。両手持ちも消え、右手は素の剣、左手は空に戻る。所持品も消え、弾は0)。自機の素の性能(HP・EN・怯み)は変えない。画面は揺らさない。
    /// 両手持ち(両手の近接・両手の銃・砲):右手と左手の両方の所に同じ武装を入れる。R1 が主の技、L1 が別の技(近接は別の振り、射撃は別の撃ち方)。
    ///   付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付き、それまでの右手・左手の武装は拾った所に置く(素の剣も)。
    ///   両手持ちのまま片手の武装を付けると、両手持ちは外れて置かれ、もう片方の手は空になる。捨てると両手が空く。盾がないのでガードはできない。
    ///   両手の銃・砲の撃つ状態は右手の所で持ち、L1 の撃ち方は別の状態(AltIndex)で持つ。溜め撃ちは L1 を押して溜め、溜まってから離すと撃つ。
    /// リロード:弾倉のある銃は、撃ち切ると自動でリロードする。撃ちかけでも △ を押しながらその銃を撃つボタンを押すとリロードする
    ///   (作者「△+射撃ボタンで手動リロードできるようにして、待てば自動リロードは消してください」。TryManualReload)。
    ///   リロード中はその銃だけ撃てない(ほかの武装は使える)。弾倉のない銃・砲・ミサイル・剣・盾では △+ボタンは今までどおり(剣なら強攻撃)。
    /// 毎フレームの処理は DuelPlayer.Update の始めに Tick() で呼ぶ(ボタンの横取りを、自機が読むより先に決めるため)。
    /// </summary>
    public class DuelArms : MonoBehaviour
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
        readonly int[] mag = new int[StateCount];
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
        /// ボタン(0=R1 1=L1 2=R2 3=L2 4=✗)を、離すまで使わない(付け先を決めたボタンが、そのまま攻撃・射撃・飛行にならないように)。
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
        /// <summary>分解(弾にするか回復にするか)を選んでいる武装(選んでいなければ null)。十字上で弾、十字下で回復、十字右でやめる。</summary>
        public DuelLoot Breaking { get; private set; }

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
        public DuelArmsRules Rules => game != null && game.stage != null && game.stage.armsRules != null ? game.stage.armsRules : FallbackRules;
        static readonly DuelArmsRules FallbackRules = new();
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
        bool Fits(float total) => total <= CarryMax + 0.001f;

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
        /// 構えの間、右スティック・マウスは画面の上の照準を動かし(カメラは回さない。DuelCamera)、自機は照準の先を向く(DuelPlayer)。
        /// ロックオン中の右スティックの弾きでの切り替え・自動で選ぶのは、構えの間はしない(DuelPlayer)。
        /// 近接・回避を出したら終わる(それより前に撃った分は数えない。ボタンを押したままなら、近接・回避が終わったらまた構える)。
        /// </summary>
        public bool Aiming
        {
            get
            {
                if (player == null || player.Dead || Choosing != null) return false;
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

        /// <summary>付け先を選ぶ表示で、その所に決めるボタン(手・肩はその所のボタン、背中は ✗)。</summary>
        static int ChoiceBtn(DuelSlot s) => s == DuelSlot.Back ? FlyBtn : Btn(s);

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
        /// ✗ を飛行に使わない(付け先を選んでいる間と、選ぶ表示を終えた時に押していた ✗ を離すまで)。DuelPlayer の飛行が見る。
        /// 空中では選ぶ表示を開かない(Nearby が null)。
        /// </summary>
        public bool FlyBlocked => Choosing != null || suppress[FlyBtn];

        /// <summary>その所のボタンを押している(付け先を選んでいる間・決めたボタンを離すまでは数えない)。</summary>
        public bool Held(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && Choosing == null && !suppress[b] && RawHeld(b);
        }

        /// <summary>その所のボタンをこのフレームに押した(付け先を選んでいる間・決めたボタンを離すまでは数えない)。</summary>
        public bool Pressed(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && Choosing == null && !suppress[b] && RawPressed(b);
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
        public float CooldownLeft(DuelSlot s) => lastFire[(int)s] < 0f ? 0f : Mathf.Max(0f, cd[(int)s]);   // まだ撃っていなければ 0(付けた直後の待ちは出さない)
        /// <summary>L1 の撃ち方が次に撃てるまでの残り(秒)。</summary>
        public float AltCooldownLeft => AltFire != null && lastFire[AltIndex] >= 0f ? Mathf.Max(0f, cd[AltIndex]) : 0f;
        /// <summary>手動でリロードできる銃(弾倉のある銃)をどこかに付けている(操作説明に「△+撃つボタン リロード」を出すか)。</summary>
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
        /// 始めたら true。
        /// </summary>
        bool StartReload(int i, DuelWeapon w)
        {
            if (w == null || w.magazine <= 0 || reload[i] > 0f || mag[i] >= w.magazine) return false;
            int n = Loadable(w, w.magazine - mag[i]);
            if (n <= 0)
            {
                OutOfAmmo();
                return false;
            }
            resN[i] = n;
            resAmmo[i] = w.ammoCost > 0f ? n * w.ammoCost : 0f;
            Ammo = Mathf.Max(0f, Ammo - resAmmo[i]);
            reload[i] = w.reload;
            return true;
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
                    if (reload[i] <= 0f)
                    {
                        // 始めたときに取り置いた分を詰める(2つの銃が同時にリロードしても、先に終わった方が弾を使い切らないように)
                        mag[i] = Mathf.Min(w.magazine, mag[i] + resN[i]);
                        resN[i] = 0;
                        resAmmo[i] = 0f;
                    }
                }
                // 待てば満タンに戻る自動リロードはない(作者「待てば自動リロードは消してください」)。撃ちかけの弾倉は △+撃つボタンで詰め直す
            }
            for (int b = 0; b < suppress.Length; b++)
                if (suppress[b] && !RawHeld(b)) suppress[b] = false;
            UpdateCursor();   // 画面の上の照準(撃つ向きと枠の中心。撃つ前に決める)

            if (player == null || player.Dead)
            {
                Nearby = null;
                Breaking = null;
                StopCharge();
                if (Choosing != null) EndChoice();
                Frames.Clear();
                Marks.Clear();
                return;
            }
            // ボスを倒した後(武装が消し飛んだ後)は拾えない
            // 空中では拾う案内も付け先を選ぶ表示も出さない(作者「空中では拾うメニューが開かないようにしてください」)。
            // 選んでいる途中で空中に出たら、Choosing != Nearby で取り消しになる
            Nearby = (game.stage != null && game.stage.Cleared) || player.Airborne ? null : DuelLoot.Nearest(player);
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
                if (!can || Choosing != null)   // 付け先を選び始めたら溜めをやめる(ボタンが選ぶ側に回り、離したことになって撃たないように)
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

        /// <summary>
        /// △を押しながら、弾倉のある銃を撃つボタンを押した:その銃のリロードを始める(弾倉が満タン・もうリロード中なら何もしない)。
        /// どちらの場合も、押したボタンは離すまで撃つ・溜める・振るのに使わない(この押下で弾が出たり、L1 の溜め撃ちが始まったりしないように)。
        /// 押して覚えている射撃(先行入力)も忘れる。押したのが剣・盾・砲・弾倉のない銃のボタンなら何もしない(△+R1 の強攻撃などは DuelPlayer がそのまま読む)。
        /// 撃てない間(振っている途中・回避中など)でも始められる。付け先を選んでいる間は Pressed が数えないので始めない。
        /// </summary>
        /// <summary>△+撃つボタンをリロードに使った時刻(DuelPlayer の強攻撃の同時押しの救済が、この△を拾わないように)。</summary>
        public float ReloadChordAt { get; private set; } = -10f;

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

        // ───────── 付ける・外す ─────────

        /// <summary>
        /// 十字右:付け先を選ぶ表示にする(手・肩・背中・両手持ちとも同じ流れ)。
        /// 選んでいる間は R1/L1(手)・R2/L2(肩)・✗(背中)で決める(背中は R1〜L2 では付かず、手・肩は ✗ では付かない)。
        /// ✗ は押し始めだけ数える(飛んでいて押し続けていた ✗ では付かない)。両手持ちは R1/L1 のどちらでも両手に付く(R2/L2 では付かない。Equip)。もう一度十字右・武装から離れる・動ける状態(Free・ガード)でなくなると取り消し。
        /// 選ぶ・付けるのは動ける状態のときだけ(振っている最中・回避の間に手の武装が替わって、当たりと見た目がずれないように)。
        /// </summary>
        /// 拾う案内(選んでいない間)では、十字右で付け先を選ぶ表示、十字下でそのまま所持品へ(重さの上限を超えるなら持てない)、
        /// 十字上で分解を選ぶ表示(十字上で弾・十字下で回復・十字右でやめる。離れる・動ける状態でなくなるでも取り消し)。
        /// 付け先を選ぶ表示で、付けると重さの上限を超える所のボタンを押したら、付けずに知らせる(表示はそのまま)。
        void UpdateChoice()
        {
            bool ready = player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;
            if (Breaking != null)
            {
                if (Breaking != Nearby || ProtoInput.EquipNow || !ready)
                {
                    Breaking = null;
                    return;
                }
                bool toAmmo = ProtoInput.BreakNow, toHeal = !toAmmo && ProtoInput.StashNow;
                if (!toAmmo && !toHeal) return;
                if (SalvageFull(toHeal)) return;   // 弾が上限・HP が満タン:武装だけ消えないよう、分解しない(選ぶ表示はそのまま)
                var loot = Breaking;
                Breaking = null;
                var w = loot.Weapon;
                loot.Take();
                Salvage(w, toHeal);
                return;
            }
            if (Choosing != null)
            {
                if (Choosing != Nearby || ProtoInput.EquipNow || !ready)
                {
                    EndChoice();
                    return;
                }
                foreach (var s in DuelWeapon.SlotsFor(Choosing.Weapon.kind))
                {
                    int b = ChoiceBtn(s);
                    if (b < 0 || !RawPressed(b)) continue;
                    var loot = Choosing;
                    float after = WeightIfEquipped(loot.Weapon, s);
                    if (!Fits(after))
                    {
                        TooHeavy(after, "付けられない");
                        return;
                    }
                    EndChoice();
                    Equip(loot, s);
                    return;
                }
                return;
            }
            if (!ready || Nearby == null) return;
            if (ProtoInput.EquipNow) Choosing = Nearby;
            else if (ProtoInput.StashNow) StashLoot(Nearby);
            else if (ProtoInput.BreakNow) Breaking = Nearby;
        }

        /// <summary>落ちている武装を所持品に入れる(重さの上限を超えるなら入れない)。弾倉の残りも覚える。</summary>
        void StashLoot(DuelLoot loot)
        {
            var w = loot.Weapon;
            float after = WeightIfStashed(w);
            if (!Fits(after))
            {
                TooHeavy(after, "持てない");
                return;
            }
            stash.Add(new Carried { w = w, memo = loot.Memo });
            loot.Take();
            game.Log($"{w.name}({w.RarityName})を持った(所持 {stash.Count}  重さ {TotalWeight:0}/{CarryMax:0})");
        }

        /// <summary>分解しても何も得ない(弾にする:弾が上限 / 回復にする:HP が満タンか撃墜中)。</summary>
        public bool SalvageUseless(bool heal) => heal ? player == null || player.Dead || player.HP >= player.MaxHP - 0.001f : Ammo >= Rules.ammoMax - 0.001f;

        /// <summary>分解しても何も得ないなら、知らせて true(分解しない)。</summary>
        bool SalvageFull(bool heal)
        {
            if (!SalvageUseless(heal)) return false;
            game.Log(heal ? "HP は満タン ― 回復にしても何も得ない" : $"弾は上限({Rules.ammoMax:0})― 弾にしても何も得ない");
            return true;
        }

        /// <summary>武装を分解して、共通の弾(上限まで)か HP の回復にする。量は DuelArmsRules(レア度と重さ)。</summary>
        void Salvage(DuelWeapon w, bool heal)
        {
            var r = Rules;
            if (heal)
            {
                float before = player.HP;
                player.Heal(r.BreakHeal(w));
                game.Log($"{w.name}を分解して回復 +{player.HP - before:0}(HP {player.HP:0}/{player.MaxHP:0})");
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(0.45f, 1f, 0.55f), 14, 0.9f);
            }
            else
            {
                float before = Ammo;
                Ammo = Mathf.Min(r.ammoMax, Ammo + r.BreakAmmo(w));
                game.Log($"{w.name}を分解して弾 +{Ammo - before:0}(弾 {Ammo:0}/{r.ammoMax:0})" + (Ammo >= r.ammoMax ? " 上限" : ""));
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(1f, 0.85f, 0.35f), 14, 0.9f);
            }
        }

        /// <summary>分解したときの量(弾・回復)。案内に出す。</summary>
        public int SalvageAmmo(DuelWeapon w) => Rules.BreakAmmo(w);
        public int SalvageHeal(DuelWeapon w) => Rules.BreakHeal(w);

        /// <summary>
        /// 付け先を選ぶ表示を終える(決めた・取り消した)。その時に押している R1/L1/R2/L2 は全部、離すまで攻撃・射撃・ガードにしない
        /// (決めたボタンと同時に押したボタンや、選んでいる間に押し続けていたボタンが、終わった瞬間に効かないように)。
        /// </summary>
        void EndChoice()
        {
            Choosing = null;
            for (int b = 0; b < suppress.Length; b++)
                if (RawHeld(b) || RawPressed(b)) suppress[b] = true;
        }

        /// <summary>付け先を選ぶ表示の途中なら取り消す(装備の確認を開いたとき)。L1 の溜めもやめる(閉じた後に、離したことになって撃たないように)。</summary>
        public void CancelChoice()
        {
            StopCharge();
            Breaking = null;
            if (Choosing != null) EndChoice();
        }

        /// <summary>先行入力(押してからの受付時間内の射撃)を捨てる。装備の確認を開いたとき(閉じた直後に撃たないように)。</summary>
        public void ClearBufferedInput()
        {
            for (int i = 0; i < StateCount; i++) pressedAt[i] = -10f;
        }

        /// <summary>捨てた武装を出す所(所ごとに少しずらす。Drop と HasFloorBelow で同じ)。</summary>
        Vector3 DropPos(int i)
        {
            float ang = (i - 2) * 50f;
            Vector3 off = Quaternion.Euler(0f, ang, 0f) * player.transform.forward * (player.Radius * 0.6f);
            return player.transform.position + off + Vector3.up * 2f;
        }

        /// <summary>
        /// その所の武装を捨てたときに置く所の真下に床があるか(穴の上・空中で捨てると、落ちる武装が -200m より下まで落ちて消えるので)。
        /// 下の階の床でもよい(そこまで落ちて拾い直せる)。
        /// </summary>
        public bool HasFloorBelow(DuelSlot slot)
        {
            if (player == null) return false;
            Vector3 p = DropPos((int)slot);
            return Physics.Raycast(p, Vector3.down, p.y + 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// その所の武装を外して、自機の足もとの床に置く(装備の確認の画面から。素の剣も捨てられ、どの所も空にできる)。
        /// 置き方は付け替えで外した武装と同じ DuelLoot.Place(勢いなしで真下の床まで落ちる。拾い直せる)。
        /// 機体の中心から半径の内側(所ごとに少しずらす)の、床から少し上に出す(壁の外・床の下に出ないように)。
        /// 撃墜中・ボスを倒した後(拾い直せない)・真下に床がない所(HasFloorBelow)では捨てない。捨てたら true。
        /// </summary>
        public bool Drop(DuelSlot slot)
        {
            int i = (int)slot;
            if (slots[i] == null || !CanDrop || !HasFloorBelow(slot)) return false;
            var w = Unequip(slot, out var memo, out bool two);   // memo:撃ちかけの弾倉の残り・装填待ち(拾い直しても満タン・待ちなしにしない)
            DuelLoot.Place(w, DropPos(i)).Memo = memo;
            game.Log(two ? $"両手の{w.name}を床に捨てた" : $"{DuelWeapon.SlotName(slot)}の{w.name}を床に捨てた");
            return true;
        }

        /// <summary>
        /// その所の武装を外す(両手持ちなら両手とも空ける)。外した武装・撃つ状態(弾倉の残り・装填待ち)・両手持ちだったかを返す(空なら null)。
        /// リロード中ならやめて、取り置いた弾を返す。盾ならガードを引き直し、剣ならその手の振り・押して覚えている攻撃をやめる。
        /// </summary>
        DuelWeapon Unequip(DuelSlot slot, out Memo memo, out bool two)
        {
            int i = (int)slot;
            var w = slots[i];
            memo = Memo.Fresh;
            two = false;
            if (w == null) return null;
            two = w.IsTwoHand && (i == R || i == L);
            memo = MemoOf(two ? R : i);
            if (two)
            {
                // 両手持ち:右手・左手の両方を空ける
                StopCharge();
                slots[R] = slots[L] = null;
                ResetState(R);
                ResetState(L);
                ResetState(AltIndex);
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
            }
            else
            {
                slots[i] = null;
                ResetState(i);
                Refresh(slot);
            }
            if (w.kind == DuelKind.Shield) player.OnShieldsChanged(false);
            if (two && w.IsBlade)
            {
                player.OnSwordDropped(0);
                player.OnSwordDropped(1);
            }
            else if (w.IsBlade) player.OnSwordDropped(slot == DuelSlot.RightHand ? 0 : 1);
            return w;
        }

        // ───────── 所持品(装備の確認の画面から) ─────────

        /// <summary>
        /// 装備の確認から付け外し・捨てる・分解ができるか:自機が動ける(Free)かガード中だけ(床から拾うときと同じ決まり)。
        /// 撃墜中・攻撃・回避・怯み・ガードで受けて固まっている・崩れている間はできない(止まった時間の中で外して、固まりを解くなどを防ぐ)。
        /// </summary>
        public bool CanRearrange => player != null && !player.Dead && player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;

        /// <summary>今は所持品・装備を床に捨てられるか(付け外しできる間で、ボスを倒した後でない)。</summary>
        bool CanDrop => CanRearrange && !(game.stage != null && game.stage.Cleared);

        /// <summary>付けている武装を外して所持品にする(重さの合計は増えない。付け外しできる間だけ)。外したら true。</summary>
        public bool StoreSlot(DuelSlot slot)
        {
            if (!CanRearrange) return false;
            var w = Unequip(slot, out var memo, out bool two);
            if (w == null) return false;
            stash.Add(new Carried { w = w, memo = memo });
            game.Log(two ? $"両手の{w.name}を外して持った" : $"{DuelWeapon.SlotName(slot)}の{w.name}を外して持った");
            return true;
        }

        /// <summary>付けている武装を分解して、弾か回復にする(付け外しできる間だけ。弾が上限・HP が満タンなら分解しない)。分解したら true。</summary>
        public bool SalvageSlot(DuelSlot slot, bool heal)
        {
            if (!CanRearrange || slots[(int)slot] == null || SalvageFull(heal)) return false;
            Salvage(Unequip(slot, out _, out _), heal);
            return true;
        }

        /// <summary>
        /// 所持品をその所に付ける。今その所の武装(両手持ちが絡むときは両手の分)は所持品に入る(1つ目は付けた所持品と同じ番号に入れ替え、2つ目は末尾)。
        /// 付け外しできない間・付けられない所・重さの上限を超える(所持品を軽く数えている場合)なら付けない。付けたら true。
        /// 撃つ状態(弾倉の残り・装填待ち)は外したときのまま戻す。盾のゲージは満タンにせず、今の値を新しい最大で切るだけ(外して付け直して満タンにしないように)。
        /// </summary>
        public bool EquipFromStash(int index, DuelSlot slot)
        {
            if (!CanRearrange || index < 0 || index >= stash.Count) return false;
            var c = stash[index];
            if (System.Array.IndexOf(DuelWeapon.SlotsFor(c.w.kind), slot) < 0) return false;
            float keep = 1f - Rules.stockWeightRate;   // 所持品から付けると、所持品として軽く数えていた分だけ重くなる
            float after = TotalWeight + (c.w.weight - RemovedWeight(c.w, slot)) * keep;
            if (!Fits(after))
            {
                TooHeavy(after, "付けられない");
                return false;
            }
            stash.RemoveAt(index);
            var removed = Mount(c.w, c.memo, slot, false);
            for (int k = 0; k < removed.Count; k++)
            {
                var nc = new Carried { w = removed[k].w, memo = removed[k].memo };
                if (k == 0) stash.Insert(index, nc);   // 入れ替え(所持品の並びが変わらないように)
                else stash.Add(nc);
            }
            game.Log((c.w.IsTwoHand ? $"両手に{c.w.name}を付けた" : $"{DuelWeapon.SlotName(slot)}に{c.w.name}を付けた")
                     + (removed.Count > 0 ? "(外した武装は所持品へ)" : ""));
            return true;
        }

        /// <summary>所持品を足もとの床に捨てる(真下に床がない・撃墜中・ボスの後は捨てない)。捨てたら true。</summary>
        public bool DropStash(int index)
        {
            if (index < 0 || index >= stash.Count || !CanDrop || !HasFloorBelow(DuelSlot.Back)) return false;
            var c = stash[index];
            stash.RemoveAt(index);
            DuelLoot.Place(c.w, DropPos((int)DuelSlot.Back)).Memo = c.memo;
            game.Log($"所持品の{c.w.name}を床に捨てた");
            return true;
        }

        /// <summary>所持品を分解して、弾か回復にする(付け外しできる間だけ。弾が上限・HP が満タンなら分解しない)。分解したら true。</summary>
        public bool SalvageStash(int index, bool heal)
        {
            if (!CanRearrange || index < 0 || index >= stash.Count || SalvageFull(heal)) return false;
            var c = stash[index];
            stash.RemoveAt(index);
            Salvage(c.w, heal);
            return true;
        }

        /// <summary>撃つ状態を空に戻す(弾倉・リロード・間隔・押したことの記憶)。リロード中なら取り置いた弾を返す。</summary>
        void ResetState(int i)
        {
            Refund(resAmmo[i]);
            resAmmo[i] = 0f;
            resN[i] = 0;
            mag[i] = 0;
            bloom[i] = 0f;
            reload[i] = cd[i] = cdMax[i] = 0f;
            pressedAt[i] = -10f;
        }

        /// <summary>その状態の弾倉の残り(弾倉のある銃でなければ -1=満タン扱い)。</summary>
        int MagOf(int i) => slots[i] is { IsGun: true, magazine: > 0 } ? Mathf.Clamp(mag[i], 0, slots[i].magazine) : -1;

        /// <summary>その状態が次に撃てる時刻(待ちがなければ 0)。</summary>
        float ReadyOf(int i) => cd[i] > 0f ? Time.time + cd[i] : 0f;

        /// <summary>外すときに覚えさせる撃つ状態(弾倉の残り・主の撃ち方と、右手の両手の銃・砲なら L1 の撃ち方の装填待ち)。</summary>
        Memo MemoOf(int i) => new()
        {
            mag = MagOf(i),
            ready = ReadyOf(i),
            altReady = i == R && TwoHandGun != null ? ReadyOf(AltIndex) : 0f,
        };

        /// <summary>
        /// 付けたばかりの撃つ状態(弾倉は left、-1 なら満タンに)。撃てるまで少なくとも 0.3 秒待つ。
        /// 外したときの装填待ち(ready。時刻)が残っていれば、その残りを待つ(外して付け直して待ちが縮まないように)。
        /// </summary>
        void ArmState(int i, DuelWeapon w, int left = -1, float ready = 0f)
        {
            ResetState(i);
            if (w == null) return;
            mag[i] = left >= 0 ? Mathf.Min(left, w.magazine) : w.magazine;
            cd[i] = cdMax[i] = w.IsShooter ? 0.3f : 0f;
            lastFire[i] = -10f;   // 付けた直後の 0.3 秒の待ちは、装填のゲージに出さない(CooldownLeft)
            float rest = ready - Time.time;
            if (w.IsShooter && rest > cd[i])
            {
                // 外したときの装填待ちの続き(ゲージにも出す)
                cd[i] = rest;
                cdMax[i] = Mathf.Max(rest, w.interval);
                lastFire[i] = Time.time;
            }
        }

        /// <summary>
        /// 落ちている武装をその所に付ける。今の武装は、拾った武装のあった所にそのまま置く(その場で入れ替え。拾い直せる)。
        /// 両手持ちを付けるときは(R1/L1 のどちらで決めても)両手に付け、右手・左手の今の武装を両方置く(素の剣も。同じ武装を2つ持っていれば2つとも)。
        /// 両手持ちのまま片手の武装を付けるときは、両手持ちを外して置き、選んだ手に付ける(もう片方の手は空になる)。
        /// </summary>
        void Equip(DuelLoot loot, DuelSlot slot)
        {
            var w = loot.Weapon;
            Vector3 at = loot.transform.position;
            loot.Take();
            bool wasTwo = TwoHanded;
            var removed = Mount(w, loot.Memo, slot, true);
            // 外した武装を置く:1つ目は拾った所、2つ目は自機の側へ少し寄せて(重ならないように)
            for (int k = 0; k < removed.Count; k++) DuelLoot.Place(removed[k].w, k == 0 ? at : Beside(at)).Memo = removed[k].memo;
            game.Log(w.IsTwoHand ? $"両手に{w.name}({w.RarityName})を付けた"
                     : wasTwo && w.IsHand ? $"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた(両手の{removed[0].w.name}は外した)"
                     : $"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた");
        }

        readonly List<(DuelWeapon w, Memo memo)> mountRemoved = new();

        /// <summary>
        /// w をその所に付け(撃つ状態は memo:弾倉の残り・装填待ち)、外れた武装と撃つ状態を返す(置き場所は呼ぶ側が決める:床か所持品)。
        /// refillShield:盾を付けたらガードのゲージを満タンにする(床から拾ったとき)。false なら今の値を最大で切るだけ(所持品から付けたとき)。
        /// 両手持ちを付けるときは(R1/L1 のどちらで決めても)両手に付け、右手・左手の今の武装を両方外す(同じ武装を2つ持っていれば2つとも)。
        /// 両手持ちのまま片手の武装を付けるときは、両手持ちを外し、選んだ手に付ける(もう片方の手は空になる)。
        /// 外れた手の剣は振り・押して覚えている攻撃をやめ、盾が絡めばガードを引き直す。返すリストは次に呼ぶまで使える。
        /// </summary>
        List<(DuelWeapon w, Memo memo)> Mount(DuelWeapon w, Memo memo, DuelSlot slot, bool refillShield)
        {
            mountRemoved.Clear();
            int i = (int)slot;
            bool hand = i == R || i == L;
            bool shieldBefore = Hand(0) is { kind: DuelKind.Shield } || Hand(1) is { kind: DuelKind.Shield };
            StopCharge();
            if (hand && (w.IsTwoHand || TwoHanded))
            {
                var oldR = slots[R];
                var oldL = slots[L];
                bool oldTwo = oldR is { IsTwoHand: true };
                Memo memR = MemoOf(R), memL = MemoOf(L);
                slots[R] = slots[L] = null;
                if (w.IsTwoHand) slots[R] = slots[L] = w;
                else slots[i] = w;
                if (oldR != null) mountRemoved.Add((oldR, memR));
                if (oldL != null && !oldTwo) mountRemoved.Add((oldL, memL));
                ArmState(R, slots[R], slots[R] == w ? memo.mag : -1, slots[R] == w ? memo.ready : 0f);
                ArmState(L, slots[L] != null && slots[L].IsTwoHand ? null : slots[L], slots[L] == w ? memo.mag : -1, slots[L] == w ? memo.ready : 0f);
                ArmState(AltIndex, AltFire, -1, w.IsTwoHand ? memo.altReady : 0f);
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
                if (oldR is { IsBlade: true }) player.OnSwordDropped(0);
                if (oldL is { IsBlade: true }) player.OnSwordDropped(1);
            }
            else
            {
                var old = slots[i];
                var oldMemo = MemoOf(i);
                slots[i] = w;
                if (old != null) mountRemoved.Add((old, oldMemo));
                ArmState(i, w, memo.mag, memo.ready);
                Refresh(slot);
                if (hand && old is { IsBlade: true }) player.OnSwordDropped(i == R ? 0 : 1);
            }
            // 盾を付けた・盾を持っていた(外れたかもしれない):ゲージと構えている盾を引き直す
            if (w.kind == DuelKind.Shield || shieldBefore) player.OnShieldsChanged(refillShield && w.kind == DuelKind.Shield);
            return mountRemoved;
        }

        /// <summary>at から自機の方へ少し寄せた所(2つ目の武装を置く。壁の外に出ないよう、拾えた=見通せる向きへ寄せる)。</summary>
        Vector3 Beside(Vector3 at)
        {
            Vector3 d = player.transform.position - at;
            d.y = 0f;
            float len = d.magnitude;
            return len < 0.5f ? at + Vector3.up * 0.5f : at + d / len * Mathf.Min(4f, len * 0.5f);
        }

        /// <summary>
        /// 全部の武装を失う(ボスの爆発)。付けていた武装は火花を散らして吹き飛び、全部の所を空にしてから右手に素の剣を入れ直す
        /// (左手に拾い直した素の剣も吹き飛ぶ)。失った数を返す(素の剣は数えない)。
        /// </summary>
        public int LoseAll()
        {
            int n = 0;
            var twoHand = TwoHanded ? slots[R] : null;   // 両手持ちは1つとして数える(左手の所は右手と同じ武装)
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (w == null) continue;
                bool baseSword = w == DuelWeapon.BaseSword;
                if (baseSword && i == (int)DuelSlot.RightHand) continue;   // 右手の素の剣はそのまま(下で入れ直す)
                if (i == L && w == twoHand)
                {
                    slots[i] = null;
                    continue;
                }
                if (!baseSword) n++;
                slots[i] = null;
                var blade = rig == null || !w.IsBlade ? null : i == (int)DuelSlot.RightHand ? rig.sword : rig.swordL;
                if (blade != null)
                {
                    // 手の剣:機体の剣は素に戻し(左手は隠し)、同じ形の刃を吹き飛ばす
                    var copy = CreateVisual(w, null);
                    copy.SetPositionAndRotation(blade.position, blade.rotation);
                    Fling(copy);
                }
                var v = visuals[i];
                visuals[i] = null;
                if (v != null && blade != null) Destroy(v.gameObject);   // 機体の剣の柄(吹き飛ばす刃の写しに柄も入っている)
                else if (v != null) Fling(v);
            }
            if (rig != null && rig.sword != null && n > 0)
            {
                DuelSpark.Burst(rig.sword.position, Vector3.up, new Color(1f, 0.8f, 0.4f), 24, 1.3f);
                ProtoFx.Blast(rig.sword.position, 3f, new Color(1f, 0.7f, 0.3f), 0.25f);
            }
            slots[(int)DuelSlot.RightHand] = DuelWeapon.BaseSword;
            for (int i = 0; i < SlotCount; i++) Refresh((DuelSlot)i);
            StopCharge();
            for (int i = 0; i < StateCount; i++) ResetState(i);
            if (Choosing != null) EndChoice();
            Breaking = null;
            player.OnShieldsChanged(false);
            // 所持品も消し飛び、共通の弾も0に戻る(弧の終わりに全部失う。仮置き)
            n += stash.Count;
            stash.Clear();
            Ammo = 0f;
            return n;
        }

        /// <summary>
        /// 外れた武装を、回しながら外へ吹き飛ばして少しして消す。当たり判定は付けず自前で動かす
        /// (カメラの壁よけ・足もとのレイ・敵の見通しに当たらないよう、機体と同じ Ignore Raycast の層にも置く)。
        /// </summary>
        void Fling(Transform v)
        {
            v.SetParent(null, true);
            foreach (var c in v.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            foreach (var tr in v.GetComponentsInChildren<Transform>()) tr.gameObject.layer = DuelFighter.FighterLayer;
            ProtoFx.Blast(v.position, 3f, new Color(1f, 0.6f, 0.2f), 0.25f);
            DuelSpark.Burst(v.position, Vector3.up, new Color(1f, 0.75f, 0.4f), 12, 1f);
            Vector3 out_ = v.position - player.Chest;
            out_.y = 0f;
            Vector3 vel = (out_.sqrMagnitude > 0.01f ? out_.normalized : Random.onUnitSphere) * 14f + Vector3.up * 16f;
            StartCoroutine(Debris(v, vel, Random.insideUnitSphere * 360f, 2.5f));
        }

        /// <summary>破片の動き:重力で落ち、床で弾んで勢いを失い、life 秒で消える(床は真下へのレイで、移動量を含めて調べる)。</summary>
        static IEnumerator Debris(Transform v, Vector3 vel, Vector3 spin, float life)
        {
            float t = 0f;
            while (t < life && v != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                vel.y -= DuelFighter.Gravity * dt;
                Vector3 p = v.position, next = p + vel * dt;
                if (vel.y < 0f && Physics.Raycast(new Vector3(next.x, p.y, next.z), Vector3.down, out var hit, p.y - next.y + 0.5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    next.y = hit.point.y + 0.5f;
                    vel = new Vector3(vel.x * 0.5f, -vel.y * 0.3f, vel.z * 0.5f);
                    spin *= 0.5f;
                }
                v.position = next;
                v.Rotate(spin * dt, Space.World);
                yield return null;
            }
            if (v != null) Destroy(v.gameObject);
        }

        /// <summary>その所の見た目を作り直す。</summary>
        void Refresh(DuelSlot s)
        {
            int i = (int)s;
            var w = slots[i];
            if (visuals[i] != null) Destroy(visuals[i].gameObject);
            visuals[i] = null;
            float k = game.mechHeight / 9.06f;

            if (s is DuelSlot.RightHand or DuelSlot.LeftHand)
            {
                bool right = s == DuelSlot.RightHand;
                var blade = rig == null ? null : right ? rig.sword : rig.swordL;
                if (!right && w is { IsTwoHand: true })
                {
                    // 両手持ちの左手:見た目は右手の側でまとめて出す(左手の剣は隠す)
                    if (blade != null) blade.gameObject.SetActive(false);
                    return;
                }
                bool sword = w is { IsBlade: true };
                if (blade != null)
                {
                    // 剣:機体の剣を出し、握りはそのままで長さを届く範囲に、太さを武装の太さに合わせ、レア以上はレア度の色に塗る
                    // (両手の近接は太く・長く、握りの後ろに柄を伸ばして両手で持つ感じを出す)
                    blade.gameObject.SetActive(sword);
                    if (sword)
                    {
                        Vector3 bs = right ? swordBaseScale : swordLBaseScale;
                        float th = Mathf.Max(0.1f, w.thickMul);
                        blade.localScale = new Vector3(bs.x * th, bs.y * w.reachMul, bs.z * th);
                        MaterialPropertyBlock block = null;
                        if (w.rarity >= 1)
                        {
                            block = new MaterialPropertyBlock();
                            block.SetColor("_BaseColor", w.Color);
                        }
                        foreach (var r in blade.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
                        if (w.haft > 0f) visuals[i] = Haft(blade, w.haft * k, 0.35f * k);
                        return;
                    }
                }
                if (w == null) return;
                // 盾・手の銃(剣の握り点がない機体の剣も):前腕に付ける。前腕の取付点は Z が手の向き、X が外側(右腕は +、左腕は -)
                var mount = rig == null ? null : right ? rig.mountRightArm : rig.mountLeftArm;
                float side = right ? 1f : -1f;
                if (mount != null)
                {
                    visuals[i] = CreateVisual(w, mount);
                    visuals[i].localPosition = w.kind == DuelKind.Shield ? new Vector3(0.75f * side, 0f, 0.2f) * k
                        : w.kind == DuelKind.TwoHandGun ? new Vector3(-0.3f, -0.45f, 1.8f) * k   // 両手の銃・砲:大きく、少し体の内側へ(両手で抱える感じ)
                        : new Vector3(0f, -0.45f, 1.2f) * k;
                }
                else
                {
                    visuals[i] = CreateVisual(w, transform);
                    visuals[i].localPosition = new Vector3(2.6f * side, game.mechHeight * 0.5f, 0.8f);
                }
                return;
            }

            if (w == null) return;
            Transform m = rig == null ? null : s switch
            {
                DuelSlot.Back => rig.mountBack,
                DuelSlot.RightShoulder => rig.mountShoulderR,
                _ => rig.mountShoulderL,
            };
            if (m != null) visuals[i] = CreateVisual(w, m);
            else
            {
                // 取付点がない機体:胸の高さに仮に付ける
                visuals[i] = CreateVisual(w, transform);
                float sx = s == DuelSlot.RightShoulder ? 2f : s == DuelSlot.LeftShoulder ? -2f : 0f;
                visuals[i].localPosition = new Vector3(sx, game.mechHeight * 0.85f, s == DuelSlot.Back ? -1.8f : 0f);
            }
        }

        /// <summary>
        /// 武装の仮の見た目(レア度の色)。剣は細長い刃(長さは届く範囲、太さは武装の太さに合わせ、柄があれば後ろに伸ばす)、盾は板、手の銃は細長い箱。
        /// parent が null なら親なし。
        /// </summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            var root = new GameObject(w.name).transform;
            if (parent != null) root.SetParent(parent, false);
            bool blade = w.IsBlade;
            var prim = GameObject.CreatePrimitive(blade ? PrimitiveType.Cube : w.shape);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            float th = Mathf.Max(0.1f, w.thickMul), len = 4.5f * w.reachMul;
            prim.transform.localScale = blade ? new Vector3(0.4f * th, 1f * th, len) : w.scale;
            prim.transform.localRotation = Quaternion.Euler(blade ? Vector3.zero : w.euler);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(w.Color);
            if (blade && w.haft > 0f)
            {
                var haft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(haft.GetComponent<Collider>());
                haft.transform.SetParent(root, false);
                haft.transform.localScale = new Vector3(0.35f, 0.35f, w.haft);
                haft.transform.localPosition = new Vector3(0f, 0f, -(len + w.haft) * 0.5f);
                haft.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(HaftColor);
            }
            return root;
        }

        static readonly Color HaftColor = new(0.25f, 0.25f, 0.28f);

        /// <summary>
        /// 機体の剣の握りの後ろ(剣の -Y)へ伸びる柄(両手の近接)。剣の拡大に引きずられないよう、世界の大きさから逆算して付ける。
        /// 機体と同じ層に置く(カメラの壁よけ・敵の見通しのレイに当たらないように)。
        /// </summary>
        static Transform Haft(Transform blade, float length, float thick)
        {
            var root = new GameObject("Haft").transform;
            root.SetParent(blade, false);
            var prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            Vector3 ls = blade.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y)), sz = Mathf.Max(0.0001f, Mathf.Abs(ls.z));
            prim.transform.localScale = new Vector3(thick / sx, length / sy, thick / sz);
            prim.transform.localPosition = new Vector3(0f, -length * 0.5f / sy, 0f);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(HaftColor);
            root.gameObject.layer = prim.layer = blade.gameObject.layer;
            return root;
        }

        // ───────── 狙い ─────────

        /// <summary>撃ち出す所(その所の武装の先。なければ胸・肩の高さの左右)。</summary>
        Vector3 Muzzle(DuelSlot s)
        {
            var v = visuals[(int)s];
            Vector3 f = player.transform.forward;
            if (v != null) return v.position + f * (slots[(int)s] is { kind: DuelKind.TwoHandGun } ? 3.5f : 1.5f);   // 両手の銃・砲は長いので先まで
            bool hand = s is DuelSlot.RightHand or DuelSlot.LeftHand;
            float side = s is DuelSlot.RightShoulder or DuelSlot.RightHand ? 2f : -2f;
            return player.transform.position + Vector3.up * game.mechHeight * (hand ? 0.55f : 0.85f) + player.transform.right * side + f * 1.5f;
        }

        // ───────── 画面の上の照準(カーソル) ─────────

        Vector2 cursor;
        Vector3 aimDir = Vector3.forward;
        bool aimDirValid;
        Vector2 cursorTan;
        bool cursorOnEnemy;

        /// <summary>照準の位置(画面の中央からのずれ。ビューポートの単位で、0.5 が画面の端。x は右、y は上が +)。</summary>
        public Vector2 Cursor => cursor;
        /// <summary>
        /// 照準を可動範囲の端へ押し付けている向きと強さ(長さ 0〜1。x は右、y は上が +)。押し付けていなければ 0。
        /// ロックオンしていないカメラだけが、これでゆっくり回る(DuelCamera)。反動で端へ押されたときは入れない(右スティック・マウスの入力だけ)。
        /// </summary>
        public Vector2 EdgePush { get; private set; }
        /// <summary>照準の先(カメラから照準を通る向きで最初に当たる地形・敵。なければ遠く)。自機は構えの間、ここを向く。</summary>
        public Vector3 CursorPoint { get; private set; }

        /// <summary>構えの間に自機が向く向き(照準の先へ。水平)。決まらなければ zero。</summary>
        public Vector3 AimFacing
        {
            get
            {
                if (player == null) return Vector3.zero;
                Vector3 d = CursorPoint - player.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1f) return d.normalized;
                d = aimDir;
                d.y = 0f;
                return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// 照準を動かす(毎フレーム Tick から。ヒットストップ中も応えるよう実時間)。
        /// 構えの間:右スティック(倒した量を cursorStickCurve 乗。照準が敵の上なら cursorSlowOnEnemy だけ遅く)とマウスで動かし、楕円の可動範囲に収める。
        ///   範囲の外へ押した分は EdgePush に(カメラがゆっくり回る。ロックオンしていないときだけ)。
        /// 構えていない間:ゆっくり画面の中央へ戻る(最後は速さを落として止まる)。
        /// 最後に、カメラから照準を通る向き・照準の先・照準が敵の上か・枠の中心(tan)を求める。
        /// </summary>
        void UpdateCursor()
        {
            var r = Rules;
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            float aspect = cam != null ? Mathf.Max(0.1f, cam.aspect) : 16f / 9f;
            float rx = Mathf.Clamp(r.cursorRangeX, 0.001f, 0.49f), ry = Mathf.Clamp(r.cursorRangeY, 0.001f, 0.49f);
            Vector2 push = Vector2.zero;
            if (player != null && !player.Dead && CursorActive)
            {
                // 動かす量は画面の高さを単位に(横も縦も、画面の上で同じ速さ)。横はビューポートの単位へ直すとき aspect で割る
                Vector2 stick = ProtoInput.RightStick, mouse = ProtoInput.MouseDelta;
                float sm = stick.magnitude, eff = 0f;
                Vector2 move = Vector2.zero;
                if (sm > 0.0001f)
                {
                    eff = Mathf.Pow(Mathf.InverseLerp(0.15f, 1f, Mathf.Min(1f, sm)), Mathf.Max(0.1f, r.cursorStickCurve));
                    float slow = cursorOnEnemy ? 1f - Mathf.Clamp01(r.cursorSlowOnEnemy) : 1f;
                    move += stick / sm * (eff * Mathf.Max(0f, r.cursorStickSpeed) * slow * dt);
                }
                move += mouse * Mathf.Max(0f, r.cursorMouseSens);
                Vector2 next = cursor + new Vector2(move.x / aspect, move.y);
                Vector2 e = new(next.x / rx, next.y / ry);
                if (e.sqrMagnitude > 1f)
                {
                    // 可動範囲の外へ押した:端に止め、外向きに押している強さを EdgePush に(スティックは倒した量、マウスは動かした速さ)
                    Vector2 n = e.normalized;
                    next = new Vector2(n.x * rx, n.y * ry);
                    float s = sm > 0.0001f ? Mathf.Clamp01(Vector2.Dot(stick / sm, n)) * eff : 0f;
                    if (dt > 0f) s = Mathf.Max(s, Mathf.Clamp01(Vector2.Dot(mouse, n) / dt / Mathf.Max(1f, r.cursorMouseEdgeFull)));
                    push = n * s;
                }
                cursor = next;
            }
            else
            {
                // 構えていない:画面の中央へゆっくり戻る(画面の上の表示が動くだけ。カメラは動かない)
                float len = cursor.magnitude;
                if (len > 0f) cursor = Vector2.MoveTowards(cursor, Vector2.zero, Mathf.Min(Mathf.Max(0f, r.cursorReturnSpeed), len * 4f) * dt + 0.00001f);
            }
            EdgePush = push;
            ClampCursor(rx, ry);

            aimDirValid = false;
            cursorOnEnemy = false;
            if (cam == null || game == null) return;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f + cursor.x, 0.5f + cursor.y, 0f));
            aimDir = ray.direction;
            aimDirValid = true;
            Vector3 local = cam.transform.InverseTransformDirection(aimDir);
            cursorTan = local.z > 0.01f ? new Vector2(local.x / local.z, local.y / local.z) : Vector2.zero;
            CursorPoint = AimPointAlong(cam.transform.position, aimDir, out var hitE);
            cursorOnEnemy = hitE != null;
        }

        /// <summary>照準を楕円の可動範囲に収める。</summary>
        void ClampCursor(float rx, float ry)
        {
            Vector2 e = new(cursor.x / rx, cursor.y / ry);
            if (e.sqrMagnitude > 1f)
            {
                e.Normalize();
                cursor = new Vector2(e.x * rx, e.y * ry);
            }
        }

        /// <summary>
        /// 反動で照準そのものをずらす(1回撃つごと):上へ aimKick 度、左右へ ±aimKickSide 度のランダム(kickMul を掛ける)。
        /// 角度は今の画角で画面の上の距離へ直す。可動範囲で止まる(カメラは動かさない。端へ押し付けてもカメラは回らない)。
        /// </summary>
        void Kick(DuelWeapon w)
        {
            var r = Rules;
            if (!r.cursorMovable) return;   // 照準を動かせないときは、反動で照準をずらさない(押さえ込めないため。ばらつきの広がりは残る)
            float mul = Mathf.Max(0f, r.kickMul);
            if (mul <= 0f || (w.aimKick <= 0f && w.aimKickSide <= 0f)) return;
            var cam = Camera.main;
            float tanV = Mathf.Tan((cam != null ? cam.fieldOfView : 55f) * 0.5f * Mathf.Deg2Rad);
            float aspect = cam != null ? Mathf.Max(0.1f, cam.aspect) : 16f / 9f;
            float up = Mathf.Tan(Mathf.Max(0f, w.aimKick) * mul * Mathf.Deg2Rad) / (2f * tanV);
            float side = Mathf.Tan(Random.Range(-1f, 1f) * Mathf.Max(0f, w.aimKickSide) * mul * Mathf.Deg2Rad) / (2f * tanV * aspect);
            cursor += new Vector2(side, up);
            ClampCursor(Mathf.Clamp(r.cursorRangeX, 0.001f, 0.49f), Mathf.Clamp(r.cursorRangeY, 0.001f, 0.49f));
        }

        /// <summary>カメラから照準を通る向き(このフレームに求めていなければカメラの前)。</summary>
        Vector3 AimDir(Camera cam) => aimDirValid ? aimDir : cam.transform.forward;

        /// <summary>撃ち方の弾の速さ(m/s。DuelArmsRules.shotSpeedMul を掛ける)。0 以下なら弾を飛ばさず、今までどおりすぐ届く光線。</summary>
        float SpeedOf(DuelWeapon w) => w.shotSpeed * Mathf.Max(0f, Rules.shotSpeedMul);

        // ───────── 照準の枠(射撃だけ。アーマード・コアの FCS のように、枠の中の敵に当てる) ─────────

        /// <summary>画面に出す照準の枠1つ(大きさが同じ武装はまとめる)。halfW / halfH は画面の中央からの tan、buttons は使うボタン(1=R1 2=L1 4=R2 8=L2)。</summary>
        public struct AimFrame
        {
            public float halfW, halfH;
            public int buttons;
            /// <summary>その武装を今撃っている・撃ったばかり・溜めている(枠を明るく)。</summary>
            public bool hot;
            /// <summary>枠の中に当てる相手がいる(枠の色を変える)。</summary>
            public bool locked;
            /// <summary>画面に描く大きさ(tan)。反動で広がっている分だけ halfW / halfH より大きい(吸い付く範囲は広げない。見た目だけ)。</summary>
            public float drawW, drawH;
            /// <summary>今のばらつき(spread + 反動の広がり)の tan。真ん中の十字の開きに出す。ミサイルだけの枠は -1(十字を出さない)。</summary>
            public float spread;
        }

        /// <summary>今の照準の枠(撃つ武装ごと、同じ大きさはまとめる)と、枠の中で当てる相手(印を出す)。毎フレーム Tick で作り直す。</summary>
        public readonly List<AimFrame> Frames = new();
        public readonly List<DuelEnemy> Marks = new();
        readonly List<DuelEnemy> missileBuf = new();

        /// <summary>両手の銃・砲の L1 の撃ち方なら、その両手の武装(枠の大きさを借りる)。</summary>
        DuelWeapon ParentOf(int i) => i == AltIndex ? TwoHandGun : null;

        /// <summary>枠の横の半分の大きさ(画面の中央からの角度の tan)。縦は frameAspect 倍。L1 の撃ち方に枠がなければ両手の武装の枠(DuelArmsRules.FrameDeg)。</summary>
        public float FrameTan(DuelWeapon w, DuelWeapon parent = null) => Mathf.Tan(Rules.FrameDeg(w, parent) * Mathf.Deg2Rad);

        /// <summary>
        /// 敵1体の、このフレームの見え方(1フレームに1回まとめて求め、枠の判定・撃つ相手選び・ミサイルで使い回す)。
        /// a / b は体(縦長のカプセル)の下と上の球の中心を画面に写した位置(tan)、ra / rb はその半径、off は胸が画面の中央からずれている角度(度)。
        /// sight は自機(LockPoint)から胸まで見通せるか(0 = まだ調べていない / 1 = 見通せる / -1 = さえぎられている。要るときだけ調べる)。
        /// </summary>
        struct EnemyView
        {
            public DuelEnemy e;
            public bool front;
            public float ax, ay, ra, bx, by, rb, off;
            public sbyte sight;
        }

        EnemyView[] views = new EnemyView[32];
        int viewCount, viewFrame = -1;
        Vector3 viewEye;

        /// <summary>このフレームの敵ごとの見え方を作る(もう作ってあれば何もしない)。カメラの後ろの敵は front が false。</summary>
        void PrepareViews(Camera cam)
        {
            if (viewFrame == Time.frameCount) return;
            viewFrame = Time.frameCount;
            var list = game.Enemies;
            if (views.Length < list.Count) views = new EnemyView[Mathf.NextPowerOfTwo(list.Count)];
            viewCount = list.Count;
            viewEye = player.LockPoint;
            var ct = cam.transform;
            float mh = game.mechHeight;
            for (int k = 0; k < viewCount; k++)
            {
                var e = list[k];
                ref var v = ref views[k];
                v = default;
                v.e = e;
                if (e == null || e.Dead) continue;
                Vector3 c = ct.InverseTransformPoint(e.Chest);
                if (c.z <= 0.5f) continue;
                float r = e.Radius, h = mh * e.Size;
                Vector3 a = ct.InverseTransformPoint(e.transform.position + Vector3.up * r);
                Vector3 b = ct.InverseTransformPoint(e.transform.position + Vector3.up * Mathf.Max(r, h - r));
                if (a.z <= 0.5f) a = c;
                if (b.z <= 0.5f) b = c;
                v.ax = a.x / a.z;
                v.ay = a.y / a.z;
                v.ra = r / a.z;
                v.bx = b.x / b.z;
                v.by = b.y / b.z;
                v.rb = r / b.z;
                v.off = Mathf.Atan(new Vector2(c.x / c.z, c.y / c.z).magnitude) * Mathf.Rad2Deg;
                v.front = true;
            }
        }

        /// <summary>
        /// k 番目の敵の体が、照準の位置を中心にした枠(横 ±hw・縦 ±hh。tan)に掛かっているか。近い敵ほど画面で大きいので掛かりやすい。カメラの後ろの敵は入らない。
        /// </summary>
        bool InFrame(int k, float hw, float hh)
        {
            ref var v = ref views[k];
            if (!v.front || v.e == null || v.e.Dead) return false;
            float cx = cursorTan.x, cy = cursorTan.y;
            if (Mathf.Max(v.ax + v.ra, v.bx + v.rb) - cx < -hw || Mathf.Min(v.ax - v.ra, v.bx - v.rb) - cx > hw) return false;
            return !(Mathf.Max(v.ay + v.ra, v.by + v.rb) - cy < -hh || Mathf.Min(v.ay - v.ra, v.by - v.rb) - cy > hh);
        }

        /// <summary>k 番目の敵を自機から見通せるか(このフレームで初めて聞かれたときだけ調べ、あとは覚えておく)。</summary>
        bool Visible(int k)
        {
            ref var v = ref views[k];
            if (v.sight == 0) v.sight = DuelSenses.ClearSight(viewEye, v.e.Chest) ? (sbyte)1 : (sbyte)-1;
            return v.sight > 0;
        }

        /// <summary>
        /// 吸い付く相手(銃・砲):照準の枠に掛かっている、射程の内で見通せる敵のうち、照準から体まで寄せる角度が一番小さい敵
        /// (同じくらいならロックオン中の敵。ロックオン中の敵も枠は広くみなさない)。いなければ null(照準の先へまっすぐ撃つ)。
        /// 枠に掛かっていても、寄せきれなければ外れる(AssistDir)。射程は撃ち出す所(from)から測る。
        /// 見通しは自機から調べる(敵ごとに1フレーム1回。武装ごとに撃ち出す所から調べ直さない)。
        /// </summary>
        DuelEnemy AimTarget(DuelWeapon w, DuelWeapon parent, Vector3 from)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            PrepareViews(cam);
            var rules = Rules;
            float hw = FrameTan(w, parent), hh = hw * rules.frameAspect;
            float range2 = w.range * w.range;
            Vector3 o = cam.transform.position, d = AimDir(cam);
            var t = player.Target;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            for (int k = 0; k < viewCount; k++)
            {
                var e = views[k].e;
                if (e == null || !InFrame(k, hw, hh)) continue;
                float d2 = (e.Chest - from).sqrMagnitude;
                if (d2 > range2) continue;
                float score = BodyNeedDeg(o, d, e, out _) - (e == t ? 0.5f : 0f) + Mathf.Sqrt(d2) * 0.002f;
                if (score >= bestScore || !Visible(k)) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        /// <summary>
        /// 吸い付きの強さ:照準の狙いを、枠の中の敵へ寄せられる最大の角度(度)。枠の大きさ × assistPart(小さい枠ほど弱い)。
        /// 弾が速さを持つ撃ち方(実弾の銃・砲)は、projectileAssist がオフ(既定)なら 0(照準を先に置いた偏差射撃を、敵の今の位置へ引き戻さないように)。
        /// </summary>
        float AssistDeg(DuelWeapon w, DuelWeapon parent)
        {
            var r = Rules;
            if (!r.projectileAssist && (w.Explosive || SpeedOf(w) > 0f)) return 0f;
            return r.FrameDeg(w, parent) * Mathf.Clamp01(r.assistPart);
        }

        /// <summary>
        /// 向き d(o から)を敵 e の体に掛けるのに要る角度(度。もう掛かっていれば 0)。q は体の芯(縦の線)の上で d に一番近い点。
        /// </summary>
        float BodyNeedDeg(Vector3 o, Vector3 d, DuelEnemy e, out Vector3 q)
        {
            float r = e.Radius, h = game.mechHeight * e.Size;
            Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
            SegSegDistance(a, b, o, o + d * AimFar, out float s);
            q = a + (b - a) * s;
            Vector3 to = q - o;
            float len = to.magnitude;
            if (len <= r) return 0f;
            float body = Mathf.Asin(Mathf.Clamp01(r / len)) * Mathf.Rad2Deg;
            return Mathf.Max(0f, Vector3.Angle(d, to) - body);
        }

        /// <summary>
        /// 照準の向き(カメラから照準を通る向き)を、敵 t の体へ最大 maxDeg 度だけ寄せた向き。reach は寄せた向きが体に掛かるか(当たるか)。
        /// </summary>
        Vector3 AssistDir(Camera cam, DuelEnemy t, float maxDeg, out bool reach)
        {
            Vector3 o = cam.transform.position, d = AimDir(cam);
            reach = false;
            if (t == null || t.Dead) return d;
            float need = BodyNeedDeg(o, d, t, out Vector3 q);
            if (need <= 0f) { reach = true; return d; }
            reach = need <= maxDeg;
            // 体の芯へ向けて回す。体に掛かるのに要る分(+少し)より多くは回さない(中央の狙いをなるべく残す)
            float turn = Mathf.Min(maxDeg, need + 0.3f);
            return Vector3.RotateTowards(d, (q - o).normalized, turn * Mathf.Deg2Rad, 0f);
        }

        /// <summary>
        /// 銃・砲の狙う所:照準の向きを、吸い付く相手 t へ少しだけ寄せた向きの先(最初に当たる地形・敵)。from から射程(range)までに切る。
        /// </summary>
        Vector3 AssistedAimPoint(DuelEnemy t, float maxDeg, Vector3 from, float range)
        {
            var cam = Camera.main;
            if (cam == null) return t != null ? t.Chest : FreeAimPoint(from, range);
            Vector3 dir = AssistDir(cam, t, maxDeg, out _);
            Vector3 p = AimPointAlong(cam.transform.position, dir, out _);
            Vector3 d = p - from;
            return d.magnitude > range ? from + d.normalized * range : p;
        }

        /// <summary>UpdateFrames が求めた、撃つ状態ごとの当てる相手(銃・砲)とミサイルの割り振り先。aimCacheFrame のフレームだけ使える。</summary>
        readonly DuelEnemy[] aimCache = new DuelEnemy[StateCount];
        readonly List<DuelEnemy>[] missileCache = new List<DuelEnemy>[StateCount];
        int aimCacheFrame = -1;

        /// <summary>撃つ状態 i の当てる相手(このフレームに UpdateFrames が求めていればそれ。このフレームに倒れていれば null)。</summary>
        DuelEnemy TargetFor(DuelWeapon w, int i, Vector3 from)
        {
            if (aimCacheFrame != Time.frameCount) return AimTarget(w, ParentOf(i), from);
            var c = aimCache[i];
            return c != null && !c.Dead ? c : null;
        }

        /// <summary>撃つ状態 i のミサイルの割り振り先(このフレームに UpdateFrames が求めていればそれ)。</summary>
        List<DuelEnemy> MissilesFor(DuelWeapon w, int i) =>
            aimCacheFrame == Time.frameCount && missileCache[i] != null ? missileCache[i] : MissileTargets(w, ParentOf(i), missileBuf);

        /// <summary>
        /// 画面に出す枠と印を作り直し、撃つ状態ごとの当てる相手をとっておく(撃つ武装ごと。同じ大きさの枠は1つにまとめ、ボタン名を並べる)。
        /// Tick で撃つ前に呼び、撃つとき(FireGun・FireHeavy)はこの結果を使う。付け先を選んでいる間(性能のグラフを出している間)は出さない。
        /// </summary>
        void UpdateFrames()
        {
            Frames.Clear();
            Marks.Clear();
            var cam = Camera.main;
            if (cam == null || Choosing != null) return;
            float aspect = Rules.frameAspect;
            for (int i = 0; i < StateCount; i++)
            {
                aimCache[i] = null;
                var w = StateWeapon(i);
                if (w == null || !w.IsShooter || (i == L && w.IsTwoHand)) continue;
                var parent = ParentOf(i);
                bool alt = i == AltIndex;
                var at = alt ? DuelSlot.RightHand : (DuelSlot)i;
                int btn = alt ? 1 : Btn(at);
                bool hot = Time.time - lastFire[i] < 0.6f || Held(alt ? DuelSlot.LeftHand : at) || (alt && charge > 0f);
                float hw = FrameTan(w, parent);
                // 反動の広がりを枠の見た目と真ん中の十字に出す(ミサイルは狙いの要らない武装なので出さない)
                float drawW = hw, spread = -1f;
                if (!w.IsMissile)
                {
                    float deg = Rules.FrameDeg(w, parent);
                    drawW = bloom[i] > 0f ? Mathf.Tan(Mathf.Min(80f, deg + bloom[i]) * Mathf.Deg2Rad) : hw;
                    spread = Mathf.Tan(Mathf.Min(80f, Mathf.Max(0f, w.spread) + bloom[i]) * Mathf.Deg2Rad);
                }
                bool any;
                if (w.IsMissile)
                {
                    var list = missileCache[i] ??= new List<DuelEnemy>();
                    MissileTargets(w, parent, list);
                    any = list.Count > 0;
                    for (int k = 0; k < list.Count; k++) AddMark(list[k]);
                }
                else
                {
                    var t = AimTarget(w, parent, Muzzle(at));
                    aimCache[i] = t;
                    // 枠を橙にして印を出すのは、吸い付いた狙いが体に掛かる(当たる)ときだけ
                    any = false;
                    if (t != null) AssistDir(cam, t, AssistDeg(w, parent), out any);
                    if (any) AddMark(t);
                }
                int same = -1;
                for (int k = 0; k < Frames.Count; k++)
                    if (Mathf.Abs(Frames[k].halfW - hw) < 0.002f) { same = k; break; }
                if (same >= 0)
                {
                    var f = Frames[same];
                    f.buttons |= 1 << btn;
                    f.hot |= hot;
                    f.locked |= any;
                    f.drawW = Mathf.Max(f.drawW, drawW);
                    f.drawH = f.drawW * aspect;
                    f.spread = Mathf.Max(f.spread, spread);
                    Frames[same] = f;
                }
                else Frames.Add(new AimFrame
                {
                    halfW = hw, halfH = hw * aspect, buttons = 1 << btn, hot = hot, locked = any,
                    drawW = drawW, drawH = drawW * aspect, spread = spread,
                });
            }
            aimCacheFrame = Time.frameCount;
        }

        void AddMark(DuelEnemy e)
        {
            if (!Marks.Contains(e)) Marks.Add(e);
        }

        /// <summary>狙う相手がいないとき:照準の先(最初に当たる地形・敵。なければ遠く)。</summary>
        Vector3 FreeAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return player.Chest + player.transform.forward * 100f;
            return AimPointAlong(cam.transform.position, AimDir(cam), out _);
        }

        const float AimFar = 250f;

        /// <summary>o から向き d の先で最初に当たる地形・敵(なければ AimFar 先)。enemy は当たった敵(地形・空なら null)。</summary>
        Vector3 AimPointAlong(Vector3 o, Vector3 d, out DuelEnemy enemy)
        {
            float best = AimFar;
            if (Physics.Raycast(o, d, out var hit, AimFar, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (RayHitsEnemy(o, d, best, out enemy, out float ed)) best = ed;
            return o + d * best;
        }

        /// <summary>照準の先の点を、from から射程(range)までに切ったもの(狙う敵がいないとき、射程より遠くへ飛ばさない)。</summary>
        Vector3 FreeAimPoint(Vector3 from, float range)
        {
            Vector3 p = FreeAimPoint();
            Vector3 d = p - from;
            return d.magnitude > range ? from + d.normalized * range : p;
        }

        /// <summary>from から dir へ len まで飛ばして、最初に当たる敵(機体を縦長のカプセルとみなす)。</summary>
        bool RayHitsEnemy(Vector3 from, Vector3 dir, float len, out DuelEnemy hitEnemy, out float dist)
        {
            hitEnemy = null;
            dist = len;
            Vector3 to = from + dir * len;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = e.Radius, h = game.mechHeight * e.Size;
                Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
                if (SegSegDistance(from, to, a, b, out float s) > r) continue;
                if (s * len >= dist) continue;
                dist = s * len;
                hitEnemy = e;
            }
            return hitEnemy != null;
        }

        /// <summary>線分どうしの最短距離。s は1本目の線分の上の、いちばん近い点の位置(0〜1)。</summary>
        static float SegSegDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float t;
            if (a <= 1e-6f && e <= 1e-6f) { s = 0f; return r.magnitude; }
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }

        /// <summary>向きを最大 deg 度の範囲でランダムにぶらす。</summary>
        static Vector3 Jitter(Vector3 dir, float deg)
        {
            if (deg <= 0f) return dir;
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            Vector3 tilted = Quaternion.AngleAxis(Random.Range(0f, deg), perp.normalized) * dir;
            return Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * tilted;
        }

        // ───────── 銃(手・肩) ─────────

        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);

        /// <summary>
        /// 銃を撃つ(撃てる間隔・弾倉の残り・共通の弾・ENがあれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。
        /// エネルギー武器はENが0以下なら撃たない(charged の溜め撃ちは除く)。1発ごとにEN(EnPerShot)を使い、足りない分はマイナスになる。
        /// </summary>
        bool FireGun(DuelWeapon w, int i, DuelSlot s, bool charged = false)
        {
            if (cd[i] > 0f || reload[i] > 0f) return false;
            if (w.magazine > 0 && mag[i] <= 0)
            {
                StartReload(i, w);   // 共通の弾が1発分もなければ知らせるだけ
                return false;
            }
            bool prepaid = charged && chargeAmmo > 0f;   // 溜め撃ち:溜め始めに1回分を取り置いてある
            if (!prepaid && w.PaysPerShot && Ammo < w.ammoCost - 0.0001f)
            {
                OutOfAmmo();
                return false;
            }
            if (!charged && NoEnergy(w))
            {
                OutOfEnergy();
                return false;
            }
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            // 照準の先へ撃つ(枠の中の敵がいれば、狙いを少しだけ寄せる。弾が速さを持つ撃ち方は既定で寄せない。AssistDeg)
            var t = TargetFor(w, i, from);
            Vector3 aim = AssistedAimPoint(t, AssistDeg(w, ParentOf(i)), from, w.range);
            Vector3 dir = (aim - from).normalized;
            Color c = w.magazine <= 0 ? BeamColor : BulletColor;
            int n = Mathf.Max(1, w.pellets);
            float speed = SpeedOf(w);   // 0 以下ならすぐ届く光線(照射し続けるレーザー)
            // 間隔の端数は持ち越す(1フレームより短い間隔なら、そのフレームに何発か出す。最大4回。押すたびに撃つ銃は1回)
            int bursts = w.hold ? 4 : 1;
            for (int k = 0; k < bursts && cd[i] <= 0f; k++)
            {
                if (k > 0 && NoEnergy(w)) break;   // 同じフレームの2発目からは、ENが0以下になったら止める
                if (!prepaid && !PayAmmo(w)) break;   // 弾倉のない実弾の撃ち方は撃つたびに共通の弾を使う(1回目は上で確かめてある)
                if (w.UsesEnergy) player.SpendEnergy(w.EnPerShot);
                cd[i] += Mathf.Max(0.01f, w.interval);
                // ばらつき = 武装の spread + 反動の広がり(撃つたびに広がる。カメラは動かさない)
                float jitter = w.spread + bloom[i];
                for (int p = 0; p < n; p++)
                {
                    if (speed > 0f) StartCoroutine(Fly(from, Jitter(dir, jitter), w, c, n > 1, speed, false));   // 見える弾が速さを持って飛ぶ
                    else Shot(from, Jitter(dir, jitter), w, c, n > 1);
                }
                AddRecoil(i, w);
                Kick(w);   // 照準そのものが上へずれる(カメラは動かさない)
                if (w.magazine > 0 && --mag[i] <= 0) { StartReload(i, w); break; }   // 撃ち切ったらリロード(共通の弾が足りれば)
            }
            cd[i] = Mathf.Max(cd[i], 0f);
            cdMax[i] = Mathf.Max(0.01f, w.interval);   // 次に撃てるまでのゲージの分母(両手の銃の L1 の弾。付けたときの 0.3 秒のままだとゲージが合わない)
            return true;
        }

        /// <summary>撃った後の反動:撃つ状態 i の広がりを1回分足す(上限まで。DuelArmsRules.recoilMul を掛ける)。</summary>
        void AddRecoil(int i, DuelWeapon w)
        {
            float mul = Mathf.Max(0f, Rules.recoilMul);
            if (w.recoilPerShot <= 0f || mul <= 0f) return;
            bloom[i] = Mathf.Min(Mathf.Max(0f, w.recoilMax) * mul, bloom[i] + w.recoilPerShot * mul);
        }

        /// <summary>
        /// 射撃(銃・砲)が敵 e に当たった:照準の真ん中のヒットマーカーを出す。同じ瞬間に何発も当たったら(散弾・貫く弾・爆発)強い方の印。
        /// TakeRangedHit の後に呼ぶ(倒した・崩れている・崩せるを見分ける)。
        /// </summary>
        void NoteHit(DuelEnemy e)
        {
            if (e == null) return;
            float cap = game.rangedStaggerCap;
            int kind = e.Dead ? 3 : e.Flinching ? 2 : cap < 1f && e.Stagger >= e.maxStagger * cap - 0.01f ? 1 : 0;
            float now = Time.unscaledTime;
            if (now - HitMarkAt < 0.05f) kind = Mathf.Max(kind, HitMarkKind);
            HitMarkAt = now;
            HitMarkKind = kind;
        }

        /// <summary>
        /// 弾の筋:from から to へ光る線を出し、life 秒で細くなって消える。外れた弾(miss)は少し太く・長く残して、どこへ飛んだか見えるようにする。
        /// </summary>
        void Tracer(Vector3 from, Vector3 to, Color c, float width, bool miss)
        {
            var r = Rules;
            float life = Mathf.Max(0.02f, miss ? r.missTracerLife : r.tracerLife);
            if (miss) width = Mathf.Max(width, r.missTracerWidth);
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.sharedMaterial = ProtoFx.Unlit(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startWidth = width * 0.5f;
            lr.endWidth = width;
            StartCoroutine(FadeTracer(lr, width, life));
            Destroy(go, life + 0.5f);   // 念のため(止まった時間の中などでコルーチンが進まなくても残らない)
        }

        /// <summary>弾の筋を life 秒で細くして消す(撃つ側の根元から先に細くなる)。</summary>
        static IEnumerator FadeTracer(LineRenderer lr, float width, float life)
        {
            float t = 0f;
            while (t < life && lr != null)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / life);
                lr.startWidth = width * 0.5f * k * k;
                lr.endWidth = width * k;
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
        }

        /// <summary>
        /// 銃の1発(弾の速さが 0 の撃ち方だけ。照射し続けるレーザーなど):すぐ届く光線。最初に当たった地形・敵で止まる(貫く弾は地形まで飛び、線の上の敵みんなに当たる)。
        /// 当たればヒットマーカー。外れた弾は筋を長めに残し、地形に当たったら小さな火花を出す。速さのある弾は Fly。
        /// </summary>
        void Shot(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            float len = w.range;
            bool wall = Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (wall) len = hit.distance;
            Vector3 end = from + dir * len;
            if (w.pierce)
            {
                PierceShot(from, dir, len, w, c, wall ? hit : (RaycastHit?)null);
                return;
            }
            bool hitEnemy = RayHitsEnemy(from, dir, len, out var e, out float d);
            if (hitEnemy)
            {
                end = from + dir * d;
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                DuelSpark.Burst(end, -dir, c, pellet ? 2 : 3, 0.6f);
                NoteHit(e);
            }
            else if (wall) DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
            Tracer(from, end, c, pellet ? 0.15f : 0.25f, !hitEnemy);
        }

        /// <summary>貫く弾:from から len まで、線から(敵の半径 + 太さ)の内にいる敵みんなに当てる。太いほど光線も太く長く残る。</summary>
        void PierceShot(Vector3 from, Vector3 dir, float len, DuelWeapon w, Color c, RaycastHit? wall)
        {
            Vector3 to = from + dir * len;
            bool any = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = e.Radius, h = game.mechHeight * e.Size;
                Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                DuelSpark.Burst(from + dir * (s * len), -dir, c, 3, 0.6f + w.beamWidth * 0.2f);
                NoteHit(e);
                any = true;
            }
            if (!any && wall is { } wh) DuelSpark.Burst(wh.point, wh.normal, c, 1, 0.45f);   // 外れて地形に当たった:小さな火花
            // 太い光線(溜め撃ち)は今までどおり長めに残す。細い光線は弾の筋と同じ(外れたら長めに)
            if (w.beamWidth >= 1f) ProtoFx.Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, 0.18f);
            else Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, !any);
            if (any && w.charge > 0f) game.HitStop(0.06f);   // 溜め撃ちが当たったら短く止める(画面は揺らさない)
        }

        // ───────── 砲・ミサイル(肩) ─────────

        static readonly Color ShellColor = new(0.8f, 1f, 0.35f), MissileColor = new(1f, 0.6f, 0.25f), BlastColor = new(1f, 0.6f, 0.25f);

        /// <summary>砲・ミサイルを撃つ(次に撃てるまでの時間がたっていれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。</summary>
        /// 今の一覧に ENを使う砲・ミサイルはないが、enCost を入れれば銃と同じ決まりで ENを使う。
        bool FireHeavy(DuelWeapon w, int i, DuelSlot s, bool charged = false)
        {
            if (cd[i] > 0f) return false;
            if (!charged && NoEnergy(w))
            {
                OutOfEnergy();
                return false;
            }
            if (!(charged && chargeAmmo > 0f) && !PayAmmo(w))   // 溜め撃ちは溜め始めに取り置いた分を使う
            {
                OutOfAmmo();
                return false;
            }
            if (w.UsesEnergy) player.SpendEnergy(w.EnPerShot);
            cd[i] = cdMax[i] = w.interval;
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            if (w.IsMissile)
            {
                // ミサイル:枠(照準の位置が中心)に掛かっている狙える敵に順番に割り振る(ロックオン中の敵が先、あとは近い順)。いなければ照準の先へ
                var targets = MissilesFor(w, i);
                Vector3 point = FreeAimPoint(from, w.range);
                for (int j = 0; j < w.salvo; j++)
                {
                    var t = targets.Count > 0 ? targets[j % targets.Count] : null;
                    Vector2 sc = Random.insideUnitCircle * w.radius;
                    StartCoroutine(Missile(from, t, point + new Vector3(sc.x, 0f, sc.y), w, j * 0.06f));
                }
                return true;
            }
            // 砲も銃と同じく照準の先へ(弾が速さを持つので、既定では吸い付かない)。砲弾は速さを持って飛び、地形・敵に当たるか射程の端で爆発する
            var target = TargetFor(w, i, from);
            Vector3 aim = AssistedAimPoint(target, AssistDeg(w, ParentOf(i)), from, w.range);
            Vector3 dir = Jitter((aim - from).normalized, bloom[i]);   // 反動の広がり(今の砲は広がり 0。.Recoil を入れれば広がる)
            AddRecoil(i, w);
            Kick(w);   // 照準そのものが上へずれる(カメラは動かさない)
            float speed = SpeedOf(w);
            StartCoroutine(Fly(from, dir, w, ShellColor, false, speed > 0f ? speed : FallbackShellSpeed, true));
            return true;
        }

        /// <summary>
        /// ミサイルの狙える敵(list に入れて返す。多くても1回に撃つ数 salvo まで):照準の枠に掛かっていて(ロックオン中の敵は lockFrameMul 倍に広くみなす)、
        /// 射程の内で見通せる敵。ロックオン中の敵が先、あとは近い順。先に枠と射程で絞って並べ、見通し(Linecast)は前から salvo 体そろうまでだけ調べる。
        /// </summary>
        List<DuelEnemy> MissileTargets(DuelWeapon w, DuelWeapon parent, List<DuelEnemy> list)
        {
            list.Clear();
            var cam = Camera.main;
            if (cam == null) return list;
            PrepareViews(cam);
            var rules = Rules;
            float range = w.range, hw = FrameTan(w, parent), hh = hw * rules.frameAspect, lm = Mathf.Max(1f, rules.lockFrameMul);
            Vector3 eye = viewEye;
            var t = player.Target;
            missileCand.Clear();
            for (int k = 0; k < viewCount; k++)
            {
                var e = views[k].e;
                if (e == null || e.Dead) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                float m = e == t ? lm : 1f;
                if (!InFrame(k, hw * m, hh * m)) continue;
                missileCand.Add((e == t ? -1f : (e.transform.position - eye).sqrMagnitude, k));   // ロックオン中の敵が先(-1)、あとは近い順
            }
            missileCand.Sort(ByNear);
            int cap = Mathf.Max(1, w.salvo);
            for (int c = 0; c < missileCand.Count && list.Count < cap; c++)
            {
                int k = missileCand[c].k;
                if (Visible(k)) list.Add(views[k].e);
            }
            return list;
        }

        readonly List<(float key, int k)> missileCand = new();
        static readonly System.Comparison<(float key, int k)> ByNear = (a, b) => a.key.CompareTo(b.key);

        /// <summary>弾の見た目:光る球と、尾を引く線(trail 秒の長さ。弾の筋)。</summary>
        static Transform Projectile(Vector3 at, Color c, float size, float trail = 0.18f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Shot";
            go.transform.position = at;
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = trail;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>砲の弾の速さが 0 のときの速さ(m/s。前の砲弾の速さ)。</summary>
        const float FallbackShellSpeed = 160f;

        /// <summary>
        /// 速さを持って飛ぶ弾(実弾の銃・砲。作者が選んだ形「弾に速さを持たせる」)。from から dir へ speed(m/s)でまっすぐ飛び、射程(range)まで。
        /// 毎フレーム、進む分の線で地形と敵を調べる(偏差の自動補正はしない。動く敵には照準を先に置く)。
        ///   銃の弾:最初に当たった敵に当てて火花とヒットマーカー、地形なら小さな火花で消える。貫く弾は地形・射程の端まで飛び、線の上の敵に1回ずつ当てる。
        ///   砲弾(explosive):敵・地形に当たるか射程の端で爆発する。
        /// 弾の筋は弾の尾(TrailRenderer)。止まったら球を隠して尾だけ消えていく。ヒットストップ中は止まる(Time.deltaTime)。
        /// </summary>
        IEnumerator Fly(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet, float speed, bool explosive)
        {
            var r = Rules;
            float size = explosive ? 1.2f + w.radius * 0.05f : (pellet ? r.pelletSize : r.bulletSize) + w.beamWidth * 0.5f;
            float trail = explosive ? 0.18f : Mathf.Max(0.01f, r.bulletTrail);
            var p = Projectile(from, explosive ? ShellColor : c, size, trail);
            Destroy(p.gameObject, w.range / Mathf.Max(1f, speed) + trail + 1f);   // 念のため(コルーチンが止まっても残らない)
            List<DuelEnemy> pierced = w.pierce && !explosive ? new List<DuelEnemy>() : null;
            Vector3 pos = from;
            float flown = 0f;
            while (true)
            {
                yield return null;
                if (p == null) yield break;
                float step = speed * Time.deltaTime;
                if (step <= 0f) continue;   // ヒットストップ・止まった時間の中
                bool last = flown + step >= w.range;
                if (last) step = Mathf.Max(0f, w.range - flown);
                bool wall = Physics.Raycast(pos, dir, out var hit, step, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float len = wall ? hit.distance : step;
                if (pierced != null)
                {
                    if (PierceSegment(pos, dir, len, w, c, pierced) && w.charge > 0f) game.HitStop(0.06f);   // 溜め撃ちが当たったら短く止める(画面は揺らさない)
                }
                else if (RayHitsEnemy(pos, dir, len, out var e, out float d))
                {
                    Vector3 at = pos + dir * d;
                    if (explosive) Explode(at, w, true);
                    else
                    {
                        e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                        DuelSpark.Burst(at, -dir, c, pellet ? 2 : 3, 0.6f);
                        NoteHit(e);
                    }
                    EndProjectile(p, at, trail);
                    yield break;
                }
                pos += dir * len;
                flown += len;
                p.position = pos;
                if (wall)
                {
                    if (explosive) Explode(hit.point, w, true);
                    else DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
                    EndProjectile(p, hit.point, trail);
                    yield break;
                }
                if (last)
                {
                    if (explosive) Explode(pos, w, true);   // 射程の端で爆発(前と同じ)
                    EndProjectile(p, pos, trail);
                    yield break;
                }
            }
        }

        /// <summary>弾が止まった:その所まで動かしてから球を隠し、尾が消えるのを待って消す(当たった所まで筋が届いて見えるように)。</summary>
        static void EndProjectile(Transform p, Vector3 at, float trail)
        {
            if (p == null) return;
            p.position = at;
            var rend = p.GetComponent<MeshRenderer>();
            if (rend != null) rend.enabled = false;
            var tr = p.GetComponent<TrailRenderer>();
            if (tr != null) tr.emitting = false;
            Destroy(p.gameObject, trail + 0.05f);
        }

        /// <summary>
        /// 貫く弾が from から len 進む間に、線から(敵の半径 + 太さ)の内にいる敵に当てる(1発で同じ敵には1回。pierced に覚える)。当たったら true。
        /// </summary>
        bool PierceSegment(Vector3 from, Vector3 dir, float len, DuelWeapon w, Color c, List<DuelEnemy> pierced)
        {
            if (len <= 0f) return false;
            Vector3 to = from + dir * len;
            bool any = false;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead || pierced.Contains(e)) continue;
                float r = e.Radius, h = game.mechHeight * e.Size;
                Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                pierced.Add(e);
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                DuelSpark.Burst(from + dir * (s * len), -dir, c, 3, 0.6f + w.beamWidth * 0.2f);
                NoteHit(e);
                any = true;
            }
            return any;
        }

        /// <summary>ミサイル1発:上へ打ち上げてから、狙った敵へ曲がって飛ぶ(敵が動いても追いかける)。着いたら小さく爆発する。</summary>
        IEnumerator Missile(Vector3 from, DuelEnemy target, Vector3 point, DuelWeapon w, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Vector3 lift = from + Vector3.up * 12f - player.transform.forward * 3f + Random.insideUnitSphere * 7f;
            Vector3 goal = point;
            var p = Projectile(from, MissileColor, 0.8f);
            float dur = Random.Range(0.55f, 0.75f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                if (target != null && !target.Dead) goal = target.Chest;
                float k = Mathf.Clamp01(t / dur);
                p.position = Vector3.Lerp(Vector3.Lerp(from, lift, k), Vector3.Lerp(lift, goal, k), k);
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(goal, w, false);
        }

        /// <summary>爆発:半径の中の敵に当てる(上下に離れすぎた敵には当たらない)。自機には当たらない。</summary>
        void Explode(Vector3 at, DuelWeapon w, bool shell)
        {
            ProtoFx.Blast(at, w.radius, shell ? ShellColor : BlastColor, shell ? 0.35f : 0.25f);
            DuelSpark.Burst(at, Vector3.zero, new Color(1f, 0.75f, 0.4f), shell ? 20 : 6, shell ? 1.2f : 0.7f);
            bool any = false;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead) continue;
                Vector3 d = e.transform.position - at;
                float dy = at.y - e.transform.position.y;
                d.y = 0f;
                if (d.magnitude - e.Radius > w.radius) continue;
                if (dy < -w.radius || dy > game.mechHeight * e.Size + w.radius) continue;
                e.TakeRangedHit(w.damage, w.stagger, (d.sqrMagnitude > 0.01f ? d.normalized : player.transform.forward) * w.knock, 0.6f);
                if (shell) NoteHit(e);   // 砲だけヒットマーカー(ミサイルは自動で飛ぶので出さない)
                any = true;
            }
            if (any && shell) game.HitStop(0.05f);   // 砲だけ。ミサイルは何発も当たるので止めない
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }
    }
}
