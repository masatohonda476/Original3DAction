using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 落ちている武装(ステージだけ)。倒した敵が落とし、真下の床まで落ちて、レア度の色の光の柱を立てる。時間では消えない。
    /// 自機が近くにいると比べた内容が画面に出て、十字右(V)で付ける(DuelArms)。無視すれば残る。付け替えた古い武装も足もとに落ちる。
    /// </summary>
    public class DuelLoot : MonoBehaviour
    {
        /// <summary>拾える水平の距離(機体の半径に足す)と、上下の差。</summary>
        public const float PickRadius = 8f, PickHeight = 12f;

        public DuelWeapon Weapon { get; private set; }

        static readonly List<DuelLoot> all = new();
        Transform visual;
        float groundY, vy, t;
        Vector3 kick;

        /// <summary>pos に武装を落とす(少し跳ねて、真下の床まで落ちる)。</summary>
        public static DuelLoot Drop(DuelWeapon w, Vector3 pos, Vector3 kick)
        {
            var go = new GameObject($"Loot_{w.name}");
            go.transform.position = pos;
            var l = go.AddComponent<DuelLoot>();
            l.Weapon = w;
            l.vy = 12f;
            l.kick = kick;
            l.Build();
            all.Add(l);
            return l;
        }

        /// <summary>拾える範囲にある一番近い武装(なければ null)。</summary>
        public static DuelLoot Nearest(DuelFighter who)
        {
            DuelLoot best = null;
            float bestD = float.MaxValue;
            foreach (var l in all)
            {
                if (l == null) continue;
                Vector3 d = who.transform.position - l.transform.position;
                float dy = d.y;
                d.y = 0f;
                if (d.magnitude > PickRadius + who.Radius || Mathf.Abs(dy) >= PickHeight) continue;
                if (d.magnitude < bestD) { bestD = d.magnitude; best = l; }
            }
            return best;
        }

        /// <summary>拾った:光って消える。</summary>
        public void Take()
        {
            ProtoFx.Blast(transform.position + Vector3.up, 4f, Weapon.Color, 0.3f);
            Destroy(gameObject);
        }

        void OnDestroy() => all.Remove(this);

        void Build()
        {
            visual = DuelArms.CreateVisual(Weapon, transform);
            // 光の柱(レア度の色。エピックは太く)
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(beam.GetComponent<Collider>());
            beam.SetParent(transform, false);
            float w = Weapon.rarity >= 2 ? 0.9f : Weapon.rarity == 1 ? 0.65f : 0.45f;
            beam.localScale = new Vector3(w, 14f, w);
            beam.localPosition = new Vector3(0f, 14f, 0f);
            var r = beam.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(Weapon.Color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            groundY = GroundBelow(transform.position);
        }

        /// <summary>真下の床の高さ(機体・見えない壁は Ignore Raycast なので当たらない)。</summary>
        static float GroundBelow(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point.y : -500f;

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            Vector3 p = transform.position;
            if (p.y > groundY + 1.5f || vy > 0f)
            {
                vy -= 30f * dt;
                Vector3 next = p + (kick + Vector3.up * vy) * dt;
                // 横へ飛び散る途中で壁に当たったら、横の勢いを消す
                Vector3 side = new(next.x - p.x, 0f, next.z - p.z);
                if (side.sqrMagnitude > 0.0001f && Physics.Raycast(p, side.normalized, side.magnitude + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    next.x = p.x;
                    next.z = p.z;
                    kick = Vector3.zero;
                }
                kick = Vector3.MoveTowards(kick, Vector3.zero, 10f * dt);
                groundY = GroundBelow(next);
                if (next.y < groundY + 1.5f) { next.y = groundY + 1.5f; vy = 0f; kick = Vector3.zero; }
                transform.position = next;
            }
            visual.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
            visual.localPosition = Vector3.up * (1f + Mathf.Sin(t * 3f) * 0.4f);
            if (transform.position.y < -200f) Destroy(gameObject);
        }
    }
}
