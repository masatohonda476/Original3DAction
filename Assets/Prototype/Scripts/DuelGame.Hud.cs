using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelGame の画面の表示:ゲージ・照準・頭上のゲージ・ステージの知らせ・左下のメニューの案内(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
        // ───────── 画面 ─────────

        void DrawRepairHud(DuelArms arms)
        {
            var rect = new Rect(24f, ArmsTop, 232f, 54f);
            MenuPanel(rect);
            Chip(new Rect(rect.x, rect.y, 3f, rect.height), DuelLoot.RepairTint);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, 24f),
                $"補修用装甲 ×{arms.RepairCount}", small);
            string input = ProtoInput.Current == ProtoInput.Device.Keyboard ? "↑" : "十字キー上";
            string status = arms.UsingRepair ? "補修中" : arms.RepairNotice ?? $"{input} で使用";
            GUI.Label(new Rect(rect.x + 10f, rect.y + 25f, rect.width - 20f, 22f), status, cellNameSmall);
            if (arms.UsingRepair)
                Bar(new Rect(rect.x + 10f, rect.yMax - 6f, rect.width - 20f, 3f), arms.RepairProgress, DuelLoot.RepairTint);
        }

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
            float healed = player.Arms.RecentRepairGain;
            if (healed > 0f)
            {
                GUI.color = DuelLoot.RepairTint;
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
                    if (!player.Dead && !stage.Cleared) DrawRepairHud(player.Arms);
                    DrawPickup(player.Arms);
                }
            }

            DrawLockHud(cam, player.Arms);

            // 上の中央:ロックオンしている敵のHPと怯みゲージ(ロックオンしていなくてもボスは出す)
            var e = TopGaugeEnemy();
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

        /// <summary>カメラ中央からの角度の tan を、HUD上の長さに換算する倍率。</summary>
        float SightPixelsPerTangent(Camera cam) =>
            SH * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        /// <summary>画面中央に武器のサイトを描く。補正する敵が入ると橙になり、十字は弾道を示す。</summary>
        void DrawAimFrames(DuelArms arms, Camera cam)
        {
            if (Event.current.type != EventType.Repaint || cam == null || !arms.Aiming) return;
            if (arms.ShotFrame is not { } frame) return;
            float k = SightPixelsPerTangent(cam);
            float cx = SW * 0.5f, cy = SH * 0.5f;
            float gap = Mathf.Clamp(frame.spread * k, CrossMinGap, SH * 0.3f);
            float crossAlpha = frame.hot ? 0.85f : 0.4f;
            if (!frame.showCorners)
            {
                crossShownAt = -1f;
                DrawCross(cx, cy, gap, 0.95f, outline: true);
                return;
            }
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
                DrawCross(crossPos.x, crossPos.y, gap, crossAlpha, crossHit);
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
        static void DrawCross(float cx, float cy, float g, float alpha, float hit = 0f, bool outline = false)
        {
            var sc = Color.Lerp(FrameIdle, FrameHit, hit);
            sc.a = Mathf.Lerp(alpha, Mathf.Max(alpha, CrossHitAlpha), hit);
            const float len = 7f, th = 2f;
            void Stroke(Rect line)
            {
                if (outline)
                {
                    GUI.color = new Color(0.03f, 0.05f, 0.08f, 0.85f);
                    GUI.DrawTexture(new Rect(line.x - 1f, line.y - 1f, line.width + 2f, line.height + 2f), Texture2D.whiteTexture);
                }
                GUI.color = sc;
                GUI.DrawTexture(line, Texture2D.whiteTexture);
            }
            Stroke(new Rect(cx - g - len, cy - th / 2f, len, th));
            Stroke(new Rect(cx + g, cy - th / 2f, len, th));
            Stroke(new Rect(cx - th / 2f, cy - g - len, th, len));
            Stroke(new Rect(cx - th / 2f, cy + g, th, len));
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
