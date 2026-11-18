using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 武装を付ける所。剣(右手の剣を強くする)/ 背中(スラスター)/ 右肩(R2・押している間撃つ銃)/ 左肩(L2・砲・ミサイル)。
    /// </summary>
    public enum DuelSlot { Sword, Back, RightShoulder, LeftShoulder }

    /// <summary>
    /// ステージで敵が落とす武装1つの中身(弧ひとつ分の試作)。耐久・重量・品質はない。強さはレア度(0=コモン / 1=レア / 2=エピック)で分ける。
    /// 使う項目は付ける所で違う(剣:倍率 / 背中:倍率 / 右肩:銃 / 左肩:砲・ミサイル)。数値は仮置き。
    /// 一覧はステージ(DuelStage.arsenal)のインスペクターで触れる。初期値は Defaults()。
    /// </summary>
    [System.Serializable]
    public class DuelWeapon
    {
        public string name = "武装";
        public DuelSlot slot;
        [Range(0, 2)] public int rarity;
        [Tooltip("拾うときに見せる一言")]
        public string feel = "";

        [Header("剣(素の剣に掛ける倍率)")]
        public float damageMul = 1f;
        public float reachMul = 1f;
        [Tooltip("敵の怯みゲージへの量")]
        public float staggerMul = 1f;
        [Tooltip("振りの速さ(大きいほど振りかぶり・戻りが短い)")]
        public float speedMul = 1f;
        [Tooltip("当たる扇の広さに足す角度(片側。最大180で全周)")]
        public float arcAdd;
        public float knockMul = 1f;

        [Header("背中(スラスター。素の値に掛ける倍率)")]
        [Tooltip("地上を走る速さ")] public float runMul = 1f;
        [Tooltip("ブースト移動の速さと飛ぶときの上昇の速さ")] public float dashMul = 1f;
        [Tooltip("回避で動く距離")] public float dodgeMul = 1f;
        [Tooltip("回避・ブースト・飛行で使うEN(小さいほど燃費が良い)")] public float enCostMul = 1f;

        [Header("肩(右肩=銃 / 左肩=砲・ミサイル)")]
        [Tooltip("1発(散弾は1粒・ミサイルは1発)のダメージ")] public float damage = 1f;
        [Tooltip("右肩:発射の間隔(秒) / 左肩:次に撃てるまで(秒)")] public float interval = 0.1f;
        public float range = 60f;
        [Tooltip("1発ごとの敵の怯みゲージへの量")] public float stagger = 0.5f;
        [Tooltip("右肩:ばらつき(度)")] public float spread;
        [Tooltip("右肩:1回で出る粒の数(2以上なら散弾)")] public int pellets = 1;
        [Tooltip("右肩:弾倉(0なら弾倉なし)")] public int magazine;
        [Tooltip("右肩:撃ち切ったときのリロード(秒)")] public float reload = 2f;
        [Tooltip("左肩:爆発の半径(m)")] public float radius = 5f;
        [Tooltip("左肩:1回で撃つ数(2以上ならミサイル。狙える敵に割り振って追いかける)")] public int salvo = 1;
        [Tooltip("左肩:爆発で吹き飛ばす速さ(怯んだときだけ)")] public float knock = 10f;

        [Header("見た目(仮の形)")]
        public PrimitiveType shape = PrimitiveType.Cylinder;
        public Vector3 scale = new(0.8f, 1.6f, 0.8f);
        public Vector3 euler = new(90f, 0f, 0f);

        public static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        public static readonly Color[] RarityColors = { new(0.75f, 0.77f, 0.8f), new(0.3f, 0.6f, 1f), new(1f, 0.75f, 0.15f) };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, 2)];
        public Color Color => RarityColors[Mathf.Clamp(rarity, 0, 2)];
        public bool IsMissile => salvo > 1;

        public static string SlotName(DuelSlot s) => s switch
        {
            DuelSlot.Sword => "剣",
            DuelSlot.Back => "背中",
            DuelSlot.RightShoulder => "右肩",
            _ => "左肩",
        };

        // ───────── 比べる(拾うとき) ─────────

        /// <summary>比べる項目:名前・値・表示の書式・大きいほど良いか。w が null なら素の状態(剣・背中は倍率1、肩は空き)。</summary>
        public static List<(string name, float value, string text, bool higherBetter)> Stats(DuelWeapon w, DuelSlot slot)
        {
            var list = new List<(string, float, string, bool)>();
            switch (slot)
            {
                case DuelSlot.Sword:
                {
                    float d = w?.damageMul ?? 1f, r = w?.reachMul ?? 1f, sp = w?.speedMul ?? 1f, st = w?.staggerMul ?? 1f, a = w?.arcAdd ?? 0f;
                    list.Add(("威力", d, $"×{d:0.00}", true));
                    list.Add(("届く", r, $"×{r:0.00}", true));
                    list.Add(("速さ", sp, $"×{sp:0.00}", true));
                    list.Add(("怯ませ", st, $"×{st:0.00}", true));
                    list.Add(("薙ぐ幅", 100f + a, $"+{a:0}°", true));
                    break;
                }
                case DuelSlot.Back:
                {
                    float run = w?.runMul ?? 1f, dash = w?.dashMul ?? 1f, dodge = w?.dodgeMul ?? 1f, en = w?.enCostMul ?? 1f;
                    list.Add(("走り", run, $"×{run:0.00}", true));
                    list.Add(("ブースト", dash, $"×{dash:0.00}", true));
                    list.Add(("回避", dodge, $"×{dodge:0.00}", true));
                    list.Add(("EN消費", en, $"×{en:0.00}", false));
                    break;
                }
                case DuelSlot.RightShoulder:
                    if (w == null) break;
                    float shot = w.damage * Mathf.Max(1, w.pellets);
                    list.Add(("毎秒", shot / Mathf.Max(0.01f, w.interval), $"{shot / Mathf.Max(0.01f, w.interval):0}", true));
                    list.Add(("1発", shot, w.pellets > 1 ? $"{w.damage:0.#}×{w.pellets}粒" : $"{w.damage:0.#}", true));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    list.Add(("弾倉", w.magazine <= 0 ? 9999f : w.magazine, w.magazine <= 0 ? "無限" : $"{w.magazine}", true));
                    break;
                default:
                    if (w == null) break;
                    float total = w.damage * Mathf.Max(1, w.salvo);
                    list.Add(("威力", total, w.salvo > 1 ? $"{w.damage:0.#}×{w.salvo}発" : $"{w.damage:0.#}", true));
                    list.Add(("爆風", w.radius, $"{w.radius:0.#}m", true));
                    list.Add(("装填", w.interval, $"{w.interval:0.0}秒", false));
                    list.Add(("射程", w.range, $"{w.range:0}m", true));
                    break;
            }
            return list;
        }

        /// <summary>
        /// 拾う候補(next)と今の装備(cur)の比べ:「威力 ×1.30→×1.70↑↑」のように並べる(良くなるのは緑、悪くなるのは赤。リッチテキスト)。
        /// 今が空きなら値だけ。
        /// </summary>
        public static string CompareText(DuelWeapon next, DuelWeapon cur, DuelSlot slot)
        {
            var a = Stats(next, slot);
            var b = Stats(cur, slot);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Count; i++)
            {
                if (sb.Length > 0) sb.Append("   ");
                if (b.Count != a.Count)
                {
                    sb.Append($"{a[i].name} {a[i].text}");
                    continue;
                }
                float ratio = a[i].value / Mathf.Max(0.0001f, b[i].value);
                string arrow = PartDefinition.Arrow(ratio);
                bool better = (ratio > 1f) == a[i].higherBetter;
                string body = $"{a[i].name} {b[i].text}→{a[i].text}";
                sb.Append(arrow.Length == 0 ? body : $"<color={(better ? "#7CFC8A" : "#FF7A6A")}>{body}{arrow}</color>");
            }
            return sb.ToString();
        }

        // ───────── 初期の一覧(仮置き) ─────────

        static DuelWeapon Sword(string name, int rarity, string feel, float dmg, float reach, float speed, float stagger, float arc, float knock) => new()
        {
            name = name, slot = DuelSlot.Sword, rarity = rarity, feel = feel,
            damageMul = dmg, reachMul = reach, speedMul = speed, staggerMul = stagger, arcAdd = arc, knockMul = knock,
        };

        static DuelWeapon Back(string name, int rarity, string feel, float run, float dash, float dodge, float en, Vector3 scale) => new()
        {
            name = name, slot = DuelSlot.Back, rarity = rarity, feel = feel, runMul = run, dashMul = dash, dodgeMul = dodge, enCostMul = en,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon Gun(string name, int rarity, string feel, float dmg, float interval, float range, float spread, int pellets, int mag, float reload, float stagger, Vector3 scale) => new()
        {
            name = name, slot = DuelSlot.RightShoulder, rarity = rarity, feel = feel, damage = dmg, interval = interval, range = range,
            spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, scale = scale,
        };

        static DuelWeapon Heavy(string name, int rarity, string feel, float dmg, int salvo, float radius, float interval, float range, float stagger, float knock, PrimitiveType shape, Vector3 scale) => new()
        {
            name = name, slot = DuelSlot.LeftShoulder, rarity = rarity, feel = feel, damage = dmg, salvo = salvo, radius = radius,
            interval = interval, range = range, stagger = stagger, knock = knock, shape = shape, scale = scale,
            euler = shape == PrimitiveType.Cylinder ? new Vector3(90f, 0f, 0f) : Vector3.zero,
        };

        /// <summary>
        /// 初期の一覧。素の剣(通常攻撃9・強攻撃24、毎秒16ほど)・敵のHP(兵卒28・剣士47)に合わせた仮の値。
        /// 下(コモン)は素の剣を少し補う程度、上(エピック)は群れをまとめて倒せる強さ。
        /// </summary>
        public static List<DuelWeapon> Defaults() => new()
        {
            // 剣
            Sword("鋼の剣", 0, "素の剣より重く、少し長い", 1.3f, 1.1f, 1f, 1.2f, 0f, 1.1f),
            Sword("軽剣", 0, "振りが速い。威力は控えめ", 1.1f, 1f, 1.2f, 1f, 0f, 1f),
            Sword("大剣", 1, "振りは遅いが、広く重い。まとめて薙げる", 1.7f, 1.3f, 0.9f, 1.6f, 20f, 1.5f),
            Sword("高周波ブレード", 1, "速く鋭い", 1.5f, 1.15f, 1.25f, 1.3f, 10f, 1.2f),
            Sword("斬艦刀", 2, "長大な刃で、周りごと薙ぎ払う", 2.4f, 1.7f, 1f, 2.2f, 45f, 2.2f),
            // 背中
            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f)),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f)),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f)),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f)),
            // 右肩(銃。R2 を押している間撃つ)
            Gun("肩部機関銃", 0, "弱いが、離れた所から削れる", 1.2f, 0.12f, 55f, 2.5f, 1, 24, 2.2f, 0.4f, new Vector3(0.7f, 1.6f, 0.7f)),
            Gun("重機関銃", 1, "一発が重く、よく削れる", 2f, 0.09f, 75f, 2f, 1, 45, 2.5f, 0.6f, new Vector3(1.2f, 2f, 1.2f)),
            Gun("肩部散弾銃", 1, "近いほど多く当たる9粒の散弾", 2.2f, 0.6f, 35f, 7f, 9, 6, 2f, 0.8f, new Vector3(1f, 1.5f, 1f)),
            Gun("ビームガトリング", 2, "弾切れなし。撃ち続けて溶かす", 1.8f, 0.06f, 95f, 1.5f, 1, 0, 0f, 0.5f, new Vector3(1.1f, 2.4f, 1.1f)),
            // 左肩(砲・ミサイル。L2 で撃つ)
            Heavy("速射砲", 0, "爆発は小さいが、すぐ次が撃てる", 8f, 1, 5f, 1.4f, 70f, 6f, 8f, PrimitiveType.Cylinder, new Vector3(0.6f, 1.4f, 0.6f)),
            Heavy("重榴弾砲", 1, "装填は遅いが、大きな爆発で群れを崩す", 22f, 1, 12f, 3.5f, 80f, 14f, 22f, PrimitiveType.Cylinder, new Vector3(1.3f, 2f, 1.3f)),
            Heavy("ミサイルポッド", 1, "4発が狙える敵を追いかける", 6f, 4, 4f, 3f, 100f, 4f, 6f, PrimitiveType.Cube, new Vector3(1.2f, 1f, 1.4f)),
            Heavy("多弾頭ミサイル", 2, "10発が周りの敵へ散って追いかける", 10f, 10, 5f, 4f, 110f, 6f, 8f, PrimitiveType.Cube, new Vector3(1.8f, 1.4f, 1.8f)),
            Heavy("超重砲", 2, "装填はとても遅いが、群れごと吹き飛ばす", 40f, 1, 20f, 6f, 90f, 30f, 30f, PrimitiveType.Cylinder, new Vector3(1.9f, 3.2f, 1.9f)),
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
