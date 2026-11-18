using UnityEngine;
using UnityEngine.Rendering;

namespace HuntProto
{
    /// <summary>
    /// 武装の性能をレーダーチャートで描く(OnGUI の中から呼ぶ)。
    /// 軸は種類ごとのパラメータ。外側ほど良い向き(反動・重量・装填・燃費は小さいほど外側)で、
    /// 真ん中の輪がその種類の標準の武装。軸が3本未満の種類は横棒で描く。
    /// </summary>
    public static class ProtoRadar
    {
        public static readonly Color MainColor = new(0.35f, 0.8f, 1f);
        public static readonly Color CompareColor = new(1f, 0.85f, 0.4f);
        static readonly Color Good = new(0.45f, 1f, 0.55f), Bad = new(1f, 0.45f, 0.4f), Plain = new(0.85f, 0.87f, 0.9f);

        static Material mat;
        static GUIStyle axisStyle;

        /// <summary>
        /// main を塗りつぶしで、compare(同じ種類のときだけ)を線で重ねる。
        /// r は GUI の座標(GUI.matrix で uiScale 倍される前の値)。swing は目盛りの幅の倍率(ProtoGame.radarScale)。
        /// mainUses / compareUses は今の残り回数(耐久の軸に使う。負なら最大値)。
        /// </summary>
        public static void Draw(Rect r, float uiScale, PartDefinition main, PartDefinition compare, float swing, Color mainColor, GUIStyle labelStyle,
            int mainUses = -1, int compareUses = -1)
        {
            if (main == null || !main.HasStats) return;
            if (compare != null && (!compare.HasStats || compare.type != main.type)) compare = null;
            var stats = PartDefinition.StatsOf(main.type);
            if (axisStyle == null || axisStyle.fontSize != labelStyle.fontSize)
                axisStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            if (stats.Length < 3) { DrawBars(r, uiScale, main, compare, swing, mainColor, stats, mainUses, compareUses); return; }

            int n = stats.Length;
            Vector2 c = r.center;
            float labelPad = axisStyle.fontSize + 8f;
            float R = Mathf.Min(r.width, r.height) * 0.5f - labelPad;
            Vector2 Dir(int i) { float a = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / n; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
            Vector2 P(int i, float v) => c + Dir(i) * (R * v);

            if (Event.current.type == EventType.Repaint && Begin(uiScale))
            {
                // 目盛り:4段の輪(真ん中=元の性能だけ明るく)と軸
                for (int k = 1; k <= 4; k++)
                {
                    float v = Radius(k * 0.25f);
                    var col = k == 2 ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.12f);
                    for (int i = 0; i < n; i++) Line(P(i, v), P((i + 1) % n, v), k == 2 ? 1.5f : 1f, col, uiScale);
                }
                for (int i = 0; i < n; i++) Line(c, P(i, 1f), 1f, new Color(1f, 1f, 1f, 0.12f), uiScale);

                // 本体:塗り+縁取り
                var pts = new Vector2[n];
                for (int i = 0; i < n; i++) pts[i] = P(i, Radius(main.Rating(stats[i], swing, mainUses)));
                GL.Begin(GL.TRIANGLES);
                GL.Color(new Color(mainColor.r, mainColor.g, mainColor.b, 0.32f));
                for (int i = 0; i < n; i++) { V(c, uiScale); V(pts[i], uiScale); V(pts[(i + 1) % n], uiScale); }
                GL.End();
                for (int i = 0; i < n; i++) Line(pts[i], pts[(i + 1) % n], 2f, mainColor, uiScale);

                // 比べる相手:線だけ
                if (compare != null)
                    for (int i = 0; i < n; i++)
                        Line(P(i, Radius(compare.Rating(stats[i], swing, compareUses))), P((i + 1) % n, Radius(compare.Rating(stats[(i + 1) % n], swing, compareUses))), 2f, CompareColor, uiScale);
                GL.PopMatrix();
            }

            // 軸の名前(比べる相手より良ければ緑、悪ければ赤)
            for (int i = 0; i < n; i++)
            {
                Vector2 lp = c + Dir(i) * (R + labelPad * 0.6f);
                axisStyle.normal.textColor = AxisColor(stats[i], main, compare, mainUses, compareUses);
                GUI.Label(new Rect(lp.x - 30f, lp.y - 10f, 60f, 20f), PartDefinition.RadarName(stats[i]), axisStyle);
            }
        }

        /// <summary>軸が少ない種類:横棒(中央の縦線が元の性能)。</summary>
        static void DrawBars(Rect r, float uiScale, PartDefinition main, PartDefinition compare, float swing, Color mainColor, PartDefinition.Stat[] stats,
            int mainUses, int compareUses)
        {
            float rowH = Mathf.Min(34f, r.height / stats.Length);
            float labelW = 60f;
            float y = r.center.y - rowH * stats.Length * 0.5f;
            foreach (var s in stats)
            {
                var bar = new Rect(r.x + labelW, y + rowH * 0.3f, r.width - labelW - 6f, rowH * 0.4f);
                if (Event.current.type == EventType.Repaint && Begin(uiScale))
                {
                    Quad(bar, new Color(1f, 1f, 1f, 0.1f), uiScale);
                    Quad(new Rect(bar.x, bar.y, bar.width * Radius(main.Rating(s, swing, mainUses)), bar.height), new Color(mainColor.r, mainColor.g, mainColor.b, 0.75f), uiScale);
                    float mid = bar.x + bar.width * Radius(0.5f);
                    Line(new Vector2(mid, bar.y - 3f), new Vector2(mid, bar.yMax + 3f), 1.5f, new Color(1f, 1f, 1f, 0.45f), uiScale);
                    if (compare != null)
                    {
                        float cx = bar.x + bar.width * Radius(compare.Rating(s, swing, compareUses));
                        Line(new Vector2(cx, bar.y - 4f), new Vector2(cx, bar.yMax + 4f), 2.5f, CompareColor, uiScale);
                    }
                    GL.PopMatrix();
                }
                axisStyle.normal.textColor = AxisColor(s, main, compare, mainUses, compareUses);
                var st = new GUIStyle(axisStyle) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(r.x, y, labelW, rowH), PartDefinition.RadarName(s), st);
                y += rowH;
            }
        }

        /// <summary>評価(0〜1)を半径へ。0でも点にならないよう少しだけ底上げする。</summary>
        static float Radius(float rating) => 0.12f + 0.88f * rating;

        static Color AxisColor(PartDefinition.Stat s, PartDefinition main, PartDefinition compare, int mainUses, int compareUses)
        {
            if (compare == null) return Plain;
            float ratio = main.StatValue(s, mainUses) / Mathf.Max(0.0001f, compare.StatValue(s, compareUses));
            if (PartDefinition.Arrow(ratio).Length == 0) return Plain;
            return (ratio > 1f) == PartDefinition.HigherIsBetter(s) ? Good : Bad;
        }

        // ───────── GL(GUI の座標を uiScale 倍して画面の画素へ) ─────────

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

        static void Line(Vector2 a, Vector2 b, float width, Color col, float s)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            Vector2 nrm = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
            GL.Begin(GL.QUADS);
            GL.Color(col);
            V(a - nrm, s); V(a + nrm, s); V(b + nrm, s); V(b - nrm, s);
            GL.End();
        }

        static void Quad(Rect r, Color col, float s)
        {
            GL.Begin(GL.QUADS);
            GL.Color(col);
            V(new Vector2(r.x, r.y), s); V(new Vector2(r.xMax, r.y), s); V(new Vector2(r.xMax, r.yMax), s); V(new Vector2(r.x, r.yMax), s);
            GL.End();
        }
    }
}
