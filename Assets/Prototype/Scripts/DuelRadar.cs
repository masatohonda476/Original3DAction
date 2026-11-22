using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prototype
{
    /// <summary>
    /// 近接・遠距離・背中の性能を、同じ枠のカタログ内で比較する。
    /// 性能値・目盛り・文字列は武装の組につき一度だけ作り、描画時に使い回す。
    /// </summary>
    public static class DuelRadar
    {
        public static readonly Color MainColor = new(0.35f, 0.8f, 1f);
        public static readonly Color OtherColor = new(1f, 0.8f, 0.3f);
        static readonly Color Good = new(0.49f, 0.99f, 0.54f), Bad = new(1f, 0.48f, 0.42f), Plain = new(0.85f, 0.87f, 0.9f);
        const string GoodHex = "#7CFC8A", BadHex = "#FF7A6A";
        const float LogSpread = 8f;
        const float EqualRatio = 1.06f;

        sealed class Axis
        {
            public string name;
            public bool higherBetter;
            public Func<DuelWeapon, float> value;
            public Func<DuelWeapon, float, string> text;
            public bool log;
            public float min, max;
        }

        static Axis A(string name, Func<DuelWeapon, float> value, Func<DuelWeapon, float, string> text, bool higherBetter = true) =>
            new() { name = name, value = value, text = text, higherBetter = higherBetter };

        static Axis[] MeleeAxes() => new[]
        {
            A("通常威力", w => DuelMoveSet.Base.light[0].damage * w.MeleePower, (_, v) => $"{v:0.##}"),
            A("強攻威力", w => w.HeavyMove.damage * w.MeleePower, (_, v) => $"{v:0.#}"),
            A("強攻崩し", w => w.HeavyMove.stagger, (_, v) => $"{v:0.#}"),
            A("強攻発生", w => w.HeavyMove.windup, (_, v) => $"{v:0.##}秒", false),
            A("強攻幅", w => w.HeavyMove.arc, (_, v) => $"±{v:0.#}°"),
            A("強攻燃費", w => w.HeavyMove.cost, (_, v) => $"EN {v:0.#}", false),
        };

        static int ShotsPerTrigger(DuelWeapon w) => Mathf.Max(1, w.Primary.burstCount);

        // R1 一入力の最大威力。散弾や点射は、すべて単体に命中したときの合計。
        public static float VolleyDamage(DuelWeapon w) => w.Primary.damage * Mathf.Max(1, w.Primary.pellets) * ShotsPerTrigger(w);

        // 弾倉で撃てる弾数。EN 銃は満タンから EN が尽きるまでの弾数。
        static float Endurance(DuelWeapon w)
        {
            if (w.HasMagazine) return w.magazine;
            return w.enCost > 0f ? Mathf.Ceil((DuelPlayer.I != null ? DuelPlayer.I.maxEnergy : 150f) / w.enCost) : float.PositiveInfinity;
        }

        static Axis[] RangedAxes() => new[]
        {
            A("通常最大", VolleyDamage, (_, v) => $"{v:0.#}"),
            A("連射", w => ShotsPerTrigger(w) / Mathf.Max(0.01f, w.Primary.interval), (_, v) => $"{v:0.#}発/秒"),
            A("射程", w => w.Primary.range, (_, v) => $"{v:0.#} m"),
            A("継戦", Endurance, (w, v) => float.IsPositiveInfinity(v) ? "制限なし"
                : w.HasMagazine ? $"{v:0}発" : $"{v:0}発 (EN)"),
            // 1 m 進む時間で評価し、即着の値は 0 秒。表示は弾速に戻す。
            A("弾速", w => w.Primary.shotSpeed > 0f ? 1f / w.Primary.shotSpeed : 0f,
                (_, v) => v == 0f ? "即着" : $"{1f / v:0} m/s", false),
            A("大技弾費", w => w.SpecialAmmo, (_, v) => $"共通弾 {v:0}", false),
        };

        static Axis[] BackAxes() => new[]
        {
            A("走り", w => w.runMul, (_, v) => $"×{v:0.00}"),
            A("ブースト", w => w.dashMul, (_, v) => $"×{v:0.00}"),
            A("回避", w => w.dodgeMul, (_, v) => $"×{v:0.00}"),
            A("燃費", w => w.enCostMul, (_, v) => $"×{v:0.00}", false),
            A("軽さ", w => w.weight, (_, v) => $"{v:0.#}", false),
        };

        static Axis[][] axes;

        // 枠ごとに初期装備と落とし物を集め、各性能の目盛りを作る。
        static void Init()
        {
            if (axes != null) return;
            axes = new[] { MeleeAxes(), RangedAxes(), BackAxes() };
            var all = DuelWeapon.Defaults();
            all.Add(DuelWeapon.BaseSword);
            all.Add(DuelWeapon.BaseGun);
            all.Add(DuelWeapon.BaseThruster);
            for (int slot = 0; slot < axes.Length; slot++)
            {
                foreach (var a in axes[slot])
                {
                    a.min = float.PositiveInfinity;
                    a.max = float.NegativeInfinity;
                    foreach (var w in all)
                    {
                        if ((int)w.Slot != slot) continue;
                        float v = a.value(w);
                        if (float.IsInfinity(v) || float.IsNaN(v)) continue;
                        a.min = Mathf.Min(a.min, v);
                        a.max = Mathf.Max(a.max, v);
                    }
                    if (a.min > a.max) a.min = a.max = 1f;
                    a.log = a.min > 0f && a.max >= a.min * LogSpread;
                }
            }
        }

        static float Norm(Axis a, float v)
        {
            float t;
            if (float.IsPositiveInfinity(v)) t = 1f;
            else if (a.max - a.min < 1e-5f) t = 0.5f;
            else if (a.log) t = Mathf.InverseLerp(Mathf.Log(a.min), Mathf.Log(a.max), Mathf.Log(Mathf.Max(v, a.min)));
            else t = Mathf.InverseLerp(a.min, a.max, v);
            return a.higherBetter ? t : 1f - t;
        }

        public sealed class Pair
        {
            internal string[] names, valueText;
            internal float[] mainNorm, otherNorm;
            internal int[] diff;
            public bool Compared => otherNorm != null;
        }

        static readonly Dictionary<(DuelWeapon, DuelWeapon), Pair> cache = new();

        // カタログの武装は固定。装備の残弾・現在の EN によってグラフは動かさない。
        public static Pair Get(DuelWeapon main, DuelWeapon other = null)
        {
            if (main == null) return null;
            if (other != null && other.Slot != main.Slot) other = null;
            var key = (main, other);
            if (cache.TryGetValue(key, out var p)) return p;

            Init();
            var ax = axes[(int)main.Slot];
            int n = ax.Length;
            p = new Pair { names = new string[n], valueText = new string[n], mainNorm = new float[n] };
            if (other != null)
            {
                p.otherNorm = new float[n];
                p.diff = new int[n];
            }
            for (int i = 0; i < n; i++)
            {
                var a = ax[i];
                float v = a.value(main);
                string tv = a.text(main, v);
                p.names[i] = a.name;
                p.mainNorm[i] = Norm(a, v);
                p.valueText[i] = tv;
                if (other == null) continue;

                float o = a.value(other);
                int d = Difference(a, v, o);
                p.otherNorm[i] = Norm(a, o);
                p.diff[i] = d;
                string body = a.text(other, o) + " → " + tv;
                p.valueText[i] = d == 0 ? body : $"<color={(d > 0 ? GoodHex : BadHex)}>{body}</color>";
            }
            cache.Add(key, p);
            return p;
        }

        static int Difference(Axis axis, float main, float other)
        {
            if (main == other) return 0;
            float lower = Mathf.Min(main, other), higher = Mathf.Max(main, other);
            if (higher < lower * EqualRatio) return 0;
            return (main > other) == axis.higherBetter ? 1 : -1;
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
        /// 軸の名前は、相手より良ければ緑・悪ければ赤。r は GUI の座標(uiScale 倍する前)。
        /// </summary>
        public static void DrawRadar(Rect r, float uiScale, Pair p)
        {
            if (p == null) return;
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

            if (Event.current.type == EventType.Repaint && Begin())
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
        /// 装備画面では選んだ武装の帯1本と数値を描く。
        /// </summary>
        public static void DrawBars(Rect r, Pair p, float labelW = 66f, float textW = 112f)
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
                GUI.color = Color.white;
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

        static bool Begin()
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
