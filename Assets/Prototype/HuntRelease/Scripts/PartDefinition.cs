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

        // ───────── 品質(同じ武装でも強さに差をつける) ─────────

        [HideInInspector] public int grade = 1;
        public static readonly string[] GradeNames = { "粗悪", "標準", "上物", "逸品" };
        public static readonly Color[] GradeColors =
        {
            new(0.42f, 0.36f, 0.3f), new(0.75f, 0.77f, 0.8f), new(0.25f, 0.55f, 1f), new(1f, 0.75f, 0.15f),
        };
        /// <summary>武装そのものの色(品質で決まる。形で種類、色で品質を見分ける)。</summary>
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
    }
}
