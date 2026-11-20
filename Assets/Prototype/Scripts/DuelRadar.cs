using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prototype
{
    /// <summary>
    /// 武装の性能のレーダーチャートと帯グラフ(弧の試作。OnGUI の中から呼ぶ)。武装ハントの ProtoRadar と同じ描き方(GL で多角形)。
    /// 軸は武装の仲間(DuelWeapon.Family:振る / 盾 / 背中 / 弾の銃 / 爆発)ごとに決める。どの軸も「良いほど外側(長い)」(EN 消費・削れやすさなどは反転)。
    /// 目盛りは、その仲間の一覧(DuelWeapon.Defaults() と素の剣)の最小〜最大を 0〜1 にそろえる。片手の剣と両手の近接は同じ仲間なので同じ目盛り。
    /// 最大が最小の LogSpread 倍以上に開く軸は、弱い武装どうしの差が潰れないよう対数の目盛りにする。
    /// 比べる2つの仲間が違うとき(剣と盾など)は、レーダーチャートは出さず、帯グラフも主の武装だけ(作者の指示)。
    /// 値と文字列は Get で武装の組ごとに1回だけ作ってとっておく(毎フレームは作らない)。
    /// </summary>
    public static class DuelRadar
    {
        /// <summary>主の武装(拾う候補 / 装備の確認で選んでいる所)の色と、比べる相手(今の武装 / もう片方の所)の色。</summary>
        public static readonly Color MainColor = new(0.35f, 0.8f, 1f);
        public static readonly Color OtherColor = new(1f, 0.8f, 0.3f);
        public const string MainHex = "#59CCFF", OtherHex = "#FFCC4D";
        /// <summary>良くなる / 悪くなる(比較文 DuelWeapon.CompareText と同じ色)。</summary>
        static readonly Color Good = new(0.49f, 0.99f, 0.54f), Bad = new(1f, 0.48f, 0.42f), Plain = new(0.85f, 0.87f, 0.9f);
        const string GoodHex = "#7CFC8A", BadHex = "#FF7A6A";

        /// <summary>
        /// 最大が最小のこの倍以上に開く軸は対数の目盛りにする(今の一覧では銃の1発・連射・弾持ち・怯ませ、砲の威力・弾数・怯ませ)。
        /// 近接(威力は約5倍の開き)は直線のままにして、両手の近接が片手の剣より外へ大きく張り出すのが見えるようにする。
        /// </summary>
        const float LogSpread = 8f;

        // ───────── 軸 ─────────

        sealed class Axis
        {
            public string name;
            public bool higherBetter = true;
            public System.Func<DuelWeapon, float> value;
            public System.Func<DuelWeapon, string> text;
            // 目盛り(Init で一覧から決める)
            public bool log;
            public float min, max;
        }

        static Axis A(string name, System.Func<DuelWeapon, float> value, System.Func<DuelWeapon, string> text, bool higherBetter = true) =>
            new() { name = name, value = value, text = text, higherBetter = higherBetter };

        static float Avg(DuelSwing[] ms, System.Func<DuelSwing, float> f)
        {
            float s = 0f;
            foreach (var m in ms) s += f(m);
            return s / Mathf.Max(1, ms.Length);
        }

        /// <summary>振る(片手の剣・両手の近接):連撃(R1)の一振りの平均。威力・速さ・届く・幅は拾うときの比較(DuelWeapon.Stats)と同じ値。</summary>
        static float BladeRate(DuelWeapon w)
        {
            var L = w.LightMoves;
            float cyc = 0f;
            foreach (var m in L) cyc += m.windup + m.active + DuelSwing.ChainAfter;
            return L.Length / Mathf.Max(0.01f, cyc) * Mathf.Max(0.3f, w.speedMul);
        }

        static Axis[] BladeAxes() => new[]
        {
            A("威力", w => Avg(w.LightMoves, m => m.damage) * w.damageMul, w => $"{Avg(w.LightMoves, m => m.damage) * w.damageMul:0.#}"),
            A("速さ", BladeRate, w => $"{BladeRate(w):0.0}/秒"),
            A("届く", w => w.LightMoves[0].reach * w.reachMul, w => $"{w.LightMoves[0].reach * w.reachMul:0.#}m"),
            A("幅", w => Mathf.Min(180f, Avg(w.LightMoves, m => m.arc) + w.arcAdd), w => $"±{Mathf.Min(180f, Avg(w.LightMoves, m => m.arc) + w.arcAdd):0}°"),
            A("怯ませ", w => Avg(w.LightMoves, m => m.stagger) * w.staggerMul, w => $"{Avg(w.LightMoves, m => m.stagger) * w.staggerMul:0.#}"),
            A("EN軽さ", w => Avg(w.LightMoves, m => m.cost), w => $"EN{Avg(w.LightMoves, m => m.cost):0}", false),
        };

        static Axis[] ShieldAxes() => new[]
        {
            A("ゲージ", w => w.guardMax, w => $"{w.guardMax:0}"),
            A("削れにくさ", w => w.guardCostMul, w => $"×{w.guardCostMul:0.00}", false),
            A("傷を防ぐ", w => w.guardChip, w => $"通す{w.guardChip * 100f:0}%", false),
            A("構え歩き", w => w.guardMoveMul, w => $"{w.guardMoveMul * 100f:0}%"),
            A("立ち直り", w => w.guardStunMul, w => $"×{w.guardStunMul:0.00}", false),
            A("走り", w => w.runMul, w => $"×{w.runMul:0.00}"),
        };

        static Axis[] BackAxes() => new[]
        {
            A("走り", w => w.runMul, w => $"×{w.runMul:0.00}"),
            A("ブースト", w => w.dashMul, w => $"×{w.dashMul:0.00}"),
            A("回避", w => w.dodgeMul, w => $"×{w.dodgeMul:0.00}"),
            A("燃費", w => w.enCostMul, w => $"×{w.enCostMul:0.00}", false),
        };

        static Axis[] GunAxes() => new[]
        {
            A("毎秒", w => w.damage * Mathf.Max(1, w.pellets) / Mathf.Max(0.01f, w.interval), w => $"{w.damage * Mathf.Max(1, w.pellets) / Mathf.Max(0.01f, w.interval):0}"),
            A("1発", w => w.damage * Mathf.Max(1, w.pellets), w => w.pellets > 1 ? $"{w.damage:0.#}×{w.pellets}" : $"{w.damage:0.#}"),
            A("怯ませ", w => w.stagger * Mathf.Max(1, w.pellets), w => $"{w.stagger * Mathf.Max(1, w.pellets):0.#}"),
            A("射程", w => w.range, w => $"{w.range:0}m"),
            A("連射", w => 1f / Mathf.Max(0.01f, w.interval), w => $"{1f / Mathf.Max(0.01f, w.interval):0.#}/秒"),
            A("弾持ち", ShotsPerLoad, w => w.magazine > 0 || !w.UsesEnergy ? (w.magazine <= 0 ? "∞" : $"{w.magazine}発") : $"{ShotsPerLoad(w):0}発(EN)"),
        };

        /// <summary>続けて撃てる数:弾倉のある銃は弾倉の数、エネルギー武器はENが満タンから0になるまでの数(素のENの最大で数える)。</summary>
        static float ShotsPerLoad(DuelWeapon w)
        {
            if (w.magazine > 0) return w.magazine;
            if (!w.UsesEnergy) return float.PositiveInfinity;
            float max = DuelPlayer.I != null ? DuelPlayer.I.maxEnergy : 150f;
            return Mathf.Ceil(max / Mathf.Max(0.01f, w.EnPerShot));
        }

        static Axis[] BlastAxes() => new[]
        {
            A("威力", w => w.damage * Mathf.Max(1, w.salvo), w => w.salvo > 1 ? $"{w.damage:0.#}×{w.salvo}" : $"{w.damage:0.#}"),
            A("爆風", w => w.radius, w => $"{w.radius:0.#}m"),
            A("装填", w => w.interval, w => $"{w.interval:0.0}秒", false),
            A("射程", w => w.range, w => $"{w.range:0}m"),
            A("弾数", w => Mathf.Max(1, w.salvo), w => $"{Mathf.Max(1, w.salvo)}発"),
            A("怯ませ", w => w.stagger * Mathf.Max(1, w.salvo), w => $"{w.stagger * Mathf.Max(1, w.salvo):0.#}"),
        };

        /// <summary>仲間(DuelWeapon.Family の 0〜4)ごとの軸。</summary>
        static Axis[][] axes;

        /// <summary>軸を作り、目盛り(仲間ごとの一覧の最小・最大)を決める。最初に使うときに1回だけ。</summary>
        static void Init()
        {
            if (axes != null) return;
            axes = new[] { BladeAxes(), ShieldAxes(), BackAxes(), GunAxes(), BlastAxes() };
            var all = DuelWeapon.Defaults();
            all.Add(DuelWeapon.BaseSword);
            for (int f = 0; f < axes.Length; f++)
            {
                foreach (var a in axes[f])
                {
                    a.min = float.MaxValue;
                    a.max = float.MinValue;
                    foreach (var w in all)
                    {
                        if (DuelWeapon.Family(w) != f) continue;
                        float v = a.value(w);
                        if (float.IsInfinity(v) || float.IsNaN(v)) continue;   // 弾切れなしは目盛りに入れず、いちばん外にする
                        a.min = Mathf.Min(a.min, v);
                        a.max = Mathf.Max(a.max, v);
                    }
                    if (a.min > a.max) a.min = a.max = 1f;
                    a.log = a.min > 0f && a.max >= a.min * LogSpread;
                }
            }
        }

        /// <summary>値を目盛り(0〜1。良いほど大きい)へ。一覧の外の値は端に寄せる。</summary>
        static float Norm(Axis a, float v)
        {
            float t;
            if (float.IsPositiveInfinity(v)) t = 1f;
            else if (a.max - a.min < 1e-5f) t = 0.5f;
            else if (a.log) t = (Mathf.Log(Mathf.Max(v, a.min * 0.25f)) - Mathf.Log(a.min)) / (Mathf.Log(a.max) - Mathf.Log(a.min));
            else t = (v - a.min) / (a.max - a.min);
            t = Mathf.Clamp01(t);
            return a.higherBetter ? t : 1f - t;
        }

        // ───────── 武装の組ごとの値(作ってとっておく) ─────────

        /// <summary>主の武装(main)と比べる相手(other)の、軸ごとの値・目盛り・文字列。</summary>
        public sealed class Pair
        {
            public DuelWeapon main, other;
            /// <summary>比べる相手がいるが、仲間が違う(このときは比べない:レーダーは出さず、帯グラフも主だけ)。</summary>
            public bool otherDifferent;
            public string[] names;
            public float[] mainNorm, otherNorm;   // otherNorm は同じ仲間の相手がいるときだけ
            /// <summary>軸ごとの差の向き(+1 良くなる / -1 悪くなる / 0 ほぼ同じ。比べる相手がいるときだけ)。</summary>
            public int[] diff;
            /// <summary>帯グラフの右の数値(リッチテキスト。比べるときは「相手→主↑」)。</summary>
            public string[] valueText;
            public bool Compared => otherNorm != null;
            /// <summary>比べる相手が主と同じ武装(性能は同じ。凡例で「同じ」と出す)。</summary>
            public bool Same => other != null && other == main;
            public bool ShowRadar => !otherDifferent;
        }

        static readonly Dictionary<(DuelWeapon, DuelWeapon), Pair> cache = new();

        /// <summary>武装の組のとっておきを捨てる(やり直すたびに、前のシーンの武装の組がたまらないように。DuelGame.Awake)。</summary>
        public static void ClearCache() => cache.Clear();

        /// <summary>
        /// main の性能を、other(今の武装 / もう片方の所。空き・同じ武装なら null)と比べる組。作るのは組ごとに1回だけ。
        /// main が null なら null。
        /// </summary>
        public static Pair Get(DuelWeapon main, DuelWeapon other)
        {
            if (main == null) return null;
            // 相手が同じ武装(同じ一覧の同じもの:今と同じ武装を拾う・両手に同じ片手剣)でも、空きにはせず自分どうしで比べる(差はなし。Same)
            if (cache.TryGetValue((main, other), out var p)) return p;
            Init();
            int f = DuelWeapon.Family(main);
            var ax = axes[f];
            int n = ax.Length;
            p = new Pair
            {
                main = main, other = other, names = new string[n], mainNorm = new float[n], valueText = new string[n],
                otherDifferent = other != null && DuelWeapon.Family(other) != f,
            };
            bool cmp = other != null && !p.otherDifferent;
            if (cmp)
            {
                p.otherNorm = new float[n];
                p.diff = new int[n];
            }
            for (int i = 0; i < n; i++)
            {
                var a = ax[i];
                float v = a.value(main);
                p.names[i] = a.name;
                p.mainNorm[i] = Norm(a, v);
                string tv = a.text(main);
                if (!cmp)
                {
                    p.valueText[i] = tv;
                    continue;
                }
                float o = a.value(other);
                p.otherNorm[i] = Norm(a, o);
                // 差の向き:比(主/相手)が 6% 以上離れていれば(比較文の矢印と同じ決まり)
                float ratio = float.IsPositiveInfinity(v) ? (float.IsPositiveInfinity(o) ? 1f : 2f)
                    : float.IsPositiveInfinity(o) ? 0.5f
                    : Mathf.Max(0.0001f, v) / Mathf.Max(0.0001f, o);
                string arrow = DuelWeapon.Arrow(ratio);
                int d = arrow.Length == 0 ? 0 : (ratio > 1f) == a.higherBetter ? 1 : -1;
                p.diff[i] = d;
                string body = a.text(other) + "→" + tv;
                p.valueText[i] = d == 0 ? body : "<color=" + (d > 0 ? GoodHex : BadHex) + ">" + body + arrow + "</color>";
            }
            cache[(main, other)] = p;
            return p;
        }

        // ───────── 描く ─────────

        static Material mat;
        static GUIStyle axisStyle, barLabelStyle, barTextStyle;
        static readonly Vector2[] mainPts = new Vector2[8], otherPts = new Vector2[8];

        static void EnsureStyles()
        {
            if (axisStyle != null) return;
            axisStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            barLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            barTextStyle = new GUIStyle(barLabelStyle) { richText = true };
        }

        /// <summary>評価(0〜1)を半径・帯の長さへ。0でも点にならないよう少しだけ底上げする。</summary>
        static float Radius(float rating) => 0.1f + 0.9f * rating;

        static Color DiffColor(Pair p, int i) => !p.Compared ? Plain : p.diff[i] > 0 ? Good : p.diff[i] < 0 ? Bad : Plain;

        /// <summary>
        /// レーダーチャート:主を塗りつぶしと縁(MainColor)、同じ仲間の相手がいれば薄い塗りと縁(OtherColor)で重ねる。
        /// 軸の名前は、相手より良ければ緑・悪ければ赤。仲間が違うときは描かない(ShowRadar)。r は GUI の座標(uiScale 倍する前)。
        /// </summary>
        public static void DrawRadar(Rect r, float uiScale, Pair p)
        {
            if (p == null || !p.ShowRadar) return;
            EnsureStyles();
            int n = p.mainNorm.Length;
            Vector2 c = r.center;
            float labelPad = axisStyle.fontSize + 8f;
            float R = Mathf.Min(r.width, r.height) * 0.5f - labelPad;
            Vector2 Dir(int i)
            {
                float ang = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / n;
                return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            }
            Vector2 P(int i, float v) => c + Dir(i) * (R * v);

            if (Event.current.type == EventType.Repaint && Begin(uiScale))
            {
                // 目盛り:4段の輪(いちばん外は少し明るく)と軸
                for (int k = 1; k <= 4; k++)
                {
                    float v = Radius(k * 0.25f);
                    var col = new Color(1f, 1f, 1f, k == 4 ? 0.28f : 0.12f);
                    for (int i = 0; i < n; i++) Line(P(i, v), P((i + 1) % n, v), 1f, col, uiScale);
                }
                for (int i = 0; i < n; i++) Line(c, P(i, 1f), 1f, new Color(1f, 1f, 1f, 0.12f), uiScale);

                for (int i = 0; i < n; i++) mainPts[i] = P(i, Radius(p.mainNorm[i]));
                if (p.Compared)
                    for (int i = 0; i < n; i++) otherPts[i] = P(i, Radius(p.otherNorm[i]));
                // 塗り:相手(薄く)→ 主
                if (p.Compared) Fill(c, otherPts, n, new Color(OtherColor.r, OtherColor.g, OtherColor.b, 0.12f), uiScale);
                Fill(c, mainPts, n, new Color(MainColor.r, MainColor.g, MainColor.b, 0.3f), uiScale);
                // 縁:主 → 相手(相手の線が主の塗りに埋もれないよう上に)
                for (int i = 0; i < n; i++) Line(mainPts[i], mainPts[(i + 1) % n], 2f, MainColor, uiScale);
                if (p.Compared)
                    for (int i = 0; i < n; i++) Line(otherPts[i], otherPts[(i + 1) % n], 2f, OtherColor, uiScale);
                GL.PopMatrix();
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 lp = c + Dir(i) * (R + labelPad * 0.6f);
                axisStyle.normal.textColor = DiffColor(p, i);
                GUI.Label(new Rect(lp.x - 36f, lp.y - 10f, 72f, 20f), p.names[i], axisStyle);
            }
        }

        /// <summary>
        /// 帯グラフ:軸ごとに1行(名前・帯・数値)。比べるときは上に相手(OtherColor)、下に主(MainColor)の2本を並べ、
        /// 主の帯の相手との差の部分を、良くなる分は緑・悪くなる分は赤(薄く)で塗る。数値は「相手→主」で、差の向きの色と矢印つき。
        /// 比べないとき(空き・仲間が違う)は主の1本と数値だけ。
        /// </summary>
        public static void DrawBars(Rect r, float uiScale, Pair p, float labelW = 66f, float textW = 112f)
        {
            if (p == null) return;
            EnsureStyles();
            int n = p.mainNorm.Length;
            float rowH = r.height / n;
            float bx = r.x + labelW, bw = Mathf.Max(20f, r.width - labelW - textW - 6f);
            bool cmp = p.Compared;
            float barH = cmp ? Mathf.Clamp(rowH * 0.2f, 3f, 7f) : Mathf.Clamp(rowH * 0.32f, 4f, 10f);
            for (int i = 0; i < n; i++)
            {
                float y = r.y + i * rowH, mid = y + rowH * 0.5f;
                barLabelStyle.normal.textColor = Plain;
                GUI.Label(new Rect(r.x, y, labelW, rowH), p.names[i], barLabelStyle);
                float mainLen = bw * Radius(p.mainNorm[i]);
                if (cmp)
                {
                    float otherLen = bw * Radius(p.otherNorm[i]);
                    var top = new Rect(bx, mid - barH - 1f, bw, barH);
                    var bot = new Rect(bx, mid + 1f, bw, barH);
                    Box(top, new Color(1f, 1f, 1f, 0.08f));
                    Box(bot, new Color(1f, 1f, 1f, 0.08f));
                    Box(new Rect(top.x, top.y, otherLen, barH), new Color(OtherColor.r, OtherColor.g, OtherColor.b, 0.85f));
                    int d = p.diff[i];
                    if (d == 0) Box(new Rect(bot.x, bot.y, mainLen, barH), MainColor);
                    else
                    {
                        float lo = Mathf.Min(mainLen, otherLen), hi = Mathf.Max(mainLen, otherLen);
                        Box(new Rect(bot.x, bot.y, lo, barH), MainColor);
                        // 良くなる分(主が長い)は緑、悪くなる分(主が短い。相手の長さまで)は薄い赤
                        Box(new Rect(bot.x + lo, bot.y, Mathf.Max(1f, hi - lo), barH), d > 0 ? Good : new Color(Bad.r, Bad.g, Bad.b, 0.5f));
                    }
                }
                else
                {
                    var bar = new Rect(bx, mid - barH * 0.5f, bw, barH);
                    Box(bar, new Color(1f, 1f, 1f, 0.08f));
                    Box(new Rect(bar.x, bar.y, mainLen, barH), MainColor);
                }
                GUI.Label(new Rect(bx + bw + 6f, y, textW, rowH), p.valueText[i], barTextStyle);
            }
            GUI.color = Color.white;
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        // ───────── GL(GUI の座標を uiScale 倍して画面の画素へ。ProtoRadar と同じ) ─────────

        static bool Begin(float uiScale)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return false;
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_Cull", (int)CullMode.Off);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_ZTest", (int)CompareFunction.Always);
            }
            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);   // 左上が原点(GUI と同じ向き)
            return true;
        }

        static void V(Vector2 p, float s) => GL.Vertex3(p.x * s, p.y * s, 0f);

        static void Fill(Vector2 c, Vector2[] pts, int n, Color col, float s)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(col);
            for (int i = 0; i < n; i++)
            {
                V(c, s);
                V(pts[i], s);
                V(pts[(i + 1) % n], s);
            }
            GL.End();
        }

        static void Line(Vector2 a, Vector2 b, float width, Color col, float s)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            Vector2 nrm = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
            GL.Begin(GL.QUADS);
            GL.Color(col);
            V(a - nrm, s);
            V(a + nrm, s);
            V(b + nrm, s);
            V(b - nrm, s);
            GL.End();
        }
    }
}
