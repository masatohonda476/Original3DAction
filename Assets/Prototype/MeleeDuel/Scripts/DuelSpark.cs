using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 攻撃が当たった所から飛び散る火花(筋を引いて飛び、重力で落ちながら細くなって消える)。
    /// ヒットストップ中は時間に合わせて止まる。
    /// </summary>
    public class DuelSpark : MonoBehaviour
    {
        Vector3 vel;
        float life, t, width;
        LineRenderer lr;

        /// <summary>pos から normal の側へ広がるように count 本飛ばす(normal が 0 なら全方向)。size は太さ・速さの倍率。</summary>
        public static void Burst(Vector3 pos, Vector3 normal, Color c, int count, float size = 1f)
        {
            var mat = ProtoFx.Unlit(c);
            Vector3 n = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Spark");
                go.transform.position = pos;
                var s = go.AddComponent<DuelSpark>();
                Vector3 dir = (n * 0.9f + Random.insideUnitSphere).normalized;
                if (dir.sqrMagnitude < 0.001f) dir = Random.onUnitSphere;
                s.vel = dir * 38f * size * Random.Range(0.5f, 1.2f);
                s.life = Random.Range(0.16f, 0.32f);
                s.width = 0.2f * size * Random.Range(0.7f, 1.3f);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.sharedMaterial = mat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                s.lr = lr;
                s.UpdateLine();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            vel += Vector3.down * 45f * dt;
            vel *= Mathf.Exp(-3f * dt);
            transform.position += vel * dt;
            UpdateLine();
            if (t >= life) Destroy(gameObject);
        }

        void UpdateLine()
        {
            float k = Mathf.Clamp01(1f - t / life);
            Vector3 p = transform.position;
            lr.SetPosition(0, p);
            lr.SetPosition(1, p - vel * 0.035f);   // 速いほど長い筋
            lr.startWidth = width * k;
            lr.endWidth = width * 0.3f * k;
        }
    }
}
