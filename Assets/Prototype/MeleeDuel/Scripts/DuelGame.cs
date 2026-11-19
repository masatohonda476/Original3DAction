using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作。平らな部屋で、雑魚(兵卒)の群れと中型(剣士)が交互に出てくる。全滅させると次の組が出る。
    /// 確かめたいこと:剣士との1対1を続けて面白いか / 群れを位置取りと回避斬りでさばけるか /
    /// 回避が軸になっているか / ガードが便利すぎないか。
    /// F1(または SHARE+△)でやり直し。
    /// stage(DuelStage)があれば、波で出す代わりに、墜落した船の中を登るステージになる(MeleeDuelStage.unity)。
    /// </summary>
    public class DuelGame : MonoBehaviour
    {
        public static DuelGame I;

        [Header("機体(シーンを組み立てるときに入る)")]
        public GameObject mechPrefab;
        public float mechHeight = 9f;

        [Header("部屋")]
        [Tooltip("部屋の半分の広さ。0 以下なら外へ出ないようにはしない(ステージは壁で止める)")]
        public float arenaHalf = 60f;

        [Header("ステージ(あれば波の代わりにステージを進む)")]
        public DuelStage stage;

        [Header("敵")]
        [Tooltip("剣士(中型)のHP")]
        public float enemyHP = 47f;
        [Tooltip("兵卒(雑魚)のHP")]
        public float gruntHP = 28f;
        [Tooltip("同時に攻撃してくる敵の数の上限(ほかは回り込んで順番を待つ)")]
        public int maxAttackers = 2;
        public float respawnDelay = 2f;

        [Header("頭上のゲージ(電脳斬禅と同じ。近くの敵の頭の上に、HPと怯みの小さなバー)")]
        [Tooltip("自機からこの距離(m)より遠い敵には出さない")]
        public float overheadRange = 90f;
        [Tooltip("この距離(m)より遠い敵は、ゲージを小さくしていく(最小で7割)")]
        public float overheadShrinkFrom = 45f;

        /// <summary>出てくる組の順番(最後まで行ったら最初に戻る)。G = 兵卒、D = 剣士。</summary>
        static readonly string[] Waves = { "GGG", "D", "GGGG", "DGG" };

        public List<DuelEnemy> Enemies { get; } = new();

        DuelPlayer player;
        int kills, wave;
        float startTime, restartAt = -1f, warpUntil, nextSpawnAt = -1f;
        readonly List<(string msg, float t)> log = new();
        GUIStyle small, label, big, center, richCenter, rightSmall, richSmall, richRight, viewHeadStyle, viewTitleStyle;
        /// <summary>頭上のゲージで、減ったHPを少し遅れて追う白い部分(敵ごと)。</summary>
        readonly Dictionary<DuelEnemy, (float ghost, float hp, float holdUntil)> overheadGhost = new();
        float uiScale = 1f;
        float SW => Screen.width / uiScale;
        float SH => Screen.height / uiScale;

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            ProtoInput.Paused = false;
        }

        void OnDestroy()
        {
            if (I != this) return;
            // 装備の確認を開いたまま抜けた(やり直し・Play の停止):止めた時間と入力を戻す
            if (equipOpen) Time.timeScale = 1f;
            equipOpen = false;
            ProtoInput.Paused = false;
        }

        void Start()
        {
            Vector3 startPos = new(0f, 0f, -20f);
            Quaternion startRot = Quaternion.identity;
            if (stage != null)
            {
                stage.Prepare(this);   // 地形を組み立てる
                startPos = stage.StartPos;
                startRot = stage.StartRot;
            }
            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(startPos, startRot);
            player = go.AddComponent<DuelPlayer>();
            player.Init(this);

            var cam = Camera.main != null ? Camera.main.GetComponent<DuelCamera>() : null;
            if (cam != null) cam.scale = mechHeight / 2.1f;   // 人間サイズのカメラを機体の大きさへ

            startTime = Time.time;
            if (stage != null)
            {
                stage.Begin(player);
                Log("ステージの試作:敵が落とす武装を拾って強くなりながら船の中を登り、頭頂部のボスを倒せばクリア。F1で最初から");
            }
            else
            {
                SpawnWave();
                Log("近接の試作:兵卒の群れと剣士が交互に出る。F1でやり直し");
            }
        }

        /// <summary>次の組を、自機から少し離れた所に扇状に並べて出す。</summary>
        void SpawnWave()
        {
            string w = Waves[wave % Waves.Length];
            wave++;
            Log($"第{wave}組:" + (w.Contains('D') ? "剣士" : "") + (w.Contains('D') && w.Contains('G') ? "と" : "") + (w.Contains('G') ? $"兵卒×{w.Split('G').Length - 1}" : ""));
            Vector3 basePos = player != null ? player.transform.position : Vector3.zero;
            Vector3 center = new Vector3(0f, 0f, 0f) - basePos;
            center.y = 0f;
            Vector3 fwd = center.sqrMagnitude > 1f ? center.normalized : Vector3.forward;
            for (int i = 0; i < w.Length; i++)
            {
                // 部屋の中心の側へ、40m先に扇状に(±70°)
                float ang = w.Length == 1 ? 0f : Mathf.Lerp(-70f, 70f, i / (float)(w.Length - 1));
                Vector3 pos = basePos + Quaternion.Euler(0f, ang, 0f) * fwd * 40f;
                pos.x = Mathf.Clamp(pos.x, -arenaHalf + 6f, arenaHalf - 6f);
                pos.z = Mathf.Clamp(pos.z, -arenaHalf + 6f, arenaHalf - 6f);
                pos.y = 0f;
                var go = new GameObject($"Enemy{i}");
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(Flat(basePos - pos));
                var e = go.AddComponent<DuelEnemy>();
                e.Init(this, player, w[i] == 'D' ? DuelEnemy.Duelist(enemyHP) : DuelEnemy.Grunt(gruntHP));
                Enemies.Add(e);
                player.AutoLock(e);
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        /// <summary>その敵が技を出し始めてよいか(同時に攻撃している敵が maxAttackers 未満なら)。</summary>
        public bool CanStartAttack(DuelEnemy who)
        {
            int n = 0;
            foreach (var e in Enemies)
                if (e != null && e != who && !e.Dead && e.Attacking) n++;
            return n < maxAttackers;
        }

        void Update()
        {
            ProtoInput.TrackDevice();
            if (ProtoInput.Restart || (restartAt > 0f && Time.unscaledTime >= restartAt))
            {
                Restart();
                return;
            }
            UpdateEquipView();
            // 装備の確認を開いている間は時間を止める。ほかはヒットストップ:短い間だけ時間をほぼ止める
            Time.timeScale = equipOpen ? 0f : Time.unscaledTime < warpUntil ? 0.05f : 1f;
            if (equipOpen) return;

            Enemies.RemoveAll(e => e == null);
            if (nextSpawnAt > 0f && Time.time >= nextSpawnAt)
            {
                nextSpawnAt = -1f;
                SpawnWave();
            }
        }

        void Restart()
        {
            Time.timeScale = 1f;
            string path = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
#endif
        }

        // ───────── 装備の確認(左のメニューボタン) ─────────

        bool equipOpen;
        /// <summary>装備の確認を開いている(時間が止まっている)。自機・敵・ステージの Update はこの間なにもしない。</summary>
        public static bool Paused => I != null && I.equipOpen;

        /// <summary>
        /// 左のメニューボタン(SHARE・View・− を離す / I)で装備の確認を開く・閉じる。○・B / Esc / Backspace でも閉じる。
        /// ステージ(武装がある)だけ。開いている間は時間を止め、入力を止める(ProtoInput.Paused)。付け先を選ぶ表示の途中なら取り消す。
        /// 閉じたあとは、その時に押していたボタンを離すまで効かない(ProtoInput 側)。
        /// 開いている間は ↑↓(十字・左スティック・↑↓・W/S)で所を選び、捨てるボタン(□・X / X キー)を2回押すと、その武装を足もとの床に捨てる
        /// (1回目で「もう一度押すと捨てる」になり、選び直す・○/Esc で取り消し)。撃墜中・ボスを倒した後は捨てられない。
        /// </summary>
        void UpdateEquipView()
        {
            bool toggle = ProtoInput.EquipView;   // 組み合わせの判定のため、毎フレーム読む
            bool up = ProtoInput.MenuUp, down = ProtoInput.MenuDown;   // スティックの倒し始めの判定のため、毎フレーム読む
            if (equipOpen)
            {
                if (toggle || player == null || player.Arms == null) SetEquipOpen(false);
                else if (ProtoInput.EquipViewBack)
                {
                    if (dropArmed) dropArmed = false;   // 捨てる確認の途中なら、確認だけ取り消す
                    else SetEquipOpen(false);
                }
                else EquipViewInput(player.Arms, up, down);
                return;
            }
            if (toggle && stage != null && player != null && player.Arms != null) SetEquipOpen(true);
        }

        void SetEquipOpen(bool open)
        {
            if (open == equipOpen) return;
            if (open)
            {
                player.Arms.CancelChoice();
                // 武装が付いている最初の所を選ぶ
                viewSel = 0;
                for (int i = 0; i < ArmRows; i++)
                    if (player.Arms.Get((DuelSlot)i) != null) { viewSel = i; break; }
            }
            dropArmed = false;
            equipOpen = open;
            ProtoInput.Paused = open;
            viewBuilt = false;
        }

        /// <summary>選んでいる所(0〜4 = DuelSlot)と、捨てる確認の途中か。</summary>
        int viewSel;
        bool dropArmed;

        /// <summary>今は捨てられない理由(0 = 捨てられる / 1 = 撃墜中 / 2 = ボスを倒した後で拾えない)。</summary>
        int DropBlock() => player == null || player.Dead ? 1 : stage != null && stage.Cleared ? 2 : 0;

        void EquipViewInput(DuelArms arms, bool up, bool down)
        {
            int d = (down ? 1 : 0) - (up ? 1 : 0);
            if (d != 0)
            {
                viewSel = (viewSel + d + ArmRows) % ArmRows;
                dropArmed = false;
            }
            if (!ProtoInput.MenuDiscard) return;
            var slot = (DuelSlot)viewSel;
            if (arms.Get(slot) == null || DropBlock() != 0) dropArmed = false;
            else if (!dropArmed) dropArmed = true;   // 1回目:確認
            else
            {
                dropArmed = false;
                arms.Drop(slot);   // 2回目:捨てる
            }
        }

        /// <summary>当たった瞬間に、realSeconds 秒だけ時間をほぼ止める。</summary>
        public void HitStop(float realSeconds) => warpUntil = Mathf.Max(warpUntil, Time.unscaledTime + realSeconds);

        public void OnEnemyDied(DuelEnemy e)
        {
            if (stage != null)
            {
                // ステージ:倒した敵は復活しないので、そのまま数える。ボスならクリア
                kills++;
                stage.OnEnemyDied(e);
                return;
            }
            kills++;
            // 組を全滅させたら、少しして次の組
            bool anyAlive = Enemies.Exists(x => x != null && !x.Dead);
            if (!anyAlive && nextSpawnAt < 0f) nextSpawnAt = Time.time + respawnDelay;
        }

        public void OnPlayerDied()
        {
            Log("撃墜された");
            if (stage != null) stage.OnPlayerDied();   // 最後に触れたセーブポイントから再開する
            else restartAt = Time.unscaledTime + 2.5f;
        }

        public void Log(string msg)
        {
            log.Add((msg, Time.unscaledTime));
            if (log.Count > 6) log.RemoveAt(0);
        }

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
            }
            var cam = Camera.main;
            if (player != null) DrawOverheadGauges(cam);   // 被ダメージの数字より奥に描く
            ProtoDamageText.Draw(cam, uiScale, SH);
            if (player == null) return;

            // 左下:自機のHP・EN・ガード
            float x = 24f, y = SH - 118f;
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
            GUI.Label(new Rect(x + 308f, y - 7f, 300f, 24f),
                (minus ? $"EN {Mathf.FloorToInt(player.Energy)}(回復待ち)" : $"EN {Mathf.FloorToInt(player.Energy)}") + (player.BoostOn ? "  ブースト" : ""), small);
            y += 22f;
            // ガード:ステージで盾を持っていなければ空(ガードできない)
            bool broken = player.Current == DuelPlayer.State.GuardBroken;
            float maxGuard = player.MaxGuard;
            Bar(new Rect(x, y, 300f, 10f), maxGuard > 0f ? player.Guard / maxGuard : 0f,
                broken ? new Color(1f, 0.4f, 0.2f) : player.Guarding ? new Color(0.5f, 0.8f, 1f) : new Color(0.4f, 0.55f, 0.7f));
            GUI.Label(new Rect(x + 308f, y - 7f, 200f, 24f), maxGuard <= 0f ? "ガード(盾なし)" : broken ? "ガード崩れ" : player.Guarding ? "ガード中" : "ガード", small);
            y += 22f;
            GUI.Label(new Rect(x, y, SW - x * 2f, 44f), Help(), small);   // 長ければ2行に折り返す(キーボードの表記は長い)
            if (player.Arms != null)
            {
                DrawArms(player.Arms, x, SH - 118f - ArmRows * 21f - 10f);
                if (!equipOpen) DrawPickup(player.Arms);
            }

            // ロックオンの印:敵の胸の上のほうに、目立ちすぎない細い白の十字
            var e = player.Target;
            if (e != null && !e.Dead && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(e.LockPoint);
                if (sp.z > 0f)
                {
                    float cx = sp.x / uiScale, cy = SH - sp.y / uiScale;
                    const float arm = 9f, th = 2f, gap = 3f;
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                    GUI.DrawTexture(new Rect(cx - arm - gap, cy - th / 2f, arm, th), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(cx + gap, cy - th / 2f, arm, th), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(cx - th / 2f, cy - arm - gap, th, arm), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(cx - th / 2f, cy + gap, th, arm), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }

            // 上の中央:ロックオンしている敵のHPと怯みゲージ(ステージでは、ロックオンしていなくてもボスは出す)
            e = TopGaugeEnemy();
            if (e != null)
            {
                float w = 520f, ex = (SW - w) / 2f;
                GUI.Label(new Rect(ex, 2f, w, 22f), e.KindName, center);
                Bar(new Rect(ex, 24f, w, 14f), e.HP / e.MaxHP, new Color(0.9f, 0.3f, 0.25f));
                // 怯みゲージ:HPのバーの1/4の長さ
                float sw = w / 4f, sx = (SW - sw) / 2f;
                Bar(new Rect(sx, 42f, sw, 8f), e.Flinching ? 1f : e.Stagger / e.maxStagger, e.Flinching ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.6f, 0.2f));
                GUI.Label(new Rect(ex, 52f, w, 24f), e.Flinching ? "怯み!" : "怯み", center);
            }

            // 右上:知らせと撃破数
            var rs = rightSmall;
            string head = stage != null ? $"撃破 {kills}" : $"第{wave}組  撃破 {kills}";
            float elapsed = stage != null && stage.Cleared ? stage.ClearTime : Time.time - startTime;
            GUI.Label(new Rect(SW - 320f, 16f, 300f, 22f), $"{head}  {elapsed:0}秒", rs);
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

            if (stage != null) DrawStage();
            if (player.Dead) GUI.Label(new Rect(0f, SH * 0.4f, SW, 50f), stage != null ? "撃墜 ― セーブポイントから再開します" : "撃墜 ― やり直します", big);
            if (equipOpen && player.Arms != null) DrawEquipView(player.Arms);
        }

        /// <summary>上の中央のゲージに出す敵:ロックオンしている敵。いなければ、ステージで動いているボス。</summary>
        DuelEnemy TopGaugeEnemy()
        {
            var e = player.Target;
            if ((e == null || e.Dead) && stage != null) e = stage.ActiveBoss;
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
                // 怯み:上の中央と同じ色。怯んでいる間は満タンの黄、8割を超えたら白く点滅して「あと少し」を知らせる
                float st = e.Flinching ? 1f : Mathf.Clamp01(e.Stagger / e.maxStagger);
                Color stC = e.Flinching ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.6f, 0.2f);
                if (!e.Flinching && st > 0.8f) stC = Color.Lerp(stC, Color.white, 0.5f + 0.5f * Mathf.Sin(now * 16f));
                GUI.color = stC;
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

        // 武装の表示の文字列は、中身が変わったときだけ作り直す(OnGUI は1フレームに何度も呼ばれる)
        const int ArmRows = 5;
        readonly string[] armsHead = new string[ArmRows], armsBody = new string[ArmRows];
        readonly (DuelWeapon w, int mag, int reload, ProtoInput.Device dev, bool set)[] armsKey = new (DuelWeapon, int, int, ProtoInput.Device, bool)[ArmRows];
        (DuelLoot loot, DuelLoot choosing, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev) pickupKey;
        readonly string[] pickupLines = new string[3];
        int pickupLineCount;
        (ProtoInput.Device dev, bool arms, bool set) helpKey;
        string helpText;

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

        /// <summary>
        /// 左下(HP の上):付けている武装。右手・左手・背中・右肩・左肩の5行(使うボタンつき)。空きは灰色。
        /// 銃は弾倉・リロード、砲・ミサイルは装填の残りも出す。
        /// </summary>
        void DrawArms(DuelArms arms, float x, float y)
        {
            var dev = ProtoInput.Current;
            for (int i = 0; i < ArmRows; i++)
            {
                var slot = (DuelSlot)i;
                var w = arms.Get(slot);
                bool gun = w != null && w.IsGun;
                int mag = gun ? arms.MagLeft(slot) : 0;
                int reload = gun ? Mathf.CeilToInt(arms.ReloadLeft(slot) * 10f) : 0;
                var key = (w, mag, reload, dev, true);
                if (armsKey[i] != key || armsHead[i] == null)
                {
                    armsKey[i] = key;
                    string btn = SlotButton(slot);
                    armsHead[i] = DuelWeapon.SlotName(slot) + (btn.Length > 0 ? " " + btn : "");
                    string body = w == null ? "なし" : w == DuelWeapon.BaseSword ? "素の剣" : w.name + "〈" + w.RarityName + "〉";
                    if (gun) body += reload > 0 ? $"  リロード {reload / 10f:0.0}秒" : mag >= 0 ? $"  {mag}/{w.magazine}" : "  弾数∞";
                    armsBody[i] = body;
                }
                GUI.color = new Color(1f, 1f, 1f, 0.75f);
                GUI.Label(new Rect(x, y + i * 21f, 120f, 22f), armsHead[i], small);
                GUI.color = w == null ? new Color(0.6f, 0.6f, 0.6f) : w.Color;
                GUI.Label(new Rect(x + 120f, y + i * 21f, 330f, 22f), armsBody[i], small);
                GUI.color = Color.white;
                if (w != null && w.kind == DuelKind.ShoulderHeavy)
                {
                    // 装填:撃てるまでの残りを短いバーで
                    float k = arms.Cooldown01(slot);
                    Bar(new Rect(x + 370f, y + i * 21f + 8f, 60f, 6f), 1f - k, k > 0f ? new Color(0.6f, 0.6f, 0.6f) : w.Color);
                }
            }
        }

        /// <summary>
        /// 画面の下の中央:拾える武装があれば、名前と今の装備と比べた内容。無視すればそのまま残る。
        /// 十字右で、背中ならそのまま付ける。手・肩なら付け先を選ぶ表示にして、左右それぞれの今の武装と比べる。
        /// </summary>
        void DrawPickup(DuelArms arms)
        {
            var loot = arms.Choosing != null ? arms.Choosing : arms.Nearby;
            if (loot == null || player.Dead) return;
            var w = loot.Weapon;
            var to = DuelWeapon.SlotsFor(w.kind);
            var a = arms.Get(to[0]);
            var b = to.Length > 1 ? arms.Get(to[1]) : null;
            var key = (loot, arms.Choosing, a, b, ProtoInput.Current);
            if (pickupKey != key || pickupLines[0] == null)
            {
                pickupKey = key;
                string col = ColorUtility.ToHtmlStringRGB(w.Color);
                string name = "<color=#" + col + ">" + w.name + "〈" + w.RarityName + "〉</color>";
                string mode = w.FireMode.Length > 0 ? "・" + w.FireMode : "";
                string equip = "[" + ProtoInput.L(ProtoInput.Btn.EquipNow) + "]";
                if (to.Length == 1)
                {
                    pickupLines[0] = equip + " " + DuelWeapon.SlotName(to[0]) + "に付ける:" + name + "  ― " + w.feel + "(今:" + CurName(a) + ")";
                    pickupLines[1] = DuelWeapon.CompareText(w, a);
                    pickupLineCount = 2;
                }
                else if (arms.Choosing == null)
                {
                    pickupLines[0] = equip + " 拾う:" + name + "(" + DuelWeapon.KindName(w.kind) + mode + ")  ― " + w.feel;
                    pickupLines[1] = DuelWeapon.CompareText(w, null);
                    pickupLineCount = 2;
                }
                else
                {
                    pickupLines[0] = name + " をどちらに付ける?  [" + SlotButton(to[0]) + "] " + DuelWeapon.SlotName(to[0]) + "(今:" + (a != null ? a.name : "なし")
                                     + ") / [" + SlotButton(to[1]) + "] " + DuelWeapon.SlotName(to[1]) + "(今:" + (b != null ? b.name : "なし") + ")    " + equip + " やめる";
                    pickupLines[1] = "[" + SlotButton(to[0]) + "] " + DuelWeapon.SlotName(to[0]) + "と比べて  " + DuelWeapon.CompareText(w, a);
                    pickupLines[2] = "[" + SlotButton(to[1]) + "] " + DuelWeapon.SlotName(to[1]) + "と比べて  " + DuelWeapon.CompareText(w, b);
                    pickupLineCount = 3;
                }
            }
            // 左下の武装の欄(5行)に重ならない高さ
            float h = 6f + 26f * pickupLineCount, wd = 1200f, bx = (SW - wd) / 2f, by = SH * 0.56f;
            GUI.color = new Color(0f, 0f, 0f, arms.Choosing != null ? 0.7f : 0.55f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < pickupLineCount; i++)
                GUI.Label(new Rect(bx, by + 4f + i * 26f, wd, 24f), pickupLines[i], richCenter);
        }

        /// <summary>操作説明。ボタン名は使っているコントローラー(キーボード / PlayStation / Xbox)に合わせる。変わったときだけ作り直す。</summary>
        string Help()
        {
            var dev = ProtoInput.Current;
            bool arms = player != null && player.Arms != null;
            var key = (dev, arms, true);
            if (helpKey == key && helpText != null) return helpText;
            helpKey = key;
            static string B(ProtoInput.Btn b) => ProtoInput.L(b);
            bool kb = dev == ProtoInput.Device.Keyboard;
            var sb = new System.Text.StringBuilder();
            if (arms)
            {
                // ステージ:手のボタンはその手の武装(剣=攻撃 / 盾=ガード / 銃=射撃)
                sb.Append(B(ProtoInput.Btn.R1)).Append('/').Append(B(ProtoInput.Btn.L1)).Append(" 右手/左手の武装");
                sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append('+').Append(B(ProtoInput.Btn.R1)).Append('/').Append(B(ProtoInput.Btn.L1)).Append(" 強攻撃");
                sb.Append("  ").Append(B(ProtoInput.Btn.R2)).Append('/').Append(B(ProtoInput.Btn.L2)).Append(" 右肩/左肩");
                sb.Append("  ").Append(B(ProtoInput.Btn.Dodge)).Append(" 回避");
                sb.Append("  ").Append(B(ProtoInput.Btn.Fly)).Append("長押し 飛行");
                sb.Append("  ").Append(B(ProtoInput.Btn.Boost)).Append(" ブースト");
                sb.Append("  ").Append(B(ProtoInput.Btn.EquipNow)).Append(" 拾う");
                sb.Append("  ").Append(B(ProtoInput.Btn.Lock)).Append(" ロックオン");
                sb.Append("  ").Append(B(ProtoInput.Btn.EquipView)).Append(" 装備・捨てる");
                helpText = sb.ToString();
                return helpText;
            }
            sb.Append(B(ProtoInput.Btn.R1)).Append(" 通常攻撃(回避の後で回避斬り)");
            sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append('+').Append(B(ProtoInput.Btn.R1)).Append(" 強攻撃");
            sb.Append("  ").Append(B(ProtoInput.Btn.Dodge)).Append(" 回避");
            sb.Append("  ").Append(B(ProtoInput.Btn.Fly)).Append("長押し 飛行");
            sb.Append("  ").Append(B(ProtoInput.Btn.Boost)).Append(" ブースト切替");
            sb.Append("  ").Append(B(ProtoInput.Btn.L1)).Append(" ガード");
            sb.Append("  ").Append(B(ProtoInput.Btn.Lock)).Append(" ロックオン");
            sb.Append(kb ? "  マウス左右 / Tab 切替" : "  右スティック左右 切替");
            helpText = sb.ToString();
            return helpText;
        }

        // ───────── 装備の確認の画面 ─────────

        // 文字列は開いたとき・中身(武装・機器)が変わったときだけ作る
        readonly string[] viewHead = new string[ArmRows], viewName = new string[ArmRows], viewStats = new string[ArmRows], viewUse = new string[ArmRows];
        readonly DuelWeapon[] viewKeyW = new DuelWeapon[ArmRows];
        ProtoInput.Device viewKeyDev;
        int viewKeyBlock;
        bool viewBuilt;
        string viewFoot, viewDropHint, viewDropConfirm;

        /// <summary>
        /// 画面の中央:5つの所それぞれの武装(名前・レア度の色・種類・一言)、主な性能(拾うときの比較と同じ値)、使うボタン。空きは「なし」。
        /// 選んでいる所は明るい帯と左の縦線で目立たせ、右上に捨てるボタンを出す(確認の途中は赤い帯と「もう一度押すと捨てる」)。
        /// </summary>
        void DrawEquipView(DuelArms arms)
        {
            var dev = ProtoInput.Current;
            int block = DropBlock();
            bool changed = !viewBuilt || viewKeyDev != dev || viewKeyBlock != block;
            for (int i = 0; i < ArmRows && !changed; i++) changed = viewKeyW[i] != arms.Get((DuelSlot)i);
            if (changed) BuildEquipView(arms, dev, block);

            const float wd = 940f, rowH = 76f, headW = 130f, pad = 18f, titleH = 46f, footH = 34f;
            float h = pad + titleH + rowH * ArmRows + footH + 6f;
            float bx = (SW - wd) / 2f, by = (SH - h) / 2f;
            // 画面全体を少し暗くし、その上にパネル
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "装備", viewTitleStyle);

            float y = by + pad + titleH;
            float tx = bx + pad + headW, tw = wd - pad * 2f - headW;
            for (int i = 0; i < ArmRows; i++)
            {
                var w = arms.Get((DuelSlot)i);
                bool armed = i == viewSel && dropArmed && w != null;
                if (i == viewSel)
                {
                    // 選んでいる所:帯と左の縦線(捨てる確認の途中は赤)
                    GUI.color = armed ? new Color(0.9f, 0.25f, 0.2f, 0.28f) : new Color(1f, 1f, 1f, 0.09f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, wd - pad * 2f + 16f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = armed ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.8f, 1f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, 4f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (w != null && block == 0 && !armed) GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewDropHint, richRight);
                }
                // 区切りの細い線
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(bx + pad, y + 6f, headW, 26f), viewHead[i], viewHeadStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewName[i], richSmall);
                if (w != null) GUI.Label(new Rect(tx, y + 29f, tw, 22f), viewStats[i], richSmall);
                // 3行目:使うボタン(捨てる確認の途中は、その案内に替える)
                GUI.color = armed ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(tx, y + (w != null ? 51f : 29f), tw, 22f), armed ? viewDropConfirm : viewUse[i], richSmall);
                GUI.color = Color.white;
                y += rowH;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(bx, y + 6f, wd, 24f), viewFoot, center);
            GUI.color = Color.white;
        }

        void BuildEquipView(DuelArms arms, ProtoInput.Device dev, int block)
        {
            viewBuilt = true;
            viewKeyDev = dev;
            viewKeyBlock = block;
            bool kb = dev == ProtoInput.Device.Keyboard;
            string aim = ProtoInput.L(ProtoInput.Btn.Aim);
            for (int i = 0; i < ArmRows; i++)
            {
                var slot = (DuelSlot)i;
                var w = arms.Get(slot);
                viewKeyW[i] = w;
                string btn = SlotButton(slot);
                viewHead[i] = DuelWeapon.SlotName(slot) + (btn.Length > 0 ? "  " + btn : "");
                if (w == null)
                {
                    viewName[i] = "<color=#999999>なし</color>";
                    viewStats[i] = "";
                    viewUse[i] = slot switch
                    {
                        DuelSlot.RightHand or DuelSlot.LeftHand => "剣・盾・手の銃を付けられる",
                        DuelSlot.Back => "スラスターを付けられる",
                        _ => "肩の銃・砲・ミサイルを付けられる",
                    };
                    continue;
                }
                string col = ColorUtility.ToHtmlStringRGB(w.Color);
                string name = w == DuelWeapon.BaseSword ? w.name : w.name + "〈" + w.RarityName + "〉";
                viewName[i] = "<size=19><b><color=#" + col + ">" + name + "</color></b></size>   <color=#C8C8C8>" + DuelWeapon.KindName(w.kind)
                              + (w.feel.Length > 0 ? " ― " + w.feel : "") + "</color>";
                viewStats[i] = StatLine(w);
                viewUse[i] = w.kind switch
                {
                    DuelKind.Sword => $"{btn} 攻撃(連撃{w.LightMoves.Length}段)  {aim}+{btn} 強攻撃  回避の後に {btn} 回避斬り",
                    DuelKind.Shield => $"{btn} 押している間ガード",
                    DuelKind.Back => "ボタンなし(付けているだけで効く。倍率は素の性能に掛かる)",
                    _ => $"{btn} {w.FireMode}",
                };
            }
            string close = kb ? "I / Esc" : ProtoInput.L(ProtoInput.Btn.EquipView) + " / " + ProtoInput.L(ProtoInput.Btn.Back);
            string drop = ProtoInput.L(ProtoInput.Btn.Discard), back = kb ? "Esc" : ProtoInput.L(ProtoInput.Btn.Back);
            viewDropHint = $"<color=#AAAAAA>{drop} 床に捨てる</color>";
            viewDropConfirm = $"<color=#FF8A70><b>もう一度 {drop} で足もとに捨てる</b>  {back} やめる</color>";
            string sel = kb ? "↑↓/W・S" : "↑↓";
            viewFoot = block switch
            {
                1 => $"一時停止中 ― {sel} 選ぶ  撃墜中は捨てられない  {close} 閉じる",
                2 => $"一時停止中 ― {sel} 選ぶ  ボスを倒した後は拾い直せないので、捨てられない  {close} 閉じる",
                _ => $"一時停止中 ― {sel} 選ぶ  {drop} 床に捨てる(もう一度押して決める。拾い直せる)  {close} 閉じる",
            };
        }

        /// <summary>主な性能の1行。拾うときの比較(DuelWeapon.Stats)と同じ値に、銃は連射・リロードを足す。</summary>
        static string StatLine(DuelWeapon w)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in DuelWeapon.Stats(w))
            {
                if (sb.Length > 0) sb.Append("    ");
                if (s.name == "弾倉" && w.magazine <= 0) sb.Append("弾切れなし");
                else sb.Append(s.name).Append(' ').Append(s.text);
            }
            if (w.IsGun)
            {
                sb.Append("    連射 ").Append((1f / Mathf.Max(0.01f, w.interval)).ToString("0.#")).Append("発/秒");
                if (w.magazine > 0) sb.Append("    リロード ").Append(w.reload.ToString("0.0")).Append("秒");
            }
            return sb.ToString();
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
