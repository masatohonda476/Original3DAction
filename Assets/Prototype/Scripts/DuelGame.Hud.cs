using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelGame の画面の表示:ゲージ・照準・ロックオンの印と照準枠・頭上のゲージ・ステージの知らせ・左下のメニューの案内(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
        // ───────── 画面 ─────────

        void OnGUI()
        {
            RefreshUiScale();
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label = new GUIStyle(GUI.skin.label) { fontSize = 20 };
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                center = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                richSmall = new GUIStyle(small) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
                viewHeadStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold, wordWrap = false };
                viewTitleStyle = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                rightSmall = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
                // 右下の武装の欄(武装ハントの WeaponCell と同じ大きさ。色は GUI.color で付けるので文字は白)
                cellTiny = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
                cellTiny.normal.textColor = Color.white;
                cellTinyRight = new GUIStyle(cellTiny) { alignment = TextAnchor.UpperRight };
                cellNameStyle = new GUIStyle(cellTiny) { fontSize = 17 };
                cellNameSmall = new GUIStyle(cellTiny) { fontSize = 14 };
            }
            var cam = Camera.main;
            if (player != null) DrawOverheadGauges(cam);   // 被ダメージの数字より奥に描く
            ProtoDamageText.Draw(cam, uiScale, SH);
            if (player == null) return;
            if (!MenuOpen)
            {
                if (player.Arms.SubAiming) DrawSubAim(player.Arms, cam);
                else DrawAimFrames(player.Arms, cam);   // 照準の枠は、ほかの表示より奥に描く
                DrawHitMark(player.Arms);
            }

            // 左下:自機のHP・EN、その下にメニューを開くボタンの短い案内を1行。
            // 操作方法は出し続けず、メニューの「操作方法」のページで見せる(作者「操作方法は常に出すのではなく、オプションにメニューを出してください」。DuelGame.Menu.cs)。
            const float hintH = LeftHintH;
            float x = 24f;
            float y = leftTop + 5f;
            Bar(new Rect(x, y, 300f, 14f), player.HP / player.MaxHP, new Color(0.35f, 0.85f, 0.45f));
            GUI.Label(new Rect(x + 308f, y - 5f, 120f, 24f), $"HP {Mathf.CeilToInt(player.HP)}", small);
            float healed = player.Arms.RecentItemGain(DuelItem.RepairArmor);
            if (healed > 0f)
            {
                GUI.color = DuelItems.Get(DuelItem.RepairArmor).Tint;
                GUI.Label(new Rect(x + 308f, y - 26f, 120f, 22f), $"HP +{healed:0.#}", small);
                GUI.color = Color.white;
            }
            // 怯みゲージ(HPの右、HPのバーの1/4の長さ)
            bool hurt = player.Current == DuelPlayer.State.Hurt;
            Bar(new Rect(x + 400f, y + 2f, 75f, 8f), hurt ? 1f : player.Stagger / player.maxStagger,
                hurt ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.6f, 0.2f));
            GUI.Label(new Rect(x + 482f, y - 5f, 120f, 24f), hurt ? "怯み!" : "怯み", small);
            y += 22f;
            // マイナスの間は、マイナスの量を赤で出す(0を超えるまで回避できない)
            bool minus = player.Energy <= 0f;
            Bar(new Rect(x, y, 300f, 10f), Mathf.Abs(player.Energy) / player.maxEnergy,
                minus ? new Color(1f, 0.35f, 0.25f) : player.BoostOn ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.82f, 0.25f));   // 通常は黄、ブーストオンで水色
            // ENが0以下でビーム・レーザーを撃てなかった直後は、文字を赤く光らせる(弾切れの表示と同じ合図)
            if (Time.time - player.Arms.NoEnergyAt < 1f) GUI.color = new Color(1f, 0.55f, 0.45f);
            GUI.Label(new Rect(x + 308f, y - 7f, 300f, 24f),
                (minus ? $"EN {Mathf.FloorToInt(player.Energy)}(回復待ち)" : $"EN {Mathf.FloorToInt(player.Energy)}") + (player.BoostOn ? "  ブースト" : ""), small);
            GUI.color = Color.white;
            y += 22f;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);   // HP・EN より控えめに
            GUI.Label(new Rect(x, y - 2f, ArmsLeft - 16f - x, hintH), MenuHint(), small);
            GUI.color = Color.white;
            if (Event.current.type == EventType.Repaint)
            {
                DrawArms(player.Arms);
                if (!MenuOpen)
                {
                    if (!player.Dead && !stage.Cleared) DrawItemsHud(player.Arms);
                    DrawPickup(player.Arms);
                }
            }

            // ロックオンの印は四つの角の枠だけ(敵を囲む。DrawLockCorners)。胸の上の白い十字は、枠と二重になるのでやめた(作者「敵の上で印が重なる」→「A」)
            var e = player.Target;
            if (e != null && !e.Exploded && cam != null)   // 倒した相手も、爆発するまではロックオンが残る(DuelPlayer.UpdateLock)
            {
                Vector3 sp = cam.WorldToScreenPoint(e.LockPoint);
                // 画面の外(敵が背中側へ回り、カメラが上限の速さで向き直っている途中など):画面の端に、その方向を示す小さな印(見失わないように)
                if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) DrawOffscreenLock(cam, e.LockPoint);
                else if (player.Arms.ShotPoint == null && !player.Arms.FreeAim)
                {
                    // 近接を構えている間、十字は相手の胸に出す。遠距離は弾が飛んでいく所を示す(DrawAimFrames)。
                    Vector3 cp = cam.WorldToScreenPoint(e.Chest);
                    if (cp.z > 0f) DrawCross(cp.x / uiScale, SH - cp.y / uiScale, CrossMinGap, 0.6f);
                }
            }
            DrawLockCorners(cam);   // ロックオンのオン・オフの照準枠

            // 上の中央:ロックオンしている敵のHPと怯みゲージ(ロックオンしていなくてもボスは出す)
            e = TopGaugeEnemy();
            if (e != null)
            {
                float w = 520f, ex = (SW - w) / 2f;
                GUI.Label(new Rect(ex, 2f, w, 22f), e.KindName, center);
                Bar(new Rect(ex, 24f, w, 14f), e.HP / e.MaxHP, new Color(0.9f, 0.3f, 0.25f));
                // 怯みゲージ:HPのバーの1/4の長さ
                float sw = w / 4f, sx = (SW - sw) / 2f;
                float est = e.Flinching ? 1f : Mathf.Clamp01(e.Stagger / e.maxStagger);
                Bar(new Rect(sx, 42f, sw, 8f), est, StaggerColor(e, est, Time.unscaledTime));
                GUI.Label(new Rect(ex, 52f, w, 24f), e.Flinching ? "怯み!" : "怯み", center);
            }

            // 右上:知らせと撃破数
            var rs = rightSmall;
            float elapsed = stage.Cleared ? stage.ClearTime : Time.time - startTime;
            GUI.Label(new Rect(SW - 320f, 16f, 300f, 22f), $"撃破 {kills}  {elapsed:0}秒", rs);
            float ly = 40f;
            for (int i = 0; i < log.Count; i++)
            {
                float age = Time.unscaledTime - log[i].t;
                if (age > 4f) continue;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((4f - age) / 1f));
                GUI.Label(new Rect(SW - 520f, ly, 500f, 22f), log[i].msg, rs);
                ly += 22f;
            }
            GUI.color = Color.white;

            DrawStage();
            if (player.Dead) GUI.Label(new Rect(0f, SH * 0.4f, SW, 50f), "撃墜 ― セーブポイントから再開します", big);
            if (MenuOpen) DrawMenu(player.Arms);   // メニュー(項目・装備の確認・操作方法)は、ほかの表示の上に描く
        }

        // ───────── 照準の枠(射撃だけ) ─────────

        static readonly Color FrameIdle = new(0.85f, 0.92f, 1f), FrameLocked = new(1f, 0.72f, 0.3f);
        /// <summary>
        /// 弾が当たるときの濃い橙(真ん中の十字と、ロックオンの相手を囲む角)。FrameLocked(ほかの敵の印の薄い橙)より濃い
        /// (作者「弾が当たるタイミングで今より濃いオレンジにしてほしいです」「十字の視認性が少し悪いので、当たるタイミングではもっと濃くしてください」)。
        /// </summary>
        static readonly Color FrameHit = new(1f, 0.45f, 0.05f);

        /// <summary>画面中央に武器のサイトを描く。補正する敵が入ると橙になり、十字は弾道を示す。</summary>
        void DrawAimFrames(DuelArms arms, Camera cam)
        {
            if (Event.current.type != EventType.Repaint || cam == null || !arms.Aiming) return;
            if (arms.ShotFrame is not { } frame) return;
            float k = SH * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);   // tan → 画面の大きさ
            float cx = SW * 0.5f, cy = SH * 0.5f;
            DrawWeaponSightCorners(frame.halfW * k, frame.halfH * k, frame.locked, frame.hot);

            // 弾道の十字は補正と反動を反映する。カメラの後ろに来たら隠す。
            Vector3 shotSp = arms.ShotPoint is Vector3 shot ? cam.WorldToScreenPoint(shot) : Vector3.forward;
            if (shotSp.z > 0f)
            {
                float sx = cx, sy = cy;
                if (arms.ShotPoint != null) { sx = shotSp.x / uiScale; sy = SH - shotSp.y / uiScale; }
                // 見た目だけ、出す所へ CrossFollowTime 秒ほどで追いつかせる(狙いが急に変わっても跳ばずに滑る。弾の向きは変えない。作者「見た目だけでも間を補正とかできますか？」)
                float now = Time.unscaledTime;
                // 弾が敵の体に当たる所に十字があれば濃い橙にする(DuelArms.ShotOnEnemy。体の端で 1 フレームだけ色が変わってちらつかないよう、CrossHitTime 秒で色を移す)
                float onEnemy = arms.ShotOnEnemy ? 1f : 0f;
                if (crossShownAt < 0f || now - crossShownAt > 0.2f)
                {
                    // しばらく出していなければ、その場に出す
                    crossPos = new Vector2(sx, sy);
                    crossHit = onEnemy;
                }
                else
                {
                    crossPos = Vector2.Lerp(crossPos, new Vector2(sx, sy), 1f - Mathf.Exp(-3f * (now - crossShownAt) / CrossFollowTime));
                    crossHit = Mathf.MoveTowards(crossHit, onEnemy, (now - crossShownAt) / CrossHitTime);
                }
                crossShownAt = now;
                DrawCross(crossPos.x, crossPos.y, Mathf.Clamp(frame.spread * k, CrossMinGap, SH * 0.3f), frame.hot ? 0.85f : 0.4f, crossHit);
            }
            DrawAimTargetMark(cam, arms.MarkedEnemy);
        }

        void DrawWeaponSightCorners(float hw, float hh, bool locked, bool hot)
        {
            var color = locked ? FrameLocked : FrameIdle;
            color.a = locked || hot ? 0.9f : 0.6f;
            float cx = SW * 0.5f, cy = SH * 0.5f;
            Brackets(new Rect(cx - hw, cy - hh, hw * 2f, hh * 2f), Mathf.Clamp(Mathf.Min(hw, hh) * 0.35f, 8f, 26f), 2f, color);
        }

        void DrawAimTargetMark(Camera cam, DuelEnemy target)
        {
            // ロックオンの角が囲んでいる相手には印を重ねない。
            if (target == null || target.Dead || (target == player.Target && lockFollow > 0f)) return;
            Vector3 sp = cam.WorldToScreenPoint(target.Chest);
            if (sp.z <= 0f) return;
            float x = sp.x / uiScale, y = SH - sp.y / uiScale;
            var color = FrameLocked;
            color.a = 0.95f;
            Brackets(new Rect(x - 11f, y - 11f, 22f, 22f), 6f, 2f, color);
        }

        /// <summary>真ん中の十字の見た目の位置(GUI の座標)と、最後に出した時刻(実時間。-1 はまだ)。出す所へ CrossFollowTime 秒ほどで追いつく。</summary>
        Vector2 crossPos;
        float crossShownAt = -1f;
        const float CrossFollowTime = 0.08f;
        /// <summary>十字の真ん中の空きの最小(ぶれていないときの詰まり具合。作者「ぶれてないときのカーソルはもっと線が詰まった状態にしてください」で 3 → 1)。武装そのもののばらつきの分はこれより開く。</summary>
        const float CrossMinGap = 1f;
        /// <summary>十字が敵に重なっている度合い(0 = 重なっていない / 1 = 重なっている。濃い橙にする)。CrossHitTime 秒で移る。</summary>
        float crossHit;
        const float CrossHitTime = 0.06f;
        /// <summary>敵に重なっている十字の濃さ(透けさせない。作者「十字の視認性が少し悪いので、当たるタイミングではもっと濃くしてください」で 0.9 → 1、色も濃い橙に)。</summary>
        const float CrossHitAlpha = 1f;

        /// <summary>十字(4本の短い線。g は真ん中の空き)。照準の色で、濃さは alpha。hit(0〜1)は敵に重なっている度合いで、1 なら濃い橙(FrameHit。弾が当たるときのロックオンの角と同じ色)で透けさせない。</summary>
        static void DrawCross(float cx, float cy, float g, float alpha, float hit = 0f)
        {
            var sc = Color.Lerp(FrameIdle, FrameHit, hit);
            sc.a = Mathf.Lerp(alpha, Mathf.Max(alpha, CrossHitAlpha), hit);
            GUI.color = sc;
            const float len = 7f, th = 2f;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(cx - g - len, cy - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(cx + g, cy - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy - g - len, th, len), t);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy + g, th, len), t);
            GUI.color = Color.white;
        }

        // ヒットマーカーの色:当たった / 崩せる(怯みが射撃の上限まで。斬りにいけ)/ 崩れている敵に当たった / 倒した
        static readonly Color[] HitMarkColors =
        {
            new(1f, 1f, 1f), new(1f, 0.5f, 0.12f), new(1f, 0.9f, 0.25f), new(1f, 0.28f, 0.22f),
        };
        static readonly float[] HitMarkSizes = { 7f, 9f, 10f, 12f };

        /// <summary>
        /// ヒットマーカー:射撃(銃・砲)が敵に当たったら、照準の位置に小さな斜めの十字を短く出す(実時間。ヒットストップ中も消えていく)。
        /// 色と大きさで、当たった(白)/ 崩せる(橙。怯みが射撃の上限まで溜まった)/ 崩れている敵(黄)/ 倒した(赤)を見分ける。
        /// 出た瞬間だけ少し大きく、すぐ縮む(画面は揺らさない)。
        /// 照準の先が視界の外(カメラの後ろ・画面の外)の間は出さない(作者「消してください」。前は画面の中央か端に出ていた。視界の外の敵に当たっても合図は出ない)。
        /// 視界の外へ撃った弾は、当たったときに照準の先が画面に戻っていても出さない(弾ごとに覚える。DuelArms.NoteHit の mark)。
        /// </summary>
        void DrawHitMark(DuelArms arms)
        {
            if (Event.current.type != EventType.Repaint || player.Dead || (arms.Aiming && !arms.ShotInView)) return;
            float life = Mathf.Max(0.02f, arms.Rules.hitMarkTime);
            float age = Time.unscaledTime - arms.HitMarkAt;
            if (age < 0f || age > life) return;
            int kind = Mathf.Clamp(arms.HitMarkKind, 0, HitMarkColors.Length - 1);
            float a = 1f - age / life;
            var c = HitMarkColors[kind];
            c.a = Mathf.Clamp01(a * 1.5f);
            float len = HitMarkSizes[kind] * (1f + 0.35f * a * a), th = kind >= 1 ? 3f : 2f, gap = 4f;
            Vector2 center = new(SW * 0.5f, SH * 0.5f);
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center * uiScale);   // 回す中心は画面の画素で渡す(GUI.matrix の拡大の外側で回るため)
            GUI.color = c;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(center.x - gap - len, center.y - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(center.x + gap, center.y - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(center.x - th / 2f, center.y - gap - len, th, len), t);
            GUI.DrawTexture(new Rect(center.x - th / 2f, center.y + gap, th, len), t);
            GUI.matrix = saved;
            GUI.color = Color.white;
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
            if (player.Dead || MenuOpen)
            {
                lockOnAt = lockOffAt = -1f;
                lockCornersShown = false;
                lockFollow = lockHit = 0f;
                lockHudAt = -1f;
                lockGaugeShow = lockMarkShow = 0f;
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
                    lockCornersShown = false;
                    lockFollow = lockHit = 0f;
                    lockHudAt = -1f;
                    lockGaugeShow = lockMarkShow = 0f;
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
            DrawLockGauge(on && !aiming && player.Target == null, new Vector2(SW * 0.5f, SH * 0.5f), hw, hh, 1f - e, dt);
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
            float k = SH * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);   // tan → 画面の大きさ(DrawAimFrames と同じ)
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

        /// <summary>四角の4つの角に鉤(長さ len・太さ th)を描く。</summary>
        static void Brackets(Rect r, float len, float th, Color c)
        {
            GUI.color = c;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, len, th), t);
            GUI.DrawTexture(new Rect(r.x, r.y, th, len), t);
            GUI.DrawTexture(new Rect(r.xMax - len, r.y, len, th), t);
            GUI.DrawTexture(new Rect(r.xMax - th, r.y, th, len), t);
            GUI.DrawTexture(new Rect(r.x, r.yMax - th, len, th), t);
            GUI.DrawTexture(new Rect(r.x, r.yMax - len, th, len), t);
            GUI.DrawTexture(new Rect(r.xMax - len, r.yMax - th, len, th), t);
            GUI.DrawTexture(new Rect(r.xMax - th, r.yMax - len, th, len), t);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 怯みのバーの色(上の中央・頭上で同じ)。怯んでいる(崩れている)間は満タンの黄。StaggerBlinkFrom を超えたら白く点滅して
        /// 「あと少し・斬りにいけ」を知らせる(射撃は上限 rangedStaggerCap で止まるので、射撃で溜めきると点滅したままになる)。
        /// </summary>
        Color StaggerColor(DuelEnemy e, float st, float now)
        {
            if (e.Flinching) return new Color(1f, 0.85f, 0.2f);
            var c = new Color(1f, 0.6f, 0.2f);
            return st > StaggerBlinkFrom ? Color.Lerp(c, Color.white, 0.5f + 0.5f * Mathf.Sin(now * 16f)) : c;
        }

        /// <summary>上の中央のゲージに出す敵:ロックオンしている敵。いなければ、動いているボス。</summary>
        DuelEnemy TopGaugeEnemy()
        {
            var e = player.Target;
            if (e == null || e.Dead) e = stage.ActiveBoss;
            return e != null && !e.Dead ? e : null;
        }

        /// <summary>
        /// 頭上のゲージ(電脳斬禅と同じ作り):動いている敵のうち、近くて胸が画面に映り、壁や柱に隠れていないものの
        /// 頭の上に、HP(赤)と怯み(橙)の小さなバーを出す。遠いほど少し小さく。減ったHPは白で少し遅れて追う。
        /// 上の中央にボスが出ている間は、ボスには出さない(ロックオン中の兵卒・剣士には両方に出る)。
        /// </summary>
        void DrawOverheadGauges(Camera cam)
        {
            // 描くのは Repaint のときだけ(OnGUI は1フレームに何度も呼ばれる。レイと白い部分の更新を1回にする)
            if (Event.current.type != EventType.Repaint) return;
            if (cam == null || player.Dead)
            {
                overheadGhost.Clear();
                return;
            }
            var top = TopGaugeEnemy();
            var bossOnTop = top != null && top.IsBoss ? top : null;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            Vector3 eye = cam.transform.position;
            foreach (var e in Enemies)
            {
                if (e == null || e.Dead || e == bossOnTop) continue;
                float dist = Vector3.Distance(e.transform.position, player.transform.position);
                if (dist > overheadRange) continue;
                // 胸が画面の中に映っていて、カメラとの間に壁がない敵だけ(機体はレイを無視するレイヤー)
                Vector3 chest = e.Chest, vp = cam.WorldToViewportPoint(chest);
                if (vp.z <= 0.1f || vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.02f || vp.y > 0.98f) continue;
                if (Physics.Linecast(eye, chest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (mechHeight * e.Size * 1.1f));
                if (sp.z <= 0.1f) continue;

                float s = Mathf.Clamp(overheadShrinkFrom / Mathf.Max(1f, dist), 0.7f, 1f);
                float w = 130f * s, hpH = 7f * s, stH = 4f * s;
                float cx = Mathf.Clamp(sp.x / uiScale, w / 2f + 4f, SW - w / 2f - 4f);
                float cy = Mathf.Clamp(SH - sp.y / uiScale, 4f, SH - 30f);
                var hpRect = new Rect(cx - w / 2f, cy - hpH - stH - 2f * s, w, hpH);
                var stRect = new Rect(hpRect.x, hpRect.yMax + 2f * s, w, stH);

                // 減ったHPの白い部分:当たってから少し止まり、そのあと縮む
                float hp = Mathf.Clamp01(e.HP / e.MaxHP);
                if (!overheadGhost.TryGetValue(e, out var g)) g = (hp, hp, 0f);
                if (hp < g.hp) g.holdUntil = now + 0.4f;
                if (hp > g.ghost) g.ghost = hp;
                else if (now >= g.holdUntil) g.ghost = Mathf.Max(hp, g.ghost - 0.8f * dt);
                g.hp = hp;
                overheadGhost[e] = g;

                // 縁(黒)→ 中身
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(hpRect.x - 1f, hpRect.y - 1f, w + 2f, stRect.yMax - hpRect.y + 2f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(new Rect(hpRect.x, hpRect.y, w * g.ghost, hpH), Texture2D.whiteTexture);
                GUI.color = new Color(0.9f, 0.3f, 0.25f);
                GUI.DrawTexture(new Rect(hpRect.x, hpRect.y, w * hp, hpH), Texture2D.whiteTexture);
                // 怯み:上の中央と同じ色(StaggerColor)
                float st = e.Flinching ? 1f : Mathf.Clamp01(e.Stagger / e.maxStagger);
                GUI.color = StaggerColor(e, st, now);
                GUI.DrawTexture(new Rect(stRect.x, stRect.y, w * st, stH), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            // 消えた・やられた敵の分を捨てる
            if (overheadGhost.Count > Enemies.Count) overheadGhost.Clear();
        }

        /// <summary>ステージ:左上に全体の残りの敵、入ったとき(マップ名)・セーブのときに中央へ短い知らせ、ボスを倒したら「ステージクリア」。</summary>
        void DrawStage()
        {
            GUI.Label(new Rect(24f, 12f, 360f, 24f), stage.Status, small);

            if (stage.Cleared)
            {
                GUI.Label(new Rect(0f, SH * 0.36f, SW, 50f), "ステージクリア", big);
                string restartButton = ProtoInput.L(ProtoInput.Btn.Restart);
                GUI.Label(new Rect(0f, SH * 0.36f + 48f, SW, 26f),
                    $"クリアタイム {Mathf.FloorToInt(stage.ClearTime / 60f)}:{Mathf.FloorToInt(stage.ClearTime % 60f):00}  ―  {restartButton} で最初から", center);
                if (!string.IsNullOrEmpty(stage.ClearNote)) GUI.Label(new Rect(0f, SH * 0.36f + 76f, SW, 26f), stage.ClearNote, center);
                return;
            }
            float age = Time.unscaledTime - stage.BannerAt;
            if (!string.IsNullOrEmpty(stage.Banner) && age < 2.5f)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((2.5f - age) / 0.6f));
                GUI.Label(new Rect(0f, SH * 0.22f, SW, 50f), stage.Banner, big);
                GUI.color = Color.white;
            }
        }

        /// <summary>左下のメニューの案内の1行の高さ。</summary>
        const float LeftHintH = 24f;
        /// <summary>
        /// 左下の HP・EN とメニューの案内のまとまりの上端(拾う案内をこれより上に出す。ロックオンの照準枠の縦もこれより上まで)。
        /// 画面の高さだけで決まる(OnGUI の外=ロックオンの付け始めの判定からも同じ値を読めるよう、OnGUI で入れる値にしない)。
        /// </summary>
        float leftTop => SH - 8f - LeftHintH - 44f - 5f;

        void Bar(Rect r, float k, Color c)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
