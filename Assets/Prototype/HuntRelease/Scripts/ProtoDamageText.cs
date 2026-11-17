using System.Collections.Generic;
using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// 自機が与えたダメージの数字。当たった所に出て、少し浮かびながら消える。
    /// 本体へのダメージは白、部位へのダメージは橙。描画は ProtoGame.OnGUI から呼ぶ。
    /// </summary>
    public static class ProtoDamageText
    {
        public const float Life = 0.8f, MaxDistance = 160f;

        struct Pop { public Vector3 pos; public float dmg, t0; public bool part; public Vector2 jitter; }

        static readonly List<Pop> pops = new();
        static readonly Color BodyColor = Color.white, PartColor = new(1f, 0.6f, 0.15f);
        static GUIStyle style, shadow;

        public static void Show(Vector3 worldPos, float dmg, bool part = false)
        {
            if (dmg <= 0f) return;
            // 連射で数字が重ならないよう、少しずらす
            pops.Add(new Pop { pos = worldPos, dmg = dmg, t0 = Time.time, part = part, jitter = Random.insideUnitCircle * 14f });
        }

        public static void Clear() => pops.Clear();

        /// <summary>uiScale で拡大した GUI.matrix の中で呼ぶ(座標は 1280x720 基準)。</summary>
        public static void Draw(Camera cam, float uiScale, float screenH)
        {
            if (cam == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                shadow = new GUIStyle(style);
                shadow.normal.textColor = Color.black;
            }
            float now = Time.time;
            pops.RemoveAll(p => now - p.t0 > Life);
            foreach (var p in pops)
            {
                float k = (now - p.t0) / Life;
                Vector3 sp = cam.WorldToScreenPoint(p.pos);
                if (sp.z < 0f || sp.z > MaxDistance) continue;
                sp /= uiScale;
                float x = sp.x + p.jitter.x, y = screenH - sp.y + p.jitter.y - 40f * k;   // 浮かび上がる
                float a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                float size = k < 0.15f ? Mathf.Lerp(1.4f, 1f, k / 0.15f) : 1f;          // 出た瞬間だけ大きく
                style.fontSize = Mathf.RoundToInt((p.part ? 24f : 27f) * size);
                shadow.fontSize = style.fontSize;
                string s = Mathf.Max(1, Mathf.RoundToInt(p.dmg)).ToString();
                var r = new Rect(x - 60f, y - 22f, 120f, 44f);
                Color c = p.part ? PartColor : BodyColor;
                shadow.normal.textColor = new Color(0f, 0f, 0f, a * 0.8f);
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, shadow);
                style.normal.textColor = new Color(c.r, c.g, c.b, a);
                GUI.Label(r, s, style);
            }
        }
    }
}
