using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // 通常射撃・大技。時間差の連射、複数の弾、貫通、爆発で撃ち方を分ける。
    public partial class DuelArms
    {
        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);
        static readonly Color ShellColor = new(1f, 0.6f, 0.25f);
        const float BlastWallOffset = 0.08f; // 仮。爆心を着弾面から発射側へずらす距離。
        const float BlastEdgePower = 0.35f; // 仮。爆風の端で残る威力・崩し・押し出しの割合。
        const float BlastLife = 0.30f; // 仮
        const int BlastSparks = 14; // 仮
        const float FuseArmingDistance = 4f; // 仮。銃口からこの距離を飛ぶと近接信管が働く。
        const float WideBeamThreshold = 1f, WideBeamLife = 0.18f; // 仮

        static bool IsWideBeam(DuelShot shot) =>
            shot.pierce && shot.shotSpeed <= 0f && shot.beamWidth >= WideBeamThreshold;

        int roundsPending;
        bool burstSpecial;
        int burstAmmoPaid, burstRoundCount;
        float nextRoundAt;

        int RefundableBurstAmmo() => burstSpecial && roundsPending > 0
            ? burstAmmoPaid * roundsPending / burstRoundCount : 0; // 仮。未発射分の端数は切り捨てる。

        internal void StopBurst()
        {
            int refund = RefundableBurstAmmo();
            roundsPending = 0;
            if (refund > 0) AddAmmo(refund);
        }

        bool CanPayPrimary(DuelWeapon weapon) =>
            weapon.HasMagazine ? Magazine > 0 : weapon.enCost <= 0f || !player.Overheated;

        bool PayPrimary(DuelWeapon weapon)
        {
            if (!CanPayPrimary(weapon))
            {
                if (weapon.HasMagazine)
                {
                    NoAmmoAt = Time.time;
                    game.Log("弾倉が空。近接に戻ると弾を詰められる");
                }
                else
                {
                    NoEnergyAt = Time.time;
                    game.Log("射撃に必要な EN が足りない");
                }
                return false;
            }

            if (weapon.HasMagazine) rangedMemo.mag--;
            else if (weapon.enCost > 0f) player.SpendEnergy(weapon.enCost);
            return true;
        }

        void StartFiring(bool special, Vector3 from, Vector3 aim)
        {
            var weapon = RangedWeapon;
            var shot = special ? weapon.Special : weapon.Primary;
            if (special)
            {
                if (Ammo < weapon.SpecialAmmo)
                {
                    NoAmmoAt = Time.time;
                    game.Log("大技に必要な共通の弾が足りない");
                    return;
                }
                Ammo -= weapon.SpecialAmmo;
            }
            else if (!PayPrimary(weapon)) return;

            float now = Time.time;
            rangedMemo.cooldown = shot.interval;
            rangedMemo.ready = now + shot.interval;
            burstSpecial = special;
            // 残弾が少ないときも、弾倉から出せる分だけ点射する。
            int count = !special && weapon.HasMagazine ? Mathf.Min(shot.burstCount, Magazine + 1) : shot.burstCount;
            burstAmmoPaid = special ? weapon.SpecialAmmo : 0;
            burstRoundCount = count;
            roundsPending = count - 1;
            nextRoundAt = now + shot.burstInterval;
            FireRound(weapon, shot, special, from, aim);
        }

        void FirePendingRounds(float now, Vector3 from, Vector3 aim)
        {
            var weapon = RangedWeapon;
            var shot = burstSpecial ? weapon.Special : weapon.Primary;
            // 長いフレームでも発射間隔を持ち越す。1 回の点射に含まれる発射数が上限。
            while (roundsPending > 0 && now >= nextRoundAt)
            {
                if (!burstSpecial && !PayPrimary(weapon))
                {
                    StopBurst();
                    break;
                }
                roundsPending--;
                nextRoundAt += shot.burstInterval;
                FireRound(weapon, shot, burstSpecial, from, aim);
            }
        }

        void FireRound(DuelWeapon weapon, DuelShot shot, bool special, Vector3 from, Vector3 aim)
        {
            lastFire = Time.time;
            Vector3 direction = (aim - from).normalized;
            if (direction == Vector3.zero) direction = player.transform.forward;
            bool beam = IsWideBeam(shot) || (!special && weapon.enCost > 0f);
            Color color = shot.blastRadius > 0f ? ShellColor : beam ? BeamColor : BulletColor;
            float speed = SpeedOf(shot);
            bool spread = shot.pellets > 1;
            for (int i = 0; i < shot.pellets; i++)
            {
                Vector3 dir = direction;
                if (special && spread)
                {
                    // 三方向射撃と拡散爆発は横へ等間隔に撃ち分ける。通常の散弾は立体的に散る。
                    float angle = Mathf.Lerp(-shot.spread, shot.spread, (float)i / (shot.pellets - 1));
                    dir = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                }
                else dir = Jitter(direction, shot.spread + (special ? 0f : bloom));
                if (speed > 0f)
                    StartCoroutine(Fly(from, dir, shot, color, spread, speed, ShotInView, inheritMovement: player.LockOn));
                else Shot(from, dir, shot, color, spread);
            }
            float recoil = Mathf.Max(0f, Rules.recoilMul);
            if (shot.recoilPerShot > 0f)
                bloom = Mathf.Min(shot.recoilMax * recoil, bloom + shot.recoilPerShot * recoil);
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
        /// mark は、その弾を撃ったときに予測した弾道の先が視界の中だったか(ShotInView)。視界の外へ撃った弾は、当たっても印を出さない
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
            bool hitEnemy = FindProjectileTarget(from, dir, len, w, out var enemy, out float distance);
            if (hitEnemy) end = from + dir * distance;
            if (w.blastRadius > 0f && (hitEnemy || wall || w.blastAtEnd))
                Explode(wall && distance >= len ? BlastCenter(hit) : end, dir, w, ShotInView);
            else if (hitEnemy)
                BulletHit(enemy, end, dir, w, c, pellet, ShotInView);
            else if (wall)
                DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);
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
                Vector3 at = from + dir * (s * len);
                if (!DuelSenses.ClearSight(at, ClosestBodyPoint(at, a, b))) continue;
                PierceHit(e, at, dir, w, c, ShotInView);
                any = true;
            }
            if (!any && wall is { } wh) DuelSpark.Burst(wh.point, wh.normal, c, 1, 0.45f);   // 外れて地形に当たった:小さな火花
            float width = 0.25f + w.beamWidth * 0.5f;
            if (IsWideBeam(w)) ProtoFx.Tracer(from, to, c, width, WideBeamLife);
            else Tracer(from, to, c, width, !any);
        }

        // ───────── 飛ぶ弾 ─────────

        /// <summary>見た目だけの球。生成したフレームも、弾やカメラの地形判定に使われない。</summary>
        static GameObject VisualSphere(string name, Vector3 at, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.position = at;
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>弾の見た目:光る弾体と、尾を引く線(trail 秒の長さ。弾の筋)。</summary>
        internal static Transform Projectile(Vector3 at, Color c, float size, float trail, float length = 0f)
        {
            var go = VisualSphere("Shot", at, c, size);
            if (length > 0f)
                go.transform.localScale = new Vector3(size, size, length);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = trail;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>速さを持つ弾。地形で止まり、貫通弾は射程までに通った敵に一度ずつ当たる。</summary>
        IEnumerator Fly(Vector3 from, Vector3 dir, DuelShot w, Color c, bool pellet, float speed, bool mark,
            DuelEnemy homing = null, float turnRate = 0f, int flightVersion = -1, bool inheritMovement = true)
        {
            var r = Rules;
            // サイト内への補正と弾道表示も、発射時に自機から受け継ぐ速度を使っている。
            if (inheritMovement)
            {
                Vector3 velocity = ShotVelocity(dir, speed);
                speed = velocity.magnitude;
                dir = velocity.normalized;
            }
            const float ShellSize = 1.1f; // 仮
            float size = w.blastRadius > 0f ? ShellSize : (pellet ? r.pelletSize : r.bulletSize) + w.beamWidth * 0.5f;
            float trail = Mathf.Max(0.01f, r.bulletTrail);
            if (w.projectileLength > 0f) size *= 0.5f;
            var p = Projectile(from, c, size, trail, w.projectileLength);
            if (w.projectileLength > 0f) p.rotation = Quaternion.LookRotation(dir);
            Destroy(p.gameObject, w.range / Mathf.Max(1f, speed) + trail + 1f);   // 念のため(コルーチンが止まっても残らない)
            var guidance = new DuelMissileGuidance(homing);
            List<DuelEnemy> pierced = w.pierce ? new List<DuelEnemy>() : null;
            Vector3 pos = from;
            float flown = 0f;
            while (true)
            {
                yield return null;
                if (p == null) yield break;
                if (flightVersion >= 0 && flightVersion != subFlightVersion)
                {
                    EndProjectile(p, pos, trail);
                    yield break;
                }
                if (homing != null)
                    dir = guidance.Steer(pos, dir, turnRate * Mathf.Deg2Rad * Time.deltaTime);
                float step = speed * Time.deltaTime;
                if (step <= 0f) continue;   // ヒットストップ・止まった時間の中
                bool last = flown + step >= w.range;
                if (last) step = Mathf.Max(0f, w.range - flown);
                bool wall = Physics.Raycast(pos, dir, out var hit, step, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float len = wall ? hit.distance : step;
                if (pierced != null)
                    PierceSegment(pos, dir, len, w, c, pierced, mark);
                else if (FindProjectileTarget(pos, dir, len, w, out var enemy, out float distance, flown: flown))
                {
                    Vector3 at = pos + dir * distance;
                    if (w.blastRadius > 0f) Explode(wall && distance >= len ? BlastCenter(hit) : at, dir, w, mark);
                    else BulletHit(enemy, at, dir, w, c, pellet, mark);
                    EndProjectile(p, at, trail);
                    yield break;
                }

                Vector3 end = pos + dir * len;
                if (wall || last)
                {
                    if (w.blastRadius > 0f && (wall || w.blastAtEnd))
                        Explode(wall ? BlastCenter(hit) : end, dir, w, mark);
                    else if (wall)
                        DuelSpark.Burst(hit.point, hit.normal, c, 1, pellet ? 0.3f : 0.45f);
                    EndProjectile(p, end, trail);
                    yield break;
                }
                pos = end;
                flown += len;
                p.position = pos;
            }
        }

        // 直撃と近接信管を地形までの線分で調べる。十字の判定だけ、狙った奥行きと敵の太さでも範囲を絞る。
        bool FindProjectileTarget(Vector3 from, Vector3 dir, float len, DuelShot shot, out DuelEnemy hit, out float distance,
            float flown = 0f, float aimDepth = float.PositiveInfinity)
        {
            if (shot.blastRadius <= 0f) return RayHitsEnemy(from, dir, len, out hit, out distance);

            bool hasFuse = shot.proximityRadius > 0f;
            float fuseOffset = Mathf.Max(0f, FuseArmingDistance - flown);
            Vector3 fuseStart = from + dir * fuseOffset;
            hit = null;
            distance = len;
            foreach (var enemy in game.Enemies)
            {
                if (enemy == null || enemy.Dead) continue;
                float forward = hasFuse ? Vector3.Dot(enemy.Chest - from, dir) : 0f;
                if (forward < 0f) continue;
                float radius = BodyAxis(enemy, out Vector3 a, out Vector3 b);
                float reach = Mathf.Min(len, aimDepth + Mathf.Max(shot.proximityRadius, radius * 2f));
                Vector3 end = from + dir * reach;
                float along = SegSegDistance(from, end, a, b, out float direct) <= radius
                    ? direct * reach : float.PositiveInfinity;
                // 信管の有効距離をまたぐフレームは、その地点から先だけを調べる。直撃は全区間で調べる。
                if (hasFuse && fuseOffset <= reach && forward >= fuseOffset &&
                    SegSegDistance(fuseStart, end, a, b, out float near) <= radius + shot.proximityRadius)
                    along = Mathf.Min(along, fuseOffset + near * (reach - fuseOffset));

                if (along > distance || (hit != null && along == distance)) continue;
                Vector3 at = from + dir * along;
                if (!DuelSenses.ClearSight(at, ClosestBodyPoint(at, a, b))) continue;
                distance = along;
                hit = enemy;
            }
            return hit != null;
        }

        static Vector3 ClosestBodyPoint(Vector3 point, Vector3 bottom, Vector3 top) =>
            new(bottom.x, Mathf.Clamp(point.y, bottom.y, top.y), bottom.z);

        static Vector3 BlastCenter(RaycastHit wall) => wall.point + wall.normal * BlastWallOffset;

        void Explode(Vector3 at, Vector3 dir, DuelShot shot, bool mark)
        {
            foreach (var enemy in game.Enemies)
            {
                if (enemy == null || enemy.Dead) continue;
                float bodyRadius = BodyAxis(enemy, out Vector3 a, out Vector3 b);
                Vector3 nearest = ClosestBodyPoint(at, a, b);
                float gap = Mathf.Max(0f, Vector3.Distance(at, nearest) - bodyRadius);
                if (gap > shot.blastRadius || !DuelSenses.ClearSight(at, nearest)) continue;
                float power = Mathf.Lerp(1f, BlastEdgePower, gap / shot.blastRadius);
                Vector3 away = DuelFighter.Flat(enemy.Chest - at);
                if (away == Vector3.zero) away = DuelFighter.Flat(dir);
                enemy.TakeRangedHit(shot.damage * power, shot.stagger * power, away * (shot.knock * power), 0.3f);
                NoteHit(enemy, mark);
            }
            var blast = VisualSphere("射撃の爆発", at, ShellColor, shot.blastRadius * 0.4f);
            blast.AddComponent<FxAnim>().Setup(blast.transform.localScale, Vector3.one * (shot.blastRadius * 2f), BlastLife);
            DuelSpark.Burst(at, Vector3.zero, ShellColor, BlastSparks);
        }

        /// <summary>弾が止まった:その所まで動かしてから球を隠し、尾が消えるのを待って消す(当たった所まで筋が届いて見えるように)。</summary>
        internal static void EndProjectile(Transform p, Vector3 at, float trail)
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
                Vector3 at = from + dir * (s * len);
                if (!DuelSenses.ClearSight(at, ClosestBodyPoint(at, a, b))) continue;
                pierced.Add(e);
                PierceHit(e, at, dir, w, c, mark);
            }
        }

    }
}
