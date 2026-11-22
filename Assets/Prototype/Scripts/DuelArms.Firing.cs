using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelArms の撃つ処理:遠距離武器・ミサイル(partial。本体と全体の説明は DuelArms.cs)
    public partial class DuelArms
    {
        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);

        void Fire(bool special, Vector3 from, Vector3 aim)
        {
            var weapon = RangedWeapon;
            var shot = special ? weapon.Special : weapon.Primary;
            if (special ? Ammo < weapon.SpecialAmmo : weapon.HasMagazine && Magazine == 0)
            {
                NoAmmoAt = Time.time;
                game.Log(special ? "大技に必要な共通の弾が足りない" : "弾倉が空。近接に戻ると弾を詰められる");
                return;
            }
            if (!special && weapon.enCost > 0f && player.Overheated)
            {
                NoEnergyAt = Time.time;
                game.Log("射撃に必要な EN が足りない");
                return;
            }

            if (special) Ammo -= weapon.SpecialAmmo;
            else if (weapon.HasMagazine) rangedMemo.mag--;
            else if (weapon.enCost > 0f) player.SpendEnergy(weapon.enCost);

            rangedMemo.cooldown = shot.interval;
            rangedMemo.ready = Time.time + shot.interval;
            lastFire = Time.time;

            Vector3 direction = (aim - from).normalized;
            if (direction == Vector3.zero) direction = player.transform.forward;
            Color color = weapon.HasMagazine ? BulletColor : BeamColor;
            float speed = SpeedOf(shot);
            // 拡散の大技は中央と左右へ一度に発射する。
            for (int i = 0; i < shot.pellets; i++)
            {
                float angle = shot.pellets > 1 ? Mathf.Lerp(-shot.spread, shot.spread, (float)i / (shot.pellets - 1)) : 0f;
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (!special) dir = Jitter(dir, bloom);
                if (speed > 0f) StartCoroutine(Fly(from, dir, shot, color, shot.pellets > 1, speed, CursorInView));
                else Shot(from, dir, shot, color, shot.pellets > 1);
            }
            float recoil = Mathf.Max(0f, Rules.recoilMul);
            if (shot.recoilPerShot > 0f)
                bloom = Mathf.Min(shot.recoilMax * recoil, bloom + shot.recoilPerShot * recoil);
            Kick(shot);
        }

        /// <summary>銃の弾が敵 e の at に当たった:当てて、当たった所から火花を出し、ヒットマーカー(Shot・Fly で同じ。mark は NoteHit)。</summary>
        void BulletHit(DuelEnemy e, Vector3 at, Vector3 dir, DuelShot w, Color c, bool pellet, bool mark)
        {
            e.TakeRangedHit(w.damage, w.stagger, DuelFighter.Flat(dir) * w.knock, 0.3f);
            DuelSpark.Burst(at, -dir, c, pellet ? 2 : 3, 0.6f);
            NoteHit(e, mark);
        }

        /// <summary>貫く弾が敵 e の at に当たった:当てて、太さに応じた火花を出し、ヒットマーカー(PierceShot・PierceSegment で同じ。mark は NoteHit)。</summary>
        void PierceHit(DuelEnemy e, Vector3 at, Vector3 dir, DuelShot w, Color c, bool mark)
        {
            e.TakeRangedHit(w.damage, w.stagger, DuelFighter.Flat(dir) * w.knock, 0.3f);
            DuelSpark.Burst(at, -dir, c, 3, 0.6f + w.beamWidth * 0.2f);
            NoteHit(e, mark);
        }

        /// <summary>
        /// 遠距離武器が敵 e に当たった:照準の真ん中のヒットマーカーを出す。同じ瞬間に何発も当たったら(拡散・貫通)強い方の印。
        /// TakeRangedHit の後に呼ぶ(倒した・崩れている・崩せるを見分ける)。
        /// mark は、その弾を撃ったときに照準の先が視界の中だったか(CursorInView)。視界の外へ撃った弾は、当たっても印を出さない
        /// (撃ったあと向きを変えて照準の先が画面に戻っても、敵のいない所に印が出ないように。作者が 2 案から「撃ったときに視界外だった弾では出さない」)。
        /// </summary>
        void NoteHit(DuelEnemy e, bool mark)
        {
            if (e == null || !mark) return;
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
        void Shot(Vector3 from, Vector3 dir, DuelShot w, Color c, bool pellet)
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
                BulletHit(e, end, dir, w, c, pellet, CursorInView);   // すぐ届く光線は、撃ったフレームに当たる(今の照準の先 = 撃ったときの照準の先)
            }
            else if (wall) DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
            Tracer(from, end, c, pellet ? 0.15f : 0.25f, !hitEnemy);
        }

        /// <summary>貫く弾:from から len まで、線から(敵の半径 + 太さ)の内にいる敵みんなに当てる。太いほど光線も太く長く残る。</summary>
        void PierceShot(Vector3 from, Vector3 dir, float len, DuelShot w, Color c, RaycastHit? wall)
        {
            Vector3 to = from + dir * len;
            bool any = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                if (wall.HasValue && !DuelSenses.ClearSight(from, e.Chest)) continue;
                PierceHit(e, from + dir * (s * len), dir, w, c, CursorInView);
                any = true;
            }
            if (!any && wall is { } wh) DuelSpark.Burst(wh.point, wh.normal, c, 1, 0.45f);   // 外れて地形に当たった:小さな火花
            // 太い光線は今までどおり長めに残す。細い光線は弾の筋と同じ(外れたら長めに)
            if (w.beamWidth >= 1f) ProtoFx.Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, 0.18f);
            else Tracer(from, to, c, 0.25f + w.beamWidth * 0.5f, !any);
        }

        // ───────── 飛ぶ弾 ─────────

        /// <summary>弾の見た目:光る球と、尾を引く線(trail 秒の長さ。弾の筋)。</summary>
        static Transform Projectile(Vector3 at, Color c, float size, float trail = 0.18f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(go.GetComponent<Collider>());
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

        /// <summary>速さを持つ弾。地形で止まり、貫通弾は射程までに通った敵に一度ずつ当たる。</summary>
        IEnumerator Fly(Vector3 from, Vector3 dir, DuelShot w, Color c, bool pellet, float speed, bool mark)
        {
            var r = Rules;
            // 撃った自機の速度を弾が受け継ぐ(作者「弾は、停止中を狙うより、相対速度を合わせる方が楽しそうです」):
            // 敵と同じ向き・速さで動きながら撃てば、狙った所へ当たる。止まって動く敵を撃つと外れる。先読みの自動補正はしない
            if (player != null && r.inheritShooterVelocity > 0f)
            {
                Vector3 v = dir * speed + inheritVel * r.inheritShooterVelocity;
                float m = v.magnitude;
                if (m > 1f) { dir = v / m; speed = m; }
            }
            float size = (pellet ? r.pelletSize : r.bulletSize) + w.beamWidth * 0.5f;
            float trail = Mathf.Max(0.01f, r.bulletTrail);
            var p = Projectile(from, c, size, trail);
            Destroy(p.gameObject, w.range / Mathf.Max(1f, speed) + trail + 1f);   // 念のため(コルーチンが止まっても残らない)
            List<DuelEnemy> pierced = w.pierce ? new List<DuelEnemy>() : null;
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
                    PierceSegment(pos, dir, len, w, c, pierced, mark);
                }
                else if (RayHitsEnemy(pos, dir, len, out var e, out float d))
                {
                    Vector3 at = pos + dir * d;
                    BulletHit(e, at, dir, w, c, pellet, mark);
                    EndProjectile(p, at, trail);
                    yield break;
                }
                pos += dir * len;
                flown += len;
                p.position = pos;
                if (wall)
                {
                    DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);   // 地形に当たった:小さな火花
                    EndProjectile(p, hit.point, trail);
                    yield break;
                }
                if (last)
                {
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

        /// <summary>弾が進む間、線の近くの敵に当てる。同じ敵には一発につき一度。</summary>
        void PierceSegment(Vector3 from, Vector3 dir, float len, DuelShot w, Color c, List<DuelEnemy> pierced, bool mark)
        {
            if (len <= 0f) return;
            Vector3 to = from + dir * len;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead || pierced.Contains(e)) continue;
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                if (SegSegDistance(from, to, a, b, out float s) > r + w.beamWidth) continue;
                pierced.Add(e);
                PierceHit(e, from + dir * (s * len), dir, w, c, mark);
            }
        }

    }
}
