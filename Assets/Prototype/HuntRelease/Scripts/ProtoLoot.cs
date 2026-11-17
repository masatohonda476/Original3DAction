using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// 倒した敵が落とした武装。地面に落ちて光の柱を立て、自機が近づくと拾う(在庫へ)。
    /// 見た目は部位そのもの(品質の色)。時間では消えない(谷底へ落ちたものだけ消える)。
    /// </summary>
    public class ProtoLoot : MonoBehaviour
    {
        public const float PickRadius = 8f;

        PartDefinition def;   // null なら回復アイテム
        public PartDefinition Def => def;
        /// <summary>残り回数(捨てたものは捨てた時点の回数、新品は定義どおり。-1 は外れない)。</summary>
        public int Uses => keptUses != -2 ? keptUses : def != null && def.uses > 0 ? def.uses : -1;
        /// <summary>自機が捨てたもの:消えずに残り、残り回数を引き継ぐ。一度離れるまでは自動で拾わない(現行の方式用)。</summary>
        bool discarded, armed = true;
        int keptUses = -2;   // -2 = 定義どおり(新品)
        /// <summary>落ちている武装(パラメータ制では自動で拾わず、近くで△を押して拾う)。</summary>
        static readonly System.Collections.Generic.List<ProtoLoot> weapons = new();

        /// <summary>拾える範囲にある一番近い武装(なければ null)。</summary>
        public static ProtoLoot NearestWeapon(ProtoPlayer pl)
        {
            ProtoLoot best = null;
            float bestD = float.MaxValue;
            foreach (var l in weapons)
            {
                if (l == null) continue;
                Vector3 d = pl.transform.position - l.transform.position;
                float dy = d.y;
                d.y = 0f;
                if (d.magnitude > PickRadius + pl.Radius || Mathf.Abs(dy) >= 12f) continue;
                if (d.magnitude < bestD) { bestD = d.magnitude; best = l; }
            }
            return best;
        }

        /// <summary>その場で装備したとき:在庫を通さないので、演出を出して消えるだけ。</summary>
        public void Take()
        {
            ProtoFx.Blast(transform.position, 4f, Tint, 0.3f);
            Destroy(gameObject);
        }

        /// <summary>△で拾う。</summary>
        public void PickUp(ProtoPlayer pl)
        {
            ProtoFx.Blast(transform.position, 4f, Tint, 0.3f);
            Give(pl);
            Destroy(gameObject);
        }

        void Give(ProtoPlayer pl)
        {
            if (keptUses != -2) pl.Acquire(def, keptUses);
            else pl.Acquire(def);
        }

        /// <summary>自機が捨てた武装を足元に落とす(拾い直せる。消えない)。uses は残り回数(-1 は外れない)。</summary>
        public static void DropDiscarded(PartDefinition def, int uses, Vector3 pos, Vector3 kick)
        {
            var go = new GameObject($"Loot_{def.displayName}(捨てた)");
            go.transform.position = pos;
            var l = go.AddComponent<ProtoLoot>();
            l.def = def;
            l.vy = 8f;
            l.kick = kick;
            l.discarded = true;
            l.armed = false;
            l.keptUses = uses;
            l.Build();
            weapons.Add(l);
        }

        static bool ManualPick => ProtoGame.Arms != ArmsRule.Classic;

        void OnDestroy() => weapons.Remove(this);
        static readonly Color HealColor = new(0.3f, 1f, 0.5f);
        Transform visual, beam;
        float groundY, vy, t;

        public static void Drop(PartDefinition def, Vector3 pos, Vector3 kick)
        {
            var go = new GameObject($"Loot_{def.displayName}");
            go.transform.position = pos;
            var l = go.AddComponent<ProtoLoot>();
            l.def = def;
            l.vy = 10f;
            l.kick = kick;
            l.Build();
            weapons.Add(l);
        }

        /// <summary>回復アイテム(緑の十字)を落とす。</summary>
        public static void DropHeal(Vector3 pos, Vector3 kick)
        {
            var go = new GameObject("Loot_回復");
            go.transform.position = pos;
            var l = go.AddComponent<ProtoLoot>();
            l.vy = 10f;
            l.kick = kick;
            l.Build();
        }

        Vector3 kick;
        Color Tint => def != null ? def.GradeColor : HealColor;

        void Build()
        {
            if (def != null) visual = MechFactory.CreatePartVisual(def, transform, Vector3.zero, false).transform;
            else
            {
                // 仮の見た目:緑の十字
                visual = new GameObject("Cross").transform;
                visual.SetParent(transform, false);
                foreach (var sc in new[] { new Vector3(2.4f, 0.8f, 0.8f), new Vector3(0.8f, 2.4f, 0.8f) })
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(b.GetComponent<Collider>());
                    b.transform.SetParent(visual, false);
                    b.transform.localScale = sc;
                    b.transform.localPosition = Vector3.up * 1f;
                    b.GetComponent<Renderer>().sharedMaterial = ProtoFx.Unlit(HealColor);
                }
            }
            beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(beam.GetComponent<Collider>());
            beam.SetParent(transform, false);
            beam.localScale = new Vector3(0.5f, 14f, 0.5f);
            beam.localPosition = new Vector3(0f, 14f, 0f);
            var r = beam.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(Tint);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            groundY = ProtoStage.GroundHeight(transform.position);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;

            // 落下(真下の足場まで)。横へは少しだけ飛び散る
            Vector3 p = transform.position;
            if (p.y > groundY + 1.5f || vy > 0f)
            {
                vy -= 30f * dt;
                p += (kick + Vector3.up * vy) * dt;
                kick = Vector3.MoveTowards(kick, Vector3.zero, 10f * dt);
                groundY = ProtoStage.GroundHeight(p);
                if (p.y < groundY + 1.5f) { p.y = groundY + 1.5f; vy = 0f; kick = Vector3.zero; }
                transform.position = p;
            }
            visual.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
            visual.localPosition = Vector3.up * (0.5f + Mathf.Sin(t * 3f) * 0.4f);

            if (transform.position.y < -200f) { Destroy(gameObject); return; }   // 谷底へ落ちたものだけ消す(時間では消えない)

            var pl = ProtoPlayer.I;
            if (pl == null || pl.IsDead) return;
            Vector3 d = pl.transform.position - transform.position;
            float dy = d.y;
            d.y = 0f;
            if (def != null && ManualPick) return;   // 武装は△で拾う(ProtoPlayer が拾う)
            bool inRange = d.magnitude <= PickRadius + pl.Radius && Mathf.Abs(dy) < 12f;
            if (!armed) { if (!inRange) armed = true; return; }   // 捨てた直後に拾い直さないよう、一度離れるまで待つ
            if (inRange)
            {
                if (def == null && !pl.TryAddHeal()) return;   // 回復アイテムは持ちきれなければ残す
                ProtoFx.Blast(transform.position, 4f, Tint, 0.3f);
                if (def != null) Give(pl);
                Destroy(gameObject);
            }
        }
    }
}
