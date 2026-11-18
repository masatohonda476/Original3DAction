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
    /// stage(DuelStage)があれば、波で出す代わりに区画を進むステージになる(MeleeDuelStage.unity)。
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

        [Header("ステージ(あれば波の代わりに区画を進む)")]
        public DuelStage stage;

        [Header("敵")]
        [Tooltip("剣士(中型)のHP")]
        public float enemyHP = 93f;
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
        GUIStyle small, label, big, center;
        /// <summary>頭上のゲージで、減ったHPを少し遅れて追う白い部分(敵ごと)。</summary>
        readonly Dictionary<DuelEnemy, (float ghost, float hp, float holdUntil)> overheadGhost = new();
        float uiScale = 1f;
        float SW => Screen.width / uiScale;
        float SH => Screen.height / uiScale;

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
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
                Log("ステージの試作:区画を進み、奥のボスを倒せばクリア。F1で最初から");
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
            // ヒットストップ:短い間だけ時間をほぼ止める
            Time.timeScale = Time.unscaledTime < warpUntil ? 0.05f : 1f;

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

        /// <summary>当たった瞬間に、realSeconds 秒だけ時間をほぼ止める。</summary>
        public void HitStop(float realSeconds) => warpUntil = Mathf.Max(warpUntil, Time.unscaledTime + realSeconds);

        public void OnEnemyDied(DuelEnemy e)
        {
            if (stage != null)
            {
                // ステージ:区画の残りは DuelStage が数える。やり直しで置き直した敵は二重に数えない
                if (stage.CountKill(e)) kills++;
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
            if (stage != null) stage.OnPlayerDied();   // 区画の入口からやり直す
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
            bool broken = player.Current == DuelPlayer.State.GuardBroken;
            Bar(new Rect(x, y, 300f, 10f), player.Guard / player.maxGuard,
                broken ? new Color(1f, 0.4f, 0.2f) : player.Guarding ? new Color(0.5f, 0.8f, 1f) : new Color(0.4f, 0.55f, 0.7f));
            GUI.Label(new Rect(x + 308f, y - 7f, 200f, 24f), broken ? "ガード崩れ" : player.Guarding ? "ガード中" : "ガード", small);
            y += 22f;
            GUI.Label(new Rect(x, y, SW - x * 2f, 24f), Help(), small);

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
            var rs = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
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
            if (player.Dead) GUI.Label(new Rect(0f, SH * 0.4f, SW, 50f), stage != null ? "撃墜 ― 区画の入口からやり直します" : "撃墜 ― やり直します", big);
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
                if (e == null || e.Dead || e.Dormant || e == bossOnTop) continue;
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

        /// <summary>ステージ:左上に区画名と残りの敵、区画に入ったときに中央へ区画名、ボスを倒したら「ステージクリア」。</summary>
        void DrawStage()
        {
            GUI.Label(new Rect(24f, 12f, 360f, 28f), stage.ZoneName, label);
            GUI.Label(new Rect(24f, 40f, 360f, 24f), stage.ZoneStatus, small);

            if (stage.Cleared)
            {
                GUI.Label(new Rect(0f, SH * 0.36f, SW, 50f), "ステージクリア", big);
                bool pad = ProtoInput.Current != ProtoInput.Device.Keyboard;
                GUI.Label(new Rect(0f, SH * 0.36f + 48f, SW, 26f),
                    $"クリアタイム {Mathf.FloorToInt(stage.ClearTime / 60f)}:{Mathf.FloorToInt(stage.ClearTime % 60f):00}  ―  {(pad ? "SHARE+△" : "F1")} で最初から", center);
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

        static string Help()
        {
            bool pad = ProtoInput.Current != ProtoInput.Device.Keyboard;
            return pad
                ? "R1 通常攻撃(回避の後で回避斬り)  △+R1 強攻撃  □ 回避  ✗長押し 飛行  ○ ブースト切替  L1 ガード  十字左 ロックオン  右スティック左右 切替"
                : "左クリック 通常攻撃(回避の後で回避斬り)  C+左クリック 強攻撃  左Ctrl 回避  Space長押し 飛行  左Shift ブースト切替  右クリック ガード  Q ロックオン  マウス左右 / Tab 切替";
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
