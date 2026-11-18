using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// ボスの跳躍叩きつけの衝撃波:地面を這って広がる輪。輪が自機の所を通るときに当たる(1回だけ)。
    /// 空中にいれば当たらない(跳べば越えられる)。回避の無敵でも抜けられる。予告の印ではなく、攻撃そのものの見た目。
    /// </summary>
    public class DuelShockwave : MonoBehaviour
    {
        const int Segments = 64;

        Vector3 center;
        float radius, maxRadius, speed, width;
        DuelHit hit;
        bool done;
        LineRenderer line;

        public static void Spawn(Vector3 center, float startRadius, float maxRadius, float speed, float width, DuelHit hit)
        {
            if (maxRadius <= startRadius) return;
            var go = new GameObject("Shockwave");
            var w = go.AddComponent<DuelShockwave>();
            w.center = center;
            w.radius = startRadius;
            w.maxRadius = maxRadius;
            w.speed = speed;
            w.width = width;
            w.hit = hit;
            w.line = go.AddComponent<LineRenderer>();
            w.line.loop = true;
            w.line.positionCount = Segments;
            w.line.useWorldSpace = true;
            w.line.sharedMaterial = ProtoFx.Unlit(new Color(1f, 0.78f, 0.5f));
            w.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            w.Draw();
        }

        void Update()
        {
            radius += speed * Time.deltaTime;
            if (radius >= maxRadius) { Destroy(gameObject); return; }
            Draw();

            var p = DuelPlayer.I;
            if (done || p == null || p.Dead) return;
            Vector3 d = p.transform.position - center;
            float dy = d.y;
            d.y = 0f;
            // 輪の太さの中に自機が入った。空中(地面から浮いている)・高さが違う所にいれば当たらない
            if (Mathf.Abs(d.magnitude - radius) > width * 0.5f + p.Radius * 0.5f) return;
            done = true;
            if (p.Airborne || Mathf.Abs(dy) > 3f) return;
            p.ReceiveHit(hit);
        }

        void Draw()
        {
            // 広がるほど細く(消えていく)
            float k = Mathf.Clamp01(radius / maxRadius);
            line.startWidth = line.endWidth = width * (1f - k * 0.8f);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.4f, Mathf.Sin(a) * radius));
            }
        }
    }
}
