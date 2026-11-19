using System.Collections.Generic;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機とカメラの間に入ったステージの物(壁・柱・張り出し)を半透明にする(DuelCamera の透かすモード)。
    /// 半透明の作り方:元のマテリアル(URP の Lit)ごとに、透明の設定にした複製を一度だけ作って使い回し、薄くしている間だけ
    /// レンダラーの sharedMaterials をその複製へ差し替える。濃さはレンダラーごとに MaterialPropertyBlock の _BaseColor のアルファで変える
    /// (毎フレーム新しいマテリアルは作らない)。元の濃さへ戻りきったら元のマテリアルへ戻し、PropertyBlock も外す。
    /// 判定:自機の体の6点(腰・胸・頭・左右の肩・脚)からカメラまでのレイ(NonAlloc)。当たった面が立っている(壁・柱の側面)物だけを薄くする。
    /// 床の上面には当たっても薄くしない(坂で自機の立つ床が透けないように)。天井の下面は ceilings なら薄くする。
    /// 機体・見えない壁は Ignore Raycast の層なので当たらない。念のため機体・拾う武装の中の物、レンダラーのない当たり判定、
    /// 元から半透明の物、URP の Lit 系でない物は薄くしない。
    /// </summary>
    sealed class DuelWallFader
    {
        sealed class Entry
        {
            public Renderer r;
            public Material[] originals, faded;
            public Color[] colors;
            public float alpha = 1f, lastHit;
            public bool active;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        /// <summary>体の点(x = カメラの右へ、y = 足もとから上へ。どちらも機体の高さに対する割合。x は fadeBodyWidth を掛ける)。</summary>
        static readonly Vector2[] BodyPoints =
        {
            new(0f, 0.2f), new(0f, 0.5f), new(0f, 0.7f), new(0f, 0.95f), new(-1f, 0.7f), new(1f, 0.7f),
        };

        readonly Dictionary<Collider, Entry> byCollider = new();   // 薄くできない物は null で覚える(毎回調べない)
        readonly Dictionary<Material, Material> copies = new();
        readonly List<Entry> active = new();
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly MaterialPropertyBlock mpb = new();

        /// <summary>自機の体の点からカメラまでの間に入った物に、今の時刻の印を付ける(印が付いている間は薄くする)。</summary>
        public void Mark(DuelPlayer p, Vector3 cam, Vector3 camRight, float bodyWidth, float floorNormalY, bool ceilings)
        {
            Vector3 feet = p.transform.position;
            float h = Mathf.Max(1f, (p.LockPoint.y - feet.y) / 0.7f);   // 機体の高さ(LockPoint は高さの7割)
            float now = Time.unscaledTime;
            foreach (var bp in BodyPoints)
            {
                Vector3 from = feet + Vector3.up * (bp.y * h) + camRight * (bp.x * bodyWidth * h);
                Vector3 d = cam - from;
                float dist = d.magnitude;
                if (dist < 0.05f) continue;
                int n = Physics.RaycastNonAlloc(from, d / dist, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    float ny = hits[i].normal.y;
                    if (ny >= floorNormalY) continue;                // 床の上面:薄くしない
                    if (ny <= -floorNormalY && !ceilings) continue;  // 天井の下面:寄せるなら薄くしない
                    var e = Get(hits[i].collider);
                    if (e == null) continue;
                    e.lastHit = now;
                    if (!e.active)
                    {
                        e.active = true;
                        e.r.sharedMaterials = e.faded;
                        active.Add(e);
                    }
                }
            }
        }

        /// <summary>薄くしている物の濃さを進める。印が holdTime 秒より前なら戻し、戻りきったら元のマテリアルへ。</summary>
        public void Tick(float dt, float minAlpha, float inTime, float outTime, float holdTime)
        {
            if (active.Count == 0) return;
            float now = Time.unscaledTime;
            float span = Mathf.Max(0.01f, 1f - minAlpha);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                if (e.r == null) { RemoveAt(i); continue; }
                bool held = now - e.lastHit <= holdTime;
                float target = held ? minAlpha : 1f;
                float time = target < e.alpha ? inTime : outTime;
                float a = Mathf.MoveTowards(e.alpha, target, span / Mathf.Max(0.01f, time) * dt);
                if (!held && a >= 1f)
                {
                    Restore(e);
                    RemoveAt(i);
                    continue;
                }
                if (!Mathf.Approximately(a, e.alpha) || e.alpha >= 1f) Apply(e, a);
                e.alpha = a;
            }
        }

        /// <summary>すぐに全部元へ戻す(カメラを切った・消したとき)。</summary>
        public void RestoreAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].r != null) Restore(active[i]);
            foreach (var e in active) { e.active = false; e.alpha = 1f; }
            active.Clear();
        }

        /// <summary>元へ戻して、作った透明の複製を消す。</summary>
        public void Dispose()
        {
            RestoreAll();
            foreach (var m in copies.Values) if (m != null) Object.Destroy(m);
            copies.Clear();
            byCollider.Clear();
        }

        void RemoveAt(int i)
        {
            var e = active[i];
            e.active = false;
            e.alpha = 1f;
            active[i] = active[^1];
            active.RemoveAt(active.Count - 1);
        }

        void Apply(Entry e, float a)
        {
            if (e.faded.Length == 1)
            {
                mpb.Clear();
                var c = e.colors[0];
                c.a *= a;
                mpb.SetColor(BaseColorId, c);
                e.r.SetPropertyBlock(mpb);
                return;
            }
            for (int k = 0; k < e.faded.Length; k++)
            {
                mpb.Clear();
                var c = e.colors[k];
                c.a *= a;
                mpb.SetColor(BaseColorId, c);
                e.r.SetPropertyBlock(mpb, k);
            }
        }

        static void Restore(Entry e)
        {
            e.r.sharedMaterials = e.originals;
            if (e.originals.Length == 1) e.r.SetPropertyBlock(null);
            else for (int k = 0; k < e.originals.Length; k++) e.r.SetPropertyBlock(null, k);
        }

        Entry Get(Collider c)
        {
            if (c == null) return null;
            if (byCollider.TryGetValue(c, out var e)) return e;
            e = Make(c);
            byCollider[c] = e;
            return e;
        }

        Entry Make(Collider c)
        {
            if (c.isTrigger || !c.TryGetComponent<Renderer>(out var r)) return null;
            if (c.GetComponentInParent<DuelFighter>() != null || c.GetComponentInParent<DuelLoot>() != null) return null;
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return null;
            var faded = new Material[mats.Length];
            var colors = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty(BaseColorId) || !m.HasProperty(SurfaceId) || m.GetFloat(SurfaceId) > 0.5f) return null;
                faded[i] = Copy(m);
                colors[i] = m.GetColor(BaseColorId);
            }
            return new Entry { r = r, originals = mats, faded = faded, colors = colors };
        }

        /// <summary>元のマテリアルを URP の透明(アルファブレンド)にした複製(元ごとに一度だけ作る。元のアセットは書き換えない)。</summary>
        Material Copy(Material src)
        {
            if (copies.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + " (透かし)" };
            m.SetFloat(SurfaceId, 1f);
            SetIf(m, "_Blend", 0f);                       // アルファ
            SetIf(m, "_BlendModePreserveSpecular", 0f);   // 薄いときに照り返しだけ残らないように
            SetIf(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetIf(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetIf(m, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            SetIf(m, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetIf(m, "_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            copies[src] = m;
            return m;
        }

        static void SetIf(Material m, string name, float v)
        {
            if (m.HasProperty(name)) m.SetFloat(name, v);
        }
    }
}
