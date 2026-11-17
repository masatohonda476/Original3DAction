using UnityEngine;

namespace HuntProto
{
    public enum PartType { Gatling, Thruster, Cannon, Blade }
    public enum MountSlot { RightArm, LeftArm, Back, Shoulder }
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

        [Header("パラメータ制(A案・B案)でだけ使う")]
        [Tooltip("重さ。付けている武装の合計で自機の機動力(走り・ブースト・回避・上昇)が下がる")]
        public float weight = 10f;
        [Tooltip("ガトリング:1発ごとに広がるばらつき(度)。撃ち続けるほど外れやすくなる")]
        public float recoil = 0.5f;
        [Tooltip("スラスター:ブースト中のエネルギー消費の倍率(小さいほど燃費が良い)")]
        public float drain = 1f;

        // ───────── 品質(同じ武装でも強さに差をつける) ─────────

        [HideInInspector] public int grade = 1;
        public static readonly string[] GradeNames = { "粗悪", "標準", "上物", "逸品" };
        public static readonly Color[] GradeColors =
        {
            new(0.42f, 0.36f, 0.3f), new(0.75f, 0.77f, 0.8f), new(0.25f, 0.55f, 1f), new(1f, 0.75f, 0.15f),
        };
        /// <summary>武装そのものの色(品質で決まる。形で種類、色で品質を見分ける)。</summary>
        public Color GradeColor => GradeColors[hideRank ? 1 : Mathf.Clamp(grade, 0, GradeColors.Length - 1)];
        /// <summary>評価を見せない個体(色も標準にそろえ、名前に評価を付けない)。</summary>
        [System.NonSerialized] public bool hideRank;
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

        // ───────── パラメータ制(A案・B案) ─────────
        // 使い切りをなくし、1個ごとにパラメータが大きく振れる。総合値から評価(C/B/A/S)を付ける。
        // A案:大きく振れるのは数個だけ(どこが振れたかがその個体の個性)/ B案:全部が大きく振れる(今の装備との差で比べる)

        public enum Stat { Damage, FireRate, Recoil, Range, Weight, Radius, Reload, Thrust, Drain, BladePower }

        /// <summary>種類ごとのパラメータ。</summary>
        public static Stat[] StatsOf(PartType t) => t switch
        {
            PartType.Gatling => new[] { Stat.Damage, Stat.FireRate, Stat.Recoil, Stat.Range, Stat.Weight },
            PartType.Cannon => new[] { Stat.Damage, Stat.Radius, Stat.Reload, Stat.Range, Stat.Weight },
            PartType.Thruster => new[] { Stat.Thrust, Stat.Drain, Stat.Weight },
            _ => new[] { Stat.BladePower, Stat.Weight },
        };

        /// <summary>値が大きいほど良いパラメータか(反動・重量・装填時間・燃費は小さいほど良い)。</summary>
        public static bool HigherIsBetter(Stat s) => s is not (Stat.Recoil or Stat.Weight or Stat.Reload or Stat.Drain);

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
            _ => "剣威力",
        };

        /// <summary>比べるための値(大きい・小さいの向きは StatName のとおり)。</summary>
        public float StatValue(Stat s) => s switch
        {
            Stat.Damage => damage,
            Stat.FireRate => 1f / Mathf.Max(0.01f, interval),
            Stat.Recoil => recoil,
            Stat.Range => range,
            Stat.Weight => weight,
            Stat.Radius => radius,
            Stat.Reload => interval,
            Stat.Thrust => multiplier,
            Stat.Drain => drain,
            _ => multiplier,
        };

        public string StatText(Stat s) => s switch
        {
            Stat.FireRate => $"連射 {StatValue(s):0.#}発/秒",
            Stat.Range => $"射程 {range:0}m",
            Stat.Radius => $"爆風 {radius:0.#}m",
            Stat.Reload => $"装填 {interval:0.0}秒",
            Stat.Thrust => $"推力 ×{multiplier:0.00}",
            Stat.Drain => $"燃費 ×{drain:0.00}",
            Stat.BladePower => $"剣威力 ×{multiplier:0.00}",
            Stat.Weight => $"重量 {weight:0}",
            Stat.Recoil => $"反動 {recoil:0.00}",
            _ => $"威力 {damage:0.#}",
        };

        /// <summary>パラメータ制で作った個体か。</summary>
        public bool HasStats => swung != null;
        /// <summary>大きく振れたパラメータ(A案では数個、B案では全部)。</summary>
        [System.NonSerialized] public Stat[] swung;
        /// <summary>振る前の元の定義(A案で、どれだけ振れたかを見せるため)。</summary>
        [System.NonSerialized] public PartDefinition source;
        /// <summary>総合値(0〜100、50が平均)。</summary>
        [HideInInspector] public int score = 50;
        public static readonly string[] RankNames = { "C", "B", "A", "S" };
        public string RankName => RankNames[Mathf.Clamp(grade, 0, RankNames.Length - 1)];
        string baseName;
        public string BaseName => baseName ?? displayName;

        /// <summary>
        /// パラメータを振った複製を作る。swingCount 個(0以下なら全部)を倍率 1/swing〜swing で大きく振り、残りは ±5% だけ揺らす。
        /// bestOf 回振って総合値の一番良いものを返す(ボス・宝箱は良いものが出やすい)。
        /// </summary>
        public PartDefinition WithStats(int swingCount, float swing, int bestOf, bool showRank = true)
        {
            PartDefinition best = null;
            for (int i = 0; i < Mathf.Max(1, bestOf); i++)
            {
                var c = RollStats(swingCount, swing);
                if (best == null || c.score > best.score) { if (best != null) Destroy(best); best = c; }
                else Destroy(c);
            }
            if (!showRank)
            {
                best.hideRank = true;
                best.displayName = displayName;
            }
            return best;
        }

        PartDefinition RollStats(int swingCount, float swing)
        {
            var c = Instantiate(this);
            c.name = name;
            c.uses = 0;   // 使い切りはなし(外れない)
            c.baseName = displayName;
            c.source = this;
            var stats = StatsOf(type);
            var pick = new System.Collections.Generic.List<Stat>(stats);
            for (int i = pick.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pick[i], pick[j]) = (pick[j], pick[i]); }
            int n = swingCount <= 0 ? stats.Length : Mathf.Min(swingCount, stats.Length);
            pick.RemoveRange(n, pick.Count - n);
            // 表示の順番は種類ごとの並びにそろえる
            c.swung = System.Array.FindAll(stats, s => pick.Contains(s));

            float lnSwing = Mathf.Log(Mathf.Max(1.01f, swing));
            float sum = 0f;
            foreach (var s in stats)
            {
                bool big = pick.Contains(s);
                float ln = big ? Random.Range(-lnSwing, lnSwing) : Random.Range(-0.05f, 0.05f);
                c.Apply(s, Mathf.Exp(ln));
                if (big) sum += (HigherIsBetter(s) ? ln : -ln) / lnSwing;   // -1〜1(良い向きが+)
            }
            // 振れた数によらず散らばりが同じになるよう √n で割る(標準偏差 ≈ 0.58)
            float z = sum / Mathf.Sqrt(n);
            c.score = Mathf.Clamp(Mathf.RoundToInt(50f + z * 35f), 0, 100);
            c.grade = z < -0.45f ? 0 : z < 0.2f ? 1 : z < 0.7f ? 2 : 3;   // C 約22% / B 約41% / A 約26% / S 約11%
            c.displayName = $"[{c.RankName}] {displayName}";
            return c;
        }

        /// <summary>パラメータに倍率を掛ける(f が大きいほど、そのパラメータの値が大きくなる)。</summary>
        void Apply(Stat s, float f)
        {
            switch (s)
            {
                case Stat.Damage: damage *= f; break;
                case Stat.FireRate: interval /= f; break;
                case Stat.Recoil: recoil *= f; break;
                case Stat.Range: range *= f; break;
                case Stat.Weight: weight *= f; break;
                case Stat.Radius: radius *= f; break;
                case Stat.Reload: interval *= f; break;
                case Stat.Drain: drain *= f; break;
                default: multiplier = 1f + (multiplier - 1f) * f; break;   // 推力・剣威力:上乗せ分に掛ける
            }
        }

        /// <summary>
        /// A案用:大きく振れたパラメータを「連射↑ 反動↓」のように(値の向き。良い向きか悪い向きかは色で見せる)。
        /// </summary>
        public System.Collections.Generic.List<(string text, bool good)> SwingTags()
        {
            var list = new System.Collections.Generic.List<(string, bool)>();
            var basis = source;
            if (swung == null || basis == null) return list;
            foreach (var s in swung)
            {
                float r = StatValue(s) / Mathf.Max(0.0001f, basis.StatValue(s));
                string arrow = Arrow(r);
                if (arrow.Length == 0) continue;
                list.Add(($"{StatName(s)}{arrow}", (r > 1f) == HigherIsBetter(s)));
            }
            return list;
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
        static string Arrow(float r)
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
