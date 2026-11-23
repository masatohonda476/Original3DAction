using UnityEngine;

namespace Prototype
{
    // 遠距離武器のサイト・射撃補正・弾道の予測。
    public partial class DuelArms
    {
        // ───────── 狙い ─────────

        /// <summary>遠距離武器の銃口。</summary>
        Vector3 Muzzle()
        {
            var visual = visuals[(int)DuelSlot.Ranged];
            Vector3 forward = player.transform.forward;
            if (visual != null) return visual.position + forward * (RangedWeapon.scale.z * 0.5f);
            return player.Chest + player.transform.right * 2f + forward * 1.5f;
        }

        /// <summary>撃ち方の弾の速さ(m/s。DuelArmsRules.shotSpeedMul を掛ける)。0 以下なら弾を飛ばさず、今までどおりすぐ届く光線。</summary>
        float SpeedOf(DuelShot w) => w.shotSpeed * Mathf.Max(0f, Rules.shotSpeedMul);

        // ───────── 照準の枠(射撃だけ。アーマード・コアの FCS のように、枠の中の敵に当てる) ─────────

        /// <summary>画面に出す射撃の照準枠。</summary>
        public struct AimFrame
        {
            /// <summary>ロックオン機能が有効なときだけ照準枠を描く。</summary>
            public bool showCorners;
            /// <summary>次に撃てるまで待っている間は枠を明るくする。</summary>
            public bool hot;
            /// <summary>枠の中に当てる相手がいる(枠の色を変える)。</summary>
            public bool locked;
            /// <summary>サイトの横と縦の半分(tan)。表示と射撃補正に使う。</summary>
            public float halfW, halfH;
            /// <summary>今のばらつき(spread + 反動の広がり)の tan。真ん中の十字の開きに出す。</summary>
            public float spread;
        }

        public AimFrame? ShotFrame { get; private set; }
        public DuelEnemy MarkedEnemy { get; private set; }
        public bool ShotInView { get; private set; }
        /// <summary>
        /// 弾が飛んでいく所(遠距離武器。補正できたときは着弾予測点。ほかは自機から受け継ぐ速度を含めた弾道の先)。真ん中の十字をここに出す(作者「敵のちょうどいい位置に十字も出したい」→「弾が飛んでいく所」)。
        /// 遠距離を構えていない間は null。
        /// </summary>
        public Vector3? ShotPoint { get; private set; }
        /// <summary>
        /// ShotPoint へ飛ぶ弾が敵の体に当たる見込み。真ん中の十字が敵に重なっているときで、十字を橙にする
        /// (作者「ロックオン中の弾道を示す十字は、敵と重なったらオレンジとかになるようにしてください」)。ばらつきは入れない。毎フレーム UpdateFrame で求める。
        /// </summary>
        public bool ShotOnEnemy => ShotEnemy != null;
        /// <summary>
        /// ShotPoint へ飛ぶ弾が当たると見込む敵(いなければ null)。ロックオンの相手なら、相手を囲む角を濃い橙にする
        /// (作者「敵の囲みは枠と同じ青のままで、弾が当たるタイミングで今より濃いオレンジにしてほしいです」。DuelGame.DrawLockCorners)。
        /// </summary>
        public DuelEnemy ShotEnemy { get; private set; }

        struct EnemyView
        {
            public DuelEnemy e;
            public bool front;
            public float ax, ay, ra, bx, by, rb;
        }

        EnemyView[] views = new EnemyView[32];
        int viewCount, viewFrame = -1;

        /// <summary>このフレームの敵ごとの見え方を作る(もう作ってあれば何もしない)。カメラの後ろの敵は front が false。</summary>
        void PrepareViews(Camera cam)
        {
            if (viewFrame == Time.frameCount) return;
            viewFrame = Time.frameCount;
            var list = game.Enemies;
            if (views.Length < list.Count) views = new EnemyView[Mathf.NextPowerOfTwo(list.Count)];
            viewCount = list.Count;
            var ct = cam.transform;
            for (int k = 0; k < viewCount; k++)
            {
                var e = list[k];
                ref var v = ref views[k];
                v = default;
                v.e = e;
                if (e == null || e.Dead) continue;
                Vector3 c = ct.InverseTransformPoint(e.Chest);
                if (c.z <= 0.5f) continue;
                float r = BodyAxis(e, out Vector3 wa, out Vector3 wb);
                Vector3 a = ct.InverseTransformPoint(wa);
                Vector3 b = ct.InverseTransformPoint(wb);
                if (a.z <= 0.5f) a = c;
                if (b.z <= 0.5f) b = c;
                v.ax = a.x / a.z;
                v.ay = a.y / a.z;
                v.ra = r / a.z;
                v.bx = b.x / b.z;
                v.by = b.y / b.z;
                v.rb = r / b.z;
                v.front = true;
            }
        }

        /// <summary>k 番目の敵の体が画面中央の枠(横 ±hw・縦 ±hh。tan)に掛かっているか。</summary>
        bool InFrame(int k, float hw, float hh)
        {
            ref var v = ref views[k];
            if (!v.front || v.e == null || v.e.Dead) return false;
            if (Mathf.Max(v.ax + v.ra, v.bx + v.rb) < -hw || Mathf.Min(v.ax - v.ra, v.bx - v.rb) > hw) return false;
            return !(Mathf.Max(v.ay + v.ra, v.by + v.rb) < -hh || Mathf.Min(v.ay - v.ra, v.by - v.rb) > hh);
        }

        /// <summary>
        /// 敵 e の体が、画面の中央を中心にした枠(横 ±hw・縦 ±hh。tan)に掛かっているか。ロックオンの付け始めの判定(DuelPlayer.PickBest。枠は DuelGame.LockSightTan)。
        /// 銃の照準の枠と同じ体の見え方(PrepareViews・InFrame。体は BodyAxis の縦長のカプセル)を使い回す。どちらも画面の中央を基準に測る。
        /// index は e の DuelGame.Enemies での番号(見え方は同じ並びなので、まずその番号を見る。合えば探さない)。分からなければ -1(頭から探す)。
        /// このフレームの見え方を作ったあとに現れた敵は、次のフレームから入る。
        /// </summary>
        public bool BodyInCenterFrame(Camera cam, DuelEnemy e, int index, float hw, float hh)
        {
            if (cam == null || e == null || player == null || game == null) return false;
            PrepareViews(cam);
            if (index >= 0 && index < viewCount && views[index].e == e) return InFrame(index, hw, hh);
            for (int k = 0; k < viewCount; k++)
                if (views[k].e == e) return InFrame(k, hw, hh);
            return false;
        }

        /// <summary>
        /// サイトに体が掛かり、射程内で銃口から見通せる敵を選ぶ。中央の射線に近い敵を優先し、
        /// 同程度ならロックオン中の相手を優先する。aim は選んだ敵の体の芯の上の狙う点。
        /// </summary>
        DuelEnemy AimTarget(Camera cam, float hw, float hh, Vector3 from, float range, out Vector3 aim)
        {
            PrepareViews(cam);
            aim = default;
            float range2 = range * range;
            Vector3 eye = cam.transform.position, forward = cam.transform.forward;
            var locked = player.Target;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            for (int k = 0; k < viewCount; k++)
            {
                if (!InFrame(k, hw, hh)) continue;
                var enemy = views[k].e;
                float radius = BodyAxis(enemy, out Vector3 a, out Vector3 b);
                SegSegDistance(a, b, eye, eye + forward * AimFar, out float s);
                Vector3 point = Vector3.Lerp(a, b, s);
                float rangeToPoint2 = (point - from).sqrMagnitude;
                if (rangeToPoint2 > range2) continue;

                Vector3 to = point - eye;
                float bodyAngle = Mathf.Asin(Mathf.Clamp01(radius / Mathf.Max(0.001f, to.magnitude))) * Mathf.Rad2Deg;
                float offset = Mathf.Max(0f, Vector3.Angle(forward, to) - bodyAngle);
                float score = offset - (enemy == locked ? 0.5f : 0f) + Mathf.Sqrt(rangeToPoint2) * 0.002f;
                if (score >= bestScore || !DuelSenses.ClearSight(from, point)) continue;
                bestScore = score;
                best = enemy;
                aim = point;
            }
            return best;
        }

        Vector3 InheritedShotVelocity => inheritVel * Mathf.Max(0f, Rules.inheritShooterVelocity);

        Vector3 ShotVelocity(Vector3 direction, float speed)
        {
            Vector3 muzzleVelocity = direction * speed;
            Vector3 velocity = muzzleVelocity + InheritedShotVelocity;
            return velocity.sqrMagnitude > 1f ? velocity : muzzleVelocity;
        }

        /// <summary>今の速度で動く敵に届く最初の時刻を求め、速度を受け継ぐ分も引いて発射方向を決める。</summary>
        bool TryLeadShot(DuelShot shot, Vector3 from, Vector3 target, Vector3 targetVelocity,
            out Vector3 launchAim, out Vector3 impact)
        {
            launchAim = impact = target;
            float speed = SpeedOf(shot);
            if (speed <= 0f) return false;

            Vector3 offset = target - from;
            Vector3 relativeVelocity = targetVelocity - InheritedShotVelocity;
            // |offset + relativeVelocity * t| = speed * t の最初の交点。
            float distance2 = offset.sqrMagnitude;
            float speedGap = speed * speed - relativeVelocity.sqrMagnitude;
            float along = Vector3.Dot(offset, relativeVelocity);
            float discriminant = along * along + speedGap * distance2;
            if (discriminant < 0f) return false;
            float denominator = Mathf.Sqrt(discriminant) - along;
            if (denominator <= 0f) return false;
            float time = distance2 / denominator;

            Vector3 travel = offset + targetVelocity * time;
            if (travel.sqrMagnitude > shot.range * shot.range || travel.sqrMagnitude <= time * time) return false;
            Vector3 intercept = from + travel;
            if (targetVelocity != Vector3.zero && !DuelSenses.ClearSight(from, intercept)) return false;

            launchAim = target + relativeVelocity * time;
            impact = intercept;
            return true;
        }

        /// <summary>補正先がない射撃の弾道を、受け継ぐ速度を含めて予測する。</summary>
        Vector3 PredictShot(DuelShot shot, Vector3 from, Vector3 aim)
        {
            Vector3 to = aim - from;
            float dist = to.magnitude;
            float speed = SpeedOf(shot);
            if (dist < 0.01f || speed <= 0f) return aim;
            Vector3 dir = to / dist;
            Vector3 flightDir = ShotVelocity(dir, speed).normalized;
            float along = dist / Mathf.Max(0.05f, Vector3.Dot(flightDir, dir));
            return from + flightDir * Mathf.Min(along, shot.range);
        }

        bool UpdateFrame(bool special, out Vector3 from, out Vector3 aim)
        {
            ShotFrame = null;
            MarkedEnemy = ShotEnemy = null;
            ShotPoint = null;
            ShotInView = false;
            from = aim = default;
            var cam = Camera.main;
            if (!Aiming || cam == null) return false;

            var w = RangedWeapon;
            var shot = special ? w.Special : w.Primary;
            float recoil = special ? 0f : bloom;
            var frame = new AimFrame
            {
                showCorners = player.LockOn,
                hot = Cooling > 0f,
                spread = Mathf.Tan(Mathf.Min(80f, shot.spread + recoil) * Mathf.Deg2Rad),
            };
            from = Muzzle();
            if (!frame.showCorners)
            {
                // 手動照準はカメラ中央の地形・空を基準にする。弾はこの照準方向を保つ。
                Vector3 point = AimPointAlong(cam.transform.position, cam.transform.forward, useEnemyDepth: false);
                aim = from + Vector3.ClampMagnitude(point - from, shot.range);
                ShotPoint = aim;
                ShotInView = true;
                ShotFrame = frame;
                return true;
            }

            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = Mathf.Tan(Rules.FrameDeg(w) * Mathf.Deg2Rad);
            frame.halfW = Mathf.Min(halfW, 0.96f * tanV * cam.aspect);
            frame.halfH = Mathf.Min(halfW * Rules.frameAspect, 0.9f * tanV);

            // ロックオンの付け始めで使った投影があれば、移動とカメラ操作後の位置でもう一度作る。
            viewFrame = -1;
            MarkedEnemy = AimTarget(cam, frame.halfW, frame.halfH, from, shot.range, out aim);
            if (MarkedEnemy == null)
            {
                Vector3 point = AimPointAlong(cam.transform.position, cam.transform.forward, useEnemyDepth: true);
                aim = from + Vector3.ClampMagnitude(point - from, shot.range);
            }
            Vector3 predicted = default;
            bool led = MarkedEnemy != null &&
                TryLeadShot(shot, from, aim, MarkedEnemy.Velocity, out aim, out predicted);
            if (!led) predicted = PredictShot(shot, from, aim);
            ShotPoint = predicted;
            Vector3 vp = cam.WorldToViewportPoint(predicted);
            ShotInView = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            ShotEnemy = led ? MarkedEnemy : PathEnemy(from, predicted, shot);
            frame.locked = MarkedEnemy != null;
            ShotFrame = frame;
            return true;
        }

        const float AimFar = 250f;

        /// <summary>カメラ中央の狙う点。手動照準の奥行きは地形か AimFar 先で決める。</summary>
        Vector3 AimPointAlong(Vector3 o, Vector3 d, bool useEnemyDepth)
        {
            // カメラは壁で寄らず、壁の向こうに残って壁を透かすことがある(DuelCamera)。カメラと自機の間の物(透けている壁など)に当てて、
            // 照準の先が自機より手前(カメラ側)にならないよう、自機の位置(線の上へ下ろした所)より先だけを調べる
            float skip = player != null ? Mathf.Max(0f, Vector3.Dot(player.Chest - o, d)) : 0f;
            Vector3 s = o + d * skip;
            float best = AimFar;
            if (Physics.Raycast(s, d, out var hit, AimFar, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (useEnemyDepth && RayHitsEnemy(s, d, best, out _, out float ed)) best = ed;
            return s + d * best;
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
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                if (SegSegDistance(from, to, a, b, out float s) > r) continue;
                if (s * len >= dist) continue;
                dist = s * len;
                hitEnemy = e;
            }
            return hitEnemy != null;
        }

        /// <summary>
        /// from から p へ飛ぶ弾が、p のあたりまでに体(縦長のカプセル)を通る敵のうち、いちばん手前の敵(十字と、ロックオンの角の色。ShotEnemy)。いなければ null。
        /// p は狙いの先と同じ奥行き(体の手前の面のあたり)に置かれるので、体の太さの分(半径の2倍)だけ先まで見る(p が体の面の少し手前で止まっても、そのまま当たる弾を拾う)。
        /// </summary>
        DuelEnemy PathEnemy(Vector3 from, Vector3 p, DuelShot shot)
        {
            Vector3 d = p - from;
            float len = d.magnitude;
            if (len < 0.01f) return null;
            d /= len;
            if (shot.blastRadius > 0f)
            {
                // 信管の作動距離と敵の半径を、実際の着弾判定と同じ計算で見る。
                float reach = shot.range;
                if (Physics.Raycast(from, d, out var wall, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    reach = wall.distance;
                FindProjectileTarget(from, d, reach, shot, out var fuseTarget, out _, aimDepth: len);
                return fuseTarget;
            }
            bool wideBeam = IsWideBeam(shot);
            float beamReach = shot.range;
            if (wideBeam && Physics.Raycast(from, d, out var beamWall, beamReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                beamReach = beamWall.distance;

            DuelEnemy hit = null;
            float best = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = BodyAxis(e, out Vector3 a, out Vector3 b);
                float reach = wideBeam ? beamReach : len + r * 2f;
                float width = wideBeam ? r + shot.beamWidth : r;
                if (SegSegDistance(from, from + d * reach, a, b, out float s) > width) continue;
                if (s * reach >= best) continue;
                Vector3 at = from + d * (s * reach);
                if (wideBeam && !DuelSenses.ClearSight(at, ClosestBodyPoint(at, a, b))) continue;
                best = s * reach;
                hit = e;
            }
            return hit;
        }

        /// <summary>敵 e の体(縦長のカプセル)の芯の、下と上の球の中心(a・b)。半径を返す。</summary>
        float BodyAxis(DuelEnemy e, out Vector3 a, out Vector3 b)
        {
            float r = e.Radius, h = game.mechHeight * e.Size;
            a = e.transform.position + Vector3.up * r;
            b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
            return r;
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
    }
}
