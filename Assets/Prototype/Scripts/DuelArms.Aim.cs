using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelArms の狙い・画面の上の照準・照準の枠(partial。本体と全体の説明は DuelArms.cs)
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

        // ───────── 画面の上の照準(カーソル) ─────────

        Vector2 cursor;
        Vector3 aimDir = Vector3.forward;
        bool aimDirValid;
        Vector2 cursorTan;
        bool cursorOnEnemy;
        /// <summary>照準の先がカメラの後ろにある(自機がカメラの方を向いている。枠を画面に置けないので、遠距離武器は吸い付かない)。</summary>
        bool cursorBehind;
        /// <summary>
        /// 照準の先が画面の中に映っているか(カメラの後ろ・画面の外なら false)。自機がカメラと違う方を向いて撃つと外れる(射撃では向き直らない)。
        /// 外れている間、Cursor は画面の中央(カメラの後ろ)か端(画面の外)に張り付いた値なので、そこへ印を出さないために使う(ヒットマーカー)。
        /// </summary>
        public bool CursorInView { get; private set; } = true;

        /// <summary>照準の位置(画面の中央からのずれ。ビューポートの単位で、0.5 が画面の端。x は右、y は上が +)。</summary>
        public Vector2 Cursor => cursor;
        /// <summary>
        /// 照準を可動範囲の端へ押し付けている向きと強さ(長さ 0〜1。x は右、y は上が +)。押し付けていなければ 0。
        /// ロックオンしていないカメラだけが、これでゆっくり回る(DuelCamera)。反動で端へ押されたときは入れない(右スティック・マウスの入力だけ)。
        /// </summary>
        public Vector2 EdgePush { get; private set; }
        /// <summary>照準の先(カメラから照準を通る向きで最初に当たる地形・敵。なければ遠く)。自機は構えの間、ここを向く。</summary>
        public Vector3 CursorPoint { get; private set; }

        /// <summary>構えの間に自機が向く向き(照準の先へ。水平)。決まらなければ zero。</summary>
        public Vector3 AimFacing
        {
            get
            {
                if (player == null) return Vector3.zero;
                Vector3 d = CursorPoint - player.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1f) return d.normalized;
                d = aimDir;
                d.y = 0f;
                return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// 照準を動かす(毎フレーム Tick から。ヒットストップ中も応えるよう実時間)。
        /// 構えの間:右スティック(倒した量を cursorStickCurve 乗。照準が敵の上なら cursorSlowOnEnemy だけ遅く)とマウスで動かし、楕円の可動範囲に収める。
        ///   範囲の外へ押した分は EdgePush に(カメラがゆっくり回る。ロックオンしていないときだけ)。
        /// 構えていない間:ゆっくり画面の中央へ戻る(最後は速さを落として止まる)。
        /// 最後に、カメラから照準を通る向き・照準の先・照準が敵の上か・枠の中心(tan)を求める。
        /// </summary>
        void UpdateCursor()
        {
            var r = Rules;
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            float aspect = cam != null ? Mathf.Max(0.1f, cam.aspect) : 16f / 9f;
            float rx = Mathf.Clamp(r.cursorRangeX, 0.001f, 0.49f), ry = Mathf.Clamp(r.cursorRangeY, 0.001f, 0.49f);
            Vector2 push = Vector2.zero;
            if (player != null && !player.Dead && CursorActive)
            {
                // 動かす量は画面の高さを単位に(横も縦も、画面の上で同じ速さ)。横はビューポートの単位へ直すとき aspect で割る
                Vector2 stick = ProtoInput.RightStick, mouse = ProtoInput.MouseDelta;
                float sm = stick.magnitude, eff = 0f;
                Vector2 move = Vector2.zero;
                if (sm > 0.0001f)
                {
                    eff = Mathf.Pow(Mathf.InverseLerp(0.15f, 1f, Mathf.Min(1f, sm)), Mathf.Max(0.1f, r.cursorStickCurve));
                    float slow = cursorOnEnemy ? 1f - Mathf.Clamp01(r.cursorSlowOnEnemy) : 1f;
                    move += stick / sm * (eff * Mathf.Max(0f, r.cursorStickSpeed) * slow * dt);
                }
                move += mouse * Mathf.Max(0f, r.cursorMouseSens);
                Vector2 next = cursor + new Vector2(move.x / aspect, move.y);
                Vector2 e = new(next.x / rx, next.y / ry);
                if (e.sqrMagnitude > 1f)
                {
                    // 可動範囲の外へ押した:端に止め、外向きに押している強さを EdgePush に(スティックは倒した量、マウスは動かした速さ)
                    Vector2 n = e.normalized;
                    next = new Vector2(n.x * rx, n.y * ry);
                    float s = sm > 0.0001f ? Mathf.Clamp01(Vector2.Dot(stick / sm, n)) * eff : 0f;
                    if (dt > 0f) s = Mathf.Max(s, Mathf.Clamp01(Vector2.Dot(mouse, n) / dt / Mathf.Max(1f, r.cursorMouseEdgeFull)));
                    push = n * s;
                }
                cursor = next;
            }
            else
            {
                // 構えていない:画面の中央へゆっくり戻る(画面の上の表示が動くだけ。カメラは動かない)
                float len = cursor.magnitude;
                if (len > 0f) cursor = Vector2.MoveTowards(cursor, Vector2.zero, Mathf.Min(Mathf.Max(0f, r.cursorReturnSpeed), len * 4f) * dt + 0.00001f);
            }
            EdgePush = push;
            ClampCursor(rx, ry);

            aimDirValid = false;
            cursorOnEnemy = false;
            cursorBehind = false;
            CursorInView = true;
            if (cam == null || game == null) return;
            if (r.aimByFacing && player != null && !r.cursorMovable)
            {
                // 自機の向いている方へ撃つ(作者「自機の向いている方に弾が飛ぶようにしてください」):
                // 左右は自機の向き、上下はロックオンの相手の胸へ(いなければカメラの上下の向き)。自機の胸から伸ばした先を照準の先にし、
                // 照準の表示(cursor)と枠の中心(cursorTan)はその点を画面に映した所へ置く。吸い付きはカメラからその点への向きで求め、
                // 寄せる相手がいなければ弾はこの点へそのまま向ける(AssistedAimPoint)。
                // 射撃では自機は向き直らないので(DuelPlayer)、ロックオンしていない間は自機がカメラと違う方を向いたまま撃つことがある
                Vector3 chest = player.Chest;
                Vector3 f = DuelFighter.Flat(player.transform.forward);
                if (f.sqrMagnitude < 0.0001f) f = DuelFighter.Flat(cam.transform.forward);
                f.Normalize();
                var tg = player.Target;
                Vector3 up = tg != null && !tg.Dead ? tg.Chest - chest : cam.transform.forward;
                float dh = Mathf.Max(0.001f, new Vector2(up.x, up.z).magnitude);
                Vector3 fd = (f * dh + Vector3.up * up.y).normalized;
                CursorPoint = AimPointAlong(chest, fd, out var fhit);
                if (tg != null && !tg.Dead)
                {
                    // 相手より奥の物(壁・床)には当てず、相手と同じ距離の所を照準の先にする
                    // (線が相手の端をかすめたとき、照準の先が相手と奥の壁の間で入れ替わって、十字が跳ばないように)
                    float reach = (tg.Chest - chest).magnitude;
                    if ((CursorPoint - chest).sqrMagnitude > reach * reach) { CursorPoint = chest + fd * reach; fhit = null; }
                }
                cursorOnEnemy = fhit != null;
                aimDir = (CursorPoint - cam.transform.position).normalized;
                aimDirValid = true;
                Vector3 fl = cam.transform.InverseTransformDirection(aimDir);
                cursorBehind = fl.z <= 0.01f;
                cursorTan = fl.z > 0.01f ? new Vector2(fl.x / fl.z, fl.y / fl.z) : Vector2.zero;
                Vector3 vp = cam.WorldToViewportPoint(CursorPoint);
                CursorInView = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                cursor = vp.z > 0f ? new Vector2(Mathf.Clamp(vp.x - 0.5f, -0.49f, 0.49f), Mathf.Clamp(vp.y - 0.5f, -0.49f, 0.49f)) : Vector2.zero;
                return;
            }
            var ray = cam.ViewportPointToRay(new Vector3(0.5f + cursor.x, 0.5f + cursor.y, 0f));
            aimDir = ray.direction;
            aimDirValid = true;
            Vector3 local = cam.transform.InverseTransformDirection(aimDir);
            cursorTan = local.z > 0.01f ? new Vector2(local.x / local.z, local.y / local.z) : Vector2.zero;
            CursorPoint = AimPointAlong(cam.transform.position, aimDir, out var hitE);
            cursorOnEnemy = hitE != null;
        }

        /// <summary>照準を楕円の可動範囲に収める。</summary>
        void ClampCursor(float rx, float ry)
        {
            Vector2 e = new(cursor.x / rx, cursor.y / ry);
            if (e.sqrMagnitude > 1f)
            {
                e.Normalize();
                cursor = new Vector2(e.x * rx, e.y * ry);
            }
        }

        /// <summary>
        /// 反動で照準そのものをずらす(1回撃つごと):上へ aimKick 度、左右へ ±aimKickSide 度のランダム(kickMul を掛ける)。
        /// 角度は今の画角で画面の上の距離へ直す。可動範囲で止まる(カメラは動かさない。端へ押し付けてもカメラは回らない)。
        /// </summary>
        void Kick(DuelShot w)
        {
            var r = Rules;
            if (!r.cursorMovable) return;   // 照準を動かせないときは、反動で照準をずらさない(押さえ込めないため。ばらつきの広がりは残る)
            float mul = Mathf.Max(0f, r.kickMul);
            if (mul <= 0f || (w.aimKick <= 0f && w.aimKickSide <= 0f)) return;
            var cam = Camera.main;
            float tanV = Mathf.Tan((cam != null ? cam.fieldOfView : 55f) * 0.5f * Mathf.Deg2Rad);
            float aspect = cam != null ? Mathf.Max(0.1f, cam.aspect) : 16f / 9f;
            float up = Mathf.Tan(Mathf.Max(0f, w.aimKick) * mul * Mathf.Deg2Rad) / (2f * tanV);
            float side = Mathf.Tan(Random.Range(-1f, 1f) * Mathf.Max(0f, w.aimKickSide) * mul * Mathf.Deg2Rad) / (2f * tanV * aspect);
            cursor += new Vector2(side, up);
            ClampCursor(Mathf.Clamp(r.cursorRangeX, 0.001f, 0.49f), Mathf.Clamp(r.cursorRangeY, 0.001f, 0.49f));
        }

        /// <summary>カメラから照準を通る向き(このフレームに求めていなければカメラの前)。</summary>
        Vector3 AimDir(Camera cam) => aimDirValid ? aimDir : cam.transform.forward;

        /// <summary>撃ち方の弾の速さ(m/s。DuelArmsRules.shotSpeedMul を掛ける)。0 以下なら弾を飛ばさず、今までどおりすぐ届く光線。</summary>
        float SpeedOf(DuelShot w) => w.shotSpeed * Mathf.Max(0f, Rules.shotSpeedMul);

        // ───────── 照準の枠(射撃だけ。アーマード・コアの FCS のように、枠の中の敵に当てる) ─────────

        /// <summary>画面に出す射撃の照準枠。</summary>
        public struct AimFrame
        {
            /// <summary>次に撃てるまで待っている間は枠を明るくする。</summary>
            public bool hot;
            /// <summary>枠の中に当てる相手がいる(枠の色を変える)。</summary>
            public bool locked;
            /// <summary>画面に描く大きさ(tan)。反動で広がっている分だけ 判定範囲より大きい(吸い付く範囲は広げない。見た目だけ)。</summary>
            public float drawW, drawH;
            /// <summary>今のばらつき(spread + 反動の広がり)の tan。真ん中の十字の開きに出す。</summary>
            public float spread;
        }

        public AimFrame? ShotFrame { get; private set; }
        public DuelEnemy MarkedEnemy { get; private set; }
        /// <summary>
        /// 弾が飛んでいく所(遠距離武器。撃つときと同じ狙いの先へ向けた弾が、自機の速度を受け継いで流れた先。PredictShot)。真ん中の十字をここに出す(作者「敵のちょうどいい位置に十字も出したい」→「弾が飛んでいく所」)。
        /// 遠距離を構えていない間は null。
        /// </summary>
        public Vector3? ShotPoint { get; private set; }
        /// <summary>
        /// ShotPoint へ飛ぶ弾が敵の体に当たる(銃口から ShotPoint への線が、敵の体のカプセルを通る)。真ん中の十字が敵に重なっているときで、十字を橙にする
        /// (作者「ロックオン中の弾道を示す十字は、敵と重なったらオレンジとかになるようにしてください」)。ばらつきは入れない。毎フレーム UpdateFrame で求める。
        /// </summary>
        public bool ShotOnEnemy => ShotEnemy != null;
        /// <summary>
        /// ShotPoint へ飛ぶ弾が当たる敵(線が体を通る敵のうち、いちばん手前。いなければ null)。ロックオンの相手なら、相手を囲む角を濃い橙にする
        /// (作者「敵の囲みは枠と同じ青のままで、弾が当たるタイミングで今より濃いオレンジにしてほしいです」。DuelGame.DrawLockCorners)。
        /// </summary>
        public DuelEnemy ShotEnemy { get; private set; }

        /// <summary>照準枠の横の半分の大きさ(角度の tan)。</summary>
        float FrameTan(DuelWeapon w) => Mathf.Tan(Rules.FrameDeg(w) * Mathf.Deg2Rad);

        struct EnemyView
        {
            public DuelEnemy e;
            public bool front;
            public float ax, ay, ra, bx, by, rb;
            public sbyte sight;
        }

        EnemyView[] views = new EnemyView[32];
        int viewCount, viewFrame = -1;
        Vector3 viewEye;

        /// <summary>このフレームの敵ごとの見え方を作る(もう作ってあれば何もしない)。カメラの後ろの敵は front が false。</summary>
        void PrepareViews(Camera cam)
        {
            if (viewFrame == Time.frameCount) return;
            viewFrame = Time.frameCount;
            var list = game.Enemies;
            if (views.Length < list.Count) views = new EnemyView[Mathf.NextPowerOfTwo(list.Count)];
            viewCount = list.Count;
            viewEye = player.LockPoint;
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

        /// <summary>
        /// k 番目の敵の体が、照準の位置を中心にした枠(横 ±hw・縦 ±hh。tan)に掛かっているか。近い敵ほど画面で大きいので掛かりやすい。カメラの後ろの敵は入らない。
        /// </summary>
        bool InFrame(int k, float hw, float hh) => InFrame(k, cursorTan.x, cursorTan.y, hw, hh);

        /// <summary>k 番目の敵の体が、(cx, cy)(画面の中央からの tan)を中心にした枠(横 ±hw・縦 ±hh。tan)に掛かっているか。</summary>
        bool InFrame(int k, float cx, float cy, float hw, float hh)
        {
            ref var v = ref views[k];
            if (!v.front || v.e == null || v.e.Dead) return false;
            if (Mathf.Max(v.ax + v.ra, v.bx + v.rb) - cx < -hw || Mathf.Min(v.ax - v.ra, v.bx - v.rb) - cx > hw) return false;
            return !(Mathf.Max(v.ay + v.ra, v.by + v.rb) - cy < -hh || Mathf.Min(v.ay - v.ra, v.by - v.rb) - cy > hh);
        }

        /// <summary>
        /// 敵 e の体が、画面の中央を中心にした枠(横 ±hw・縦 ±hh。tan)に掛かっているか。ロックオンの付け始めの判定(DuelPlayer.PickBest。枠は DuelGame.LockSightTan)。
        /// 銃の照準の枠と同じ体の見え方(PrepareViews・InFrame。体は BodyAxis の縦長のカプセル)を使い回す。照準の位置(cursorTan)には寄らず、いつも画面の中央。
        /// index は e の DuelGame.Enemies での番号(見え方は同じ並びなので、まずその番号を見る。合えば探さない)。分からなければ -1(頭から探す)。
        /// このフレームの見え方を作ったあとに現れた敵は、次のフレームから入る。
        /// </summary>
        public bool BodyInCenterFrame(Camera cam, DuelEnemy e, int index, float hw, float hh)
        {
            if (cam == null || e == null || player == null || game == null) return false;
            PrepareViews(cam);
            if (index >= 0 && index < viewCount && views[index].e == e) return InFrame(index, 0f, 0f, hw, hh);
            for (int k = 0; k < viewCount; k++)
                if (views[k].e == e) return InFrame(k, 0f, 0f, hw, hh);
            return false;
        }

        /// <summary>k 番目の敵を自機から見通せるか(このフレームで初めて聞かれたときだけ調べ、あとは覚えておく)。</summary>
        bool Visible(int k)
        {
            ref var v = ref views[k];
            if (v.sight == 0) v.sight = DuelSenses.ClearSight(viewEye, v.e.Chest) ? (sbyte)1 : (sbyte)-1;
            return v.sight > 0;
        }

        /// <summary>
        /// 吸い付く相手(遠距離武器):照準の枠に掛かっている、射程の内で見通せる敵のうち、照準から体まで寄せる角度が一番小さい敵
        /// (同じくらいならロックオン中の敵。ロックオン中の敵も枠は広くみなさない)。いなければ null(照準の先へまっすぐ撃つ)。
        /// 枠に掛かっていても、寄せきれなければ外れる(AssistDir)。射程は撃ち出す所(from)から測る。
        /// 見通しは自機から調べる(敵ごとに1フレーム1回。武装ごとに撃ち出す所から調べ直さない)。
        /// </summary>
        DuelEnemy AimTarget(DuelWeapon w, DuelShot shot, Vector3 from)
        {
            var cam = Camera.main;
            // 照準の先がカメラの後ろ(自機がカメラの方を向いて撃つ)なら吸い付かない(枠が画面の中央に置かれ、背中側の敵へ狙いが寄ってしまうため)
            if (cam == null || cursorBehind) return null;
            PrepareViews(cam);
            var rules = Rules;
            float hw = FrameTan(w), hh = hw * rules.frameAspect;
            float range2 = shot.range * shot.range;
            Vector3 o = cam.transform.position, d = AimDir(cam);
            var t = player.Target;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            for (int k = 0; k < viewCount; k++)
            {
                var e = views[k].e;
                if (e == null || !InFrame(k, hw, hh)) continue;
                float d2 = (e.Chest - from).sqrMagnitude;
                if (d2 > range2) continue;
                float score = BodyNeedDeg(o, d, e, out _) - (e == t ? 0.5f : 0f) + Mathf.Sqrt(d2) * 0.002f;
                if (score >= bestScore || !Visible(k)) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        /// <summary>
        /// 吸い付きの強さ:照準の狙いを、枠の中の敵へ寄せられる最大の角度(度)。枠の大きさ × assistPart(小さい枠ほど弱い)。
        /// 照準を動かせるとき(cursorMovable)だけ、弾が速さを持つ撃ち方(実弾の遠距離武器)は projectileAssist がオフ(既定)なら 0
        /// (照準を先に置いた偏差射撃を、敵の今の位置へ引き戻さないように)。照準を動かせない既定では、どの撃ち方にも吸い付く。
        /// </summary>
        float AssistDeg(DuelWeapon w, DuelShot shot)
        {
            var r = Rules;
            // 照準を動かせない(既定)ときは、先に置く偏差射撃ができないので、弾が速さを持つ撃ち方にも吸い付かせる(敵の今の位置へ。先読みはしない)
            if (!r.projectileAssist && r.cursorMovable && SpeedOf(shot) > 0f) return 0f;
            return r.FrameDeg(w) * Mathf.Clamp01(r.assistPart);
        }

        /// <summary>
        /// 向き d(o から)を敵 e の体に掛けるのに要る角度(度。もう掛かっていれば 0)。q は体の芯(縦の線)の上で d に一番近い点。
        /// </summary>
        float BodyNeedDeg(Vector3 o, Vector3 d, DuelEnemy e, out Vector3 q)
        {
            float r = BodyAxis(e, out Vector3 a, out Vector3 b);
            SegSegDistance(a, b, o, o + d * AimFar, out float s);
            q = a + (b - a) * s;
            Vector3 to = q - o;
            float len = to.magnitude;
            if (len <= r) return 0f;
            float body = Mathf.Asin(Mathf.Clamp01(r / len)) * Mathf.Rad2Deg;
            return Mathf.Max(0f, Vector3.Angle(d, to) - body);
        }

        /// <summary>
        /// 照準の向き(カメラから照準を通る向き)を、敵 t の体へ最大 maxDeg 度だけ寄せた向き。reach は寄せた向きが体に掛かるか(当たるか)。
        /// </summary>
        Vector3 AssistDir(Camera cam, DuelEnemy t, float maxDeg, out bool reach)
        {
            Vector3 o = cam.transform.position, d = AimDir(cam);
            reach = false;
            if (t == null || t.Dead) return d;
            float need = BodyNeedDeg(o, d, t, out Vector3 q);
            if (need <= 0f) { reach = true; return d; }
            reach = need <= maxDeg;
            // 体の芯へ向けて回す。体に掛かるのに要る分(+少し)より多くは回さない(中央の狙いをなるべく残す)
            float turn = Mathf.Min(maxDeg, need + 0.3f);
            return Vector3.RotateTowards(d, (q - o).normalized, turn * Mathf.Deg2Rad, 0f);
        }

        /// <summary>
        /// 遠距離武器の狙う所:照準の向きを、吸い付く相手 t へ少しだけ寄せた向きの先(最初に当たる地形・敵)。from から射程(range)までに切る。
        /// </summary>
        Vector3 AssistedAimPoint(DuelEnemy t, float maxDeg, Vector3 from, float range)
        {
            var cam = Camera.main;
            if (cam == null) return t != null ? t.Chest : FreeAimPoint(from, range);
            if ((t == null || t.Dead) && aimDirValid && Rules.FacingAim)
            {
                // 自機の向いている方へ撃つ形で、寄せる相手がいない:自機の胸から向きの先へ伸ばした所(CursorPoint。ロックオンの相手より奥には置いていない)を
                // そのまま狙う。カメラから調べ直すと、自機がカメラと違う方を向いているとき(ロックオンしていない射撃)、
                // カメラとその点の間にある柱・壁に狙いが乗って、弾が自機の向きから逸れる
                Vector3 fd = CursorPoint - from;
                return fd.magnitude > range ? from + fd.normalized * range : CursorPoint;
            }
            Vector3 o = cam.transform.position;
            Vector3 dir = AssistDir(cam, t, maxDeg, out _);
            Vector3 p = AimPointAlong(o, dir, out _);
            // 当てる相手(いなければロックオンの相手)より奥の物には狙いを置かず、相手と同じ奥行きの所へ(線が相手をかすめても、狙いが奥へ跳ばない)
            var lt = t != null && !t.Dead ? t : player != null ? player.Target : null;
            if (lt != null && !lt.Dead)
            {
                float depth = Vector3.Dot(lt.Chest - o, dir);
                if (depth > 0f && Vector3.Dot(p - o, dir) > depth) p = o + dir * depth;
            }
            Vector3 d = p - from;
            return d.magnitude > range ? from + d.normalized * range : p;
        }

        /// <summary>弾が飛ぶ所の見込み。自機から受け継いだ速度も照準の十字に反映する。</summary>
        Vector3 PredictShot(DuelShot shot, Vector3 from, Vector3 aim)
        {
            Vector3 to = aim - from;
            float dist = to.magnitude;
            float speed = SpeedOf(shot);
            float inherit = Rules.inheritShooterVelocity;
            if (dist < 0.01f || speed <= 0f || inherit <= 0f) return aim;
            Vector3 dir = to / dist;
            Vector3 velocity = dir * speed + inheritVel * inherit;
            if (velocity.sqrMagnitude <= 1f) return aim;
            Vector3 flightDir = velocity.normalized;
            float along = dist / Mathf.Max(0.05f, Vector3.Dot(flightDir, dir));
            return from + flightDir * Mathf.Min(along, shot.range);
        }

        bool UpdateFrame(bool special, out Vector3 from, out Vector3 aim)
        {
            ShotFrame = null;
            MarkedEnemy = ShotEnemy = null;
            ShotPoint = null;
            from = aim = default;
            var cam = Camera.main;
            if (!Aiming || cam == null) return false;

            var w = RangedWeapon;
            var shot = special ? w.Special : w.Primary;
            float recoil = special ? 0f : bloom;
            float drawW = Mathf.Tan(Mathf.Min(80f, Rules.FrameDeg(w) + recoil) * Mathf.Deg2Rad);
            from = Muzzle();
            var target = AimTarget(w, shot, from);
            float assist = AssistDeg(w, shot);
            bool locked = false;
            if (target != null)
            {
                AssistDir(cam, target, assist, out locked);
                if (locked) MarkedEnemy = target;
            }
            aim = AssistedAimPoint(target, assist, from, shot.range);
            Vector3 predicted = PredictShot(shot, from, aim);
            ShotPoint = predicted;
            ShotEnemy = PathEnemy(from, predicted, shot);
            ShotFrame = new AimFrame
            {
                drawW = drawW, drawH = drawW * Rules.frameAspect, hot = Cooling > 0f, locked = locked,
                spread = Mathf.Tan(Mathf.Min(80f, shot.spread + recoil) * Mathf.Deg2Rad),
            };
            return true;
        }

        /// <summary>狙う相手がいないとき:照準の先(最初に当たる地形・敵。なければ遠く)。</summary>
        Vector3 FreeAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return player.Chest + player.transform.forward * 100f;
            return AimPointAlong(cam.transform.position, AimDir(cam), out _);
        }

        const float AimFar = 250f;

        /// <summary>o から向き d の先で最初に当たる地形・敵(なければ AimFar 先)。enemy は当たった敵(地形・空なら null)。</summary>
        Vector3 AimPointAlong(Vector3 o, Vector3 d, out DuelEnemy enemy)
        {
            // カメラは壁で寄らず、壁の向こうに残って壁を透かすことがある(DuelCamera)。カメラと自機の間の物(透けている壁など)に当てて、
            // 照準の先が自機より手前(カメラ側)にならないよう、自機の位置(線の上へ下ろした所)より先だけを調べる
            float skip = player != null ? Mathf.Max(0f, Vector3.Dot(player.Chest - o, d)) : 0f;
            Vector3 s = o + d * skip;
            float best = AimFar;
            if (Physics.Raycast(s, d, out var hit, AimFar, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (RayHitsEnemy(s, d, best, out enemy, out float ed)) best = ed;
            return s + d * best;
        }

        /// <summary>照準の先の点を、from から射程(range)までに切ったもの(狙う敵がいないとき、射程より遠くへ飛ばさない)。</summary>
        Vector3 FreeAimPoint(Vector3 from, float range)
        {
            Vector3 p = FreeAimPoint();
            Vector3 d = p - from;
            return d.magnitude > range ? from + d.normalized * range : p;
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
