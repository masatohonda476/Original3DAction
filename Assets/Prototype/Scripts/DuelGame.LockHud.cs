using UnityEngine;

namespace Prototype
{
    // ロックオンの照準枠と距離目盛り、画面外の標識。
    public partial class DuelGame
    {
        void DrawLockHud(Camera cam, DuelArms arms)
        {
            var target = player.Target;
            // 倒した相手も、爆発するまではロックオンが残る(DuelPlayer.UpdateLock)。
            if (target != null && !target.Exploded && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(target.LockPoint);
                // 画面の外(敵が背中側へ回り、カメラが上限の速さで向き直っている途中など):画面の端に、その方向を示す小さな印(見失わないように)
                if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) DrawOffscreenLock(cam, target.LockPoint);
                else if (arms.ShotPoint == null && !arms.FreeAim)
                {
                    // 近接を構えている間、十字は相手の胸に出す。遠距離は弾が飛んでいく所を示す(DrawAimFrames)。
                    Vector3 cp = cam.WorldToScreenPoint(target.Chest);
                    if (cp.z > 0f) DrawCross(cp.x / uiScale, SH - cp.y / uiScale, CrossMinGap, 0.6f);
                }
            }
            DrawLockCorners(cam);   // ロックオンのオン・オフの照準枠
        }

        /// <summary>
        /// ロックオンの相手が画面の外にいるとき、画面の端(内側に少し寄せた所)に、その方向を指す小さな三角を出す。
        /// 後ろにいるときも、カメラから見た左右・上下の向きで指す。
        /// </summary>
        void DrawOffscreenLock(Camera cam, Vector3 world)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector3 v = cam.transform.InverseTransformPoint(world);
            Vector2 d = new(v.x, v.y);
            if (d.sqrMagnitude < 0.0001f) d = Vector2.down;   // 真後ろ:下を指す
            d.Normalize();
            const float margin = 34f;
            float hx = SW * 0.5f - margin, hy = SH * 0.5f - margin;
            float t = Mathf.Min(Mathf.Abs(d.x) > 0.0001f ? hx / Mathf.Abs(d.x) : float.MaxValue, Mathf.Abs(d.y) > 0.0001f ? hy / Mathf.Abs(d.y) : float.MaxValue);
            Vector2 at = new(SW * 0.5f + d.x * t, SH * 0.5f - d.y * t);   // 画面の y は下向き
            float ang = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, at * uiScale);
            var tex = Texture2D.whiteTexture;
            // 右を指す段々の三角(回して向ける)。黒い縁で、明るい背景でも見えるように
            for (int pass = 0; pass < 2; pass++)
            {
                float o = pass == 0 ? 1.5f : 0f;
                GUI.color = pass == 0 ? new Color(0f, 0f, 0f, 0.5f) : new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(new Rect(at.x - 7f - o, at.y - 7f - o, 4f + o * 2f, 14f + o * 2f), tex);
                GUI.DrawTexture(new Rect(at.x - 3f - o, at.y - 5f - o, 4f + o * 2f, 10f + o * 2f), tex);
                GUI.DrawTexture(new Rect(at.x + 1f - o, at.y - 3f - o, 4f + o * 2f, 6f + o * 2f), tex);
                GUI.DrawTexture(new Rect(at.x + 5f - o, at.y - 1f - o, 2f + o * 2f, 2f + o * 2f), tex);
            }
            GUI.matrix = saved;
            GUI.color = Color.white;
        }

        // ───────── ロックオンのオン・オフの照準枠(電脳斬禅の DuelHud.LockState・LockCornersGraphic と同じ考え。値は 1280×720 の画面の単位) ─────────
        // オンの間:画面の縁寄りに薄い水色の四つの角を出し続ける(オンにした瞬間は外から締まって止まる)。
        //   狙う敵が画面に映って見えている間は、角ごと敵の体を囲む小さな枠へ移って(縮みながら)ついて回る。見えなくなったら画面の縁へ戻る。
        // オフにした瞬間:角が外へ広がりながら灰色に褪せて消える。オフの間は何も出さない。
        // 揺らさない(枠が動くのは付け外しの一度だけで、画面は動かさない)。文字の「LOCK ON / OFF」は出さない(電脳斬禅も作者の指示でやめた)。
        const float LockCornerHalfW = 507f, LockCornerHalfH = 180f, LockCornerEdge = 107f, LockCornerArm = 37f;
        const float LockCornerTravel = 173f, LockRestAlpha = 0.55f, LockOnTime = 0.55f, LockOffTime = 0.45f;
        const float LockFollowTime = 0.3f, LockFollowAlpha = 0.4f, LockFollowHold = 0.5f;
        static readonly Color LockInk = new(0.48f, 0.93f, 1f), LockMuted = new(0.56f, 0.66f, 0.7f, 0.65f);
        bool lockHudInit, lockShownOn, lockCornersShown;
        float lockOnAt = -1f, lockOffAt = -1f, lockFromOffset = LockCornerTravel, lockOffset, lockHalfH = LockCornerHalfH;
        Color lockFromInk = Color.white, lockInk = LockInk;
        float lockFollow, lockFollowSeenAt = -10f, lockHudAt = -1f;
        /// <summary>
        /// 角が囲んでいる相手に弾が当たる度合い(0〜1。なめらかに変える)。0 なら角は枠と同じ水色のまま、1 で濃い橙(FrameHit)にして透けさせない
        /// (作者「敵の囲みは枠と同じ青のままで、弾が当たるタイミングで今より濃いオレンジにしてほしいです」。前は弾が吸い付く相手なら薄い橙にしていた)。
        /// </summary>
        float lockHit;
        const float LockHitTime = 0.1f, LockHitAlpha = 1f;
        Vector2 lockFollowCenter, lockFollowHalf;

        void DrawLockCorners(Camera cam)
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            bool on = player.LockOn;
            if (!lockHudInit) { lockHudInit = true; lockShownOn = on; }   // 始めたときは動かさない(切り替えたときだけ動く)
            if (on != lockShownOn)
            {
                // 付け外しの途中で反対に切り替えたときは、今の広がりと色から次の動きを始める(跳ばない)
                lockShownOn = on;
                lockFromOffset = lockCornersShown ? lockOffset : on ? LockCornerTravel : 0f;
                lockFromInk = lockCornersShown ? lockInk : on ? Color.white : LockInk;
                if (!lockCornersShown) lockFromInk.a = on ? 0.95f : LockRestAlpha;
                lockOnAt = on ? now : -1f;
                lockOffAt = on ? -1f : now;
            }
            if (player.Dead || MenuOpen || (!on && player.Arms.FreeAim))
            {
                lockOnAt = lockOffAt = -1f;
                ResetLockHud();
                return;
            }
            if (on)
            {
                float t = lockOnAt >= 0f ? Mathf.Clamp01((now - lockOnAt) / LockOnTime) : 1f;
                if (t >= 1f) lockOnAt = -1f;
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                lockOffset = Mathf.Lerp(lockFromOffset, 0f, ease);
                var ink = Color.Lerp(lockFromInk, LockInk, ease);
                ink.a = Mathf.Lerp(lockFromInk.a, LockRestAlpha, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t)));
                lockInk = ink;
            }
            else
            {
                if (lockOffAt < 0f || now - lockOffAt >= LockOffTime)
                {
                    lockOffAt = -1f;
                    ResetLockHud();
                    return;
                }
                float t = Mathf.Clamp01((now - lockOffAt) / LockOffTime);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1f, t));
                lockOffset = lockFromOffset + LockCornerTravel * 0.75f * t * t;
                var ink = Color.Lerp(lockFromInk, LockMuted, Mathf.Clamp01(t * 2f));
                ink.a = alpha * Mathf.Lerp(lockFromInk.a, 0.85f, Mathf.Sin(t * Mathf.PI));
                lockInk = ink;
            }
            lockCornersShown = true;

            // 敵を捉えている間は、敵の体を囲む枠へ移る。切り替え直後に次の敵がまだ見えない短い間は、今の場所で待つ(オフにしたときは待たない)
            float dt = lockHudAt < 0f ? 0f : Mathf.Max(0f, now - lockHudAt);
            lockHudAt = now;
            var target = on ? player.Target : null;
            bool onEnemy = LockTargetBox(cam, target, out var boxCenter, out var boxHalf);
            if (onEnemy)
            {
                lockFollowSeenAt = now;
                float k = lockFollow <= 0f ? 1f : 1f - Mathf.Exp(-dt * 18f);
                lockFollowCenter = Vector2.Lerp(lockFollowCenter, boxCenter, k);
                lockFollowHalf = Vector2.Lerp(lockFollowHalf, boxHalf, k);
            }
            bool movingFreely = player.LockSuspendedForMove;
            if (movingFreely) lockFollowSeenAt = -10f;
            bool hold = on && !onEnemy && lockFollow > 0f && now - lockFollowSeenAt < LockFollowHold;
            if (!hold) lockFollow = Mathf.MoveTowards(lockFollow, onEnemy ? 1f : 0f, dt / LockFollowTime);
            float e = Mathf.SmoothStep(0f, 1f, lockFollow);
            // 角は枠と同じ水色のまま。弾が囲んでいる相手に当たるとき(真ん中の十字が相手に重なって橙になるのと同じ条件。DuelArms.ShotEnemy)だけ、濃い橙にする。
            var arms = player.Arms;
            bool aiming = arms.FreeAim;
            bool hit = onEnemy && (arms.ShotEnemy == target || SubTracksEnemy(arms, target));
            lockHit = Mathf.MoveTowards(lockHit, hit ? 1f : 0f, dt / LockHitTime);

            // 画面の縁寄りの枠(待機中に止まっている大きさは LockRestHalf。付け始めの判定と同じ値):縦が変わるときはなめらかに
            Vector2 rest = LockRestHalf();
            float wantH = rest.y;
            lockHalfH = dt <= 0f ? wantH : Mathf.MoveTowards(lockHalfH, wantH, 267f * dt);
            float baseW = rest.x;
            float hw = baseW + lockOffset, hh = lockHalfH + lockOffset * (lockHalfH / baseW);
            // 右辺の距離の目盛り:待機中の枠(敵を囲みに縮む前の大きさ。オン・オフの出入りには付いていく)の右辺に出す。
            // 出すのは、オンで狙う敵がいない(付く相手を探している)間だけ。付いている間は、相手が遮られた・画面の外に出たときも出さない
            // (枠を出し始めたフレーム=オンにした瞬間・メニューを閉じた直後に、もう付いていれば最初から出さない)。枠が敵から戻る途中は、戻るのに合わせて濃くなる
            DrawLockGauge(on && !aiming && !movingFreely && player.Target == null, new Vector2(SW * 0.5f, SH * 0.5f), hw, hh, 1f - e, dt);
            hw = Mathf.Lerp(hw, lockFollowHalf.x + lockOffset * 0.35f, e);
            hh = Mathf.Lerp(hh, lockFollowHalf.y + lockOffset * 0.35f, e);
            Vector2 c = new Vector2(SW * 0.5f, SH * 0.5f) + lockFollowCenter * e;
            var col = lockInk;
            // 構え中は武器のサイトを使い、ロックオンは敵を囲む表示だけ残す。
            if (aiming) col.a *= e;
            if (e > 0f) col.a = Mathf.Min(1f, col.a * Mathf.Lerp(1f, LockFollowAlpha / LockRestAlpha, e));   // 敵に重なる間は薄く
            float a = lockHit * e;
            if (a > 0f)
            {
                float alpha = Mathf.Lerp(col.a, Mathf.Max(col.a, LockHitAlpha * lockInk.a / LockRestAlpha), a);   // 付け外しの途中の薄さは保つ
                col = Color.Lerp(col, FrameHit, a);
                col.a = Mathf.Min(1f, alpha);
            }
            float thin = Mathf.Lerp(1f, 0.7f, e);
            // 明るい床の上でも形が読めるよう、細い暗い縁を下に敷く
            LockCornerLines(c, hw, hh, e < 0.5f, 2.7f * thin, new Color(0f, 0.025f, 0.04f, col.a * 0.8f));
            LockCornerLines(c, hw, hh, e < 0.5f, 1.35f * thin, col);
            GUI.color = Color.white;
        }

        void ResetLockHud()
        {
            lockCornersShown = false;
            lockFollow = lockHit = 0f;
            lockHudAt = -1f;
            lockGaugeShow = lockMarkShow = 0f;
        }

        // ───────── ロックオン距離の目盛り(待機中の照準枠の右辺。値は 1280×720 の画面の単位) ─────────
        // 作者「ロックオン可能な距離も枠のUIで表現したいです」→ 見せ方は「枠の辺に距離の目盛り」(右辺に 0〜ロックオン距離の目盛りを付け、
        //   枠に掛かった敵までの距離を小さな印で示す。印が目盛りの内に入ると付く。あとどれだけ近づけばよいかが続けて読める)。
        // 縦の目盛り:下が 0m(近い)・上が遠い(上端 = ロックオン距離 × LockGaugeFarMul)。ロックオン距離(DuelStage.lockOnRange)の所に目立つ刻み、
        //   その間はロックオン距離を LockGaugeDivs 等分した小さな刻み。刻みより下(付く距離)の軸は少し太く濃い。
        // 印(◀。軸の外側から軸を指す):DuelPlayer.LockCandidate(体が枠に掛かり・遮られていない敵のうち、一番近い敵)までの距離。
        //   印の敵は、係の質問「距離の目盛りの印は、どの敵の距離を示しますか?」に作者が選んだ「一番近い敵」
        //   (「枠に掛かって遮られていない敵のうち、一番近い敵の距離を示す。『印が刻みに届いたら必ず付く』がいつも成り立つ。」)。
        //   測り方は付け始めの判定と同じ(LockCandidateDistance)。印が刻みに届いたフレームにどれかの敵に付き(付く敵は点数で選ぶので、印の敵とは限らない)、
        //   枠が敵を囲みに縮むのと一緒に目盛りごと消える。
        //   上端より遠い敵は上端に止めて薄くする(上端の 1.5 倍より遠い敵は見ない。DuelPlayer.LockCandidateBeyond)。該当する敵がいなければ印は出さず、目盛りだけ薄く残す。
        // 出すのはロックオンがオンで、狙う敵がいない(付く相手を探している)間だけ(付いている間・オフ・メニュー・撃破では出さない)。揺らさない・点滅させない:
        //   印は敵の距離のとおりに動き、印の相手が替わったときだけ、今の位置から新しい位置へなめらかに滑らせる(跳ばない)。出入りは濃さだけ。文字は出さない。
        /// <summary>目盛りの上端の距離 = ロックオン距離 × これ(印に出す敵を探す距離の上限も、これから決める。DuelPlayer.PickBest)。</summary>
        public const float LockGaugeFarMul = 2f;
        const float LockGaugeAlpha = 0.9f, LockGaugeIdleAlpha = 0.55f, LockGaugeBeyondAlpha = 0.4f;
        const int LockGaugeDivs = 4;
        const float LockGaugeFadeTime = 0.2f, LockMarkFadeTime = 0.15f, LockMarkSettle = 10f;
        /// <summary>目盛りの濃さ(0〜1。オンで狙う敵がいない間に出て、付く・オフで消える)・印の濃さ(0〜1)・印の位置(0 = 0m 〜 1 = 上端)・相手が替わったときに残る位置のずれ(0 へ戻る)。</summary>
        float lockGaugeShow, lockMarkShow, lockMarkPos, lockMarkSlip;
        DuelEnemy lockMarkEnemy;

        /// <summary>
        /// 待機中の照準枠(中心 c・横の半分 hw・縦の半分 hh。オン・オフの出入りの広がりを含む)の右辺に、ロックオン距離の目盛りと敵までの距離の印を描く。
        /// seeking はロックオンがオンで狙う敵がいない(付く相手を探している)か。この間だけ出す。
        /// rest は枠が待機している度合い(1 = 待機、0 = 敵を囲んでいる)。dt は前に描いてからの秒数(0 なら濃さ・位置をその場で合わせる)。
        /// </summary>
        void DrawLockGauge(bool seeking, Vector2 c, float hw, float hh, float rest, float dt)
        {
            float range = stage != null ? stage.lockOnRange : 0f;
            lockGaugeShow = dt <= 0f ? (seeking ? 1f : 0f) : Mathf.MoveTowards(lockGaugeShow, seeking ? 1f : 0f, dt / LockGaugeFadeTime);
            float show = lockGaugeShow * rest * rest;
            // 印:相手がいる間は距離のとおりの位置へ。相手が替わったら、今の位置からのずれを残して 0 へ戻す(なめらかに滑る)。いなくなったら、その場で薄くして消す
            var cand = seeking ? player.LockCandidate : null;
            bool has = range > 0f && cand != null && !cand.Dead;
            float beyond = 0f;
            if (has)
            {
                float far = range * LockGaugeFarMul, d = player.LockCandidateDistance;
                float want = Mathf.Clamp01(d / far);
                if (lockMarkShow <= 0f || show <= 0f || dt <= 0f) lockMarkSlip = 0f;   // 出し始めは、その位置に出す(滑らせない)
                else if (cand != lockMarkEnemy) lockMarkSlip = lockMarkPos - want;
                else lockMarkSlip *= Mathf.Exp(-dt * LockMarkSettle);
                lockMarkEnemy = cand;
                lockMarkPos = want + lockMarkSlip;
                beyond = Mathf.InverseLerp(far * 0.92f, far, d);   // 上端の手前からなめらかに薄く(上端より遠ければ薄いまま)
            }
            else if (lockMarkShow <= 0f) lockMarkEnemy = null;
            lockMarkShow = dt <= 0f ? (has ? 1f : 0f) : Mathf.MoveTowards(lockMarkShow, has ? 1f : 0f, dt / LockMarkFadeTime);
            if (range <= 0f || show <= 0.01f) return;

            // 軸は右辺の、上下の角の腕の間
            float arm = Mathf.Min(LockCornerArm, Mathf.Min(hw, hh) * 0.3f);
            float half = hh - arm - 8f;
            if (half < 20f) return;
            float x = c.x + hw, yNear = c.y + half, yFar = c.y - half;
            float yRange = Mathf.Lerp(yNear, yFar, 1f / LockGaugeFarMul);
            float a = LockRestAlpha * show * Mathf.Lerp(LockGaugeIdleAlpha, LockGaugeAlpha, lockMarkShow);
            var tex = Texture2D.whiteTexture;
            // 明るい床の上でも読めるよう、暗い縁を下に敷いてから水色で描く(枠の角と同じ)
            for (int pass = 0; pass < 2; pass++)
            {
                float o = pass == 0 ? 0.7f : 0f;
                Color ink = pass == 0 ? new Color(0f, 0.025f, 0.04f, 0.8f) : LockInk;
                void Box(float x0, float y0, float w, float h, float alpha)
                {
                    GUI.color = new Color(ink.r, ink.g, ink.b, ink.a * alpha);
                    GUI.DrawTexture(new Rect(x0 - o, y0 - o, w + o * 2f, h + o * 2f), tex);
                }
                // 軸:ロックオン距離より遠い側は細く薄く、内側(付く距離)は少し太く濃く
                Box(x - 0.5f, yFar, 1f, yRange - yFar, a * 0.55f);
                Box(x - 1f, yRange, 2f, yNear - yRange, a);
                // 小さな刻み(枠の内向き):ロックオン距離を LockGaugeDivs 等分。上端と 0m も入る
                int n = Mathf.RoundToInt(LockGaugeDivs * LockGaugeFarMul);
                for (int i = 0; i <= n; i++)
                {
                    if (i == LockGaugeDivs) continue;
                    float y = Mathf.Lerp(yNear, yFar, i / (float)n);
                    Box(x - 5f, y - 0.5f, 5f, 1f, a * (i < LockGaugeDivs ? 1f : 0.55f));
                }
                // ロックオン距離の刻み(目立たせる:長く・太く、軸の外側へも出す)
                Box(x - 13f, yRange - 1f, 19f, 2f, Mathf.Min(1f, a * 1.5f));
                // 敵までの距離の印(軸の外側から軸を指す、段々の三角 ◀)
                if (lockMarkShow > 0f)
                {
                    float ma = Mathf.Min(1f, show * lockMarkShow * Mathf.Lerp(1f, LockGaugeBeyondAlpha, beyond));
                    float y = Mathf.Lerp(yNear, yFar, Mathf.Clamp01(lockMarkPos));
                    Box(x + 4f, y - 1f, 2f, 2f, ma);
                    Box(x + 6f, y - 3f, 3f, 6f, ma);
                    Box(x + 9f, y - 5f, 4f, 10f, ma);
                }
            }
        }

        /// <summary>
        /// 待機中に止まっている照準枠の、横・縦の半分(1280×720 の画面の単位。中心は画面の中央)。表示(DrawLockCorners)と付け始めの判定(LockSightTan)がこの1か所を使う。
        /// 横は画面の縁から LockCornerEdge 内側まで(上限 LockCornerHalfW)。縦は LockCornerHalfH で、下の自機のゲージ・武装の欄の手前まで(低い画面では縮める)。
        /// オン・オフの出入り(lockOffset)と、付いた敵を囲む枠へ縮む動き(lockFollow)は入れない。
        /// </summary>
        Vector2 LockRestHalf()
        {
            float bottom = Mathf.Min(leftTop, ArmsTop) - 12f;
            float h = Mathf.Max(53f, Mathf.Min(LockCornerHalfH, bottom - SH * 0.5f));
            float w = Mathf.Max(80f, Mathf.Min(LockCornerHalfW, SW * 0.5f - LockCornerEdge));
            return new Vector2(w, h);
        }

        /// <summary>
        /// ロックオンの付け始めの判定に使う枠:待機中に止まっている照準枠(LockRestHalf)の横・縦の半分を、画面の中央からの tan で返す
        /// (作者「ロックオンで出てくるサイトの枠内に入った場合のみロックオン開始する」。見えている枠と判定を合わせる)。
        /// Update から呼ばれるので、画面の表示の倍率はここでも求め直す(OnGUI に頼らない)。カメラが画面いっぱいに映している前提(照準の枠 DrawAimFrames と同じ)。
        /// 枠が出ていない間(メニュー・オンにした瞬間の締まる途中)も、止まっている枠の大きさを返す。カメラがなければ false。
        /// </summary>
        public bool LockSightTan(Camera cam, out float halfW, out float halfH)
        {
            halfW = halfH = 0f;
            if (cam == null) return false;
            RefreshUiScale();
            Vector2 rest = LockRestHalf();
            float k = SightPixelsPerTangent(cam);
            if (!(k > 0f)) return false;
            halfW = rest.x / k;
            halfH = rest.y / k;
            return true;
        }

        /// <summary>狙う敵の体を囲む画面上の枠(中心は画面の中央から、下向きが +y。横・縦の半分)。敵が画面に映っていて、自機から遮られていないときだけ true。</summary>
        bool LockTargetBox(Camera cam, DuelEnemy target, out Vector2 center, out Vector2 half)
        {
            center = half = Vector2.zero;
            if (target == null || target.Exploded || cam == null) return false;   // 倒した相手も、爆発するまでは囲む
            Vector3 vp = cam.WorldToViewportPoint(target.LockPoint);
            if (vp.z <= 0.1f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
            if (!DuelSenses.ClearSight(player.LockPoint, target.LockPoint)) return false;
            Vector3 feet = target.transform.position, head = feet + Vector3.up * (mechHeight * target.Size * 1.2f);
            Vector3 a = cam.WorldToScreenPoint(feet), b = cam.WorldToScreenPoint(head);
            if (a.z <= 0.1f || b.z <= 0.1f) return false;
            Vector2 ga = new(a.x / uiScale, SH - a.y / uiScale), gb = new(b.x / uiScale, SH - b.y / uiScale);
            center = (ga + gb) * 0.5f - new Vector2(SW * 0.5f, SH * 0.5f);
            float hh = Mathf.Clamp(Vector2.Distance(ga, gb) * 0.6f, 30f, 160f);
            half = new Vector2(hh * 0.7f, hh);
            return true;
        }

        /// <summary>照準枠の線:四つの L 字の角(腕の先寄りに内向きの小さな刻み)と、各辺の真ん中の内向きの目盛り(marks が偽なら角だけ)。</summary>
        static void LockCornerLines(Vector2 c, float hw, float hh, bool marks, float w, Color ink)
        {
            GUI.color = ink;
            var tex = Texture2D.whiteTexture;
            float arm = Mathf.Min(LockCornerArm, Mathf.Min(hw, hh) * 0.3f), hw2 = w * 0.5f;
            void H(float x0, float x1, float y) => GUI.DrawTexture(new Rect(Mathf.Min(x0, x1), y - hw2, Mathf.Abs(x1 - x0), w), tex);
            void V(float x, float y0, float y1) => GUI.DrawTexture(new Rect(x - hw2, Mathf.Min(y0, y1), w, Mathf.Abs(y1 - y0)), tex);
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
                float px = c.x + sx * hw, py = c.y + sy * hh;
                H(px + sx * hw2, px - sx * arm, py);   // 角の外側で腕を少し重ね、継ぎ目が欠けないようにする
                V(px, py + sy * hw2, py - sy * arm);
                if (!marks) continue;
                float tx = px - sx * arm * 0.7f, ty = py - sy * arm * 0.7f;
                V(tx, py, py - sy * 5f);
                H(px, px - sx * 5f, ty);
            }
            if (!marks) return;
            const float mid = 9f;
            V(c.x, c.y - hh, c.y - hh + mid);
            V(c.x, c.y + hh, c.y + hh - mid);
            H(c.x - hw, c.x - hw + mid, c.y);
            H(c.x + hw, c.x + hw - mid, c.y);
        }
    }
}
