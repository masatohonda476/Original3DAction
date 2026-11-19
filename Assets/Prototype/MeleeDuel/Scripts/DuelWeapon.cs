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
    /// 一覧は Defaults() で、ステージ(DuelStage.arsenal)が実行時に作る(シリアライズしないので、インスペクターでは触れない。数値はここで直す)。
    /// </summary>
    [System.Serializable]
    public class DuelWeapon
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

        /// <summary>この種類を付けられる所(手なら右手・左手、肩なら右肩・左肩、背中は背中だけ)。</summary>
        /// 両手持ちも右手・左手(付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付く)。
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

        /// <summary>比べられる仲間(振る武装 / 盾 / 背中 / 弾の銃 / 爆発)。同じ仲間なら、同じ名前の項目どうしで比べる。</summary>
        /// 性能のグラフ(DuelRadar)の軸の組も、この仲間ごと(0〜4)。
        public static int Family(DuelWeapon w) =>w.IsBlade ? 0 : w.kind == DuelKind.Shield ? 1 : w.kind == DuelKind.Back ? 2 : w.IsGun ? 3 : 4;
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
                w.ammoCost <= 0f ? "使わない" : w.IsGun && w.magazine > 0 ? $"{w.ammoCost:0.#}/発" : $"{w.ammoCost:0.#}/回", false));
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

        /// <summary>比(新/今)の大きさを矢印に:6%未満は無印、25%以上は二重(武装ハントの PartDefinition.Arrow と同じ決まり)。</summary>
        public static string Arrow(float r)
        {
            float a = Mathf.Abs(Mathf.Log(Mathf.Max(0.0001f, r)));
            if (a < Mathf.Log(1.06f)) return "";
            bool up = r > 1f;
            return a >= Mathf.Log(1.25f) ? (up ? "↑↑" : "↓↓") : (up ? "↑" : "↓");
        }

        // ───────── 初期の一覧(仮置き) ─────────

        static DuelWeapon Sword(string name, int rarity, string feel, DuelMoveSet moves, float dmg, float reach, float stagger, float knock) => new()
        {
            name = name, kind = DuelKind.Sword, rarity = rarity, feel = feel, moves = moves,
            damageMul = dmg, reachMul = reach, staggerMul = stagger, knockMul = knock,
        };

        static DuelWeapon Shield(string name, int rarity, string feel, float guard, float costMul, float chip, float guardMove, float run, float stunMul, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.Shield, rarity = rarity, feel = feel, guardMax = guard, guardCostMul = costMul, guardChip = chip,
            guardMoveMul = guardMove, runMul = run, guardStunMul = stunMul, shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon Back(string name, int rarity, string feel, float run, float dash, float dodge, float en, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.Back, rarity = rarity, feel = feel, runMul = run, dashMul = dash, dodgeMul = dodge, enCostMul = en,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon HandGun(string name, int rarity, string feel, bool hold, float dmg, float interval, float range, float spread, int pellets, int mag, float reload,
                                  float stagger, float knock, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.HandGun, rarity = rarity, feel = feel, hold = hold, damage = dmg, interval = interval, range = range,
            spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = knock,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon Gun(string name, int rarity, string feel, bool hold, float dmg, float interval, float range, float spread, int pellets, int mag, float reload, float stagger, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.ShoulderGun, rarity = rarity, feel = feel, hold = hold, damage = dmg, interval = interval, range = range,
            spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = 4f, scale = scale,
        };

        static DuelWeapon Heavy(string name, int rarity, string feel, bool hold, float dmg, int salvo, float radius, float interval, float range, float stagger, float knock, PrimitiveType shape, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.ShoulderHeavy, rarity = rarity, feel = feel, hold = hold, damage = dmg, salvo = salvo, radius = radius,
            interval = interval, range = range, stagger = stagger, knock = knock, shape = shape, scale = scale,
            euler = shape == PrimitiveType.Cylinder ? new Vector3(90f, 0f, 0f) : Vector3.zero,
        };

        /// <summary>両手持ちの出やすさ(同じレア度の中で、片手の武装1つを1としたときの重み。今の一覧が埋もれないように少なめ)。</summary>
        const float TwoHandDropWeight = 0.6f;

        /// <summary>両手の近接(技の表 + 倍率 + 見た目の太さ・柄の長さ)。</summary>
        static DuelWeapon TwoMelee(string name, int rarity, string feel, string altMove, DuelMoveSet moves, float dmg, float reach, float stagger, float knock,
                                   float thick, float haft) => new()
        {
            name = name, kind = DuelKind.TwoHandMelee, rarity = rarity, feel = feel, altMove = altMove, moves = moves,
            damageMul = dmg, reachMul = reach, staggerMul = stagger, knockMul = knock, thickMul = thick, haft = haft, dropWeight = TwoHandDropWeight,
        };

        /// <summary>両手の銃(主の撃ち方は弾)。alt は L1 の撃ち方。</summary>
        static DuelWeapon TwoGun(string name, int rarity, string feel, string mainMove, string altMove, bool hold, float dmg, float interval, float range, float spread,
                                 int pellets, int mag, float reload, float stagger, float knock, Vector3 scale, DuelWeapon alt, bool pierce = false, float beamWidth = 0f) => new()
        {
            name = name, kind = DuelKind.TwoHandGun, rarity = rarity, feel = feel, mainMove = mainMove, altMove = altMove, hold = hold, damage = dmg, interval = interval,
            range = range, spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = knock, pierce = pierce, beamWidth = beamWidth,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero, alt = alt, dropWeight = TwoHandDropWeight,
        };

        /// <summary>両手の砲(主の撃ち方は爆発。salvo が2以上ならミサイル)。alt は L1 の撃ち方。</summary>
        static DuelWeapon TwoCannon(string name, int rarity, string feel, string mainMove, string altMove, bool hold, float dmg, int salvo, float radius, float interval,
                                    float range, float stagger, float knock, PrimitiveType shape, Vector3 scale, DuelWeapon alt) => new()
        {
            name = name, kind = DuelKind.TwoHandGun, blast = true, rarity = rarity, feel = feel, mainMove = mainMove, altMove = altMove, hold = hold, damage = dmg,
            salvo = salvo, radius = radius, interval = interval, range = range, stagger = stagger, knock = knock, shape = shape, scale = scale,
            euler = shape == PrimitiveType.Cylinder ? new Vector3(90f, 0f, 0f) : Vector3.zero, alt = alt, dropWeight = TwoHandDropWeight,
        };

        /// <summary>L1 の撃ち方:弾(charge > 0 なら溜め撃ち)。</summary>
        static DuelWeapon AltShot(float dmg, float interval, float range, float spread, int pellets, float stagger, float knock, float charge = 0f, bool pierce = false,
                                  float beamWidth = 0f) => new()
        {
            name = "L1", kind = DuelKind.HandGun, hold = false, damage = dmg, interval = interval, range = range, spread = spread, pellets = pellets, magazine = 0,
            stagger = stagger, knock = knock, charge = charge, pierce = pierce, beamWidth = beamWidth,
        };

        /// <summary>L1 の撃ち方:爆発(salvo が2以上ならミサイル。charge > 0 なら溜め撃ち)。</summary>
        static DuelWeapon AltBlast(float dmg, int salvo, float radius, float interval, float range, float stagger, float knock, float charge = 0f) => new()
        {
            name = "L1", kind = DuelKind.ShoulderHeavy, hold = false, damage = dmg, salvo = salvo, radius = radius, interval = interval, range = range,
            stagger = stagger, knock = knock, charge = charge,
        };

        /// <summary>
        /// 初期の一覧。素の剣(通常攻撃9・強攻撃24、毎秒16ほど)・敵のHP(兵卒28・剣士47)に合わせた仮の値。
        /// 下(コモン)は素の剣を少し補う程度、上(エピック)は群れをまとめて倒せる強さ。
        /// 剣は種類ごとに技の表(振り方)が違う。盾・手の銃は右手・左手のどちらにも付けられる。
        /// </summary>
        public static List<DuelWeapon> Defaults() => new()
        {
            // 剣(手。技の表で振り方が変わる)
            Sword("鋼の剣", 0, "素の剣と同じ振り。重く、少し長い", DuelMoveSet.Standard(), 1.3f, 1.1f, 1.2f, 1.1f).Spec(6f),
            Sword("軽剣", 0, "浅く速い3段の連撃。一振りは軽い", DuelMoveSet.Quick(), 1.1f, 0.95f, 1f, 1f).Spec(4f),
            Sword("大剣", 1, "大きく振りかぶって叩き斬る。遅いが重く広い", DuelMoveSet.Great(), 1.25f, 1.3f, 1.2f, 1.2f).Spec(10f),
            Sword("高周波ブレード", 1, "速く鋭い4段の連撃と、踏み込む突き", DuelMoveSet.Blade(), 1.35f, 1.15f, 1.2f, 1f).Spec(6f),
            Sword("斬艦刀", 2, "長大な刃を体ごと回して、周りごと薙ぎ払う", DuelMoveSet.Giant(), 1.6f, 1.7f, 1.5f, 1.5f).Spec(14f),
            // 盾(手。付けた手のボタンを押している間ガード)
            Shield("鉄板の盾", 0, "軽く、構えたまま動きやすいが、すぐ削れる", 80f, 1.2f, 0.15f, 0.55f, 1f, 0.9f, new Vector3(0.3f, 2.6f, 2.2f)).Spec(6f),
            Shield("重装甲の盾", 0, "よく持つが重い。構えるとほとんど動けない", 140f, 0.9f, 0.08f, 0.3f, 0.92f, 0.8f, new Vector3(0.45f, 3.6f, 2.8f)).Spec(14f),
            Shield("複合装甲の盾", 1, "よく持ち、通す傷も少ない", 150f, 0.8f, 0.05f, 0.45f, 0.97f, 0.75f, new Vector3(0.4f, 3.2f, 2.6f)).Spec(11f),
            Shield("小型の盾", 1, "受けてもすぐ動ける。構えたまま走れる", 90f, 1f, 0.1f, 0.75f, 1f, 0.5f, new Vector3(0.3f, 2f, 1.8f)).Spec(4f),
            Shield("エネルギーシールド", 2, "ほとんど削れず、傷も通さない", 220f, 0.6f, 0f, 0.6f, 1f, 0.6f, new Vector3(0.2f, 3.8f, 3.2f)).Spec(7f),
            // 手の銃(手。付けた手のボタンで撃つ。肩の銃より一発ずつ狙って撃つ性格)
            HandGun("ハンドガン", 0, "押すたびに1発。弱いが狙いが正確", false, 4f, 0.2f, 55f, 0.4f, 1, 12, 1.3f, 1.5f, 3f, new Vector3(0.4f, 0.7f, 2.6f)).Spec(3f, 1f, 14f).Recoil(0.8f, 3f, 10f).Ballistic(170f, 1.2f, 0.4f),
            HandGun("サブマシンガン", 0, "押している間ばらまく。近い敵向け", true, 1.4f, 0.07f, 35f, 4f, 1, 30, 1.8f, 0.3f, 2f, new Vector3(0.45f, 0.8f, 3f)).Spec(4f, 0.5f, 16f).Recoil(0.35f, 5f, 6f).Ballistic(160f, 0.35f, 0.3f),
            HandGun("ライフル", 1, "一発が重く、遠くまで届く", false, 11f, 0.5f, 90f, 0.2f, 1, 6, 2f, 4f, 6f, new Vector3(0.45f, 0.8f, 4.6f)).Spec(6f, 3f, 6f).Recoil(1.5f, 4f, 9f).Ballistic(420f, 3f, 0.6f),
            HandGun("散弾銃", 1, "近くで撃てば怯みを一気にため、押し返す", false, 2.4f, 0.75f, 28f, 8f, 8, 4, 2.2f, 0.9f, 10f, new Vector3(0.6f, 0.9f, 3.6f)).Spec(6f, 4f, 18f).Recoil(1f, 3f, 6f).Ballistic(130f, 3.5f, 0.8f),
            HandGun("ビームライフル", 2, "弾を使わずENで撃つ。一発が重く遠くまで届く", false, 16f, 0.45f, 110f, 0f, 1, 0, 0f, 5f, 8f, new Vector3(0.5f, 0.9f, 5f)).Spec(7f, 0f, 7f, en: 12f).Recoil(1.2f, 3.5f, 9f).Ballistic(320f, 2.5f, 0.5f),
            // 背中
            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f)).Spec(5f),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f)).Spec(5f),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f)).Spec(2f),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f)).Spec(4.5f),
            // 肩の銃(右肩・左肩のどちらにも)
            Gun("肩部機関銃", 0, "弱いが、離れた所から削れる", true, 1.2f, 0.12f, 55f, 2.5f, 1, 24, 2.2f, 0.4f, new Vector3(0.7f, 1.6f, 0.7f)).Spec(6f, 0.5f, 14f).Recoil(0.3f, 4f, 5f).Ballistic(170f, 0.3f, 0.25f),
            Gun("重機関銃", 1, "一発が重く、よく削れる", true, 2f, 0.09f, 75f, 2f, 1, 45, 2.5f, 0.6f, new Vector3(1.2f, 2f, 1.2f)).Spec(12f, 0.5f, 10f).Recoil(0.3f, 5f, 5f).Ballistic(200f, 0.4f, 0.3f),
            Gun("肩部散弾銃", 1, "近いほど多く当たる9粒の散弾", false, 2.2f, 0.6f, 35f, 7f, 9, 6, 2f, 0.8f, new Vector3(1f, 1.5f, 1f)).Spec(8f, 4f, 18f).Recoil(1f, 3f, 6f).Ballistic(130f, 3f, 0.8f),
            Gun("ビームガトリング", 2, "弾を使わずENで撃ち続けて溶かす", true, 1.8f, 0.06f, 95f, 1.5f, 1, 0, 0f, 0.5f, new Vector3(1.1f, 2.4f, 1.1f)).Spec(11f, 0f, 10f, en: 20f).Recoil(0.2f, 4f, 5f).Ballistic(240f, 0.25f, 0.2f),
            // 肩の砲・ミサイル(右肩・左肩のどちらにも)
            Heavy("速射砲", 0, "爆発は小さいが、すぐ次が撃てる", true, 8f, 1, 5f, 1.4f, 70f, 6f, 8f, PrimitiveType.Cylinder, new Vector3(0.6f, 1.4f, 0.6f)).Spec(8f, 2f, 10f).Ballistic(110f, 2f, 0.5f),
            Heavy("重榴弾砲", 1, "装填は遅いが、大きな爆発で群れをまとめて削る", false, 22f, 1, 12f, 3.5f, 80f, 14f, 22f, PrimitiveType.Cylinder, new Vector3(1.3f, 2f, 1.3f)).Spec(20f, 6f, 8f).Ballistic(80f, 4f, 0.8f),
            Heavy("ミサイルポッド", 1, "4発が狙える敵を追いかける", false, 6f, 4, 4f, 3f, 100f, 4f, 6f, PrimitiveType.Cube, new Vector3(1.2f, 1f, 1.4f)).Spec(8f, 5f, 22f),
            Heavy("多弾頭ミサイル", 2, "10発が周りの敵へ散って追いかける", false, 10f, 10, 5f, 4f, 110f, 6f, 8f, PrimitiveType.Cube, new Vector3(1.8f, 1.4f, 1.8f)).Spec(14f, 15f, 26f),
            Heavy("超重砲", 2, "装填はとても遅いが、群れごと吹き飛ばす", false, 40f, 1, 20f, 6f, 90f, 30f, 30f, PrimitiveType.Cylinder, new Vector3(1.9f, 3.2f, 1.9f)).Spec(25f, 12f, 6f).Ballistic(70f, 6f, 1f),

            // 両手の近接(右手と左手の両方を使う。R1 = 連撃 / L1 = 別の技 / △+R1・L1 = 強攻撃。盾がないのでガードはできない)
            // 同じレア度の片手の剣より、一振りの威力・届く範囲・怯ませがはっきり上。振りはやや遅く、ENも多めに使う
            TwoMelee("両手剣", 0, "重い3段の連撃。L1 で大きく踏み込む突進斬り", "突進斬り", DuelMoveSet.TwoHandSword(), 1.15f, 1.4f, 1.2f, 1.2f, 1.5f, 2f).Spec(12f),
            TwoMelee("長槍", 0, "遠くまで届く速い突き。L1 で周りをまとめて薙ぎ払う", "薙ぎ払い", DuelMoveSet.Spear(), 1.1f, 1.7f, 1.1f, 1.1f, 0.7f, 4f).Spec(9f),
            TwoMelee("薙刀", 1, "広く流れる4段の連撃。L1 で長い突き → 斬り上げ", "突き → 斬り上げ", DuelMoveSet.Glaive(), 1.7f, 1.55f, 1.3f, 1.2f, 0.8f, 3.5f).Spec(11f),
            TwoMelee("戦鎚", 1, "遅いが一振りで敵を崩す。L1 で周りごと叩きつける", "叩きつけ", DuelMoveSet.Hammer(), 1.5f, 1.3f, 1.8f, 1.6f, 2.6f, 2.5f).Spec(16f),
            TwoMelee("斬機大太刀", 2, "長大な刃を両手で回し、周りごと斬り払う。L1 で大回転", "大回転 → 回転斬り上げ", DuelMoveSet.GreatTachi(), 2.2f, 2f, 1.6f, 1.6f, 1.8f, 2.5f).Spec(15f),
            TwoMelee("突撃槍", 2, "とても遠くまで届く突き。L1 で敵まで一気に突撃する", "突撃", DuelMoveSet.Lance(), 2f, 2.1f, 1.6f, 1.8f, 0.9f, 5f).Spec(14f),

            // 両手の銃・砲(右手と左手の両方を使う。R1 = 主の撃ち方 / L1 = 別の撃ち方。溜め撃ちは L1 を押して溜め、離して撃つ)
            // 同じレア度の手の銃・肩の砲より強い
            TwoGun("大型ライフル", 0, "重い一発。L1 を溜めて離すと、敵を貫く強い一発", "1発ずつ", "溜め撃ち(貫通)",
                   false, 14f, 0.45f, 100f, 0.2f, 1, 8, 2.2f, 6f, 8f, new Vector3(0.55f, 0.9f, 6f),
                   AltShot(40f, 0.8f, 120f, 0f, 1, 16f, 16f, charge: 1f, pierce: true, beamWidth: 0.5f).Spec(0f, 8f).Ballistic(520f, 6f, 1f))
                   .Spec(12f, 3f, 6f).Recoil(1.5f, 4f, 9f).Ballistic(450f, 3.5f, 0.6f),
            TwoGun("ガトリング砲", 0, "押している間ばらまき続ける。L1 で榴弾", "押している間連射", "榴弾",
                   true, 1.6f, 0.05f, 60f, 3f, 1, 90, 3f, 0.35f, 2f, new Vector3(0.9f, 0.9f, 4.5f),
                   AltBlast(16f, 1, 6f, 2.5f, 70f, 8f, 12f).Spec(0f, 4f, 10f).Ballistic(100f, 3f, 0.6f))
                   .Spec(16f, 0.5f, 12f).Recoil(0.25f, 6f, 6f).Ballistic(180f, 0.3f, 0.3f),
            TwoCannon("バズーカ", 1, "砲弾が大きく爆ぜる。L1 を溜めて離すと大爆発", "砲弾", "溜めて大爆発",
                      false, 30f, 1, 9f, 1.8f, 90f, 16f, 22f, PrimitiveType.Cylinder, new Vector3(1f, 2.8f, 1f),
                      AltBlast(60f, 1, 15f, 3f, 90f, 30f, 30f, charge: 1.2f).Spec(0f, 12f).Ballistic(60f, 7f, 1f)).Spec(14f, 6f, 8f).Ballistic(70f, 5f, 1f),
            TwoGun("ショットキャノン", 1, "近くの敵を12粒で押し返す。L1 で遠くまで届く重いスラグ弾", "散弾", "スラグ弾",
                   false, 2.8f, 0.8f, 32f, 9f, 12, 5, 2.4f, 1.2f, 14f, new Vector3(0.8f, 1f, 4.4f),
                   AltShot(34f, 1.2f, 70f, 0f, 1, 20f, 26f).Spec(0f, 6f, 7f).Recoil(2f, 4f, 6f).Ballistic(260f, 5f, 0.8f))
                   .Spec(13f, 6f, 16f).Recoil(1f, 3f, 6f).Ballistic(140f, 4f, 1f),
            TwoGun("レーザー砲", 2, "押している間、敵を貫く光線を撃ち続ける。L1 を溜めて離すと極太の光線", "押している間照射(貫通)", "溜め撃ち(極太・貫通)",
                   true, 3.8f, 0.08f, 110f, 0f, 1, 0, 0f, 0.6f, 2f, new Vector3(0.8f, 1.1f, 6.5f),
                   AltShot(90f, 1.5f, 130f, 0f, 1, 40f, 30f, charge: 1.5f, pierce: true, beamWidth: 3f).Spec(0f, 0f, en: 45f).Ballistic(0f, 6f, 1f), pierce: true, beamWidth: 0.3f)
                   .Spec(15f, 0f, 7f, en: 25f).Recoil(0.1f, 1.5f, 4f).Ballistic(0f, 0.15f, 0.15f),   // 照射するレーザーと極太の光線は今までどおりすぐ届く(速さ 0)
            TwoCannon("多連装ロケット", 2, "8発のロケットが狙える敵を追う。L1 を溜めて離すと20発の一斉発射", "8発", "溜めて一斉発射",
                      false, 9f, 8, 5f, 2.5f, 110f, 5f, 8f, PrimitiveType.Cube, new Vector3(1.6f, 1.4f, 3.6f),
                      AltBlast(12f, 20, 6f, 5f, 120f, 6f, 10f, charge: 1.2f).Spec(0f, 30f)).Spec(18f, 12f, 24f),
        };
    }

    /// <summary>
    /// 弧の試作の、照準の枠・重さと所持・共通の弾・分解の決まり(数値は仮)。ステージ(DuelStage.armsRules)のインスペクターで直せる。
    /// 武装ごとの値(重さ・弾の使う量・枠の大きさ・ENの使う量)は DuelWeapon.Defaults() の .Spec(...)。
    /// </summary>
    [System.Serializable]
    public class DuelArmsRules
    {
        /// <summary>武装に枠の大きさがないときの値(度)。</summary>
        public const float FallbackFrame = 12f;

        [Header("照準の枠(射撃だけ。銃・砲は照準の先へ撃ち、枠の中の敵へ少しだけ狙いを寄せる。ミサイルは枠の中の敵へ。枠は照準の位置が中心)")]
        [Tooltip("オンなら、全部の武装で枠の大きさを共通(commonFrame)にする。オフなら武装ごとの値(作者「エイムの枠はは共通にしてください」)")]
        public bool useCommonFrame = true;
        [Tooltip("共通の枠の大きさ(画面の中央からの角度。横の半分。度)")]
        public float commonFrame = 16f;
        [Tooltip("全部の武装の枠の大きさに掛ける倍率(1 で武装ごとの値・共通の値のまま)")]
        public float frameMul = 1f;
        [Tooltip("枠の縦の大きさ(横に対する割合)")]
        [Range(0.2f, 1f)] public float frameAspect = 0.6f;
        [Tooltip("ミサイルだけ:ロックオン中の敵は、枠をこの倍率で広くみなす(1 なら他の敵と同じ)。銃・砲には効かない(必ず当たる道にしないため)")]
        public float lockFrameMul = 1.5f;
        [Tooltip("銃・砲の吸い付き:枠の中の敵へ、照準の狙いを最大で「枠の大きさ(度)× これ」だけ寄せる。小さい枠ほど弱い。0 で吸い付かない、1 で枠の端の敵まで寄せる。" +
                 "弾が速さを持つ撃ち方には projectileAssist がオンのときだけ効く")]
        [Range(0f, 1f)] public float assistPart = 0.35f;
        [Tooltip("武装に枠の大きさがないときの値(度)")]
        public float defaultFrame = FallbackFrame;

        [Header("構え(狙って撃つ銃・砲のボタンを押している間と、撃ってから少しの間。ミサイルは入らない。この間は右スティック・マウスで照準を動かす)")]
        [Tooltip("最後に撃ってから、構えを続ける秒数(押すたびに撃つ銃で、撃つ合間に照準が中央へ戻り始めない・右スティックが相手の切り替えにならないように)")]
        public float aimHoldAfterShot = 0.6f;

        [Header("照準(画面の上のカーソル。銃・砲のボタンを押している間と撃ってから少しの間、右スティック・マウスで動かす。カメラは回さない)")]
        [Tooltip("照準が動ける範囲:画面の中央からの横の大きさ(画面の幅に対する割合。0.35 なら中央から左右へ画面の幅の35%まで。楕円)")]
        [Range(0.02f, 0.49f)] public float cursorRangeX = 0.35f;
        [Tooltip("照準が動ける範囲:画面の中央からの縦の大きさ(画面の高さに対する割合)")]
        [Range(0.02f, 0.49f)] public float cursorRangeY = 0.30f;
        [Tooltip("右スティックを倒しきったときの照準の速さ(画面の高さの何倍/秒。横も同じ画素の速さ)")]
        public float cursorStickSpeed = 0.6f;
        [Tooltip("右スティックの倒し具合の効き方(1 でそのまま。大きいほど少し倒したときに遅く、細かく合わせやすい)")]
        public float cursorStickCurve = 1.6f;
        [Tooltip("マウスの1画素あたりに照準が動く量(画面の高さに対する割合)")]
        public float cursorMouseSens = 0.0008f;
        [Tooltip("照準が敵の上にある間、右スティックでの動きを遅くする割合(0 でオフ。0.4 なら 6 割の速さ)。吸い付きの代わりの補助")]
        [Range(0f, 0.9f)] public float cursorSlowOnEnemy = 0.4f;
        [Tooltip("構えていない間に、照準が画面の中央へ戻る速さ(画面の高さの何倍/秒。中央の近くでは速さを落として止まる)")]
        public float cursorReturnSpeed = 0.35f;
        [Tooltip("マウスで範囲の端へ押し付けたとき、カメラが一番速く回る押し付けの速さ(画素/秒)。カメラの回る速さは DuelCamera.edgeYawSpeed")]
        public float cursorMouseEdgeFull = 600f;
        [Tooltip("オンなら、弾が速さを持つ撃ち方(実弾の銃・砲)にも吸い付き(assistPart)を効かせる。オフ(既定)は吸い付かない" +
                 "(照準を先に置いた偏差射撃を、吸い付きが敵の今の位置へ引き戻してしまうため。すぐ届く光線には効く)")]
        public bool projectileAssist = false;

        [Header("弾の速さと反動の照準のずれ(武装ごとの値は DuelWeapon.Defaults() の .Ballistic(速さ, 上へ, 左右へ))")]
        [Tooltip("全部の武装の弾の速さに掛ける倍率(1 で武装ごとの値のまま。0 にすると砲以外はすぐ届く光線)")]
        public float shotSpeedMul = 1f;
        [Tooltip("全部の武装の、撃つたびに照準をずらす角度に掛ける倍率(0 でずれない)")]
        public float kickMul = 1f;
        [Tooltip("銃の弾の球の大きさ(m。機体は約9m)/ 散弾の1粒の大きさ")]
        public float bulletSize = 0.45f, pelletSize = 0.3f;
        [Tooltip("銃の弾の尾(弾の筋)の長さ(秒。速い弾ほど長い筋になる)")]
        public float bulletTrail = 0.07f;

        [Header("反動(連射で照準が広がり、実際のばらつきも広がる。カメラは跳ね上げない・揺らさない。武装ごとの値は DuelWeapon.Defaults() の .Recoil(...))")]
        [Tooltip("全部の武装の広がり(1発ごと・上限)に掛ける倍率。0 で反動なし")]
        public float recoilMul = 1f;
        [Tooltip("最後に撃ってから、広がりが戻り始めるまでの秒数(連射の間は戻らずに広がっていく)")]
        public float recoilDelay = 0.12f;

        [Header("手応えの表示(ヒットマーカー・弾の筋)")]
        [Tooltip("当たったときに照準の真ん中に出す印の秒数(実時間)")]
        public float hitMarkTime = 0.22f;
        [Tooltip("当たった弾の筋が残る秒数(細くなって消える)")]
        public float tracerLife = 0.08f;
        [Tooltip("外れた弾の筋が残る秒数(当たったときより長く見せる)")]
        public float missTracerLife = 0.2f;
        [Tooltip("外れた弾の筋の太さ(m。機体は約9m)")]
        public float missTracerWidth = 0.35f;

        [Header("重さと所持(付けている武装 + 所持品の合計)")]
        [Tooltip("持てる重さの上限。拾って持つ・付けると超えるなら、持てない・付けられない")]
        public float carryMax = 100f;
        [Tooltip("この重さまでは遅くならない(最初の素の剣だけなら軽いまま)")]
        public float freeWeight = 15f;
        [Tooltip("それを超えた重さ1あたりに、ENを使う移動(ブースト移動・回避の距離・飛ぶときの上昇)の速さが下がる割合。歩き・走りには効かない")]
        public float slowPerWeight = 0.004f;
        [Tooltip("どれだけ重くても、この倍率より遅くはしない")]
        [Range(0.1f, 1f)] public float minMobility = 0.5f;
        [Tooltip("所持品(付けていない武装)の重さを何倍で数えるか(武装ハントは0.5。1 なら付けているのと同じ)")]
        [Range(0f, 1f)] public float stockWeightRate = 1f;

        [Header("共通の弾(実弾の銃・砲・ミサイルが使う。エネルギー武器は弾の代わりにENを使う)")]
        public float ammoMax = 300f;
        [Tooltip("弧の始め(最初から・F1)の弾")]
        public float ammoStart = 30f;

        [Header("分解(落ちている武装・所持品・装備を壊して、弾か回復にする)")]
        [Tooltip("弾にする量 = 基本 + レア度(0〜2)× これ + 重さ × これ")]
        public float breakAmmoBase = 15f, breakAmmoPerRarity = 15f, breakAmmoPerWeight = 1.5f;
        [Tooltip("回復にする量(HP) = 基本 + レア度 × これ + 重さ × これ")]
        public float breakHealBase = 8f, breakHealPerRarity = 8f, breakHealPerWeight = 0.8f;

        /// <summary>今のステージの決まり(ステージがなければ既定の値)。性能表の「枠」などで使う。</summary>
        public static DuelArmsRules Current =>
            DuelGame.I != null && DuelGame.I.stage != null && DuelGame.I.stage.armsRules != null ? DuelGame.I.stage.armsRules : Fallback;
        static readonly DuelArmsRules Fallback = new();

        /// <summary>
        /// 武装 w の照準の枠の横の半分の角度(度。frameMul を掛け、0.5〜70 に収める)。w に枠がなければ parent(両手の銃・砲の L1 なら主の撃ち方)の枠、
        /// それもなければ defaultFrame。
        /// </summary>
        public float FrameDeg(DuelWeapon w, DuelWeapon parent)
        {
            float deg = useCommonFrame ? commonFrame
                : w != null && w.aimFrame > 0f ? w.aimFrame : parent != null && parent.aimFrame > 0f ? parent.aimFrame : defaultFrame;
            return Mathf.Clamp(deg * frameMul, 0.5f, 70f);
        }

        public int BreakAmmo(DuelWeapon w) => Mathf.Max(0, Mathf.RoundToInt(breakAmmoBase + breakAmmoPerRarity * w.rarity + breakAmmoPerWeight * w.weight));
        public int BreakHeal(DuelWeapon w) => Mathf.Max(0, Mathf.RoundToInt(breakHealBase + breakHealPerRarity * w.rarity + breakHealPerWeight * w.weight));

        /// <summary>重さによる推力の倍率(ENを使う移動:ブースト移動・回避の距離・飛ぶときの上昇。歩き・走りには掛けない)。</summary>
        public float MobilityAt(float weight) => Mathf.Clamp(1f - Mathf.Max(0f, weight - freeWeight) * slowPerWeight, minMobility, 1f);
    }

    /// <summary>落ちる武装のレア度の割合(この高さ(m)から上で使う)。上ほど強い武装が落ちる。</summary>
    [System.Serializable]
    public class DuelDropBand
    {
        [Tooltip("この高さ(m)から上で使う")] public float fromY;
        [Tooltip("コモン・レア・エピックの重み")] public float common = 1f, rare, epic;

        public DuelDropBand() { }
        public DuelDropBand(float fromY, float common, float rare, float epic)
        {
            this.fromY = fromY;
            this.common = common;
            this.rare = rare;
            this.epic = epic;
        }

        /// <summary>重みに応じてレア度を1つ選ぶ。</summary>
        public int RollRarity()
        {
            float sum = Mathf.Max(0f, common) + Mathf.Max(0f, rare) + Mathf.Max(0f, epic);
            if (sum <= 0f) return 0;
            float r = Random.value * sum;
            if (r < common) return 0;
            return r < common + rare ? 1 : 2;
        }
    }
}
