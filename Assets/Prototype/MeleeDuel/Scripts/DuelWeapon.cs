using System.Collections.Generic;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 武装を付ける所(5つ)。右手(R1)/ 左手(L1)/ 背中 / 右肩(R2)/ 左肩(L2)。
    /// 両手には剣・盾・手の銃のどれでも、両肩には肩の銃・砲・ミサイルのどれでも付けられる(左右の役割は決めない)。
    /// </summary>
    public enum DuelSlot { RightHand, LeftHand, Back, RightShoulder, LeftShoulder }

    /// <summary>
    /// 武装の種類。どの所に付けられるかが決まる(手:剣・盾・手の銃 / 背中:スラスター / 肩:銃・砲とミサイル)。
    /// 両手の近接(両手剣・長柄)・両手の銃・砲は右手と左手の両方を使う(R1 が主の技、L1 が別の技。DuelArms は両方の所に同じ武装を入れる)。
    /// </summary>
    public enum DuelKind { Sword, Shield, HandGun, Back, ShoulderGun, ShoulderHeavy, TwoHandMelee, TwoHandGun }

    /// <summary>
    /// ステージで敵が落とす武装1つの中身(弧ひとつ分の試作)。耐久・重量・品質はない。強さはレア度(0=コモン / 1=レア / 2=エピック)で分ける。
    /// 使う項目は種類で違う(剣:技の表と倍率 / 盾:ガード / 背中:倍率 / 銃:射撃 / 砲・ミサイル:爆発)。数値・名前は仮置き。
    /// 一覧は Defaults()(DuelWeapon.Catalog.cs)で、ステージ(DuelStage.arsenal)が実行時に作る(シリアライズしないので、インスペクターでは触れない。数値はそこで直す)。
    /// </summary>
    [System.Serializable]
    public partial class DuelWeapon
    {
        public string name = "武装";
        public DuelKind kind;
        [Range(0, 2)] public int rarity;
        [Tooltip("拾うときに見せる一言")]
        public string feel = "";
        [Tooltip("落ちる出やすさ(倍。同じレア度の中での重みに掛かる)")]
        public float dropWeight = 1f;

        [Header("両手持ち(両手の近接・両手の銃・砲)")]
        [Tooltip("R1 の技の名前(画面に出す。空なら種類から決める)")]
        public string mainMove = "";
        [Tooltip("L1 の技の名前(画面に出す)")]
        public string altMove = "";
        /// <summary>両手の銃・砲の L1 の撃ち方(種類は手の銃=弾 / 肩の砲=爆発。charge があれば溜め撃ち)。シリアライズしない(入れ子になるため)。</summary>
        [System.NonSerialized] public DuelWeapon alt;

        [Header("剣(技の表。空なら素の剣の表。威力などには下の倍率がさらに掛かる)")]
        public DuelMoveSet moves;
        public float damageMul = 1f;
        [Tooltip("届く範囲(剣の見た目の長さも同じ倍率)")]
        public float reachMul = 1f;
        [Tooltip("敵の怯みゲージへの量")]
        public float staggerMul = 1f;
        [Tooltip("振りの速さ(大きいほど振りかぶり・戻りが短い)")]
        public float speedMul = 1f;
        [Tooltip("当たる扇の広さに足す角度(片側。最大180で全周)")]
        public float arcAdd;
        public float knockMul = 1f;

        [Header("盾(付けた手のボタンを押している間ガード)")]
        [Tooltip("ガードのゲージの量(素のガードは100)")]
        public float guardMax = 100f;
        [Tooltip("受けたときのゲージの削れ方(小さいほど削れにくい)")]
        public float guardCostMul = 1f;
        [Tooltip("ガードで受けたダメージの何割をHPに受けるか(素のガードは0.1)")]
        public float guardChip = 0.1f;
        [Tooltip("構えたまま歩く速さ(走りの何割。素のガードは0.4)")]
        public float guardMoveMul = 0.4f;
        [Tooltip("受けたときに押し戻され・固まる長さの倍率(小さいほどすぐ動ける)")]
        public float guardStunMul = 1f;

        [Header("背中(スラスター)・盾の重さ(素の値に掛ける倍率)")]
        [Tooltip("地上を走る速さ(盾は重さ。持っているだけで掛かる)")] public float runMul = 1f;
        [Tooltip("ブースト移動の速さと飛ぶときの上昇の速さ")] public float dashMul = 1f;
        [Tooltip("回避で動く距離")] public float dodgeMul = 1f;
        [Tooltip("回避・ブースト・飛行で使うEN(小さいほど燃費が良い)")] public float enCostMul = 1f;

        [Header("銃(手・肩)・砲とミサイル(肩)")]
        [Tooltip("オンなら押している間撃ち続ける。オフなら押すたびに撃つ")]
        public bool hold = true;
        [Tooltip("1発(散弾は1粒・ミサイルは1発)のダメージ")] public float damage = 1f;
        [Tooltip("銃:発射の間隔(秒) / 砲・ミサイル:次に撃てるまで(秒)")] public float interval = 0.1f;
        public float range = 60f;
        [Tooltip("1発ごとの敵の怯みゲージへの量(射撃は上限 DuelGame.rangedStaggerCap の手前までしかためず、怯ませない。崩すのは近接)")] public float stagger = 0.5f;
        [Tooltip("銃:ばらつき(度)")] public float spread;
        [Tooltip("銃:1回で出る粒の数(2以上なら散弾)")] public int pellets = 1;
        [Tooltip("銃:弾倉(0なら弾倉なし)")] public int magazine;
        [Tooltip("銃:リロードの秒数(撃ち切ったときと、△+撃つボタンの手動リロード)")] public float reload = 2f;
        [Tooltip("砲・ミサイル:爆発の半径(m)")] public float radius = 5f;
        [Tooltip("砲・ミサイル:1回で撃つ数(2以上ならミサイル。狙える敵に割り振って追いかける)")] public int salvo = 1;
        [Tooltip("当たったときに押す速さ(射撃では怯ませないので、DuelGame.rangedPushMin 以上なら1発ごとに軽く押し返す。技は止めない)")] public float knock = 10f;
        [Tooltip("両手の銃・砲だけ:オンなら主の撃ち方が爆発(砲・ミサイル)。オフなら弾(銃)")] public bool blast;
        [Tooltip("溜め撃ち:押している間に溜め、この秒数を超えてから離すと撃つ(0なら溜めない)")] public float charge;
        [Tooltip("弾が敵を貫いて、線の上の敵みんなに当たる")] public bool pierce;
        [Tooltip("貫く弾の太さ(敵の半径に足す m)")] public float beamWidth;

        [Header("重さ・共通の弾・EN・照準の枠(DuelArmsRules で効き方を決める)")]
        [Tooltip("重さ。付けている武装と所持品の合計で、持てる量(上限)と、ENを使う移動(ブースト移動・回避の距離・飛ぶときの上昇)の速さが決まる。歩き・走りには効かない")]
        public float weight = 5f;
        [Tooltip("共通の弾の使う量。弾倉のある銃:弾倉へ1発詰めるごと(リロードのとき) / 弾倉のない砲・ミサイル・L1の撃ち方:撃つたび(1回分)。0 なら使わない(エネルギー武器)")]
        public float ammoCost;
        [Tooltip("ENの使う量(エネルギー武器。0 なら使わない)。押している間撃ち続ける武装(hold オン)は1秒あたり、押すたびに撃つ・溜め撃ちは1回あたり。" +
                 "ENが0以下の間は撃てず、残りが少しでもあれば撃てて足りない分はマイナスになる(回避・攻撃と同じ決まり)。溜め撃ちは溜め始めにENが残っていれば、離したときに使う")]
        public float enCost;
        [Tooltip("照準の枠の大きさ(画面の中央からの角度。横の半分。縦は DuelArmsRules.frameAspect 倍)。0 なら DuelArmsRules.defaultFrame。両手の銃・砲の L1 は 0 なら主の撃ち方の枠")]
        public float aimFrame;

        [Header("反動(連射すると照準が広がり、実際のばらつきも広がる。撃つのをやめると戻る。カメラは動かさない)")]
        [Tooltip("1発(散弾は1回)撃つごとに、ばらつきに足す角度(度)。0 なら広がらない")]
        public float recoilPerShot;
        [Tooltip("広がりの上限(度。spread に足す分)")]
        public float recoilMax;
        [Tooltip("撃つのをやめてから戻る速さ(度/秒。DuelArmsRules.recoilDelay 秒たってから戻り始める)")]
        public float recoilRecover = 6f;

        [Header("弾の速さと、照準のずれ(作者が選んだ形:弾に速さ・反動で照準そのものがずれる。カメラは動かさない)")]
        [Tooltip("弾の速さ(m/s)。見える弾が飛び、動く敵には照準を先に置かないと当たらない。0 なら今までどおりすぐ届く光線(照射し続けるレーザー)。" +
                 "全体の倍率は DuelArmsRules.shotSpeedMul")]
        public float shotSpeed;
        [Tooltip("1回撃つごとに照準を上へずらす角度(度)。押さえ込まないと上がっていく。全体の倍率は DuelArmsRules.kickMul")]
        public float aimKick;
        [Tooltip("1回撃つごとに照準を左右へずらす角度(度。±この範囲でランダム)")]
        public float aimKickSide;

        [Header("見た目(仮の形)")]
        public PrimitiveType shape = PrimitiveType.Cylinder;
        public Vector3 scale = new(0.8f, 1.6f, 0.8f);
        public Vector3 euler = new(90f, 0f, 0f);
        [Tooltip("剣:刃の太さの倍率(機体の剣・落ちている見た目)")] public float thickMul = 1f;
        [Tooltip("剣:握りの後ろへ伸びる柄の長さ(m。0なら柄なし)。長柄・両手剣で両手で持つ感じを出す")] public float haft;

        public static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        public static readonly Color[] RarityColors = { new(0.75f, 0.77f, 0.8f), new(0.3f, 0.6f, 1f), new(1f, 0.75f, 0.15f) };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, 2)];
        public Color Color => RarityColors[Mathf.Clamp(rarity, 0, 2)];
        public bool IsMissile => salvo > 1;
        public bool IsHand => kind is DuelKind.Sword or DuelKind.Shield or DuelKind.HandGun or DuelKind.TwoHandMelee or DuelKind.TwoHandGun;
        /// <summary>右手と左手の両方を使う武装。</summary>
        public bool IsTwoHand => kind is DuelKind.TwoHandMelee or DuelKind.TwoHandGun;
        /// <summary>振る武装(片手の剣・両手の近接)。技の表で攻撃する。</summary>
        public bool IsBlade => kind is DuelKind.Sword or DuelKind.TwoHandMelee;
        /// <summary>撃つ武装(手の銃・肩の銃・砲とミサイル・両手の銃・砲)。</summary>
        public bool IsShooter => kind is DuelKind.HandGun or DuelKind.ShoulderGun or DuelKind.ShoulderHeavy or DuelKind.TwoHandGun;
        /// <summary>爆発する撃ち方(肩の砲・ミサイル、主の撃ち方が爆発の両手の砲)。</summary>
        public bool Explosive => kind == DuelKind.ShoulderHeavy || (kind == DuelKind.TwoHandGun && blast);
        /// <summary>弾の撃ち方で、弾倉・リロードのある銃(手・肩・両手の銃)。</summary>
        public bool IsGun => IsShooter && !Explosive;

        /// <summary>最初から右手に持っている素の剣(落ちる一覧には入らない。手から外すと床に置かれ、拾い直せる)。</summary>
        public static readonly DuelWeapon BaseSword = new() { name = "素の剣", kind = DuelKind.Sword, rarity = 0, feel = "最初から持っている剣", weight = 4f };

        /// <summary>重さ・共通の弾の使う量・照準の枠・ENの使う量をまとめて入れる(一覧を読みやすくするため。this を返す)。</summary>
        DuelWeapon Spec(float weight, float ammo = 0f, float frame = 0f, float en = 0f)
        {
            this.weight = weight;
            ammoCost = ammo;
            aimFrame = frame;
            enCost = en;
            return this;
        }

        /// <summary>反動(1発ごとに広がる角度・上限・戻る速さ。度)をまとめて入れる(this を返す)。</summary>
        DuelWeapon Recoil(float perShot, float max, float recover)
        {
            recoilPerShot = perShot;
            recoilMax = max;
            recoilRecover = recover;
            return this;
        }

        /// <summary>弾の速さ(m/s。0 ならすぐ届く光線)と、反動で照準をずらす角度(上へ・左右へ。度)をまとめて入れる(this を返す)。</summary>
        DuelWeapon Ballistic(float speed, float kick = 0f, float kickSide = 0f)
        {
            shotSpeed = speed;
            aimKick = kick;
            aimKickSide = kickSide;
            return this;
        }

        /// <summary>撃つたびにENを使うか(エネルギー武器)。</summary>
        public bool UsesEnergy => enCost > 0f;
        /// <summary>1回撃つ(1発・1回分)ごとに使うEN。押している間撃ち続ける武装は enCost が1秒あたりなので、発射の間隔を掛ける。</summary>
        public float EnPerShot => hold && charge <= 0f ? enCost * Mathf.Max(0.01f, interval) : enCost;
        /// <summary>ENの使う量の表示(「EN 20/秒」「EN 12/発」「EN 40/回」)。使わなければ空。</summary>
        public string EnText => !UsesEnergy ? "" : hold && charge <= 0f ? $"EN {enCost:0.#}/秒" : charge > 0f ? $"EN {enCost:0.#}/回" : $"EN {enCost:0.#}/発";

        /// <summary>撃つたびに共通の弾を使うか(弾倉のない、実弾の撃ち方)。弾倉のある銃はリロードのときに使う。</summary>
        public bool PaysPerShot => ammoCost > 0f && !(IsGun && magazine > 0);
        /// <summary>共通の弾を使うか(エネルギー武器・振る武装などは使わない)。</summary>
        public bool UsesAmmo => ammoCost > 0f;
        /// <summary>共通の弾の使う量の表示(弾倉のある銃は「3/発」、撃つたびに使う撃ち方は「6/回」)。使わなければ空。</summary>
        public string AmmoCostText => !UsesAmmo ? "" : PaysPerShot ? $"{ammoCost:0.#}/回" : $"{ammoCost:0.#}/発";

        // ───────── 剣の技(表が空なら素の剣の表) ─────────

        public DuelSwing[] LightMoves => moves != null && moves.light != null && moves.light.Length > 0 ? moves.light : DuelMoveSet.Base.light;
        public DuelSwing[] HeavyMoves => moves != null && moves.heavy != null && moves.heavy.Length > 0 ? moves.heavy : DuelMoveSet.Base.heavy;
        public DuelSwing DodgeMove => moves != null && moves.dodgeSlash.windup > 0f ? moves.dodgeSlash : DuelMoveSet.Base.dodgeSlash;
        /// <summary>L1 の別の技(両手の近接だけ。なければ null)。</summary>
        public DuelSwing[] AltMoves => IsTwoHand && moves != null && moves.alt != null && moves.alt.Length > 0 ? moves.alt : null;
        /// <summary>そのボタンの手(0=R1 / 1=L1)で振る連撃の表。両手の近接は L1 で別の技。</summary>
        public DuelSwing[] LightFor(int hand) => hand == 1 && AltMoves != null ? AltMoves : LightMoves;
        /// <summary>回避の後にそのボタンの手で出す技。両手の近接の L1 は別の技の1段目。</summary>
        public DuelSwing DodgeFor(int hand) => hand == 1 && AltMoves != null ? AltMoves[0] : DodgeMove;

        // ───────── 付ける所 ─────────

        static readonly DuelSlot[] HandSlots = { DuelSlot.RightHand, DuelSlot.LeftHand };
        static readonly DuelSlot[] BackSlots = { DuelSlot.Back };
        static readonly DuelSlot[] ShoulderSlots = { DuelSlot.RightShoulder, DuelSlot.LeftShoulder };

        /// <summary>
        /// この種類を付けられる所(手なら右手・左手、肩なら右肩・左肩、背中は背中だけ)。
        /// 両手持ちも右手・左手(付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付く)。
        /// </summary>
        public static DuelSlot[] SlotsFor(DuelKind k) => k switch
        {
            DuelKind.Sword or DuelKind.Shield or DuelKind.HandGun or DuelKind.TwoHandMelee or DuelKind.TwoHandGun => HandSlots,
            DuelKind.Back => BackSlots,
            _ => ShoulderSlots,
        };

        public static string SlotName(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => "右手",
            DuelSlot.LeftHand => "左手",
            DuelSlot.Back => "背中",
            DuelSlot.RightShoulder => "右肩",
            _ => "左肩",
        };

        public static string KindName(DuelKind k) => k switch
        {
            DuelKind.Sword => "剣",
            DuelKind.Shield => "盾",
            DuelKind.HandGun => "手の銃",
            DuelKind.Back => "スラスター",
            DuelKind.ShoulderGun => "肩の銃",
            DuelKind.TwoHandMelee => "両手の近接",
            DuelKind.TwoHandGun => "両手の銃・砲",
            _ => "砲・ミサイル",
        };

        /// <summary>R1 の技の名前(両手持ちの表示用)。</summary>
        public string MainMoveName => mainMove.Length > 0 ? mainMove : IsBlade ? $"連撃{LightMoves.Length}段" : FireMode;

        /// <summary>L1 の撃ち方の短い説明(「溜め1.0秒 40 貫通」など)。両手の銃・砲だけ。</summary>
        public string AltShotText()
        {
            var a = alt;
            if (a == null) return "";
            string body = a.Explosive
                ? (a.salvo > 1 ? $"{a.damage:0.#}×{a.salvo}発 爆風{a.radius:0.#}m" : $"{a.damage:0.#} 爆風{a.radius:0.#}m")
                : (a.pellets > 1 ? $"{a.damage:0.#}×{a.pellets}粒" : $"{a.damage:0.#}");
            // L1 の枠:自分の枠がなければ主の撃ち方の枠を借りる(同じなら出さない。違えば「枠±10°」)
            var rules = DuelArmsRules.Current;
            float fa = rules.FrameDeg(a, this), fm = rules.FrameDeg(this, null);
            return (a.charge > 0f ? $"溜め{a.charge:0.0}秒 " : "") + body + (a.pierce ? " 貫通" : "") + (a.charge > 0f ? "" : $" 間隔{a.interval:0.0}秒")
                   + (a.UsesEnergy ? " " + a.EnText : "") + (Mathf.Abs(fa - fm) > 0.05f ? $" 枠±{fa:0}°" : "");
        }

        /// <summary>
        /// 比べられる仲間(振る武装 / 盾 / 背中 / 弾の銃 / 爆発)。同じ仲間なら、同じ名前の項目どうしで比べる。
        /// 性能のグラフ(DuelRadar)の軸の組も、この仲間ごと(0〜4)。
        /// </summary>
        public static int Family(DuelWeapon w) => w.IsBlade ? 0 : w.kind == DuelKind.Shield ? 1 : w.kind == DuelKind.Back ? 2 : w.IsGun ? 3 : 4;
        public static bool SameFamily(DuelWeapon a, DuelWeapon b) => a != null && b != null && Family(a) == Family(b);

        // ───────── 比べる(拾うとき) ─────────

        /// <summary>比べる項目:名前・値・表示の書式・大きいほど良いか。w が null(空き)なら何も出さない。</summary>
        public static List<(string name, float value, string text, bool higherBetter)> Stats(DuelWeapon w)
        {
            var list = new List<(string, float, string, bool)>();
            if (w == null) return list;
            switch (w.kind)
            {
                case DuelKind.Sword:
                case DuelKind.TwoHandMelee:
                {
                    // 技の表から:連撃の一振りの平均の威力・1秒に振れる回数(続けて押したとき)・届く範囲・扇の広さ・強攻撃の威力
                    var L = w.LightMoves;
                    float dmg = 0f, cyc = 0f, arc = 0f;
                    foreach (var m in L)
                    {
                        dmg += m.damage;
                        cyc += m.windup + m.active + DuelSwing.ChainAfter;
                        arc += m.arc;
                    }
                    dmg = dmg / L.Length * w.damageMul;
                    float rate = L.Length / Mathf.Max(0.01f, cyc) * Mathf.Max(0.3f, w.speedMul);
                    arc = Mathf.Min(180f, arc / L.Length + w.arcAdd);
                    float reach = L[0].reach * w.reachMul;
                    float heavy = 0f;
                    foreach (var m in w.HeavyMoves) heavy += m.damage;
                    heavy = heavy / Mathf.Max(1, w.HeavyMoves.Length) * w.damageMul;
                    list.Add(("威力", dmg, $"{dmg:0.#}", true));
                    list.Add(("速さ", rate, $"{rate:0.0}回/秒", true));
                    list.Add(("届く", reach, $"{reach:0.#}m", true));
                    list.Add(("幅", arc, $"±{arc:0}°", true));
                    list.Add(("強攻撃", heavy, $"{heavy:0}", true));
                    var alt = w.AltMoves;
                    if (alt != null)
                    {
                        // L1 の別の技:一振りの平均の威力と、扇の広さ・届く範囲
                        float ad = 0f, aa = 0f;
                        foreach (var m in alt)
                        {
                            ad += m.damage;
                            aa += m.arc;
                        }
                        ad = ad / alt.Length * w.damageMul;
                        aa = Mathf.Min(180f, aa / alt.Length + w.arcAdd);
                        list.Add(("L1技", ad, $"{ad:0}(±{aa:0}°・{alt[0].reach * w.reachMul:0.#}m)", true));
                    }
                    break;
                }
                case DuelKind.Shield:
                    list.Add(("ゲージ", w.guardMax, $"{w.guardMax:0}", true));
                    list.Add(("削れ", w.guardCostMul, $"×{w.guardCostMul:0.00}", false));
                    list.Add(("通す傷", Mathf.Max(0.001f, w.guardChip), $"{w.guardChip * 100f:0}%", false));
                    list.Add(("構え歩き", w.guardMoveMul, $"{w.guardMoveMul * 100f:0}%", true));
                    list.Add(("走り", w.runMul, $"×{w.runMul:0.00}", true));
                    break;
                case DuelKind.Back:
                    list.Add(("走り", w.runMul, $"×{w.runMul:0.00}", true));
                    list.Add(("ブースト", w.dashMul, $"×{w.dashMul:0.00}", true));
                    list.Add(("回避", w.dodgeMul, $"×{w.dodgeMul:0.00}", true));
                    list.Add(("EN消費", w.enCostMul, $"×{w.enCostMul:0.00}", false));
                    break;
                case DuelKind.HandGun:
                case DuelKind.ShoulderGun:
                case DuelKind.TwoHandGun when !w.blast:
                {
                    float shot = w.damage * Mathf.Max(1, w.pellets);
                    float dps = shot / Mathf.Max(0.01f, w.interval);
                    list.Add(("毎秒", dps, $"{dps:0}", true));
                    list.Add(("1発", shot, w.pellets > 1 ? $"{w.damage:0.#}×{w.pellets}粒" : $"{w.damage:0.#}", true));
                    list.Add(("怯ませ", w.stagger * Mathf.Max(1, w.pellets), $"{w.stagger * Mathf.Max(1, w.pellets):0.#}", true));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    if (w.magazine > 0) list.Add(("弾倉", w.magazine, $"{w.magazine}", true));   // 弾倉のない銃(エネルギー武器)は下の「EN」で比べる
                    AddShooter(list, w);
                    AddAlt(list, w);
                    break;
                }
                default:
                {
                    float total = w.damage * Mathf.Max(1, w.salvo);
                    list.Add(("威力", total, w.salvo > 1 ? $"{w.damage:0.#}×{w.salvo}発" : $"{w.damage:0.#}", true));
                    list.Add(("爆風", w.radius, $"{w.radius:0.#}m", true));
                    list.Add(("装填", w.interval, $"{w.interval:0.0}秒", false));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    AddShooter(list, w);
                    AddAlt(list, w);
                    break;
                }
            }
            list.Add(("重さ", Mathf.Max(0.01f, w.weight), $"{w.weight:0.#}", false));
            return list;
        }

        /// <summary>
        /// 撃つ武装の照準の枠の大きさと、共通の弾の使う量(弾倉の銃は1発詰めるごと、ほかは撃つたび)。
        /// エネルギー武器は弾の代わりにENの使う量(押している間撃つ武装は1秒あたり、ほかは1発あたり)。
        /// </summary>
        static void AddShooter(List<(string, float, string, bool)> list, DuelWeapon w)
        {
            float f = DuelArmsRules.Current.FrameDeg(w, null);   // インスペクターの frameMul・defaultFrame を掛けた、実際の枠
            list.Add(("枠", f, $"±{f:0}°", true));
            if (w.UsesEnergy && w.ammoCost <= 0f)
            {
                list.Add(("EN", w.enCost, w.EnText.Substring(3), false));   // 「EN 20/秒」の「20/秒」
                return;
            }
            list.Add(("弾", w.ammoCost > 0f ? w.ammoCost : 0.01f,
                w.UsesAmmo ? w.AmmoCostText : "使わない", false));
            if (w.UsesEnergy) list.Add(("EN", w.enCost, w.EnText.Substring(3), false));
        }

        /// <summary>両手の銃・砲の L1 の撃ち方(1回の威力。粒・発の数を掛ける)。</summary>
        static void AddAlt(List<(string, float, string, bool)> list, DuelWeapon w)
        {
            if (w.alt == null) return;
            float v = w.alt.damage * Mathf.Max(1, w.alt.pellets) * Mathf.Max(1, w.alt.salvo);
            list.Add(("L1", v, w.AltShotText(), true));
        }

        /// <summary>撃ち方の一言(押している間 / 押すたび)。撃たない武装は空。</summary>
        public string FireMode => IsShooter ? (hold ? "押している間撃つ" : "押すたびに撃つ") : "";

        /// <summary>
        /// 拾う候補(next)と今の装備(cur)の比べ:「威力 9→12↑↑」のように並べる(良くなるのは緑、悪くなるのは赤。リッチテキスト)。
        /// 今が空き・違う種類なら値だけ。
        /// </summary>
        public static string CompareText(DuelWeapon next, DuelWeapon cur)
        {
            var a = Stats(next);
            // 同じ仲間(剣と両手の近接、手の銃と両手の銃など)なら、同じ名前の項目どうしで比べる。片方にしかない項目は値だけ
            var b = SameFamily(cur, next) ? Stats(cur) : null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Count; i++)
            {
                if (sb.Length > 0) sb.Append("   ");
                string nm = a[i].name;
                int j = b == null ? -1 : b.FindIndex(x => x.name == nm);
                if (j < 0)
                {
                    sb.Append($"{a[i].name} {a[i].text}");
                    continue;
                }
                float ratio = a[i].value / Mathf.Max(0.0001f, b[j].value);
                string arrow = Arrow(ratio);
                bool better = (ratio > 1f) == a[i].higherBetter;
                string body = $"{a[i].name} {b[j].text}→{a[i].text}";
                sb.Append(arrow.Length == 0 ? body : $"<color={(better ? "#7CFC8A" : "#FF7A6A")}>{body}{arrow}</color>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 比べの短い形(拾う案内のカードの、付け先ごとの1行):差のある項目だけを「威力↑↑」「速さ↓」のように items に足す
        /// (矢印は値の上がり下がり、色は良くなる=緑・悪くなる=赤。CompareText と同じ決まり:6%未満の差は出さない、25%以上は二重)。
        /// 今が空き・違う仲間、片方にしかない項目は何も足さない。
        /// </summary>
        public static void CompareShort(DuelWeapon next, DuelWeapon cur, List<string> items)
        {
            if (!SameFamily(cur, next)) return;
            var a = Stats(next);
            var b = Stats(cur);
            for (int i = 0; i < a.Count; i++)
            {
                string nm = a[i].name;
                int j = b.FindIndex(x => x.name == nm);
                if (j < 0) continue;
                float ratio = a[i].value / Mathf.Max(0.0001f, b[j].value);
                string arrow = Arrow(ratio);
                if (arrow.Length == 0) continue;
                bool better = (ratio > 1f) == a[i].higherBetter;
                items.Add("<color=" + (better ? "#7CFC8A" : "#FF7A6A") + ">" + nm + arrow + "</color>");
            }
        }

        /// <summary>比(新/今)の大きさを矢印に:6%未満は無印、25%以上は二重(武装ハントの PartDefinition.Arrow と同じ決まり)。</summary>
        public static string Arrow(float r)
        {
            float a = Mathf.Abs(Mathf.Log(Mathf.Max(0.0001f, r)));
            if (a < Mathf.Log(1.06f)) return "";
            bool up = r > 1f;
            return a >= Mathf.Log(1.25f) ? (up ? "↑↑" : "↓↓") : (up ? "↑" : "↓");
        }
    }
}
