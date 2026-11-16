using System.Collections.Generic;
using UnityEngine;

namespace HuntProto
{
    /// <summary>プロトタイプ用の仮エフェクトと仮マテリアル。見た目は全部ここに集約している。</summary>
    public static class ProtoFx
    {
        static readonly Dictionary<Color, Material> litCache = new();
        static readonly Dictionary<Color, Material> unlitCache = new();

        public static Material Lit(Color c)
        {
            if (!litCache.TryGetValue(c, out var m) || m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                litCache[c] = m;
            }
            return m;
        }

        public static Material Unlit(Color c)
        {
            if (!unlitCache.TryGetValue(c, out var m) || m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", c);
                unlitCache[c] = m;
            }
            return m;
        }

        static GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static void Tracer(Vector3 from, Vector3 to, Color c, float width = 0.3f, float life = 0.05f)
        {
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = width;
            lr.endWidth = width * 0.5f;
            lr.sharedMaterial = Unlit(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(go, life);
        }

        /// <summary>膨らんで消える球。爆発・衝撃波・ヒットの仮表現。</summary>
        public static void Blast(Vector3 pos, float radius, Color c, float life = 0.3f)
        {
            var go = Prim(PrimitiveType.Sphere, pos, Vector3.one * radius * 0.4f, Unlit(c));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var anim = go.AddComponent<FxAnim>();
            anim.Setup(Vector3.one * radius * 0.4f, Vector3.one * radius * 2f, life);
        }

        /// <summary>地面の予兆円(敵キャノンの着弾地点)。</summary>
        public static GameObject Telegraph(Vector3 pos, float radius)
        {
            var go = Prim(PrimitiveType.Cylinder, new Vector3(pos.x, pos.y + 0.05f, pos.z),
                new Vector3(radius * 2f, 0.02f, radius * 2f), Unlit(new Color(0.9f, 0.15f, 0.1f)));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
