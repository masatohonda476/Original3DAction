using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機の武装(ステージだけ。部屋のモードでは付かない)。付ける所は4つ:剣 / 背中 / 右肩 / 左肩。どれも1つずつ。
    ///   剣:素の剣の威力・届く範囲・振りの速さなどに倍率を掛ける(剣の見た目が長くなり、レア度の色になる)。
    ///   背中:スラスター。走り・ブースト・回避の距離が伸び、回避・ブースト・飛行のENが減る。
    ///   右肩:銃。R2(R)を押している間撃つ。弾倉があれば撃ち切るとリロード。
    ///   左肩:砲・ミサイル。L2(E)で撃つ(押していれば撃てるたびに撃つ)。砲は当たった所で爆発、ミサイルは狙える敵に割り振って追いかける。
    /// 狙い:ロックオン中はその敵(射程の外・壁ごしなら、画面の中央に近い敵 → いなければ画面の中央の先)。
    /// 拾う:落ちている武装の近くで十字右(V)。同じ所の今の武装は足もとに落ちる(拾い直せる)。在庫・メニューはない。
    /// 撃墜されても武装は残る。ボスを倒すと爆発で全部消し飛ぶ(LoseAll)。自機の素の性能(HP・EN・怯み)は変えない。画面は揺らさない。
    /// </summary>
    public class DuelArms : MonoBehaviour
    {
        DuelPlayer player;
        DuelGame game;
        MechRig rig;
        readonly DuelWeapon[] slots = new DuelWeapon[4];
        readonly Transform[] visuals = new Transform[4];
        Vector3 swordBaseScale = Vector3.one;
        float cdR, cdL, cdLMax = 1f, reloadR, lastFireR = -10f;
        int magR;

        /// <summary>拾える範囲にある一番近い武装(なければ null)。</summary>
        public DuelLoot Nearby { get; private set; }

        public void Init(DuelPlayer p, DuelGame g, MechRig r)
        {
            player = p;
            game = g;
            rig = r;
            if (rig != null && rig.sword != null) swordBaseScale = rig.sword.localScale;
        }

        public DuelWeapon Get(DuelSlot s) => slots[(int)s];

        // ───────── 自機に掛ける倍率(DuelPlayer から) ─────────

        public DuelWeapon SwordW => slots[(int)DuelSlot.Sword];
        public DuelWeapon BackW => slots[(int)DuelSlot.Back];
        public float RunMul => BackW != null ? BackW.runMul : 1f;
        public float DashMul => BackW != null ? BackW.dashMul : 1f;
        public float DodgeMul => BackW != null ? BackW.dodgeMul : 1f;
        public float EnCostMul => BackW != null ? BackW.enCostMul : 1f;

        // ───────── 画面に出す情報 ─────────

        /// <summary>右肩の弾倉の残り(弾倉のない銃・空きは -1)。</summary>
        public int MagLeft => Get(DuelSlot.RightShoulder) is { magazine: > 0 } ? magR : -1;
        /// <summary>右肩のリロードの残り(秒。0ならリロードしていない)。</summary>
        public float ReloadLeft => Mathf.Max(0f, reloadR);
        /// <summary>左肩が次に撃てるまでの残り(0〜1。0で撃てる)。</summary>
        public float HeavyCooldown01 => cdL > 0f ? Mathf.Clamp01(cdL / Mathf.Max(0.01f, cdLMax)) : 0f;

        void Update()
        {
            float dt = Time.deltaTime;
            cdR -= dt;
            cdL -= dt;
            var gun = Get(DuelSlot.RightShoulder);
            if (reloadR > 0f)
            {
                reloadR -= dt;
                if (reloadR <= 0f && gun != null) magR = gun.magazine;
            }
            // 撃たずにリロードの時間の1.5倍たったら、弾倉を満タンに戻す(撃ちかけの弾倉が残り続けないように)
            else if (gun != null && gun.magazine > 0 && magR < gun.magazine && Time.time - lastFireR > gun.reload * 1.5f) magR = gun.magazine;

            if (player == null || player.Dead)
            {
                Nearby = null;
                return;
            }
            Nearby = DuelLoot.Nearest(player);
            if (Nearby != null && ProtoInput.EquipNow) Equip(Nearby);

            if (!player.CanShoot) return;
            if (gun != null && ProtoInput.RightShoulderHeld) FireGun(gun);
            var heavy = Get(DuelSlot.LeftShoulder);
            if (heavy != null && ProtoInput.LeftShoulderHeld) FireHeavy(heavy);
        }

        // ───────── 付ける・外す ─────────

        /// <summary>落ちている武装を付ける。同じ所の今の武装は足もとに落とす(拾い直せる)。</summary>
        void Equip(DuelLoot loot)
        {
            var w = loot.Weapon;
            int i = (int)w.slot;
            var old = slots[i];
            slots[i] = w;
            loot.Take();
            if (old != null)
            {
                Vector3 back = -player.transform.forward;
                DuelLoot.Drop(old, player.transform.position + Vector3.up * 3f + back * 3f, back * 6f);
            }
            if (w.slot == DuelSlot.RightShoulder)
            {
                magR = w.magazine;
                reloadR = 0f;
                cdR = 0.2f;
            }
            if (w.slot == DuelSlot.LeftShoulder) cdL = 0.3f;
            Refresh(w.slot);
            game.Log($"{DuelWeapon.SlotName(w.slot)}に{w.name}({w.RarityName})を付けた");
        }

        /// <summary>
        /// 全部の武装を失う(ボスの爆発)。付けていた武装は火花を散らして吹き飛び、剣は素の剣に戻る。失った数を返す。
        /// </summary>
        public int LoseAll()
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                n++;
                slots[i] = null;
                var v = visuals[i];
                visuals[i] = null;
                if (v != null) Fling(v);
            }
            if (rig != null && rig.sword != null && n > 0)
            {
                DuelSpark.Burst(rig.sword.position, Vector3.up, new Color(1f, 0.8f, 0.4f), 24, 1.3f);
                ProtoFx.Blast(rig.sword.position, 3f, new Color(1f, 0.7f, 0.3f), 0.25f);
            }
            Refresh(DuelSlot.Sword);
            magR = 0;
            reloadR = cdR = cdL = 0f;
            return n;
        }

        /// <summary>外れた武装を、回しながら外へ吹き飛ばして少しして消す。</summary>
        void Fling(Transform v)
        {
            v.SetParent(null, true);
            ProtoFx.Blast(v.position, 3f, new Color(1f, 0.6f, 0.2f), 0.25f);
            DuelSpark.Burst(v.position, Vector3.up, new Color(1f, 0.75f, 0.4f), 12, 1f);
            var rb = v.gameObject.AddComponent<Rigidbody>();
            rb.mass = 5f;
            Vector3 out_ = v.position - player.Chest;
            out_.y = 0f;
            rb.linearVelocity = (out_.sqrMagnitude > 0.01f ? out_.normalized : Random.onUnitSphere) * 14f + Vector3.up * 16f;
            rb.angularVelocity = Random.insideUnitSphere * 6f;
            foreach (var mf in v.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<BoxCollider>();
            Destroy(v.gameObject, 3f);
        }

        /// <summary>その所の見た目を作り直す。</summary>
        void Refresh(DuelSlot s)
        {
            int i = (int)s;
            var w = slots[i];
            if (s == DuelSlot.Sword)
            {
                // 剣:握りはそのままで長さを届く範囲に合わせ、レア以上はレア度の色に塗る
                if (rig == null || rig.sword == null) return;
                float len = w != null ? w.reachMul : 1f;
                rig.sword.localScale = new Vector3(swordBaseScale.x, swordBaseScale.y * len, swordBaseScale.z);
                MaterialPropertyBlock block = null;
                if (w != null && w.rarity >= 1)
                {
                    block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", w.Color);
                }
                foreach (var r in rig.sword.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
                return;
            }
            if (visuals[i] != null) Destroy(visuals[i].gameObject);
            visuals[i] = null;
            if (w == null) return;
            Transform mount = rig == null ? null : s switch
            {
                DuelSlot.Back => rig.mountBack,
                DuelSlot.RightShoulder => rig.mountShoulderR,
                _ => rig.mountShoulderL,
            };
            if (mount != null) visuals[i] = CreateVisual(w, mount);
            else
            {
                // 取付点がない機体:胸の高さに仮に付ける
                visuals[i] = CreateVisual(w, transform);
                float side = s == DuelSlot.RightShoulder ? 2f : s == DuelSlot.LeftShoulder ? -2f : 0f;
                visuals[i].localPosition = new Vector3(side, game.mechHeight * 0.85f, s == DuelSlot.Back ? -1.8f : 0f);
            }
        }

        /// <summary>武装の仮の見た目(レア度の色)。剣は細長い刃(長さは届く範囲に合わせる)。</summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            var root = new GameObject(w.name).transform;
            root.SetParent(parent, false);
            bool blade = w.slot == DuelSlot.Sword;
            var prim = GameObject.CreatePrimitive(blade ? PrimitiveType.Cube : w.shape);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            prim.transform.localScale = blade ? new Vector3(0.4f, 1f, 4.5f * w.reachMul) : w.scale;
            prim.transform.localRotation = Quaternion.Euler(blade ? Vector3.zero : w.euler);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(w.Color);
            return root;
        }

        // ───────── 狙い ─────────

        /// <summary>撃ち出す所(その肩の武装の先。なければ胸の上の左右)。</summary>
        Vector3 Muzzle(DuelSlot s)
        {
            var v = visuals[(int)s];
            Vector3 f = player.transform.forward;
            if (v != null) return v.position + f * 1.5f;
            float side = s == DuelSlot.RightShoulder ? 2f : -2f;
            return player.transform.position + Vector3.up * game.mechHeight * 0.85f + player.transform.right * side + f * 1.5f;
        }

        /// <summary>
        /// 撃つ相手:ロックオン中の敵(射程の内で見通せれば)。なければ、画面の中央から30°以内で見通せる、射程の内の敵(中央に近く近いもの)。
        /// </summary>
        DuelEnemy AimTarget(float range, Vector3 from)
        {
            var t = player.Target;
            if (t != null && !t.Dead && Vector3.Distance(t.Chest, from) <= range && DuelSenses.ClearSight(from, t.Chest)) return t;
            var cam = Camera.main;
            Vector3 camF = cam != null ? cam.transform.forward : player.transform.forward;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                Vector3 to = e.Chest - from;
                float d = to.magnitude;
                if (d > range) continue;
                float ang = Vector3.Angle(camF, to);
                if (ang > 30f) continue;
                float score = d * (1f + ang / 15f);
                if (score >= bestScore || !DuelSenses.ClearSight(from, e.Chest)) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        /// <summary>狙う相手がいないとき:画面の中央の先(最初に当たる地形・敵。なければ遠く)。</summary>
        Vector3 FreeAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return player.Chest + player.transform.forward * 100f;
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            const float far = 250f;
            float best = far;
            if (Physics.Raycast(o, d, out var hit, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (RayHitsEnemy(o, d, best, out _, out float ed)) best = ed;
            return o + d * best;
        }

        /// <summary>from から dir へ len まで飛ばして、最初に当たる敵(機体を縦長のカプセルとみなす)。</summary>
        bool RayHitsEnemy(Vector3 from, Vector3 dir, float len, out DuelEnemy hitEnemy, out float dist)
        {
            hitEnemy = null;
            dist = len;
            Vector3 to = from + dir * len;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = e.Radius, h = game.mechHeight * e.Size;
                Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
                if (SegSegDistance(from, to, a, b, out float s) > r) continue;
                if (s * len >= dist) continue;
                dist = s * len;
                hitEnemy = e;
            }
            return hitEnemy != null;
        }

        /// <summary>線分どうしの最短距離。s は1本目の線分の上の、いちばん近い点の位置(0〜1)。</summary>
        static float SegSegDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float t;
            if (a <= 1e-6f && e <= 1e-6f) { s = 0f; return r.magnitude; }
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }

        /// <summary>向きを最大 deg 度の範囲でランダムにぶらす。</summary>
        static Vector3 Jitter(Vector3 dir, float deg)
        {
            if (deg <= 0f) return dir;
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            Vector3 tilted = Quaternion.AngleAxis(Random.Range(0f, deg), perp.normalized) * dir;
            return Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * tilted;
        }

        // ───────── 右肩:銃 ─────────

        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);

        void FireGun(DuelWeapon w)
        {
            if (cdR > 0f || reloadR > 0f) return;
            if (w.magazine > 0 && magR <= 0) { reloadR = w.reload; return; }
            cdR = w.interval;
            lastFireR = Time.time;
            Vector3 from = Muzzle(DuelSlot.RightShoulder);
            var t = AimTarget(w.range, from);
            Vector3 aim = t != null ? t.Chest : FreeAimPoint();
            Vector3 dir = (aim - from).normalized;
            Color c = w.magazine <= 0 ? BeamColor : BulletColor;
            int n = Mathf.Max(1, w.pellets);
            for (int i = 0; i < n; i++) Shot(from, Jitter(dir, w.spread), w, c, n > 1);
            if (w.magazine > 0 && --magR <= 0) reloadR = w.reload;   // 撃ち切ったらリロード
        }

        /// <summary>銃の1発:まっすぐ飛び、最初に当たった地形・敵で止まる。</summary>
        void Shot(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            float len = w.range;
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            Vector3 end = from + dir * len;
            if (RayHitsEnemy(from, dir, len, out var e, out float d))
            {
                end = from + dir * d;
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * 4f, 0.3f);
                DuelSpark.Burst(end, -dir, c, pellet ? 2 : 3, 0.6f);
            }
            ProtoFx.Tracer(from, end, c, pellet ? 0.15f : 0.25f, 0.05f);
        }

        // ───────── 左肩:砲・ミサイル ─────────

        static readonly Color ShellColor = new(0.8f, 1f, 0.35f), MissileColor = new(1f, 0.6f, 0.25f), BlastColor = new(1f, 0.6f, 0.25f);

        void FireHeavy(DuelWeapon w)
        {
            if (cdL > 0f) return;
            cdL = cdLMax = w.interval;
            Vector3 from = Muzzle(DuelSlot.LeftShoulder);
            if (w.IsMissile)
            {
                // ミサイル:狙える敵に順番に割り振る(ロックオン中の敵が先、あとは近い順)。いなければ画面の中央の先へ
                var targets = MissileTargets(w.range);
                Vector3 point = FreeAimPoint();
                for (int j = 0; j < w.salvo; j++)
                {
                    var t = targets.Count > 0 ? targets[j % targets.Count] : null;
                    Vector2 sc = Random.insideUnitCircle * w.radius;
                    StartCoroutine(Missile(from, t, point + new Vector3(sc.x, 0f, sc.y), w, j * 0.06f));
                }
                return;
            }
            var target = AimTarget(w.range, from);
            Vector3 aim = target != null ? target.Chest : FreeAimPoint();
            Vector3 dir = (aim - from).normalized;
            float len = Mathf.Min(Vector3.Distance(from, aim), w.range);
            // 間を地形・ほかの敵がさえぎっていれば、そこで爆発する
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            if (RayHitsEnemy(from, dir, len, out _, out float ed)) len = ed;
            StartCoroutine(Shell(from, from + dir * len, w));
        }

        List<DuelEnemy> MissileTargets(float range)
        {
            var list = new List<DuelEnemy>();
            Vector3 eye = player.LockPoint;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                if (!DuelSenses.ClearSight(eye, e.Chest)) continue;
                list.Add(e);
            }
            var t = player.Target;
            list.Sort((a, b) =>
            {
                if (a == t) return -1;
                if (b == t) return 1;
                return (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude);
            });
            return list;
        }

        /// <summary>弾の見た目:光る球と、尾を引く線。</summary>
        static Transform Projectile(Vector3 at, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Shot";
            go.transform.position = at;
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.18f;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>砲弾:まっすぐ飛んで、着いた所で爆発する。</summary>
        IEnumerator Shell(Vector3 from, Vector3 to, DuelWeapon w)
        {
            const float speed = 160f;
            var p = Projectile(from, ShellColor, 1.2f + w.radius * 0.05f);
            float dur = Vector3.Distance(from, to) / speed, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                p.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.001f, dur)));
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(to, w, true);
        }

        /// <summary>ミサイル1発:上へ打ち上げてから、狙った敵へ曲がって飛ぶ(敵が動いても追いかける)。着いたら小さく爆発する。</summary>
        IEnumerator Missile(Vector3 from, DuelEnemy target, Vector3 point, DuelWeapon w, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Vector3 lift = from + Vector3.up * 12f - player.transform.forward * 3f + Random.insideUnitSphere * 7f;
            Vector3 goal = point;
            var p = Projectile(from, MissileColor, 0.8f);
            float dur = Random.Range(0.55f, 0.75f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                if (target != null && !target.Dead) goal = target.Chest;
                float k = Mathf.Clamp01(t / dur);
                p.position = Vector3.Lerp(Vector3.Lerp(from, lift, k), Vector3.Lerp(lift, goal, k), k);
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(goal, w, false);
        }

        /// <summary>爆発:半径の中の敵に当てる(上下に離れすぎた敵には当たらない)。自機には当たらない。</summary>
        void Explode(Vector3 at, DuelWeapon w, bool shell)
        {
            ProtoFx.Blast(at, w.radius, shell ? ShellColor : BlastColor, shell ? 0.35f : 0.25f);
            DuelSpark.Burst(at, Vector3.zero, new Color(1f, 0.75f, 0.4f), shell ? 20 : 6, shell ? 1.2f : 0.7f);
            bool any = false;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead) continue;
                Vector3 d = e.transform.position - at;
                float dy = at.y - e.transform.position.y;
                d.y = 0f;
                if (d.magnitude - e.Radius > w.radius) continue;
                if (dy < -w.radius || dy > game.mechHeight * e.Size + w.radius) continue;
                e.TakeRangedHit(w.damage, w.stagger, (d.sqrMagnitude > 0.01f ? d.normalized : player.transform.forward) * w.knock, 0.6f);
                any = true;
            }
            if (any && shell) game.HitStop(0.05f);   // 砲だけ。ミサイルは何発も当たるので止めない
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }
    }
}
