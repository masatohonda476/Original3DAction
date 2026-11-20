using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelGame の画面の表示:ゲージ・照準・ロックオンの印と照準枠・頭上のゲージ・ステージの知らせ・左下のメニューの案内(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
        // ───────── 画面 ─────────

        void OnGUI()
        {
            uiScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label = new GUIStyle(GUI.skin.label) { fontSize = 20 };
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                center = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                richCenter = new GUIStyle(small) { richText = true, alignment = TextAnchor.MiddleCenter };
                richSmall = new GUIStyle(small) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
                richRight = new GUIStyle(richSmall) { alignment = TextAnchor.UpperRight };
                viewHeadStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold, wordWrap = false };
                viewTitleStyle = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                rightSmall = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
                chartHeadStyle = new GUIStyle(small) { fontSize = 15, richText = true, wordWrap = false, clipping = TextClipping.Clip };
                chartLegendStyle = new GUIStyle(chartHeadStyle) { fontSize = 14 };
                chartNoteStyle = new GUIStyle(small) { fontSize = 15, richText = true, wordWrap = true };
                // 右下の武装の欄(武装ハントの WeaponCell と同じ大きさ。色は GUI.color で付けるので文字は白)
                cellTiny = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
                cellTiny.normal.textColor = Color.white;
                cellTinyRight = new GUIStyle(cellTiny) { alignment = TextAnchor.UpperRight };
                cellNameStyle = new GUIStyle(cellTiny) { fontSize = 17 };
                cellNameSmall = new GUIStyle(cellTiny) { fontSize = 14 };
                cellBack = new GUIStyle(cellTiny) { fontSize = 14 };
                // 拾う案内のカードの見出し(武装の名前。色は GUI.color で付けるので文字は白)
                cardTitleStyle = new GUIStyle(cellTiny) { fontSize = 18, fontStyle = FontStyle.Bold };
            }
            var cam = Camera.main;
            if (player != null) DrawOverheadGauges(cam);   // 被ダメージの数字より奥に描く
            ProtoDamageText.Draw(cam, uiScale, SH);
            if (player == null) return;
            if (!MenuOpen)
            {
                DrawAimFrames(player.Arms, cam);   // 照準の枠は、ほかの表示より奥に描く
                DrawHitMark(player.Arms);
            }

            // 左下:自機のHP・EN、その下にメニューを開くボタンの短い案内を1行。
            // 操作方法は出し続けず、メニューの「操作方法」のページで見せる(作者「操作方法は常に出すのではなく、オプションにメニューを出してください」。DuelGame.Menu.cs)。
            // ガードのゲージは、右下の武装の欄の盾のセルに出す(ArmCell)。左下には出さない
            const float hintH = 24f;
            float x = 24f;
            float y = SH - 8f - hintH - 44f;
            leftTop = y - 5f;
            Bar(new Rect(x, y, 300f, 14f), player.HP / player.MaxHP, new Color(0.35f, 0.85f, 0.45f));
            GUI.Label(new Rect(x + 308f, y - 5f, 120f, 24f), $"HP {Mathf.CeilToInt(player.HP)}", small);
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
            DrawArms(player.Arms);
            if (!MenuOpen) DrawPickup(player.Arms);
            // 両手の銃・砲の溜め撃ち:照準の少し下に溜まり具合(溜まったら白)
            float charge = player.Arms.Charge01;
            Vector2 cc = CursorScreen(player.Arms);
            if (charge > 0f && !MenuOpen) Bar(new Rect(cc.x - 60f, cc.y + 46f, 120f, 6f), charge, charge >= 1f ? Color.white : new Color(1f, 0.8f, 0.4f));

            // ロックオンの印は四つの角の枠だけ(敵を囲む。DrawLockCorners)。胸の上の白い十字は、枠と二重になるのでやめた(作者「敵の上で印が重なる」→「A」)
            var e = player.Target;
            if (e != null && !e.Dead && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(e.LockPoint);
                // 画面の外(敵が背中側へ回り、カメラが上限の速さで向き直っている途中など):画面の端に、その方向を示す小さな印(見失わないように)
                if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) DrawOffscreenLock(cam, e.LockPoint);
                else if (player.Arms.ShotPoint == null)
                {
                    // 銃・砲を持っていなければ、十字は相手の胸に出す(持っていれば、弾が飛んでいく所に出る。DrawAimFrames)
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
        // 枠に添えるボタン名(ボタンの組と機器が変わったときだけ作る)
        readonly Dictionary<(int, ProtoInput.Device), string> frameLabels = new();

        /// <summary>画面の上の照準の位置(GUI の座標。y は下向き)。</summary>
        Vector2 CursorScreen(DuelArms arms)
        {
            Vector2 c = arms != null ? arms.Cursor : Vector2.zero;
            return new Vector2(SW * (0.5f + c.x), SH * (0.5f - c.y));
        }

        /// <summary>
        /// 照準の位置(DuelArms.Cursor。構えていない間は画面の中央へ戻る)に、撃つ武装の照準の枠を角の鉤(かぎ)だけで出す(アーマード・コアの FCS の枠)。
        /// 大きさの違う枠は入れ子で全部出し、同じ大きさの枠は1つにまとめる。左上の角の上に使うボタン名。ふだんは薄く、その武装を撃っている・撃ったばかり・溜めている間は濃く。
        /// 枠の中に当てる相手がいれば枠を橙にし、相手の胸に小さな橙の鉤の印(ミサイルは当てる相手みんな)。
        /// 構えている間は、照準が動ける範囲の縁を薄い点で出す。画面は揺らさない。
        /// </summary>
        void DrawAimFrames(DuelArms arms, Camera cam)
        {
            if (Event.current.type != EventType.Repaint || cam == null || player.Dead) return;
            if (arms.Frames.Count == 0 && arms.Marks.Count == 0) return;
            float k = SH * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);   // tan → 画面の大きさ
            Vector2 cur = CursorScreen(arms);
            float cx = cur.x, cy = cur.y;
            if (arms.CursorActive) DrawCursorRange(arms);
            var dev = ProtoInput.Current;
            float spread = -1f;
            bool spreadHot = false;
            foreach (var f in arms.Frames)
            {
                // 反動で広がっている間は、枠も少し外へ開く(見た目だけ。吸い付く範囲は広げない)
                float hw = Mathf.Min(f.drawW * k, SW * 0.48f), hh = Mathf.Min(f.drawH * k, SH * 0.45f);
                // 真ん中の十字に出すばらつき:撃っている武装を優先し、同じなら大きいほう
                if (f.spread >= 0f)
                {
                    if (f.hot && !spreadHot)
                    {
                        spread = f.spread;
                        spreadHot = true;
                    }
                    else if (f.hot == spreadHot) spread = Mathf.Max(spread, f.spread);
                }
                // 枠は出さない(作者「エイムの枠を消してください」)。狙い・吸い付きの判定は残る。出すなら DuelArmsRules.showAimFrame
                if (!DuelArmsRules.Current.showAimFrame) continue;
                var c = f.locked ? FrameLocked : FrameIdle;
                c.a = f.hot ? 0.9f : 0.38f;
                Brackets(new Rect(cx - hw, cy - hh, hw * 2f, hh * 2f), Mathf.Clamp(Mathf.Min(hw, hh) * 0.35f, 8f, 26f), 2f, c);
                if (!frameLabels.TryGetValue((f.buttons, dev), out var label))
                {
                    var sb = new System.Text.StringBuilder();
                    for (int b = 0; b < 4; b++)
                    {
                        if ((f.buttons & (1 << b)) == 0) continue;
                        if (sb.Length > 0) sb.Append('/');
                        sb.Append(SlotButton((DuelSlot)(b switch { 0 => 0, 1 => 1, 2 => 3, _ => 4 })));
                    }
                    label = sb.ToString();
                    frameLabels[(f.buttons, dev)] = label;
                }
                GUI.color = c;
                GUI.Label(new Rect(cx - hw, cy - hh - 16f, 200f, 16f), label, cellTiny);
            }
            // 真ん中の十字:弾が飛んでいく所(吸い付きを入れた狙いの先。DuelArms.ShotPoint)に出す。4本の短い線の開きが、今のばらつき(spread + 反動の広がり)。連射すると開き、やめると閉じる
            // 弾が飛んでいく所がカメラの後ろ(自機がカメラの方を向いている。射撃では向き直らない)なら、十字は出さない
            // (作者「視界外に十字がいくと画面中央に十字が表示されるのをやめてください」。前は画面の中央に出ていた)。カメラの前で画面の外なら、画面の外に描かれて見えない
            Vector3 shotSp = arms.ShotPoint is Vector3 shot ? cam.WorldToScreenPoint(shot) : Vector3.forward;
            if (spread >= 0f && shotSp.z > 0f)
            {
                float sx = cx, sy = cy;
                if (arms.ShotPoint != null) { sx = shotSp.x / uiScale; sy = SH - shotSp.y / uiScale; }
                // 見た目だけ、出す所へ CrossFollowTime 秒ほどで追いつかせる(狙いが急に変わっても跳ばずに滑る。弾の向きは変えない。作者「見た目だけでも間を補正とかできますか？」)
                float now = Time.unscaledTime;
                if (crossShownAt < 0f || now - crossShownAt > 0.2f) crossPos = new Vector2(sx, sy);   // しばらく出していなければ、その場に出す
                else crossPos = Vector2.Lerp(crossPos, new Vector2(sx, sy), 1f - Mathf.Exp(-3f * (now - crossShownAt) / CrossFollowTime));
                crossShownAt = now;
                DrawCross(crossPos.x, crossPos.y, Mathf.Clamp(spread * k, CrossMinGap, SH * 0.3f), spreadHot ? 0.85f : 0.4f);
            }
            var mc = FrameLocked;
            mc.a = 0.95f;
            foreach (var e in arms.Marks)
            {
                if (e == null || e.Dead) continue;
                if (e == player.Target && lockFollow > 0f) continue;   // ロックオンの角が囲んでいる相手は、角の色で示す(DrawLockCorners。印を重ねない)
                Vector3 sp = cam.WorldToScreenPoint(e.Chest);
                if (sp.z <= 0f) continue;
                float x = sp.x / uiScale, y = SH - sp.y / uiScale;
                Brackets(new Rect(x - 11f, y - 11f, 22f, 22f), 6f, 2f, mc);
            }
            GUI.color = Color.white;
        }

        /// <summary>真ん中の十字の見た目の位置(GUI の座標)と、最後に出した時刻(実時間。-1 はまだ)。出す所へ CrossFollowTime 秒ほどで追いつく。</summary>
        Vector2 crossPos;
        float crossShownAt = -1f;
        const float CrossFollowTime = 0.08f;
        /// <summary>十字の真ん中の空きの最小(ぶれていないときの詰まり具合。作者「ぶれてないときのカーソルはもっと線が詰まった状態にしてください」で 3 → 1)。武装そのもののばらつきの分はこれより開く。</summary>
        const float CrossMinGap = 1f;

        /// <summary>十字(4本の短い線。g は真ん中の空き)。照準の色で、濃さは alpha。</summary>
        static void DrawCross(float cx, float cy, float g, float alpha)
        {
            var sc = FrameIdle;
            sc.a = alpha;
            GUI.color = sc;
            const float len = 7f, th = 2f;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(cx - g - len, cy - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(cx + g, cy - th / 2f, len, th), t);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy - g - len, th, len), t);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy + g, th, len), t);
            GUI.color = Color.white;
        }

        /// <summary>照準が動ける範囲(楕円)の縁を、薄い小さな点で出す(構えている間だけ)。</summary>
        void DrawCursorRange(DuelArms arms)
        {
            var r = arms.Rules;
            float rx = SW * Mathf.Clamp(r.cursorRangeX, 0.001f, 0.49f), ry = SH * Mathf.Clamp(r.cursorRangeY, 0.001f, 0.49f);
            float ox = SW * 0.5f, oy = SH * 0.5f;
            const int n = 48;
            const float dot = 2f;
            var push = arms.EdgePush;
            var t = Texture2D.whiteTexture;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
                // 押し付けている側は少し濃く(カメラがそちらへゆっくり回っている合図。ロックオンしていないときだけ回る)
                float lit = player.Target == null ? Mathf.Clamp01(Vector2.Dot(d, push)) : 0f;
                GUI.color = new Color(0.85f, 0.92f, 1f, 0.16f + 0.5f * lit);
                GUI.DrawTexture(new Rect(ox + d.x * rx - dot / 2f, oy - d.y * ry - dot / 2f, dot, dot), t);
            }
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
        /// </summary>
        void DrawHitMark(DuelArms arms)
        {
            if (Event.current.type != EventType.Repaint || player.Dead) return;
            float life = Mathf.Max(0.02f, arms.Rules.hitMarkTime);
            float age = Time.unscaledTime - arms.HitMarkAt;
            if (age < 0f || age > life) return;
            int kind = Mathf.Clamp(arms.HitMarkKind, 0, HitMarkColors.Length - 1);
            float a = 1f - age / life;
            var c = HitMarkColors[kind];
            c.a = Mathf.Clamp01(a * 1.5f);
            float len = HitMarkSizes[kind] * (1f + 0.35f * a * a), th = kind >= 1 ? 3f : 2f, gap = 4f;
            Vector2 center = CursorScreen(arms);
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
        /// <summary>角が囲んでいる相手に弾が吸い付く度合い(0〜1。なめらかに変える)。1 で角を照準の橙に、濃くする。</summary>
        float lockAssist;
        const float LockAssistTime = 0.1f, LockAssistAlpha = 0.9f;
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
                lockFollow = lockAssist = 0f;
                lockHudAt = -1f;
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
                    lockFollow = lockAssist = 0f;
                    lockHudAt = -1f;
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
            // 弾が吸い付く(撃てば体に寄って当たる)相手なら、角を照準の橙にして濃くする(前は胸に小さな角の印を重ねていた)
            bool assist = onEnemy && player.Arms.Marks.Contains(target);
            lockAssist = Mathf.MoveTowards(lockAssist, assist ? 1f : 0f, dt / LockAssistTime);

            // 画面の縁寄りの枠:縦は上の敵のゲージ・右上の知らせと、下の自機のゲージ・武装の欄の手前まで(低い画面では縮める。なめらかに)
            float bottom = Mathf.Min(leftTop, ArmsTop) - 12f;
            float wantH = Mathf.Max(53f, Mathf.Min(LockCornerHalfH, bottom - SH * 0.5f));
            lockHalfH = dt <= 0f ? wantH : Mathf.MoveTowards(lockHalfH, wantH, 267f * dt);
            float baseW = Mathf.Max(80f, Mathf.Min(LockCornerHalfW, SW * 0.5f - LockCornerEdge));
            float hw = baseW + lockOffset, hh = lockHalfH + lockOffset * (lockHalfH / baseW);
            hw = Mathf.Lerp(hw, lockFollowHalf.x + lockOffset * 0.35f, e);
            hh = Mathf.Lerp(hh, lockFollowHalf.y + lockOffset * 0.35f, e);
            Vector2 c = new Vector2(SW * 0.5f, SH * 0.5f) + lockFollowCenter * e;
            var col = lockInk;
            if (e > 0f) col.a = Mathf.Min(1f, col.a * Mathf.Lerp(1f, LockFollowAlpha / LockRestAlpha, e));   // 敵に重なる間は薄く
            float a = lockAssist * e;
            if (a > 0f)
            {
                float alpha = Mathf.Lerp(col.a, Mathf.Max(col.a, LockAssistAlpha * lockInk.a / LockRestAlpha), a);   // 付け外しの途中の薄さは保つ
                col = Color.Lerp(col, FrameLocked, a);
                col.a = Mathf.Min(1f, alpha);
            }
            float thin = Mathf.Lerp(1f, 0.7f, e);
            // 明るい床の上でも形が読めるよう、細い暗い縁を下に敷く
            LockCornerLines(c, hw, hh, e < 0.5f, 2.7f * thin, new Color(0f, 0.025f, 0.04f, col.a * 0.8f));
            LockCornerLines(c, hw, hh, e < 0.5f, 1.35f * thin, col);
            GUI.color = Color.white;
        }

        /// <summary>狙う敵の体を囲む画面上の枠(中心は画面の中央から、下向きが +y。横・縦の半分)。敵が画面に映っていて、自機から遮られていないときだけ true。</summary>
        bool LockTargetBox(Camera cam, DuelEnemy target, out Vector2 center, out Vector2 half)
        {
            center = half = Vector2.zero;
            if (target == null || target.Dead || cam == null) return false;
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
                bool pad = ProtoInput.Current != ProtoInput.Device.Keyboard;
                GUI.Label(new Rect(0f, SH * 0.36f + 48f, SW, 26f),
                    $"クリアタイム {Mathf.FloorToInt(stage.ClearTime / 60f)}:{Mathf.FloorToInt(stage.ClearTime % 60f):00}  ―  {(pad ? "SHARE+△" : "F1")} で最初から", center);
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

        /// <summary>左下の HP・EN とメニューの案内のまとまりの上端(拾う案内をこれより上に出す)。</summary>
        float leftTop;

        /// <summary>その所を使うボタンの名前(背中はなし)。</summary>
        static string SlotButton(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => ProtoInput.L(ProtoInput.Btn.R1),
            DuelSlot.LeftHand => ProtoInput.L(ProtoInput.Btn.L1),
            DuelSlot.RightShoulder => ProtoInput.L(ProtoInput.Btn.R2),
            DuelSlot.LeftShoulder => ProtoInput.L(ProtoInput.Btn.L2),
            _ => "",
        };

        /// <summary>その所の今の武装の名前(空きは「なし」)。</summary>
        static string CurName(DuelWeapon w) => w == null ? "なし" : w.name + "〈" + w.RarityName + "〉";

        /// <summary>両手持ちの技の一行:「R1 連撃3段  L1 突進斬り  △+R1/L1 強攻撃」(射撃は R1 と L1 の撃ち方)。</summary>
        static string TwoHandMoves(DuelWeapon w)
        {
            string r1 = SlotButton(DuelSlot.RightHand), l1 = SlotButton(DuelSlot.LeftHand);
            if (w.IsBlade)
                return $"{r1} {w.MainMoveName}  {l1} {w.altMove}  {ProtoInput.L(ProtoInput.Btn.Aim)}+{r1}/{l1} 強攻撃  回避の後に {r1} 回避斬り・{l1} {w.altMove}  (ガードなし)";
            string alt = w.alt is { charge: > 0f } ? $"{w.altMove}(押して溜め、離して撃つ)" : w.altMove;
            return $"{r1} {w.MainMoveName}  {l1} {alt}  (ガードなし)";
        }

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
