using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelArms の撃つ処理:銃・砲・ミサイル(partial。本体と全体の説明は DuelArms.cs)
    public partial class DuelArms
    {
        // ───────── 銃(手・肩) ─────────

        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);

        /// <summary>
        /// 銃を撃つ(撃てる間隔・弾倉の残り・共通の弾・ENがあれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。
        /// エネルギー武器はENが0以下なら撃たない(charged の溜め撃ちは除く)。1発ごとにEN(EnPerShot)を使い、足りない分はマイナスになる。
        /// </summary>
        bool FireGun(DuelWeapon w, int i, DuelSlot s, bool charged = false)
        {
            if (cd[i] > 0f || reload[i] > 0f) return false;
            if (w.magazine > 0 && mag[i] <= 0)
            {
                StartReload(i, w);   // 共通の弾が1発分もなければ知らせるだけ
                return false;
            }
            bool prepaid = charged && chargeAmmo > 0f;   // 溜め撃ち:溜め始めに1回分を取り置いてある
            if (!prepaid && w.PaysPerShot && Ammo < w.ammoCost - 0.0001f)
            {
                OutOfAmmo();
                return false;
            }
            if (!charged && NoEnergy(w))
            {
                OutOfEnergy();
                return false;
            }
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            // 照準の先へ撃つ(枠の中の敵がいれば、狙いを少しだけ寄せる。照準を動かせるときは、弾が速さを持つ撃ち方は寄せない。AssistDeg)
            var t = TargetFor(w, i, from);
            Vector3 aim = AssistedAimPoint(t, AssistDeg(w, ParentOf(i)), from, w.range);
            Vector3 dir = (aim - from).normalized;
            Color c = w.magazine <= 0 ? BeamColor : BulletColor;
            int n = Mathf.Max(1, w.pellets);
            float speed = SpeedOf(w);   // 0 以下ならすぐ届く光線(照射し続けるレーザー)
            // 間隔の端数は持ち越す(1フレームより短い間隔なら、そのフレームに何発か出す。最大4回。押すたびに撃つ銃は1回)
            int bursts = w.hold ? 4 : 1;
            for (int k = 0; k < bursts && cd[i] <= 0f; k++)
            {
                if (k > 0 && NoEnergy(w)) break;   // 同じフレームの2発目からは、ENが0以下になったら止める
                if (!prepaid && !PayAmmo(w)) break;   // 弾倉のない実弾の撃ち方は撃つたびに共通の弾を使う(1回目は上で確かめてある)
                if (w.UsesEnergy) player.SpendEnergy(w.EnPerShot);
                cd[i] += Mathf.Max(0.01f, w.interval);
                // ばらつき = 武装の spread + 反動の広がり(撃つたびに広がる。カメラは動かさない)
                float jitter = w.spread + bloom[i];
                for (int p = 0; p < n; p++)
                {
                    if (speed > 0f) StartCoroutine(Fly(from, Jitter(dir, jitter), w, c, n > 1, speed, false));   // 見える弾が速さを持って飛ぶ
                    else Shot(from, Jitter(dir, jitter), w, c, n > 1);
                }
                AddRecoil(i, w);
                Kick(w);   // 照準そのものが上へずれる(カメラは動かさない)
                if (w.magazine > 0 && --mag[i] <= 0) { StartReload(i, w); break; }   // 撃ち切ったらリロード(共通の弾が足りれば)
            }
            cd[i] = Mathf.Max(cd[i], 0f);
            cdMax[i] = Mathf.Max(0.01f, w.interval);   // 次に撃てるまでのゲージの分母(両手の銃の L1 の弾。付けたときの 0.3 秒のままだとゲージが合わない)
            return true;
        }

        /// <summary>撃った後の反動:撃つ状態 i の広がりを1回分足す(上限まで。DuelArmsRules.recoilMul を掛ける)。</summary>
        void AddRecoil(int i, DuelWeapon w)
        {
            float mul = Mathf.Max(0f, Rules.recoilMul);
            if (w.recoilPerShot <= 0f || mul <= 0f) return;
            bloom[i] = Mathf.Min(Mathf.Max(0f, w.recoilMax) * mul, bloom[i] + w.recoilPerShot * mul);
        }

        /// <summary>銃の弾が敵 e の at に当たった:当てて、当たった所から火花を出し、ヒットマーカー(Shot・Fly で同じ)。</summary>
        void BulletHit(DuelEnemy e, Vector3 at, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            e.TakeRangedHit(w.damage, w.stagger, DuelFighter.Flat(dir) * w.knock, 0.3f);
            DuelSpark.Burst(at, -dir, c, pellet ? 2 : 3, 0.6f);
            NoteHit(e);
        }

        /// <summary>貫く弾が敵 e の at に当たった:当てて、太さに応じた火花を出し、ヒットマーカー(PierceShot・PierceSegment で同じ)。</summary>
        void PierceHit(DuelEnemy e, Vector3 at, Vector3 dir, DuelWeapon w, Color c)
        {
            e.TakeRangedHit(w.damage, w.stagger, DuelFighter.Flat(dir) * w.knock, 0.3f);
            DuelSpark.Burst(at, -dir, c, 3, 0.6f + w.beamWidth * 0.2f);
            NoteHit(e);
        }

        /// <summary>
        /// 射撃(銃・砲)が敵 e に当たった:照準の真ん中のヒットマーカーを出す。同じ瞬間に何発も当たったら(散弾・貫く弾・爆発)強い方の印。
        /// TakeRangedHit の後に呼ぶ(倒した・崩れている・崩せるを見分ける)。
        /// </summary>
        void NoteHit(DuelEnemy e)
        {
            if (e == null) return;
            float cap = game.rangedStaggerCap;
            int kind = e.Dead ? 3 : e.Flinching ? 2 : cap < 1f && e.Stagger >= e.maxStagger * cap - 0.01f ? 1 : 0;
            float now = Time.unscaledTime;
            if (now - HitMarkAt < 0.05f) kind = Mathf.Max(kind, HitMarkKind);
            HitMarkAt = now;
            HitMarkKind = kind;
        }

        /// <summary>
        /// 弾の筋:from から to へ光る線を出し、life 秒で細くなって消える。外れた弾(miss)は少し太く・長く残して、どこへ飛んだか見えるようにする。
        /// </summary>
        void Tracer(Vector3 from, Vector3 to, Color c, float width, bool miss)
        {
            var r = Rules;
            float life = Mathf.Max(0.02f, miss ? r.missTracerLife : r.tracerLife);
            if (miss) width = Mathf.Max(width, r.missTracerWidth);
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.sharedMaterial = ProtoFx.Unlit(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startWidth = width * 0.5f;
            lr.endWidth = width;
            StartCoroutine(FadeTracer(lr, width, life));
            Destroy(go, life + 0.5f);   // 念のため(止まった時間の中などでコルーチンが進まなくても残らない)
        }

        /// <summary>弾の筋を life 秒で細くして消す(撃つ側の根元から先に細くなる)。</summary>
        static IEnumerator FadeTracer(LineRenderer lr, float width, float life)
        {
            float t = 0f;
            while (t < life && lr != null)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / life);
                lr.startWidth = width * 0.5f * k * k;
                lr.endWidth = width * k;
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
        }

        /// <summary>
        /// 銃の1発(弾の速さが 0 の撃ち方だけ。照射し続けるレーザーなど):すぐ届く光線。最初に当たった地形・敵で止まる(貫く弾は地形まで飛び、線の上の敵みんなに当たる)。
        /// 当たればヒットマーカー。外れた弾は筋を長めに残し、地形に当たったら小さな火花を出す。速さのある弾は Fly。
        /// </summary>
        void Shot(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            float len = w.range;
            bool wall = Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (wall) len = hit.distance;
            Vector3 end = from + dir * len;
            if (w.pierce)
            {
                PierceShot(from, dir, len, w, c, wall ? hit : (RaycastHit?)null);
                return;
            }
            bool hitEnemy = RayHitsEnemy(from, dir, len, out var e, out float d);
            if (hitEnemy)
            {
                end = from + dir * d;
                BulletHit(e, end, dir, w, c, pellet);
            }
            else if (wall) DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
            Tracer(from, end, c, pellet ? 0.15f : 0.25f, !hitEnemy);
        }

        /// <summary>貫く弾:from から len まで、線から(敵の半径 + 太さ)の内にいる敵みんなに当てる。太いほど光線も太く長く残る。</summary>
        void PierceShot(Vector3 from, Vector3 dir, float len, DuelWeapon w, Color c, RaycastHit? wall)
        {
            Vector3 to = from + dir * len;
            bool any = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                PierceHit(e, from + dir * (s * len), dir, w, c);
                any = true;
            }
            if (!any && wall is { } wh) DuelSpark.Burst(wh.point, wh.normal, c, 1, 0.45f);   // 外れて地形に当たった:小さな火花
            // 太い光線(溜め撃ち)は今までどおり長めに残す。細い光線は弾の筋と同じ(外れたら長めに)
            if (w.beamWidth >= 1f) ProtoFx.Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, 0.18f);
            else Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, !any);
            if (any && w.charge > 0f) game.HitStop(0.06f);   // 溜め撃ちが当たったら短く止める(画面は揺らさない)
        }

        // ───────── 砲・ミサイル(肩) ─────────

        static readonly Color ShellColor = new(0.8f, 1f, 0.35f), MissileColor = new(1f, 0.6f, 0.25f), BlastColor = new(1f, 0.6f, 0.25f);

        /// <summary>
        /// 砲・ミサイルを撃つ(次に撃てるまでの時間がたっていれば)。i は撃つ状態の番号、s は撃ち出す所。撃ったら true。
        /// 今の一覧に ENを使う砲・ミサイルはないが、enCost を入れれば銃と同じ決まりで ENを使う。
        /// </summary>
        bool FireHeavy(DuelWeapon w, int i, DuelSlot s, bool charged = false)
        {
            if (cd[i] > 0f) return false;
            if (!charged && NoEnergy(w))
            {
                OutOfEnergy();
                return false;
            }
            if (!(charged && chargeAmmo > 0f) && !PayAmmo(w))   // 溜め撃ちは溜め始めに取り置いた分を使う
            {
                OutOfAmmo();
                return false;
            }
            if (w.UsesEnergy) player.SpendEnergy(w.EnPerShot);
            cd[i] = cdMax[i] = w.interval;
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            if (w.IsMissile)
            {
                // ミサイル:枠(照準の位置が中心)に掛かっている狙える敵に順番に割り振る(ロックオン中の敵が先、あとは近い順)。いなければ照準の先へ
                var targets = MissilesFor(w, i);
                Vector3 point = FreeAimPoint(from, w.range);
                for (int j = 0; j < w.salvo; j++)
                {
                    var t = targets.Count > 0 ? targets[j % targets.Count] : null;
                    Vector2 sc = Random.insideUnitCircle * w.radius;
                    StartCoroutine(Missile(from, t, point + new Vector3(sc.x, 0f, sc.y), w, j * 0.06f));
                }
                return true;
            }
            // 砲も銃と同じく照準の先へ(吸い付きの決まりも銃と同じ。AssistDeg)。砲弾は速さを持って飛び、地形・敵に当たるか射程の端で爆発する
            var target = TargetFor(w, i, from);
            Vector3 aim = AssistedAimPoint(target, AssistDeg(w, ParentOf(i)), from, w.range);
            Vector3 dir = Jitter((aim - from).normalized, bloom[i]);   // 反動の広がり(今の砲は広がり 0。.Recoil を入れれば広がる)
            AddRecoil(i, w);
            Kick(w);   // 照準そのものが上へずれる(カメラは動かさない)
            float speed = SpeedOf(w);
            StartCoroutine(Fly(from, dir, w, ShellColor, false, speed > 0f ? speed : FallbackShellSpeed, true));
            return true;
        }

        /// <summary>
        /// ミサイルの狙える敵(list に入れて返す。多くても1回に撃つ数 salvo まで):照準の枠に掛かっていて(ロックオン中の敵は lockFrameMul 倍に広くみなす)、
        /// 射程の内で見通せる敵。ロックオン中の敵が先、あとは近い順。先に枠と射程で絞って並べ、見通し(Linecast)は前から salvo 体そろうまでだけ調べる。
        /// </summary>
        List<DuelEnemy> MissileTargets(DuelWeapon w, DuelWeapon parent, List<DuelEnemy> list)
        {
            list.Clear();
            var cam = Camera.main;
            if (cam == null) return list;
            PrepareViews(cam);
            var rules = Rules;
            float range = w.range, hw = FrameTan(w, parent), hh = hw * rules.frameAspect, lm = Mathf.Max(1f, rules.lockFrameMul);
            Vector3 eye = viewEye;
            var t = player.Target;
            missileCand.Clear();
            for (int k = 0; k < viewCount; k++)
            {
                var e = views[k].e;
                if (e == null || e.Dead) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                float m = e == t ? lm : 1f;
                if (!InFrame(k, hw * m, hh * m)) continue;
                missileCand.Add((e == t ? -1f : (e.transform.position - eye).sqrMagnitude, k));   // ロックオン中の敵が先(-1)、あとは近い順
            }
            missileCand.Sort(ByNear);
            int cap = Mathf.Max(1, w.salvo);
            for (int c = 0; c < missileCand.Count && list.Count < cap; c++)
            {
                int k = missileCand[c].k;
                if (Visible(k)) list.Add(views[k].e);
            }
            return list;
        }

        readonly List<(float key, int k)> missileCand = new();
        static readonly System.Comparison<(float key, int k)> ByNear = (a, b) => a.key.CompareTo(b.key);

        /// <summary>弾の見た目:光る球と、尾を引く線(trail 秒の長さ。弾の筋)。</summary>
        static Transform Projectile(Vector3 at, Color c, float size, float trail = 0.18f)
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
            tr.time = trail;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>砲の弾の速さが 0 のときの速さ(m/s。前の砲弾の速さ)。</summary>
        const float FallbackShellSpeed = 160f;

        /// <summary>
        /// 速さを持って飛ぶ弾(実弾の銃・砲。作者が選んだ形「弾に速さを持たせる」)。from から dir へ speed(m/s)でまっすぐ飛び、射程(range)まで。
        /// 毎フレーム、進む分の線で地形と敵を調べる(偏差の自動補正はしない。動く敵には照準を先に置く)。
        ///   銃の弾:最初に当たった敵に当てて火花とヒットマーカー、地形なら小さな火花で消える。貫く弾は地形・射程の端まで飛び、線の上の敵に1回ずつ当てる。
        ///   砲弾(explosive):敵・地形に当たるか射程の端で爆発する。
        /// 弾の筋は弾の尾(TrailRenderer)。止まったら球を隠して尾だけ消えていく。ヒットストップ中は止まる(Time.deltaTime)。
        /// </summary>
        IEnumerator Fly(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet, float speed, bool explosive)
        {
            var r = Rules;
            // 撃った自機の速度を弾が受け継ぐ(作者「弾は、停止中を狙うより、相対速度を合わせる方が楽しそうです」):
            // 敵と同じ向き・速さで動きながら撃てば、狙った所へ当たる。止まって動く敵を撃つと外れる。先読みの自動補正はしない
            if (player != null && r.inheritShooterVelocity > 0f)
            {
                Vector3 v = dir * speed + player.Velocity * r.inheritShooterVelocity;
                float m = v.magnitude;
                if (m > 1f) { dir = v / m; speed = m; }
            }
            float size = explosive ? 1.2f + w.radius * 0.05f : (pellet ? r.pelletSize : r.bulletSize) + w.beamWidth * 0.5f;
            float trail = explosive ? 0.18f : Mathf.Max(0.01f, r.bulletTrail);
            var p = Projectile(from, explosive ? ShellColor : c, size, trail);
            Destroy(p.gameObject, w.range / Mathf.Max(1f, speed) + trail + 1f);   // 念のため(コルーチンが止まっても残らない)
            List<DuelEnemy> pierced = w.pierce && !explosive ? new List<DuelEnemy>() : null;
            Vector3 pos = from;
            float flown = 0f;
            while (true)
            {
                yield return null;
                if (p == null) yield break;
                float step = speed * Time.deltaTime;
                if (step <= 0f) continue;   // ヒットストップ・止まった時間の中
                bool last = flown + step >= w.range;
                if (last) step = Mathf.Max(0f, w.range - flown);
                bool wall = Physics.Raycast(pos, dir, out var hit, step, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float len = wall ? hit.distance : step;
                if (pierced != null)
                {
                    if (PierceSegment(pos, dir, len, w, c, pierced) && w.charge > 0f) game.HitStop(0.06f);   // 溜め撃ちが当たったら短く止める(画面は揺らさない)
                }
                else if (RayHitsEnemy(pos, dir, len, out var e, out float d))
                {
                    Vector3 at = pos + dir * d;
                    if (explosive) Explode(at, w, true);
                    else BulletHit(e, at, dir, w, c, pellet);
                    EndProjectile(p, at, trail);
                    yield break;
                }
                pos += dir * len;
                flown += len;
                p.position = pos;
                if (wall)
                {
                    if (explosive) Explode(hit.point, w, true);
                    else DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
                    EndProjectile(p, hit.point, trail);
                    yield break;
                }
                if (last)
                {
                    if (explosive) Explode(pos, w, true);   // 射程の端で爆発(前と同じ)
                    EndProjectile(p, pos, trail);
                    yield break;
                }
            }
        }

        /// <summary>弾が止まった:その所まで動かしてから球を隠し、尾が消えるのを待って消す(当たった所まで筋が届いて見えるように)。</summary>
        static void EndProjectile(Transform p, Vector3 at, float trail)
        {
            if (p == null) return;
            p.position = at;
            var rend = p.GetComponent<MeshRenderer>();
            if (rend != null) rend.enabled = false;
            var tr = p.GetComponent<TrailRenderer>();
            if (tr != null) tr.emitting = false;
            Destroy(p.gameObject, trail + 0.05f);
        }

        /// <summary>
        /// 貫く弾が from から len 進む間に、線から(敵の半径 + 太さ)の内にいる敵に当てる(1発で同じ敵には1回。pierced に覚える)。当たったら true。
        /// </summary>
        bool PierceSegment(Vector3 from, Vector3 dir, float len, DuelWeapon w, Color c, List<DuelEnemy> pierced)
        {
            if (len <= 0f) return false;
            Vector3 to = from + dir * len;
            bool any = false;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead || pierced.Contains(e)) continue;
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                pierced.Add(e);
                PierceHit(e, from + dir * (s * len), dir, w, c);
                any = true;
            }
            return any;
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
                if (shell) NoteHit(e);   // 砲だけヒットマーカー(ミサイルは自動で飛ぶので出さない)
                any = true;
            }
            if (any && shell) game.HitStop(0.05f);   // 砲だけ。ミサイルは何発も当たるので止めない
        }
    }
}
