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
        public static readonly DuelWeapon BaseSword = new() { name = "素の剣", kind = DuelKind.Sword, rarity = 0, feel = "最初から持っている剣" };

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
            return (a.charge > 0f ? $"溜め{a.charge:0.0}秒 " : "") + body + (a.pierce ? " 貫通" : "") + (a.charge > 0f ? "" : $" 間隔{a.interval:0.0}秒");
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
                    list.Add(("弾倉", w.magazine <= 0 ? 9999f : w.magazine, w.magazine <= 0 ? "無限" : $"{w.magazine}", true));
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
                    AddAlt(list, w);
                    break;
                }
            }
            return list;
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
            Sword("鋼の剣", 0, "素の剣と同じ振り。重く、少し長い", DuelMoveSet.Standard(), 1.3f, 1.1f, 1.2f, 1.1f),
            Sword("軽剣", 0, "浅く速い3段の連撃。一振りは軽い", DuelMoveSet.Quick(), 1.1f, 0.95f, 1f, 1f),
            Sword("大剣", 1, "大きく振りかぶって叩き斬る。遅いが重く広い", DuelMoveSet.Great(), 1.25f, 1.3f, 1.2f, 1.2f),
            Sword("高周波ブレード", 1, "速く鋭い4段の連撃と、踏み込む突き", DuelMoveSet.Blade(), 1.35f, 1.15f, 1.2f, 1f),
            Sword("斬艦刀", 2, "長大な刃を体ごと回して、周りごと薙ぎ払う", DuelMoveSet.Giant(), 1.6f, 1.7f, 1.5f, 1.5f),
            // 盾(手。付けた手のボタンを押している間ガード)
            Shield("鉄板の盾", 0, "軽く、構えたまま動きやすいが、すぐ削れる", 80f, 1.2f, 0.15f, 0.55f, 1f, 0.9f, new Vector3(0.3f, 2.6f, 2.2f)),
            Shield("重装甲の盾", 0, "よく持つが重い。構えるとほとんど動けない", 140f, 0.9f, 0.08f, 0.3f, 0.92f, 0.8f, new Vector3(0.45f, 3.6f, 2.8f)),
            Shield("複合装甲の盾", 1, "よく持ち、通す傷も少ない", 150f, 0.8f, 0.05f, 0.45f, 0.97f, 0.75f, new Vector3(0.4f, 3.2f, 2.6f)),
            Shield("小型の盾", 1, "受けてもすぐ動ける。構えたまま走れる", 90f, 1f, 0.1f, 0.75f, 1f, 0.5f, new Vector3(0.3f, 2f, 1.8f)),
            Shield("エネルギーシールド", 2, "ほとんど削れず、傷も通さない", 220f, 0.6f, 0f, 0.6f, 1f, 0.6f, new Vector3(0.2f, 3.8f, 3.2f)),
            // 手の銃(手。付けた手のボタンで撃つ。肩の銃より一発ずつ狙って撃つ性格)
            HandGun("ハンドガン", 0, "押すたびに1発。弱いが狙いが正確", false, 4f, 0.2f, 55f, 0.4f, 1, 12, 1.3f, 1.5f, 3f, new Vector3(0.4f, 0.7f, 2.6f)),
            HandGun("サブマシンガン", 0, "押している間ばらまく。近い敵向け", true, 1.4f, 0.07f, 35f, 4f, 1, 30, 1.8f, 0.3f, 2f, new Vector3(0.45f, 0.8f, 3f)),
            HandGun("ライフル", 1, "一発が重く、遠くまで届く", false, 11f, 0.5f, 90f, 0.2f, 1, 6, 2f, 4f, 6f, new Vector3(0.45f, 0.8f, 4.6f)),
            HandGun("散弾銃", 1, "近くで撃てば怯みを一気にため、押し返す", false, 2.4f, 0.75f, 28f, 8f, 8, 4, 2.2f, 0.9f, 10f, new Vector3(0.6f, 0.9f, 3.6f)),
            HandGun("ビームライフル", 2, "弾切れなし。一発が重く遠くまで届く", false, 16f, 0.45f, 110f, 0f, 1, 0, 0f, 5f, 8f, new Vector3(0.5f, 0.9f, 5f)),
            // 背中
            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f)),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f)),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f)),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f)),
            // 肩の銃(右肩・左肩のどちらにも)
            Gun("肩部機関銃", 0, "弱いが、離れた所から削れる", true, 1.2f, 0.12f, 55f, 2.5f, 1, 24, 2.2f, 0.4f, new Vector3(0.7f, 1.6f, 0.7f)),
            Gun("重機関銃", 1, "一発が重く、よく削れる", true, 2f, 0.09f, 75f, 2f, 1, 45, 2.5f, 0.6f, new Vector3(1.2f, 2f, 1.2f)),
            Gun("肩部散弾銃", 1, "近いほど多く当たる9粒の散弾", false, 2.2f, 0.6f, 35f, 7f, 9, 6, 2f, 0.8f, new Vector3(1f, 1.5f, 1f)),
            Gun("ビームガトリング", 2, "弾切れなし。撃ち続けて溶かす", true, 1.8f, 0.06f, 95f, 1.5f, 1, 0, 0f, 0.5f, new Vector3(1.1f, 2.4f, 1.1f)),
            // 肩の砲・ミサイル(右肩・左肩のどちらにも)
            Heavy("速射砲", 0, "爆発は小さいが、すぐ次が撃てる", true, 8f, 1, 5f, 1.4f, 70f, 6f, 8f, PrimitiveType.Cylinder, new Vector3(0.6f, 1.4f, 0.6f)),
            Heavy("重榴弾砲", 1, "装填は遅いが、大きな爆発で群れをまとめて削る", false, 22f, 1, 12f, 3.5f, 80f, 14f, 22f, PrimitiveType.Cylinder, new Vector3(1.3f, 2f, 1.3f)),
            Heavy("ミサイルポッド", 1, "4発が狙える敵を追いかける", false, 6f, 4, 4f, 3f, 100f, 4f, 6f, PrimitiveType.Cube, new Vector3(1.2f, 1f, 1.4f)),
            Heavy("多弾頭ミサイル", 2, "10発が周りの敵へ散って追いかける", false, 10f, 10, 5f, 4f, 110f, 6f, 8f, PrimitiveType.Cube, new Vector3(1.8f, 1.4f, 1.8f)),
            Heavy("超重砲", 2, "装填はとても遅いが、群れごと吹き飛ばす", false, 40f, 1, 20f, 6f, 90f, 30f, 30f, PrimitiveType.Cylinder, new Vector3(1.9f, 3.2f, 1.9f)),

            // 両手の近接(右手と左手の両方を使う。R1 = 連撃 / L1 = 別の技 / △+R1・L1 = 強攻撃。盾がないのでガードはできない)
            // 同じレア度の片手の剣より、一振りの威力・届く範囲・怯ませがはっきり上。振りはやや遅く、ENも多めに使う
            TwoMelee("両手剣", 0, "重い3段の連撃。L1 で大きく踏み込む突進斬り", "突進斬り", DuelMoveSet.TwoHandSword(), 1.15f, 1.4f, 1.2f, 1.2f, 1.5f, 2f),
            TwoMelee("長槍", 0, "遠くまで届く速い突き。L1 で周りをまとめて薙ぎ払う", "薙ぎ払い", DuelMoveSet.Spear(), 1.1f, 1.7f, 1.1f, 1.1f, 0.7f, 4f),
            TwoMelee("薙刀", 1, "広く流れる4段の連撃。L1 で長い突き → 斬り上げ", "突き → 斬り上げ", DuelMoveSet.Glaive(), 1.7f, 1.55f, 1.3f, 1.2f, 0.8f, 3.5f),
            TwoMelee("戦鎚", 1, "遅いが一振りで敵を崩す。L1 で周りごと叩きつける", "叩きつけ", DuelMoveSet.Hammer(), 1.5f, 1.3f, 1.8f, 1.6f, 2.6f, 2.5f),
            TwoMelee("斬機大太刀", 2, "長大な刃を両手で回し、周りごと斬り払う。L1 で大回転", "大回転 → 回転斬り上げ", DuelMoveSet.GreatTachi(), 2.2f, 2f, 1.6f, 1.6f, 1.8f, 2.5f),
            TwoMelee("突撃槍", 2, "とても遠くまで届く突き。L1 で敵まで一気に突撃する", "突撃", DuelMoveSet.Lance(), 2f, 2.1f, 1.6f, 1.8f, 0.9f, 5f),

            // 両手の銃・砲(右手と左手の両方を使う。R1 = 主の撃ち方 / L1 = 別の撃ち方。溜め撃ちは L1 を押して溜め、離して撃つ)
            // 同じレア度の手の銃・肩の砲より強い
            TwoGun("大型ライフル", 0, "重い一発。L1 を溜めて離すと、敵を貫く強い一発", "1発ずつ", "溜め撃ち(貫通)",
                   false, 14f, 0.45f, 100f, 0.2f, 1, 8, 2.2f, 6f, 8f, new Vector3(0.55f, 0.9f, 6f),
                   AltShot(40f, 0.8f, 120f, 0f, 1, 16f, 16f, charge: 1f, pierce: true, beamWidth: 0.5f)),
            TwoGun("ガトリング砲", 0, "押している間ばらまき続ける。L1 で榴弾", "押している間連射", "榴弾",
                   true, 1.6f, 0.05f, 60f, 3f, 1, 90, 3f, 0.35f, 2f, new Vector3(0.9f, 0.9f, 4.5f),
                   AltBlast(16f, 1, 6f, 2.5f, 70f, 8f, 12f)),
            TwoCannon("バズーカ", 1, "砲弾が大きく爆ぜる。L1 を溜めて離すと大爆発", "砲弾", "溜めて大爆発",
                      false, 30f, 1, 9f, 1.8f, 90f, 16f, 22f, PrimitiveType.Cylinder, new Vector3(1f, 2.8f, 1f),
                      AltBlast(60f, 1, 15f, 3f, 90f, 30f, 30f, charge: 1.2f)),
            TwoGun("ショットキャノン", 1, "近くの敵を12粒で押し返す。L1 で遠くまで届く重いスラグ弾", "散弾", "スラグ弾",
                   false, 2.8f, 0.8f, 32f, 9f, 12, 5, 2.4f, 1.2f, 14f, new Vector3(0.8f, 1f, 4.4f),
                   AltShot(34f, 1.2f, 70f, 0f, 1, 20f, 26f)),
            TwoGun("レーザー砲", 2, "押している間、敵を貫く光線を撃ち続ける。L1 を溜めて離すと極太の光線", "押している間照射(貫通)", "溜め撃ち(極太・貫通)",
                   true, 3.8f, 0.08f, 110f, 0f, 1, 0, 0f, 0.6f, 2f, new Vector3(0.8f, 1.1f, 6.5f),
                   AltShot(90f, 1.5f, 130f, 0f, 1, 40f, 30f, charge: 1.5f, pierce: true, beamWidth: 3f), pierce: true, beamWidth: 0.3f),
            TwoCannon("多連装ロケット", 2, "8発のロケットが狙える敵を追う。L1 を溜めて離すと20発の一斉発射", "8発", "溜めて一斉発射",
                      false, 9f, 8, 5f, 2.5f, 110f, 5f, 8f, PrimitiveType.Cube, new Vector3(1.6f, 1.4f, 3.6f),
                      AltBlast(12f, 20, 6f, 5f, 120f, 6f, 10f, charge: 1.2f)),
        };
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
