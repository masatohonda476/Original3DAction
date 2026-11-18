using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 噴射口の炎(背中に2か所・左右の脇腹に2か所)。飛んでいる間だけ出し、長さが細かく揺らぐ。
    /// 噴射口はモデルの穴の位置(胸部の骨格の、穴の空いた箱)。背中の取付点(骨)からの位置と向きで持ち、体の傾き・動きについていく。
    /// 根元は白っぽい黄色、その先はオレンジ。飛び始めにふっと伸び、止めるとすっと消える。
    /// </summary>
    public class DuelThruster : MonoBehaviour
    {
        [Tooltip("噴射口の穴の位置(背中の取付点から見た位置。機体の大きさは自動で掛かる)。0,1 = 背中の左右 / 2,3 = 脇腹の左右")]
        public Vector3[] nozzles =
        {
            // モデルの穴の位置(機体の根元から (-0.42,6.97,-0.72) など)から、背中の取付点 (0,6.17,-1.67) を引いた値
            new(-0.42f, 0.80f, 0.95f), new(0.21f, 0.81f, 0.88f),
            new(-0.54f, 0.86f, 1.91f), new(0.56f, 0.87f, 1.76f),
        };
        [Tooltip("噴射口の穴の向き(背中の取付点から見た向き)。背中は後ろやや下、脇腹は外向きやや下")]
        public Vector3[] directions =
        {
            new(0f, -0.42f, -0.91f), new(0f, -0.42f, -0.91f),
            new(-0.71f, -0.71f, 0f), new(0.71f, -0.71f, 0f),
        };
        [Tooltip("炎の長さ(m)。背中 / 脇腹")]
        public float backLength = 3f, sideLength = 1.8f;
        [Tooltip("炎の太さ(m)。背中 / 脇腹(穴の大きさに合わせる)")]
        public float backWidth = 0.4f, sideWidth = 0.22f;

        static readonly Color Outer = new(1f, 0.5f, 0.12f), Core = new(1f, 0.92f, 0.6f);

        DuelFighter owner;
        Transform anchor;   // 背中の取付点(なければ機体の見た目の根元)
        Transform body;     // 機体の見た目の根元(傾きの向きを取る)
        Transform[] outer, core;
        float power;

        public void Init(DuelFighter f, MechRig rig)
        {
            owner = f;
            body = rig != null ? rig.transform : f.transform;
            anchor = rig != null && rig.mountBack != null ? rig.mountBack : body;
            outer = new Transform[nozzles.Length];
            core = new Transform[nozzles.Length];
            for (int i = 0; i < nozzles.Length; i++)
            {
                outer[i] = MakeFlame("FlameOuter", Outer);
                core[i] = MakeFlame("FlameCore", Core);
            }
        }

        Transform MakeFlame(string name, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            return go.transform;
        }

        void LateUpdate()
        {
            if (owner == null || outer == null) return;
            // 飛んでいる間は出す。飛び始めは速く伸び、止めるとすっと消える
            float target = owner.IsFlying ? 1f : 0f;
            power = Mathf.MoveTowards(power, target, (target > power ? 6f : 4f) * Time.deltaTime);
            bool show = power > 0.02f;
            float scale = body.lossyScale.y;   // 機体の大きさ
            for (int i = 0; i < nozzles.Length; i++)
            {
                outer[i].gameObject.SetActive(show);
                core[i].gameObject.SetActive(show);
                if (!show) continue;
                bool side = i >= 2;
                // 穴の向きへ噴く(骨の回転について向きも変わる)
                Vector3 dir = anchor.TransformDirection(i < directions.Length ? directions[i] : Vector3.down).normalized;
                Vector3 from = anchor.TransformPoint(nozzles[i]);
                float flicker = 1f + (Mathf.PerlinNoise(Time.time * 30f, i * 3.7f) - 0.5f) * 0.5f;
                float len = (side ? sideLength : backLength) * scale * power * flicker;
                float w = (side ? sideWidth : backWidth) * scale;
                Place(outer[i], from, dir, len, w);
                // 芯:噴射口のすぐそばだけ、外側より少し太く短く(根元が明るく光って見える)
                Place(core[i], from, dir, len * 0.3f, w * 1.15f);
            }
        }

        /// <summary>カプセルを、from から dir の向きへ長さ len・太さ w で置く。</summary>
        static void Place(Transform t, Vector3 from, Vector3 dir, float len, float w)
        {
            t.position = from + dir * (len * 0.5f);
            t.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            t.localScale = new Vector3(w, Mathf.Max(0.01f, len * 0.5f), w);   // カプセルは高さ2なので半分
        }
    }
}
