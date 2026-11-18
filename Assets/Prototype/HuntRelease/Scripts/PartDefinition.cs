using UnityEngine;

namespace HuntProto
{
    public enum PartType { Gatling, Thruster, Cannon, Blade }
    public enum MountSlot { RightArm, LeftArm, Back, Shoulder }
    /// <summary>
    /// 剣の振り方。Slash = 標準の薙ぎ払い(3連撃) / Slam = 大きく振りかぶって叩きつけ、当たった所のまわりごと吹き飛ばす(2連撃) /
    /// Flurry = 素早い連撃(5連撃、1撃は軽い)
    /// </summary>
    public enum SwingStyle { Slash, Slam, Flurry }
    /// <summary>自機の武装の置き場所。R1=右手 / L1=左手 / R2=右肩 / L2=左肩。</summary>
    public enum Hardpoint { RightHand, LeftHand, RightShoulder, LeftShoulder, Back }

    /// <summary>
    /// 剥ぎ取れる部位の定義。敵に付いている間は敵の能力、剥いだ後は自機の武装になる。
    /// 見た目は visualPrefab に自作モデルを入れると差し替わる(未設定なら仮の形)。
    /// </summary>
    [CreateAssetMenu(menuName = "Prototype/HuntRelease/Part Definition", fileName = "Part")]
    public class PartDefinition : ScriptableObject
    {
        public PartType type;
        public string displayName = "部位";
        public MountSlot slot;

        [Header("見た目(visualPrefab 未設定なら仮の形)")]
        public GameObject visualPrefab;
        public PrimitiveType placeholderShape = PrimitiveType.Cube;
        public Vector3 placeholderScale = Vector3.one;
        public Vector3 placeholderEuler = Vector3.zero;
        public Color color = Color.gray;

        [Header("敵に付いている時")]
        [Tooltip("部位破壊(剥ぎ取り方式なら剥がす)に必要なダメージ")]
        public float partHP = 70f;

        [Header("自機の武装としての性能")]
        [Tooltip("ガトリング:1発のダメージ / キャノン:爆発ダメージ")]
        public float damage = 10f;
        [Tooltip("ガトリング:発射間隔(秒) / キャノン:再使用までの秒数")]
        public float interval = 0.1f;
        [Tooltip("ガトリング・キャノンの射程(m)")]
        public float range = 60f;
        [Tooltip("キャノン:爆発半径(m)")]
        public float radius = 10f;
        [Tooltip("性能の倍率(品質で変わる)。スラスター:ブースト中の速さ・回避距離(上昇はその半分の効き) / 刃腕:剣の威力(リーチと剣の長さはその半分の効き)")]
        public float multiplier = 2f;
        [Tooltip("使える回数(刃腕:振った回数 / ガトリング:弾数 / キャノン:発射回数)。使い切ると外れる。0なら外れない")]
        public int uses = 0;

        [Header("パラメータ制(B案)でだけ使う")]
        [Tooltip("重さ。付けている武装の合計で自機の機動力(走り・ブースト・回避・上昇)が下がる")]
        public float weight = 10f;
        [Tooltip("ガトリング:1発ごとに広がるばらつき(度)。撃ち続けるほど外れやすくなる")]
        public float recoil = 0.5f;
        [Tooltip("スラスター:ブースト中のエネルギー消費の倍率(小さいほど燃費が良い)")]
        public float drain = 1f;
        [Tooltip("キャノン:1回で撃つ発数。2以上ならミサイル(小さな爆発が、狙った敵を追いかけて何発も飛ぶ)")]
        public int salvo = 1;
        [Tooltip("剣:振り方(薙ぎ払い / 叩きつけ / 連撃)")]
        public SwingStyle swing = SwingStyle.Slash;
        [Tooltip("手の銃:1回で出る弾の数。2以上なら散弾(pelletSpread 度の範囲に広がる。近いほど多く当たる)")]
        public int pellets = 1;
        [Tooltip("手の銃:散弾の広がり(度)")]
        public float pelletSpread = 0f;
        [Tooltip("手の銃:押すたびに1発だけ撃つ(押しっぱなしでは連射しない)")]
        public bool semiAuto = false;
        [Tooltip("手の実弾銃:弾倉の弾数。撃ち切ると reloadTime 秒リロードして撃てない(0なら弾倉なし)")]
        public int magazine = 0;
        [Tooltip("手の実弾銃:弾倉を撃ち切ったときのリロード時間(秒)")]
        public float reloadTime = 0f;
        [Tooltip("エネルギー武器:1発ごとに使うEN(0なら実弾)。リロードはないが、EN切れの間は撃てない")]
        public float energyCost = 0f;
        public bool IsEnergy => energyCost > 0f;
        [Tooltip("キャノン:撃つとき、この秒数その場で構えて動けない(構え硬直。0ならなし)。弾は構えの途中で出る")]
        public float stance = 0f;
        [Tooltip("レア度(0=コモン / 1=レア / 2=エピック)。レアなほど出にくく、強い。色でも分かる")]
        [Range(0, 2)] public int rarity = 0;
        [Tooltip("操作感の一言(拾うとき・装備メニューで見せる)")]
        public string feel = "";
        [Tooltip("同じ種類のほかの武装(標準の武装にだけ書く)。パラメータ制では、自分とこの中から同じ確率で1つ落とす")]
        public PartDefinition[] variants;

        // ───────── 品質(同じ武装でも強さに差をつける) ─────────

        [HideInInspector] public int grade = 1;
        public static readonly string[] GradeNames = { "粗悪", "標準", "上物", "逸品" };
        public static readonly Color[] GradeColors =
        {
            new(0.42f, 0.36f, 0.3f), new(0.75f, 0.77f, 0.8f), new(0.25f, 0.55f, 1f), new(1f, 0.75f, 0.15f),
        };
        /// <summary>武装そのものの色(現行は品質、B案はレア度で決まる。形で種類、色で品質・レア度を見分ける)。</summary>
        public Color GradeColor => GradeColors[Mathf.Clamp(grade, 0, GradeColors.Length - 1)];
        static readonly float[] Power = { 0.7f, 1f, 1.35f, 1.8f };
        static readonly float[] Life = { 0.6f, 1f, 1.3f, 1.6f };

        /// <summary>品質を付けた複製を作る(標準ならそのまま)。威力と使える回数が変わる。</summary>
        public PartDefinition WithGrade(int g)
        {
            if (g == 1) return this;
            var c = Instantiate(this);
            c.name = name;
            c.grade = g;
            c.displayName = GradeNames[g] + displayName;
            c.damage *= Power[g];
            c.multiplier = 1f + (multiplier - 1f) * Power[g];
            if (uses > 0) c.uses = Mathf.Max(1, Mathf.RoundToInt(uses * Life[g]));
            return c;
        }

        /// <summary>品質の抽選。weights は 粗悪/標準/上物/逸品 の重み。</summary>
        public static int RollGrade(params float[] weights)
        {
            float sum = 0f;
            foreach (var w in weights) sum += w;
            float r = Random.value * sum;
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i]) return i;
                r -= weights[i];
            }
            return 1;
        }

        // ───────── パラメータ制(B案) ─────────
        // 種類ごとに性格の違う武装を一覧(variants)で用意し、そこから選んで落とす。今の装備との差で比べる

        public enum Stat { Damage, FireRate, Recoil, Range, Weight, Radius, Reload, Thrust, Drain, BladePower, Durability, Magazine, MagReload }

        /// <summary>種類ごとのパラメータ。</summary>
        public static Stat[] StatsOf(PartType t) => t switch
        {
            PartType.Gatling => new[] { Stat.Damage, Stat.FireRate, Stat.Recoil, Stat.Range, Stat.Magazine, Stat.MagReload, Stat.Weight, Stat.Durability },
            PartType.Cannon => new[] { Stat.Damage, Stat.Radius, Stat.Reload, Stat.Range, Stat.Weight, Stat.Durability },
            PartType.Thruster => new[] { Stat.Thrust, Stat.Drain, Stat.Weight, Stat.Durability },
            _ => new[] { Stat.BladePower, Stat.Weight, Stat.Durability },
        };

        /// <summary>値が大きいほど良いパラメータか(反動・重量・装填時間・燃費は小さいほど良い)。</summary>
        public static bool HigherIsBetter(Stat s) => s is not (Stat.Recoil or Stat.Weight or Stat.Reload or Stat.Drain or Stat.MagReload);

        public static string StatName(Stat s) => s switch
        {
            Stat.Damage => "威力",
            Stat.FireRate => "連射",
            Stat.Recoil => "反動",
            Stat.Range => "射程",
            Stat.Weight => "重量",
            Stat.Radius => "爆風",
            Stat.Reload => "装填",
            Stat.Thrust => "推力",
            Stat.Drain => "燃費",
            Stat.Magazine => "弾倉",
            Stat.MagReload => "リロード",
            Stat.Durability => "耐久",
            _ => "剣威力",
        };

        /// <summary>レーダーチャートの軸の名前。外側ほど良い向きなので、小さいほど良いものは言い換える。</summary>
        public static string RadarName(Stat s) => s switch
        {
            Stat.Recoil => "安定",
            Stat.Weight => "軽さ",
            Stat.Reload => "装填",
            _ => StatName(s),
        };

        /// <summary>
        /// レーダーチャート用:標準の武装と比べた良さ(0〜1。0.5が標準と同じ、1が良い向きに swing 倍、0が悪い向きに swing 倍)。
        /// </summary>
        /// <param name="curUses">耐久を今の残り回数で測るとき、その回数(負なら最大値で測る)。</param>
        public float Rating(Stat s, float swing, int curUses = -1)
        {
            if (source == null) return 0.5f;
            float f = s is Stat.Thrust or Stat.BladePower
                ? (multiplier - 1f) / Mathf.Max(0.0001f, source.multiplier - 1f)   // 上乗せ分に掛けているので、上乗せ分どうしで比べる
                : StatValue(s, curUses) / Mathf.Max(0.0001f, source.StatValue(s));
            float t = Mathf.Log(Mathf.Max(0.0001f, f)) / Mathf.Log(Mathf.Max(1.01f, swing));
            if (!HigherIsBetter(s)) t = -t;
            return Mathf.Clamp01(0.5f + 0.5f * t);
        }

        /// <summary>自機のENの上限(エネルギー武器の「弾倉」を数えるため)。</summary>
        static float EnergyMax => ProtoPlayer.I != null ? ProtoPlayer.I.maxEnergy : 150f;

        /// <summary>比べるための値。耐久は curUses(今の残り)が 0 以上ならそれを使う。</summary>
        public float StatValue(Stat s, int curUses) => s == Stat.Durability && curUses >= 0 ? curUses : StatValue(s);

        /// <summary>比べるための値(大きい・小さいの向きは StatName のとおり)。</summary>
        public float StatValue(Stat s) => s switch
        {
            Stat.Damage => damage * Mathf.Max(1, salvo) * Mathf.Max(1, pellets),   // ミサイル・散弾は1回に撃つ全部の合計
            Stat.FireRate => 1f / Mathf.Max(0.01f, interval),
            Stat.Recoil => recoil,
            Stat.Range => range,
            Stat.Weight => weight,
            Stat.Radius => radius,
            Stat.Reload => interval,
            Stat.Thrust => multiplier,
            Stat.Drain => drain,
            Stat.Durability => uses,
            // エネルギー武器の弾倉は「満タンのENで撃てる数」、リロードは0(なし)
            Stat.Magazine => IsEnergy ? EnergyMax / energyCost : magazine,
            Stat.MagReload => IsEnergy ? 0f : reloadTime,
            _ => multiplier,
        };

        public string StatText(Stat s) => s switch
        {
            Stat.FireRate => semiAuto ? $"単発 最速{StatValue(s):0.#}発/秒" : $"連射 {StatValue(s):0.#}発/秒",
            Stat.Range => $"射程 {range:0}m",
            Stat.Radius => $"爆風 {radius:0.#}m",
            Stat.Reload => $"装填 {interval:0.0}秒",
            Stat.Thrust => $"推力 ×{multiplier:0.00}",
            Stat.Drain => $"燃費 ×{drain:0.00}",
            Stat.BladePower => $"剣威力 ×{multiplier:0.00}",
            Stat.Weight => $"重量 {weight:0}",
            Stat.Recoil => $"反動 {recoil:0.00}",
            Stat.Durability => $"耐久 {uses}",
            Stat.Magazine => IsEnergy ? $"EN {energyCost:0.#}/発" : $"弾倉 {magazine}",
            Stat.MagReload => IsEnergy ? "リロードなし" : $"リロード {reloadTime:0.0}秒",
            _ => salvo > 1 ? $"威力 {damage:0.#}×{salvo}発" : pellets > 1 ? $"威力 {damage:0.#}×{pellets}粒" : $"威力 {damage:0.#}",
        };

        /// <summary>パラメータ制(B案)で作った個体か。</summary>
        public bool HasStats => swung != null;
        /// <summary>比べるパラメータ(種類ごとの全部)。</summary>
        [System.NonSerialized] public Stat[] swung;
        /// <summary>比べる基準(その種類の標準の武装。レーダーチャートの真ん中の輪)。</summary>
        [System.NonSerialized] public PartDefinition source;
        /// <summary>総合値(0〜100、50が標準と同じ)。比べる相手を決めるのに使う。</summary>
        [HideInInspector] public int score = 50;

        public static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        /// <summary>レア度ごとの出やすさ(1つあたり)。コモン1つはエピック1つの6倍出る。</summary>
        public static readonly float[] RarityWeights = { 6f, 3f, 1f };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, RarityNames.Length - 1)];

        /// <summary>
        /// パラメータ制(B案):自分(標準)と variants の中から、レア度に応じた確率で1つ選び、比べる基準(自分)を付けた複製を返す。
        /// 性能は一覧に書いたとおり(ランダムには振らない)。ratingScale はレーダーチャートの目盛りの幅。
        /// </summary>
        public PartDefinition PickVariant(float ratingScale)
        {
            var list = new System.Collections.Generic.List<PartDefinition> { this };
            if (variants != null) foreach (var v in variants) if (v != null) list.Add(v);
            float sum = 0f;
            foreach (var v in list) sum += RarityWeights[Mathf.Clamp(v.rarity, 0, 2)];
            float r = Random.value * sum;
            var pick = list[list.Count - 1];
            foreach (var v in list)
            {
                r -= RarityWeights[Mathf.Clamp(v.rarity, 0, 2)];
                if (r < 0f) { pick = v; break; }
            }
            var c = Instantiate(pick);
            c.name = pick.name;
            c.source = this;
            c.swung = StatsOf(type);
            c.grade = Mathf.Clamp(pick.rarity, 0, 2) + 1;   // 色:コモン=灰 / レア=青 / エピック=金(品質の色を流用)
            float good = 0f;
            foreach (var st in c.swung) good += c.Rating(st, ratingScale) - 0.5f;
            c.score = Mathf.Clamp(Mathf.RoundToInt(50f + 100f * good / c.swung.Length), 0, 100);
            return c;
        }

        /// <summary>B案用:other(今付けているもの)と比べた差。ほぼ同じパラメータは出さない。</summary>
        public System.Collections.Generic.List<(string text, bool good)> DiffTags(PartDefinition other)
        {
            var list = new System.Collections.Generic.List<(string, bool)>();
            if (other == null) return list;
            foreach (var s in StatsOf(type))
            {
                float r = StatValue(s) / Mathf.Max(0.0001f, other.StatValue(s));
                string arrow = Arrow(r);
                if (arrow.Length == 0) continue;
                list.Add(($"{StatName(s)}{arrow}", (r > 1f) == HigherIsBetter(s)));
            }
            return list;
        }

        /// <summary>比の大きさを矢印に:6%未満は無印、25%以上は二重。</summary>
        public static string Arrow(float r)
        {
            float a = Mathf.Abs(Mathf.Log(Mathf.Max(0.0001f, r)));
            if (a < Mathf.Log(1.06f)) return "";
            bool up = r > 1f;
            return a >= Mathf.Log(1.25f) ? (up ? "↑↑" : "↓↓") : (up ? "↑" : "↓");
        }

        public static string TagsText(System.Collections.Generic.List<(string text, bool good)> tags)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (t, _) in tags) { if (sb.Length > 0) sb.Append(' '); sb.Append(t); }
            return sb.ToString();
        }
    }
}
