using System.Collections.Generic;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 武装を付ける所(5つ)。右手(R1)/ 左手(L1)/ 背中 / 右肩(R2)/ 左肩(L2)。
    /// 両手には剣・盾・手の銃のどれでも、両肩には肩の銃・砲・ミサイルのどれでも付けられる(左右の役割は決めない)。
    /// </summary>
    public enum DuelSlot { RightHand, LeftHand, Back, RightShoulder, LeftShoulder }

    /// <summary>武装の種類。どの所に付けられるかが決まる(手:剣・盾・手の銃 / 背中:スラスター / 肩:銃・砲とミサイル)。</summary>
    public enum DuelKind { Sword, Shield, HandGun, Back, ShoulderGun, ShoulderHeavy }

    /// <summary>
    /// ステージで敵が落とす武装1つの中身(弧ひとつ分の試作)。耐久・重量・品質はない。強さはレア度(0=コモン / 1=レア / 2=エピック)で分ける。
    /// 使う項目は種類で違う(剣:技の表と倍率 / 盾:ガード / 背中:倍率 / 銃:射撃 / 砲・ミサイル:爆発)。数値・名前は仮置き。
    /// 一覧はステージ(DuelStage.arsenal)のインスペクターで触れる。初期値は Defaults()。
    /// </summary>
    [System.Serializable]
    public class DuelWeapon
    {
        public string name = "武装";
        public DuelKind kind;
        [Range(0, 2)] public int rarity;
        [Tooltip("拾うときに見せる一言")]
        public string feel = "";

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
        [Tooltip("1発ごとの敵の怯みゲージへの量")] public float stagger = 0.5f;
        [Tooltip("銃:ばらつき(度)")] public float spread;
        [Tooltip("銃:1回で出る粒の数(2以上なら散弾)")] public int pellets = 1;
        [Tooltip("銃:弾倉(0なら弾倉なし)")] public int magazine;
        [Tooltip("銃:撃ち切ったときのリロード(秒)")] public float reload = 2f;
        [Tooltip("砲・ミサイル:爆発の半径(m)")] public float radius = 5f;
        [Tooltip("砲・ミサイル:1回で撃つ数(2以上ならミサイル。狙える敵に割り振って追いかける)")] public int salvo = 1;
        [Tooltip("当たったときに押す速さ(砲・ミサイルは怯んだときだけ)")] public float knock = 10f;

        [Header("見た目(仮の形)")]
        public PrimitiveType shape = PrimitiveType.Cylinder;
        public Vector3 scale = new(0.8f, 1.6f, 0.8f);
        public Vector3 euler = new(90f, 0f, 0f);

        public static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        public static readonly Color[] RarityColors = { new(0.75f, 0.77f, 0.8f), new(0.3f, 0.6f, 1f), new(1f, 0.75f, 0.15f) };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, 2)];
        public Color Color => RarityColors[Mathf.Clamp(rarity, 0, 2)];
        public bool IsMissile => salvo > 1;
        public bool IsHand => kind is DuelKind.Sword or DuelKind.Shield or DuelKind.HandGun;
        /// <summary>撃つ武装(手の銃・肩の銃・砲とミサイル)。</summary>
        public bool IsShooter => kind is DuelKind.HandGun or DuelKind.ShoulderGun or DuelKind.ShoulderHeavy;
        /// <summary>弾倉・リロードのある銃(手・肩)。</summary>
        public bool IsGun => kind is DuelKind.HandGun or DuelKind.ShoulderGun;

        /// <summary>最初から右手に持っている素の剣(落ちる一覧には入らない。手から外すと床に置かれ、拾い直せる)。</summary>
        public static readonly DuelWeapon BaseSword = new() { name = "素の剣", kind = DuelKind.Sword, rarity = 0, feel = "最初から持っている剣" };

        // ───────── 剣の技(表が空なら素の剣の表) ─────────

        public DuelSwing[] LightMoves => moves != null && moves.light != null && moves.light.Length > 0 ? moves.light : DuelMoveSet.Base.light;
        public DuelSwing[] HeavyMoves => moves != null && moves.heavy != null && moves.heavy.Length > 0 ? moves.heavy : DuelMoveSet.Base.heavy;
        public DuelSwing DodgeMove => moves != null && moves.dodgeSlash.windup > 0f ? moves.dodgeSlash : DuelMoveSet.Base.dodgeSlash;

        // ───────── 付ける所 ─────────

        static readonly DuelSlot[] HandSlots = { DuelSlot.RightHand, DuelSlot.LeftHand };
        static readonly DuelSlot[] BackSlots = { DuelSlot.Back };
        static readonly DuelSlot[] ShoulderSlots = { DuelSlot.RightShoulder, DuelSlot.LeftShoulder };

        /// <summary>この種類を付けられる所(手なら右手・左手、肩なら右肩・左肩、背中は背中だけ)。</summary>
        public static DuelSlot[] SlotsFor(DuelKind k) => k switch
        {
            DuelKind.Sword or DuelKind.Shield or DuelKind.HandGun => HandSlots,
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
            _ => "砲・ミサイル",
        };

        // ───────── 比べる(拾うとき) ─────────

        /// <summary>比べる項目:名前・値・表示の書式・大きいほど良いか。w が null(空き)なら何も出さない。</summary>
        public static List<(string name, float value, string text, bool higherBetter)> Stats(DuelWeapon w)
        {
            var list = new List<(string, float, string, bool)>();
            if (w == null) return list;
            switch (w.kind)
            {
                case DuelKind.Sword:
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
                {
                    float shot = w.damage * Mathf.Max(1, w.pellets);
                    float dps = shot / Mathf.Max(0.01f, w.interval);
                    list.Add(("毎秒", dps, $"{dps:0}", true));
                    list.Add(("1発", shot, w.pellets > 1 ? $"{w.damage:0.#}×{w.pellets}粒" : $"{w.damage:0.#}", true));
                    list.Add(("怯ませ", w.stagger * Mathf.Max(1, w.pellets), $"{w.stagger * Mathf.Max(1, w.pellets):0.#}", true));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    list.Add(("弾倉", w.magazine <= 0 ? 9999f : w.magazine, w.magazine <= 0 ? "無限" : $"{w.magazine}", true));
                    break;
                }
                default:
                {
                    float total = w.damage * Mathf.Max(1, w.salvo);
                    list.Add(("威力", total, w.salvo > 1 ? $"{w.damage:0.#}×{w.salvo}発" : $"{w.damage:0.#}", true));
                    list.Add(("爆風", w.radius, $"{w.radius:0.#}m", true));
                    list.Add(("装填", w.interval, $"{w.interval:0.0}秒", false));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    break;
                }
            }
            return list;
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
            var b = cur != null && cur.kind == next.kind ? Stats(cur) : null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Count; i++)
            {
                if (sb.Length > 0) sb.Append("   ");
                if (b == null || b.Count != a.Count)
                {
                    sb.Append($"{a[i].name} {a[i].text}");
                    continue;
                }
                float ratio = a[i].value / Mathf.Max(0.0001f, b[i].value);
                string arrow = Arrow(ratio);
                bool better = (ratio > 1f) == a[i].higherBetter;
                string body = $"{a[i].name} {b[i].text}→{a[i].text}";
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
            HandGun("散弾銃", 1, "近くで撃てば大きく怯ませ、押し返す", false, 2.4f, 0.75f, 28f, 8f, 8, 4, 2.2f, 0.9f, 10f, new Vector3(0.6f, 0.9f, 3.6f)),
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
            Heavy("重榴弾砲", 1, "装填は遅いが、大きな爆発で群れを崩す", false, 22f, 1, 12f, 3.5f, 80f, 14f, 22f, PrimitiveType.Cylinder, new Vector3(1.3f, 2f, 1.3f)),
            Heavy("ミサイルポッド", 1, "4発が狙える敵を追いかける", false, 6f, 4, 4f, 3f, 100f, 4f, 6f, PrimitiveType.Cube, new Vector3(1.2f, 1f, 1.4f)),
            Heavy("多弾頭ミサイル", 2, "10発が周りの敵へ散って追いかける", false, 10f, 10, 5f, 4f, 110f, 6f, 8f, PrimitiveType.Cube, new Vector3(1.8f, 1.4f, 1.8f)),
            Heavy("超重砲", 2, "装填はとても遅いが、群れごと吹き飛ばす", false, 40f, 1, 20f, 6f, 90f, 30f, 30f, PrimitiveType.Cylinder, new Vector3(1.9f, 3.2f, 1.9f)),
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
