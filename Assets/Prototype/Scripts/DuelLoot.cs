using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public enum DuelSupply { RepairArmor }

    /// <summary>
    /// 落ちている武装と補給物資。倒した敵や宝箱から出て、床まで落ちて光の柱を立てる。時間では消えない。
    /// 自機が近くにいるときに△(C)で武装を調べるか、補給物資を拾う。無視すれば残る。
    /// 付け替えた古い武装は、今拾った武装のあった所にそのまま置く(その場で入れ替え)。置いた直後から拾えるが、拾う案内の優先度は低い
    /// (LowerPriority。作者「落とした後すぐ触れるようにはしてほしい(優先度を下げるだけ)」):拾える範囲にふつうの武装があればそちらが対象で、
    /// なければ外した武装が対象になる。優先度が戻るのは、自機がその武装の拾える範囲から一度出たときだけ(時間では戻さない。RestorePriorities)。
    /// ボスを倒したら全部消える。
    /// </summary>
    public class DuelLoot : MonoBehaviour
    {
        /// <summary>拾える水平の距離(機体の半径に足す)と、上下の差。</summary>
        public const float PickRadius = 8f, PickHeight = 12f;
        /// <summary>床からの浮き(見た目の中心の高さ)。</summary>
        const float Hover = 1.5f;

        public DuelWeapon Weapon { get; private set; }
        public int SubUses { get; set; }
        public DuelSupply? Supply { get; private set; }
        public string DisplayName => Supply switch
        {
            DuelSupply.RepairArmor => "補修用装甲",
            _ => Weapon.name,
        };

        internal static readonly Color RepairTint = new(0.35f, 1f, 0.65f);
        public Color Tint => Supply switch
        {
            DuelSupply.RepairArmor => RepairTint,
            _ => Weapon.Color,
        };

        /// <summary>
        /// 撃つ状態(弾倉の残り・装填待ち)。敵が落とした銃の弾倉は空。付け替えで外した銃は、撃ちかけの残りと装填待ちを覚えておく
        /// (拾い直しで満タン・待ちなしにならないように)。
        /// </summary>
        public DuelArms.Memo Memo { get; set; } = new();

        static readonly List<DuelLoot> all = new();
        /// <summary>落ちている武装と補給物資の全部。</summary>
        public static IReadOnlyList<DuelLoot> All => all;
        Transform visual;
        float vy, t;
        Vector3 kick;
        bool landed;
        /// <summary>拾う案内の優先度が低い(付け替えで床に置いた武装。自機が拾える範囲から一度出るまで)。</summary>
        bool lowPriority;
        /// <summary>優先度の低い武装が1つでもあるか(なければ RestorePriorities は何もしない)。</summary>
        static bool anyLowPriority;

        /// <summary>pos に武装を落とす(少し跳ねて、真下の床まで落ちる)。</summary>
        public static DuelLoot Drop(DuelWeapon w, Vector3 pos, Vector3 kick) => Spawn(w, null, pos, 12f, kick);

        public static DuelLoot Drop(DuelSupply supply, Vector3 pos, Vector3 kick) => Spawn(null, supply, pos, 12f, kick);

        public static DuelLoot PlaceSupply(DuelSupply supply, Vector3 pos) => Spawn(null, supply, pos, 0f, Vector3.zero);

        /// <summary>pos にそのまま置く(勢いなし。付け替えで外した武装を、拾った武装のあった所に置く)。</summary>
        public static DuelLoot Place(DuelWeapon w, Vector3 pos) => Spawn(w, null, pos, 0f, Vector3.zero);

        public static bool HasFloorAtFeet(DuelPlayer player) => FindFloorAtFeet(player, out _);

        public static bool TryPlaceAtFeet(DuelWeapon weapon, DuelPlayer player, out DuelLoot loot)
        {
            loot = null;
            if (!FindFloorAtFeet(player, out var floor)) return false;
            loot = Place(weapon, floor.point + Vector3.up * Hover);
            return true;
        }

        static bool FindFloorAtFeet(DuelPlayer player, out RaycastHit floor)
        {
            floor = default;
            return !player.Airborne && Physics.Raycast(player.transform.position + Vector3.up * 2f, Vector3.down,
                out floor, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static DuelLoot Spawn(DuelWeapon w, DuelSupply? supply, Vector3 pos, float vy, Vector3 kick)
        {
            var go = new GameObject();
            go.transform.position = pos;
            var l = go.AddComponent<DuelLoot>();
            l.Weapon = w;
            l.SubUses = w?.SubUses ?? 0;
            l.Supply = supply;
            go.name = $"Loot_{l.DisplayName}";
            l.vy = vy;
            l.kick = kick;
            l.Build();
            all.Add(l);
            return l;
        }

        /// <summary>落ちている武装と補給物資を全部消す(ボスを倒したとき)。</summary>
        public static void ClearAll()
        {
            foreach (var l in all)
            {
                if (l == null) continue;
                ProtoFx.Blast(l.transform.position + Vector3.up, 3f, l.Tint, 0.25f);
                Destroy(l.gameObject);
            }
            all.Clear();
        }

        /// <summary>
        /// 拾う案内の優先度を下げる(付け替えで床に置いた武装。DuelArms.Equip)。置いた直後から拾えるが、
        /// 拾える範囲にふつうの武装があれば、そちらが先に案内の対象になる(Nearest)。
        /// 戻るのは、自機がその武装の拾える範囲から一度出たときだけ(RestorePriorities)。
        /// </summary>
        public void LowerPriority()
        {
            lowPriority = true;
            anyLowPriority = true;
        }

        /// <summary>
        /// 優先度の低い武装のうち、自機が拾える範囲(距離・高さ。見通しは見ない)から出たものを、ふつうの優先度に戻す
        /// (DuelArms.Tick から毎フレーム。空中にいて Nearest を呼ばない間も、範囲から出たことを数える。範囲の中で空中に出ただけでは戻さない)。
        /// 時間では戻さない:戻る武装はそのとき範囲の外なので、自機が動いていないのに案内の対象が入れ替わることがない
        /// (読んでいるカードがすり替わる・付け先や分解を選んでいる途中の表示が Choosing != Nearby で黙って閉じる、を起こさない)。
        /// </summary>
        public static void RestorePriorities(DuelFighter who)
        {
            if (!anyLowPriority) return;
            anyLowPriority = false;
            foreach (var l in all)
            {
                if (l == null || !l.lowPriority) continue;
                if (!l.InPickRange(who, out _)) l.lowPriority = false;
                else anyLowPriority = true;
            }
        }

        /// <summary>自機が拾える距離・高さの内にいるか(見通しは見ない)。dist は水平の距離。</summary>
        bool InPickRange(DuelFighter who, out float dist)
        {
            Vector3 d = who.transform.position - transform.position;
            float dy = d.y;
            d.y = 0f;
            dist = d.magnitude;
            return dist <= PickRadius + who.Radius && Mathf.Abs(dy) < PickHeight;
        }

        /// <summary>
        /// この武装をいま拾えるか(拾える距離・高さの内で、壁・床ごしでない。Nearest と同じ条件)。
        /// 入れ替え・解体を選んでいる途中の武装を、対象のまま保つかの判定に使う(DuelArms.Tick)。
        /// </summary>
        public bool CanPick(DuelFighter who) =>
            InPickRange(who, out _) && DuelSenses.ClearSight(who.Chest, transform.position + Vector3.up);

        /// <summary>
        /// 拾う案内の対象は、範囲内で見通せるもの。付け替えで置いた武装の優先度は低い。
        /// 同じ優先度なら一番近いものを案内する。
        /// </summary>
        public static DuelLoot Nearest(DuelFighter who)
        {
            DuelLoot best = null;
            int bestPriority = -1;
            float bestDistance = float.MaxValue;
            foreach (var loot in all)
            {
                if (loot == null) continue;
                int priority = loot.lowPriority ? 1 : 2;
                if (priority < bestPriority) continue;
                if (!loot.InPickRange(who, out float distance) || (priority == bestPriority && distance >= bestDistance)) continue;
                if (!DuelSenses.ClearSight(who.Chest, loot.transform.position + Vector3.up)) continue;
                best = loot;
                bestPriority = priority;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>拾った:光って消える。</summary>
        public void Take()
        {
            ProtoFx.Blast(transform.position + Vector3.up, 4f, Tint, 0.3f);
            all.Remove(this);
            Destroy(gameObject);
        }

        void OnDestroy() => all.Remove(this);

        void Build()
        {
            float w, h;
            if (Weapon != null)
            {
                visual = DuelArms.CreateVisual(Weapon, transform);
                w = Weapon.rarity >= 2 ? 0.9f : Weapon.rarity == 1 ? 0.65f : 0.45f;
                h = 14f;
            }
            else
            {
                // 色付きの帯がある補給箱と、武装より短い光の柱。
                visual = new GameObject("SupplyCrate").transform;
                visual.SetParent(transform, false);
                Marker(PrimitiveType.Cube, visual, Vector3.zero, new Vector3(2.4f, 1.35f, 1.65f), new Color(0.18f, 0.23f, 0.26f));
                Marker(PrimitiveType.Cube, visual, Vector3.zero, new Vector3(0.55f, 1.4f, 1.7f), Tint);
                w = 0.32f;
                h = 6f;
            }
            Marker(PrimitiveType.Cylinder, transform, Vector3.up * h, new Vector3(w, h, w), Tint);
        }

        static void Marker(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var marker = GameObject.CreatePrimitive(type).transform;
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.SetParent(parent, false);
            marker.localPosition = pos;
            marker.localScale = scale;
            var renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = ProtoFx.Unlit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
