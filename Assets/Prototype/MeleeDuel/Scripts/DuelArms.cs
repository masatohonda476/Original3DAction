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
    /// 狙い:ロックオン中はその敵(射程の外・壁ごしなら、画面の中央に近い敵 → いなければ画面の中央の先)。
    /// 拾う:落ちている武装の近くで十字右(V)。付け先を選ぶ表示が出て、R1/L1(手)・R2/L2(肩)・✗(背中。選んでいる間は ✗ で飛ばない)で決める
    ///   (選んでいる間、R1/L1/R2/L2 は攻撃・射撃・ガードにならない。決めた・取り消した時に押していたボタンは、離すまで効かない)。
    ///   付け先を選ぶ・背中に付けるのは、自機が動ける(Free)かガード中だけ。攻撃・回避・怯み・ガードで受けた・崩れたら取り消し。もう一度十字右・離れるでも取り消し。
    ///   同じ所の今の武装は、拾った武装のあった所に置く(拾い直せる。素の剣も)。在庫はない。装備の確認の画面から、足もとの床に捨てられる(Drop)。
    /// 撃墜されても武装は残る。ボスを倒すと爆発で全部消し飛ぶ(LoseAll。両手持ちも消え、右手は素の剣、左手は空に戻る)。自機の素の性能(HP・EN・怯み)は変えない。画面は揺らさない。
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
        /// <summary>右手の銃(手の銃・両手の銃・砲)を最近撃ったか、L1 を溜めている(射撃の構えの見た目用)。</summary>
        public bool AimPose(float sec) => slots[R] is { kind: DuelKind.HandGun or DuelKind.TwoHandGun }
                                           && (ShotRecently(DuelSlot.RightHand, sec) || (TwoHandGun != null && (Time.time - lastFire[AltIndex] < sec || charge > 0f)));

        public DuelWeapon BackW => slots[(int)DuelSlot.Back];
        /// <summary>走りの倍率:スラスター × 持っている盾の重さ。</summary>
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
        public float DashMul => BackW != null ? BackW.dashMul : 1f;
        public float DodgeMul => BackW != null ? BackW.dodgeMul : 1f;
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
        public float CooldownLeft(DuelSlot s) => Mathf.Max(0f, cd[(int)s]);
        /// <summary>L1 の撃ち方が次に撃てるまでの残り(秒)。</summary>
        public float AltCooldownLeft => AltFire != null ? Mathf.Max(0f, cd[AltIndex]) : 0f;
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

        // ───────── 毎フレーム(DuelPlayer.Update の始めから) ─────────

        public void Tick()
        {
            float dt = Time.deltaTime, now = Time.time;
            bool fh = ProtoInput.FlyHeld;
            flyPressed = fh && !flyPrev;
            flyPrev = fh;
            bool two = TwoHanded;
            for (int i = 0; i < StateCount; i++)
            {
                cd[i] -= dt;
                var w = StateWeapon(i);
                if (w == null || !w.IsGun || (i == L && two)) continue;   // 両手持ちの状態は右手の所で持つ
                if (reload[i] > 0f)
                {
                    reload[i] -= dt;
                    if (reload[i] <= 0f) mag[i] = w.magazine;
                }
                // 待てば満タンに戻る自動リロードはない(作者「待てば自動リロードは消してください」)。撃ちかけの弾倉は △+撃つボタンで詰め直す
            }
            for (int b = 0; b < suppress.Length; b++)
                if (suppress[b] && !RawHeld(b)) suppress[b] = false;

            if (player == null || player.Dead)
            {
                Nearby = null;
                charge = 0f;
                if (Choosing != null) EndChoice();
                return;
            }
            // ボスを倒した後(武装が消し飛んだ後)は拾えない
            // 空中では拾う案内も付け先を選ぶ表示も出さない(作者「空中では拾うメニューが開かないようにしてください」)。
            // 選んでいる途中で空中に出たら、Choosing != Nearby で取り消しになる
            Nearby = (game.stage != null && game.stage.Cleared) || player.Airborne ? null : DuelLoot.Nearest(player);
            UpdateChoice();
            TryManualReload();

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

        /// <summary>撃つ(爆発なら砲・ミサイル、それ以外は銃)。i は撃つ状態の番号、at は撃ち出す所。撃ったら true。</summary>
        bool Fire(DuelWeapon w, int i, DuelSlot at) => w.Explosive ? FireHeavy(w, i, at) : FireGun(w, i, at);

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
                charge = 0f;
                return;
            }
            if (a.charge > 0f)
            {
                if (!can || Choosing != null)   // 付け先を選び始めたら溜めをやめる(ボタンが選ぶ側に回り、離したことになって撃たないように)
                {
                    charge = 0f;
                    return;
                }
                if (Held(DuelSlot.LeftHand))
                {
                    if (charge <= 0f && cd[i] > 0f) return;
                    float before = charge;
                    charge += dt;
                    // 溜まった合図:銃口に小さな火花(画面は揺らさない)
                    if (before < a.charge && charge >= a.charge) DuelSpark.Burst(Muzzle(DuelSlot.RightHand), player.transform.forward, new Color(1f, 0.9f, 0.6f), 10, 0.7f);
                    return;
                }
                if (charge >= a.charge) Fire(a, i, DuelSlot.RightHand);
                charge = 0f;
                return;
            }
            charge = 0f;
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
                var w = slots[i];
                if (reload[i] > 0f || mag[i] >= w.magazine) continue;
                reload[i] = w.reload;
            }
        }

        // ───────── 付ける・外す ─────────

        /// <summary>
        /// 十字右:付け先を選ぶ表示にする(手・肩・背中・両手持ちとも同じ流れ)。
        /// 選んでいる間は R1/L1(手)・R2/L2(肩)・✗(背中)で決める(背中は R1〜L2 では付かず、手・肩は ✗ では付かない)。
        /// ✗ は押し始めだけ数える(飛んでいて押し続けていた ✗ では付かない)。両手持ちは R1/L1 のどちらでも両手に付く(R2/L2 では付かない。Equip)。もう一度十字右・武装から離れる・動ける状態(Free・ガード)でなくなると取り消し。
        /// 選ぶ・付けるのは動ける状態のときだけ(振っている最中・回避の間に手の武装が替わって、当たりと見た目がずれないように)。
        /// </summary>
        void UpdateChoice()
        {
            bool ready = player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;
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
                    EndChoice();
                    Equip(loot, s);
                    return;
                }
                return;
            }
            if (!ready || Nearby == null || !ProtoInput.EquipNow) return;
            Choosing = Nearby;
        }

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
            charge = 0f;
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
            var w = slots[i];
            if (w == null || player == null || player.Dead || (game.stage != null && game.stage.Cleared) || !HasFloorBelow(slot)) return false;
            bool two = w.IsTwoHand && (i == R || i == L);
            if (two)
            {
                // 両手持ち:右手・左手の両方を空ける
                slots[R] = slots[L] = null;
                ResetState(R);
                ResetState(L);
                ResetState(AltIndex);
                charge = 0f;
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
            }
            else
            {
                slots[i] = null;
                ResetState(i);
                Refresh(slot);
            }
            DuelLoot.Place(w, DropPos(i));
            if (w.kind == DuelKind.Shield) player.OnShieldsChanged(false);
            if (two && w.IsBlade)
            {
                player.OnSwordDropped(0);
                player.OnSwordDropped(1);
            }
            else if (w.IsBlade) player.OnSwordDropped(slot == DuelSlot.RightHand ? 0 : 1);
            game.Log(two ? $"両手の{w.name}を床に捨てた" : $"{DuelWeapon.SlotName(slot)}の{w.name}を床に捨てた");
            return true;
        }

        /// <summary>撃つ状態を空に戻す(弾倉・リロード・間隔・押したことの記憶)。</summary>
        void ResetState(int i)
        {
            mag[i] = 0;
            reload[i] = cd[i] = cdMax[i] = 0f;
            pressedAt[i] = -10f;
        }

        /// <summary>付けたばかりの撃つ状態(弾倉を満タンに、撃てるまで少し待つ)。</summary>
        void ArmState(int i, DuelWeapon w)
        {
            ResetState(i);
            if (w == null) return;
            mag[i] = w.magazine;
            cd[i] = cdMax[i] = w.IsShooter ? 0.3f : 0f;
        }

        /// <summary>
        /// 落ちている武装をその所に付ける。今の武装は、拾った武装のあった所にそのまま置く(その場で入れ替え。拾い直せる)。
        /// 両手持ちを付けるときは(R1/L1 のどちらで決めても)両手に付け、右手・左手の今の武装を両方置く(素の剣も。同じ武装を2つ持っていれば2つとも)。
        /// 両手持ちのまま片手の武装を付けるときは、両手持ちを外して置き、選んだ手に付ける(もう片方の手は空になる)。
        /// </summary>
        void Equip(DuelLoot loot, DuelSlot slot)
        {
            var w = loot.Weapon;
            int i = (int)slot;
            bool hand = i == R || i == L;
            bool shieldBefore = Hand(0) is { kind: DuelKind.Shield } || Hand(1) is { kind: DuelKind.Shield };
            Vector3 at = loot.transform.position;
            loot.Take();
            charge = 0f;
            if (hand && (w.IsTwoHand || TwoHanded))
            {
                var oldR = slots[R];
                var oldL = slots[L];
                bool oldTwo = oldR is { IsTwoHand: true };
                slots[R] = slots[L] = null;
                if (w.IsTwoHand) slots[R] = slots[L] = w;
                else slots[i] = w;
                // 外した武装を置く:1つ目は拾った所、2つ目は自機の側へ少し寄せて(重ならないように)
                if (oldR != null) DuelLoot.Place(oldR, at);
                if (oldL != null && !oldTwo) DuelLoot.Place(oldL, oldR != null ? Beside(at) : at);
                ArmState(R, slots[R]);
                ArmState(L, slots[L] != null && slots[L].IsTwoHand ? null : slots[L]);
                ArmState(AltIndex, AltFire);
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
                // 振っている途中ではない(付けるのは Free・ガードの間だけ)が、押して覚えている攻撃は忘れる
                if (oldR is { IsBlade: true }) player.OnSwordDropped(0);
                if (oldL is { IsBlade: true }) player.OnSwordDropped(1);
                game.Log(w.IsTwoHand ? $"両手に{w.name}({w.RarityName})を付けた"
                                     : $"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた(両手の{oldR?.name}は外した)");
            }
            else
            {
                var old = slots[i];
                slots[i] = w;
                if (old != null) DuelLoot.Place(old, at);
                ArmState(i, w);
                Refresh(slot);
                game.Log($"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた");
            }
            // 盾を付けた・盾を持っていた(外れたかもしれない):ゲージと構えている盾を引き直す
            if (w.kind == DuelKind.Shield || shieldBefore) player.OnShieldsChanged(w.kind == DuelKind.Shield);
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
            for (int i = 0; i < StateCount; i++) ResetState(i);
            charge = 0f;
            if (Choosing != null) EndChoice();
            player.OnShieldsChanged(false);
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

        /// <summary>
        /// 撃つ相手:ロックオン中の敵(射程の内で見通せれば)。なければ、画面の中央から30°以内で見通せる、射程の内の敵(中央に近く近いもの)。
        /// </summary>
        DuelEnemy AimTarget(float range, Vector3 from)
        {
            var t = player.Target;
            if (t != null && !t.Dead && Vector3.Distance(t.Chest, from) <= range && DuelSenses.ClearSight(from, t.Chest)) return t;
            var cam = Camera.main;
            Vector3 camF = cam != null ? cam.transform.forward : player.transform.forward;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                Vector3 to = e.Chest - from;
                float d = to.magnitude;
                if (d > range) continue;
                float ang = Vector3.Angle(camF, to);
                if (ang > 30f) continue;
                float score = d * (1f + ang / 15f);
                if (score >= bestScore || !DuelSenses.ClearSight(from, e.Chest)) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        /// <summary>狙う相手がいないとき:画面の中央の先(最初に当たる地形・敵。なければ遠く)。</summary>
        Vector3 FreeAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return player.Chest + player.transform.forward * 100f;
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            const float far = 250f;
            float best = far;
            if (Physics.Raycast(o, d, out var hit, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (RayHitsEnemy(o, d, best, out _, out float ed)) best = ed;
            return o + d * best;
        }

        /// <summary>画面の中央の先の点を、from から射程(range)までに切ったもの(狙う敵がいないとき、射程より遠くへ飛ばさない)。</summary>
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

        /// <summary>銃を撃つ(撃てる間隔・弾倉の残りがあれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。</summary>
        bool FireGun(DuelWeapon w, int i, DuelSlot s)
        {
            if (cd[i] > 0f || reload[i] > 0f) return false;
            if (w.magazine > 0 && mag[i] <= 0) { reload[i] = w.reload; return false; }
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            var t = AimTarget(w.range, from);
            Vector3 aim = t != null ? t.Chest : FreeAimPoint(from, w.range);
            Vector3 dir = (aim - from).normalized;
            Color c = w.magazine <= 0 ? BeamColor : BulletColor;
            int n = Mathf.Max(1, w.pellets);
            // 間隔の端数は持ち越す(1フレームより短い間隔なら、そのフレームに何発か出す。最大4回。押すたびに撃つ銃は1回)
            int bursts = w.hold ? 4 : 1;
            for (int k = 0; k < bursts && cd[i] <= 0f; k++)
            {
                cd[i] += Mathf.Max(0.01f, w.interval);
                for (int p = 0; p < n; p++) Shot(from, Jitter(dir, w.spread), w, c, n > 1);
                if (w.magazine > 0 && --mag[i] <= 0) { reload[i] = w.reload; break; }   // 撃ち切ったらリロード
            }
            cd[i] = Mathf.Max(cd[i], 0f);
            cdMax[i] = Mathf.Max(0.01f, cd[i]);   // 次に撃てるまでのゲージの分母(両手の銃の L1 の弾。付けたときの 0.3 秒のままだとゲージが合わない)
            return true;
        }

        /// <summary>銃の1発:まっすぐ飛び、最初に当たった地形・敵で止まる(貫く弾は地形まで飛び、線の上の敵みんなに当たる)。</summary>
        void Shot(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            float len = w.range;
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            Vector3 end = from + dir * len;
            if (w.pierce)
            {
                PierceShot(from, dir, len, w, c);
                return;
            }
            if (RayHitsEnemy(from, dir, len, out var e, out float d))
            {
                end = from + dir * d;
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                DuelSpark.Burst(end, -dir, c, pellet ? 2 : 3, 0.6f);
            }
            ProtoFx.Tracer(from, end, c, pellet ? 0.15f : 0.25f, 0.05f);
        }

        /// <summary>貫く弾:from から len まで、線から(敵の半径 + 太さ)の内にいる敵みんなに当てる。太いほど光線も太く長く残る。</summary>
        void PierceShot(Vector3 from, Vector3 dir, float len, DuelWeapon w, Color c)
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
                any = true;
            }
            ProtoFx.Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, w.beamWidth >= 1f ? 0.18f : 0.05f);
            if (any && w.charge > 0f) game.HitStop(0.06f);   // 溜め撃ちが当たったら短く止める(画面は揺らさない)
        }

        // ───────── 砲・ミサイル(肩) ─────────

        static readonly Color ShellColor = new(0.8f, 1f, 0.35f), MissileColor = new(1f, 0.6f, 0.25f), BlastColor = new(1f, 0.6f, 0.25f);

        /// <summary>砲・ミサイルを撃つ(次に撃てるまでの時間がたっていれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。</summary>
        bool FireHeavy(DuelWeapon w, int i, DuelSlot s)
        {
            if (cd[i] > 0f) return false;
            cd[i] = cdMax[i] = w.interval;
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            if (w.IsMissile)
            {
                // ミサイル:狙える敵に順番に割り振る(ロックオン中の敵が先、あとは近い順)。いなければ画面の中央の先へ
                var targets = MissileTargets(w.range);
                Vector3 point = FreeAimPoint(from, w.range);
                for (int j = 0; j < w.salvo; j++)
                {
                    var t = targets.Count > 0 ? targets[j % targets.Count] : null;
                    Vector2 sc = Random.insideUnitCircle * w.radius;
                    StartCoroutine(Missile(from, t, point + new Vector3(sc.x, 0f, sc.y), w, j * 0.06f));
                }
                return true;
            }
            var target = AimTarget(w.range, from);
            Vector3 aim = target != null ? target.Chest : FreeAimPoint(from, w.range);
            Vector3 dir = (aim - from).normalized;
            float len = Mathf.Min(Vector3.Distance(from, aim), w.range);
            // 間を地形・ほかの敵がさえぎっていれば、そこで爆発する
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            if (RayHitsEnemy(from, dir, len, out _, out float ed)) len = ed;
            StartCoroutine(Shell(from, from + dir * len, w));
            return true;
        }

        List<DuelEnemy> MissileTargets(float range)
        {
            var list = new List<DuelEnemy>();
            Vector3 eye = player.LockPoint;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                if (!DuelSenses.ClearSight(eye, e.Chest)) continue;
                list.Add(e);
            }
            var t = player.Target;
            list.Sort((a, b) =>
            {
                if (a == b) return 0;
                if (a == t) return -1;
                if (b == t) return 1;
                return (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude);
            });
            return list;
        }

        /// <summary>弾の見た目:光る球と、尾を引く線。</summary>
        static Transform Projectile(Vector3 at, Color c, float size)
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
            tr.time = 0.18f;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>砲弾:まっすぐ飛んで、着いた所で爆発する。</summary>
        IEnumerator Shell(Vector3 from, Vector3 to, DuelWeapon w)
        {
            const float speed = 160f;
            var p = Projectile(from, ShellColor, 1.2f + w.radius * 0.05f);
            float dur = Vector3.Distance(from, to) / speed, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                p.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.001f, dur)));
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(to, w, true);
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
