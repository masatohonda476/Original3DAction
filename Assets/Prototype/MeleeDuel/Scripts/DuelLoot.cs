using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 落ちている武装(ステージだけ)。倒した敵が落とし、真下の床まで落ちて、レア度の色の光の柱を立てる。時間では消えない。
    /// 自機が近くにいる(壁ごしでない)と比べた内容が画面に出て、十字右(V)で付ける(手・肩は付け先を R1/L1・R2/L2 で選ぶ。DuelArms)、
    /// 十字下(G)で所持品へ、十字上(F)で分解(弾か回復)。無視すれば残る。
    /// 付け替えた古い武装は、今拾った武装のあった所にそのまま置く(その場で入れ替え)。ボスを倒したら全部消える。
    /// </summary>
    public class DuelLoot : MonoBehaviour
    {
        /// <summary>拾える水平の距離(機体の半径に足す)と、上下の差。</summary>
        public const float PickRadius = 8f, PickHeight = 12f;
        /// <summary>床からの浮き(見た目の中心の高さ)。</summary>
        const float Hover = 1.5f;

        public DuelWeapon Weapon { get; private set; }
        /// <summary>弾倉の残り(-1=満タン。捨てた・付け替えで外した銃は、撃ちかけの残りを覚えておく。拾い直しで満タンにならないように)。</summary>
        public int Mag { get; set; } = -1;

        static readonly List<DuelLoot> all = new();
        Transform visual;
        float vy, t;
        Vector3 kick;
        bool landed;

        /// <summary>pos に武装を落とす(少し跳ねて、真下の床まで落ちる)。</summary>
        public static DuelLoot Drop(DuelWeapon w, Vector3 pos, Vector3 kick) => Spawn(w, pos, 12f, kick);

        /// <summary>pos にそのまま置く(勢いなし。付け替えで外した武装を、拾った武装のあった所に置く)。</summary>
        public static DuelLoot Place(DuelWeapon w, Vector3 pos) => Spawn(w, pos, 0f, Vector3.zero);

        static DuelLoot Spawn(DuelWeapon w, Vector3 pos, float vy, Vector3 kick)
        {
            var go = new GameObject($"Loot_{w.name}");
            go.transform.position = pos;
            var l = go.AddComponent<DuelLoot>();
            l.Weapon = w;
            l.vy = vy;
            l.kick = kick;
            l.Build();
            all.Add(l);
            return l;
        }

        /// <summary>落ちている武装を全部消す(ボスを倒したとき)。</summary>
        public static void ClearAll()
        {
            foreach (var l in all)
            {
                if (l == null) continue;
                ProtoFx.Blast(l.transform.position + Vector3.up, 3f, l.Weapon.Color, 0.25f);
                Destroy(l.gameObject);
            }
            all.Clear();
        }

        /// <summary>拾える範囲にある一番近い武装(壁・床ごしのものは除く。なければ null)。</summary>
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
                float dist = d.magnitude;
                if (dist > PickRadius + who.Radius || Mathf.Abs(dy) >= PickHeight || dist >= bestD) continue;
                if (!DuelSenses.ClearSight(who.Chest, l.transform.position + Vector3.up)) continue;
                bestD = dist;
                best = l;
            }
            return best;
        }

        /// <summary>拾った:光って消える。</summary>
        public void Take()
        {
            ProtoFx.Blast(transform.position + Vector3.up, 4f, Weapon.Color, 0.3f);
            all.Remove(this);
            Destroy(gameObject);
        }

        void OnDestroy() => all.Remove(this);

        void Build()
        {
            visual = DuelArms.CreateVisual(Weapon, transform);
            // 光の柱(レア度の色。エピックは太く)
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            DestroyImmediate(beam.GetComponent<Collider>());
            beam.SetParent(transform, false);
            float w = Weapon.rarity >= 2 ? 0.9f : Weapon.rarity == 1 ? 0.65f : 0.45f;
            beam.localScale = new Vector3(w, 14f, w);
            beam.localPosition = new Vector3(0f, 14f, 0f);
            var r = beam.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(Weapon.Color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>地形と見えない壁(Ignore Raycast の層)。横へ飛び散るときは見えない壁でも止める。</summary>
        static readonly int SideMask = Physics.DefaultRaycastLayers | (1 << DuelFighter.FighterLayer);

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            visual.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
            visual.localPosition = Vector3.up * (1f + Mathf.Sin(t * 3f) * 0.4f);
            if (landed || dt <= 0f) return;

            Vector3 p = transform.position;
            vy -= 30f * dt;
            Vector3 next = p + (kick + Vector3.up * vy) * dt;
            kick = Vector3.MoveTowards(kick, Vector3.zero, 10f * dt);
            // 横:壁・見えない壁に当たったら横の勢いを消す(機体に当たっても止まるだけ)
            Vector3 side = new(next.x - p.x, 0f, next.z - p.z);
            if (side.sqrMagnitude > 0.000001f &&
                Physics.Raycast(p, side.normalized, side.magnitude + 1f, SideMask, QueryTriggerInteraction.Ignore))
            {
                next.x = p.x;
                next.z = p.z;
                kick = Vector3.zero;
            }
            // 上:天井に当たったら上がるのをやめる
            if (vy > 0f && Physics.Raycast(p, Vector3.up, out var up, next.y - p.y + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                next.y = Mathf.Min(next.y, up.point.y - 0.5f);
                vy = 0f;
            }
            // 下:今の高さから次の高さ(の床からの浮きの分下)までをまとめて調べる。1フレームが長くても床を抜けない
            if (vy <= 0f)
            {
                Vector3 from = new(next.x, p.y, next.z);
                float len = p.y - next.y + Hover;
                if (Physics.Raycast(from, Vector3.down, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    next.y = hit.point.y + Hover;
                    vy = 0f;
                    kick = Vector3.zero;
                    landed = true;
                }
            }
            transform.position = next;
            if (next.y < -200f) Destroy(gameObject);
        }
    }
}
