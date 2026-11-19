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
        GUIStyle small, label, big, center, richCenter, rightSmall, richSmall, richRight, viewHeadStyle, viewTitleStyle, chartHeadStyle, chartLegendStyle, chartNoteStyle;
        GUIStyle cellTiny, cellTinyRight, cellNameStyle, cellNameSmall, cellBack;
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
            // SHARE+△ でやり直した直後:押したままの SHARE を離しても、装備の確認を開かない
            ProtoInput.SelectHeldAsCombo();
            DuelRadar.ClearCache();   // 前のシーンの武装の組をためこまない
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
        /// 左のメニューボタン(SHARE・View・− を離す / I)で装備の確認を開く・閉じる。○・B / Backspace でも閉じる(Esc はカメラのマウスの固定を外すので使わない)。
        /// ステージ(武装がある)だけ。開いている間は時間を止め、入力を止める(ProtoInput.Paused)。付け先を選ぶ表示の途中なら取り消す。
        /// 閉じたあとは、その時に押していたボタンを離すまで効かない(ProtoInput 側)。
        /// 開いている間は ↑↓(十字・左スティック・↑↓・W/S)で所を選び、捨てるボタン(□・X / X キー)を2回押すと、その武装を足もとの床に捨てる
        /// (1回目で「もう一度押すと捨てる」になり、選び直す・○/Backspace で取り消し)。撃墜中・ボスを倒した後・真下に床がない所は捨てられない。
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
                // 先行入力(攻撃・回避・射撃)を捨てる(閉じた直後に出ないように)
                player.ClearBufferedInput();
                player.Arms.ClearBufferedInput();
                // 捨てた武装を置く所の真下に床があるか(止まっている間は動かないので、開いたときに1回だけ測る)
                for (int i = 0; i < ArmRows; i++) dropFloor[i] = player.Arms.HasFloorBelow((DuelSlot)i);
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

        /// <summary>選んでいる所の武装を置く所の真下に床があるか(開いたときに測る)。</summary>
        readonly bool[] dropFloor = new bool[ArmRows];

        /// <summary>今は捨てられない理由(0 = 捨てられる / 1 = 撃墜中 / 2 = ボスを倒した後で拾えない / 3 = 真下に床がない(穴の上・空中。落ちて消えるので))。</summary>
        int DropBlock() => player == null || player.Dead ? 1 : stage != null && stage.Cleared ? 2 : !dropFloor[viewSel] ? 3 : 0;

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
            }
            var cam = Camera.main;
            if (player != null) DrawOverheadGauges(cam);   // 被ダメージの数字より奥に描く
            ProtoDamageText.Draw(cam, uiScale, SH);
            if (player == null) return;

            // 左下:自機のHP・EN・ガード、その下に操作説明。操作説明は右下の武装の欄の手前で折り返し、
            // 3行以上になるときは全体を上へずらす(2行までなら今までと同じ高さ)
            string help = Help();
            float x = 24f;
            float helpW = (player.Arms != null ? ArmsLeft - 16f : SW - x) - x;
            float helpH = HelpHeight(help, helpW);
            float y = SH - 8f - helpH - 66f;
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
            GUI.Label(new Rect(x, y, helpW, helpH), help, small);   // 長ければ折り返す(キーボードの表記は長い)
            if (player.Arms != null)
            {
                DrawArms(player.Arms);
                if (!equipOpen) DrawPickup(player.Arms);
                // 両手の銃・砲の溜め撃ち:画面の中央の少し下に溜まり具合(溜まったら白)
                float c = player.Arms.Charge01;
                if (c > 0f && !equipOpen) Bar(new Rect(SW / 2f - 60f, SH * 0.5f + 46f, 120f, 6f), c, c >= 1f ? Color.white : new Color(1f, 0.8f, 0.4f));
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
        /// <summary>付ける所の数(DuelSlot の数。装備の確認の行数も同じ)。</summary>
        const int ArmRows = 5;
        (DuelLoot loot, DuelLoot choosing, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev) pickupKey;
        readonly string[] pickupLines = new string[4];
        int pickupLineCount;
        (ProtoInput.Device dev, bool arms, DuelWeapon two, bool reload) helpKey;
        string helpText;
        // 操作説明の高さ(折り返した行数で決まる。文字列か幅が変わったときだけ測り直す)
        string helpHText;
        float helpHW = -1f, helpHCached = 44f;
        /// <summary>左下の HP・EN・ガードと操作説明のまとまりの上端(拾う案内をこれより上に出す)。</summary>
        float leftTop;

        /// <summary>操作説明の高さ(2行分より低くはしない)。</summary>
        float HelpHeight(string help, float w)
        {
            if (!ReferenceEquals(help, helpHText) || !Mathf.Approximately(w, helpHW))
            {
                helpHText = help;
                helpHW = w;
                helpHCached = Mathf.Max(44f, small.CalcHeight(new GUIContent(help), w));
            }
            return helpHCached;
        }

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

        // ───────── 右下の武装の欄(武装ハントの DrawWeapons / WeaponCell と同じ見せ方) ─────────

        const float CellW = 200f, CellH = 44f, CellGap = 8f, BackRowH = 24f, ArmsMargin = 20f;
        /// <summary>右下の武装の欄の上端(拾う案内をこれより上に出す)。</summary>
        float ArmsTop => SH - ArmsMargin - BackRowH - 6f - CellH * 2f - CellGap;
        /// <summary>右下の武装の欄の左端(操作説明をこれより左で折り返す)。</summary>
        float ArmsLeft => SW - ArmsMargin - CellW * 2f - CellGap;

        /// <summary>セルの右上に出す状態。</summary>
        enum CellState { None, Ammo, Infinite, Reload, Loading, Charging, Charged, TwoHand }

        // セルの文字列(添字は DuelSlot)。名前・ボタンは武装か機器が、状態は武装・状態・残り(弾数か0.1秒単位)が変わったときだけ作り直す
        readonly string[] cellBtn = new string[ArmRows], cellName = new string[ArmRows], cellStatus = new string[ArmRows];
        readonly (DuelWeapon w, ProtoInput.Device dev, bool set)[] cellKey = new (DuelWeapon, ProtoInput.Device, bool)[ArmRows];
        readonly (DuelWeapon w, CellState st, int val, bool set)[] cellStatusKey = new (DuelWeapon, CellState, int, bool)[ArmRows];

        static readonly Color GaugeOrange = new(1f, 0.6f, 0.15f, 0.35f), StatusOrange = new(1f, 0.75f, 0.4f);

        /// <summary>
        /// 右下:付けている武装を肩ボタンの並び(左=L・右=R、上の段=肩・下の段=手)の 2×2 のセルで、その下に背中の1行。
        /// セルにはボタン名・レア度の色の四角・武装名。右上に状態:銃は「弾 12/20」(残りが弾倉の1/4以下で赤っぽく)、弾倉のない銃は「弾数∞」。
        /// 銃のリロード中・砲とミサイルの装填中は、セル全体を左から満ちていくオレンジのゲージにし「リロード 1.2秒」「装填 0.8秒」。
        /// 両手持ちは右手と左手のセルを細い帯でつなぎ、右手のセルに武装名と主の撃ち方の状態、左手のセルに L1 の技名と
        /// L1 の撃ち方の状態(溜めている間は溜まり具合のゲージ、撃った後は次に撃てるまでのゲージ)。
        /// </summary>
        void DrawArms(DuelArms arms)
        {
            float x1 = SW - ArmsMargin - CellW, x0 = x1 - CellGap - CellW;
            float yBack = SH - ArmsMargin - BackRowH;
            float yRow1 = yBack - 6f - CellH, yRow2 = yRow1 - CellGap - CellH;
            ArmCell(arms, new Rect(x0, yRow2, CellW, CellH), DuelSlot.LeftShoulder);
            ArmCell(arms, new Rect(x1, yRow2, CellW, CellH), DuelSlot.RightShoulder);
            ArmCell(arms, new Rect(x0, yRow1, CellW, CellH), DuelSlot.LeftHand);
            ArmCell(arms, new Rect(x1, yRow1, CellW, CellH), DuelSlot.RightHand);
            // 両手持ち:左手と右手のセルの間を、武装のレア度の色の帯でつなぐ(1つの武装が2つの所にまたがる)
            if (arms.TwoHanded)
            {
                var c = arms.Get(DuelSlot.RightHand).Color;
                c.a = 0.6f;
                Chip(new Rect(x0 + CellW, yRow1 + CellH * 0.5f - 3f, CellGap, 6f), c);
            }

            // 一番下:背中(左の列に1行)
            const int b = (int)DuelSlot.Back;
            var back = arms.Get(DuelSlot.Back);
            if (cellKey[b] != (back, ProtoInput.Current, true))
            {
                cellKey[b] = (back, ProtoInput.Current, true);
                cellName[b] = "背中  " + (back != null ? back.name : "―");
            }
            var br = new Rect(x0, yBack, CellW, BackRowH);
            Chip(br, new Color(0f, 0f, 0f, back != null ? 0.5f : 0.25f));
            if (back != null) Chip(new Rect(br.x + 8f, br.y + 7f, 10f, 10f), back.Color);
            GUI.color = back != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            GUI.Label(new Rect(br.x + 24f, br.y + 2f, CellW - 30f, 20f), cellName[b], cellBack);
            GUI.color = Color.white;
        }

        void ArmCell(DuelArms arms, Rect r, DuelSlot slot)
        {
            int i = (int)slot;
            var w = arms.Get(slot);
            // 両手持ちの左手のセル:右手のセルと同じ武装の続き(L1 の技名と、L1 の撃ち方の状態を出す。弾倉は右手のセル)
            bool twoL = slot == DuelSlot.LeftHand && w is { IsTwoHand: true };
            var dev = ProtoInput.Current;
            if (cellKey[i] != (w, dev, true))
            {
                cellKey[i] = (w, dev, true);
                cellBtn[i] = SlotButton(slot);
                cellName[i] = w == null ? "―" : twoL ? "└ " + w.altMove : w.name;
            }
            Chip(r, new Color(0f, 0f, 0f, w == null ? 0.25f : 0.5f));

            // 状態とゲージ(gauge はセルを左から満たす割合。0なら出さない)
            var st = CellState.None;
            int val = 0;
            float gauge = 0f;
            Color gaugeColor = GaugeOrange;
            if (w != null && !twoL)
            {
                if (w.IsGun)
                {
                    float left = arms.ReloadLeft(slot);
                    int mag = arms.MagLeft(slot);
                    if (left > 0f)
                    {
                        st = CellState.Reload;
                        val = Mathf.CeilToInt(left * 10f);
                        gauge = 1f - arms.Reload01(slot);
                    }
                    else if (mag >= 0)
                    {
                        st = CellState.Ammo;
                        val = mag;
                    }
                    else st = CellState.Infinite;
                }
                else if (w.Explosive && arms.CooldownLeft(slot) > 0f)
                {
                    st = CellState.Loading;
                    val = Mathf.CeilToInt(arms.CooldownLeft(slot) * 10f);
                    gauge = 1f - arms.Cooldown01(slot);
                }
            }
            else if (twoL && arms.AltFire != null)
            {
                // L1 の撃ち方:溜めている間は溜まり具合(溜まったら白)、撃った後は次に撃てるまで
                float c = arms.Charge01;
                if (c > 0f)
                {
                    st = c >= 1f ? CellState.Charged : CellState.Charging;
                    gauge = c;
                    gaugeColor = c >= 1f ? new Color(1f, 1f, 1f, 0.3f) : new Color(1f, 0.8f, 0.4f, 0.35f);
                }
                else if (arms.AltCooldownLeft > 0f)
                {
                    st = CellState.Loading;
                    val = Mathf.CeilToInt(arms.AltCooldownLeft * 10f);
                    gauge = 1f - arms.AltCooldown01;
                }
            }
            if (st == CellState.None && w is { IsTwoHand: true }) st = CellState.TwoHand;
            if (gauge > 0f) Chip(new Rect(r.x, r.y, r.width * Mathf.Clamp01(gauge), r.height), gaugeColor);

            if (st != CellState.None)
            {
                if (cellStatusKey[i] != (w, st, val, true))
                {
                    cellStatusKey[i] = (w, st, val, true);
                    cellStatus[i] = st switch
                    {
                        CellState.Ammo => $"弾 {val}/{w.magazine}",
                        CellState.Infinite => "弾数∞",
                        CellState.Reload => $"リロード {val / 10f:0.0}秒",
                        CellState.Loading => $"装填 {val / 10f:0.0}秒",
                        CellState.Charging => "溜め中",
                        CellState.Charged => "溜まった・離して撃つ",
                        _ => "両手持ち",
                    };
                }
                GUI.color = st switch
                {
                    CellState.Ammo => val <= w.magazine / 4 ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.87f, 0.9f),
                    CellState.Infinite => new Color(1f, 0.6f, 0.95f),
                    CellState.Reload or CellState.Loading => StatusOrange,
                    CellState.Charging => new Color(1f, 0.85f, 0.5f),
                    CellState.Charged => Color.white,
                    _ => new Color(1f, 1f, 1f, 0.5f),
                };
                // 武装ハントは中央寄せ。キーボードのボタン名(「右クリック」など)と重ならないよう右に寄せる
                GUI.Label(new Rect(r.x, r.y + 3f, r.width - 8f, 16f), cellStatus[i], cellTinyRight);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, 90f, 16f), cellBtn[i], cellTiny);
            if (w != null) Chip(new Rect(r.x + 8f, r.y + 23f, 10f, 10f), w.Color);
            GUI.color = w == null ? new Color(1f, 1f, 1f, 0.4f) : twoL ? new Color(1f, 1f, 1f, 0.85f) : Color.white;
            // 両手持ちの L1 の技名は長いことがあるので、少し小さい字で
            if (twoL) GUI.Label(new Rect(r.x + 24f, r.y + 19f, r.width - 30f, 22f), cellName[i], cellNameSmall);
            else GUI.Label(new Rect(r.x + 24f, r.y + 16f, r.width - 30f, 24f), cellName[i], cellNameStyle);
            GUI.color = Color.white;
        }

        void Chip(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 画面の下の中央:拾える武装があれば、名前と今の装備と比べた内容。無視すればそのまま残る。
        /// 十字右で付け先を選ぶ表示にして、手・肩なら左右それぞれの今の武装と、背中なら今の背中と比べる(背中は ✗ で付ける)。
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
                bool curTwo = a is { IsTwoHand: true };   // 今、両手持ちを付けている(右手と左手に同じ武装)
                if (w.IsTwoHand)
                {
                    // 両手持ち:付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付き、今の右手・左手の武装は両方とも置かれる
                    string r1 = SlotButton(DuelSlot.RightHand), l1 = SlotButton(DuelSlot.LeftHand);
                    var cmp = DuelWeapon.SameFamily(a, w) ? a : DuelWeapon.SameFamily(b, w) ? b : null;
                    if (arms.Choosing == null)
                    {
                        pickupLines[0] = equip + " 拾う:" + name + "(" + DuelWeapon.KindName(w.kind) + "・両手持ち)  ― " + w.feel;
                        pickupLines[1] = TwoHandMoves(w);
                        pickupLines[2] = DuelWeapon.CompareText(w, cmp);
                        pickupLineCount = 3;
                    }
                    else
                    {
                        string off = curTwo ? "両手の" + a.name : "右手の" + (a != null ? a.name : "なし") + "・左手の" + (b != null ? b.name : "なし");
                        pickupLines[0] = name + " を両手に付ける?  [" + r1 + "] / [" + l1 + "] どちらでも両手に付く    " + equip + " やめる";
                        pickupLines[1] = (a == null && b == null ? "<color=#AAAAAA>両手とも空いている</color>"
                                             : "<color=#FFB070>" + off + " は足もとに置かれる(拾い直せる)</color>");
                        pickupLines[2] = (cmp != null ? (cmp == a ? (curTwo ? "両手の" : "右手の") : "左手の") + cmp.name + "と比べて  " : "") + DuelWeapon.CompareText(w, cmp);
                        pickupLines[3] = TwoHandMoves(w);
                        pickupLineCount = 4;
                    }
                }
                else if (to.Length == 1)
                {
                    // 背中:付け先を選ぶ表示で ✗ を押すと付く(✗ は飛行と同じボタン。選んでいる間は飛ばない)
                    string fly = "[" + ProtoInput.L(ProtoInput.Btn.Fly) + "]";
                    if (arms.Choosing == null)
                    {
                        pickupLines[0] = equip + " 拾う:" + name + "(" + DuelWeapon.KindName(w.kind) + ")  ― " + w.feel + "(今:" + CurName(a) + ")";
                        pickupLines[1] = DuelWeapon.CompareText(w, a);
                        pickupLineCount = 2;
                    }
                    else
                    {
                        pickupLines[0] = name + " を背中に付ける?  " + fly + " " + DuelWeapon.SlotName(to[0]) + "に付ける(今:" + (a != null ? a.name : "なし") + ")    " + equip + " やめる";
                        pickupLines[1] = DuelWeapon.SlotName(to[0]) + "と比べて  " + DuelWeapon.CompareText(w, a);
                        pickupLines[2] = "<color=#AAAAAA>選んでいる間は " + ProtoInput.L(ProtoInput.Btn.Fly) + " で飛ばない</color>";
                        pickupLineCount = 3;
                    }
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
                    if (curTwo && w.IsHand)
                    {
                        // 両手持ちのまま片手の武装を付ける:両手持ちは外れて置かれ、もう片方の手は空になる
                        pickupLines[3] = "<color=#FFB070>両手の" + a.name + " は外れて足もとに置かれ、もう片方の手は空になる</color>";
                        pickupLineCount = 4;
                    }
                }
                BuildPickCharts(arms.Choosing != null ? w : null, to, a, b, curTwo);
            }
            // 右下の武装の欄と左下の HP・操作説明に重ならない高さ(行が多いときは上へずらす)
            float h = 6f + 26f * pickupLineCount, wd = 1200f, bx = (SW - wd) / 2f;
            float by = Mathf.Min(SH * 0.56f, Mathf.Min(ArmsTop, leftTop) - 8f - h);
            GUI.color = new Color(0f, 0f, 0f, arms.Choosing != null ? 0.7f : 0.55f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < pickupLineCount; i++)
                GUI.Label(new Rect(bx, by + 4f + i * 26f, wd, 24f), pickupLines[i], richCenter);
            if (arms.Choosing != null) DrawPickCharts(by);
        }

        // ───────── 付け先を選ぶ表示の性能のグラフ ─────────

        // [0] = 画面の左(左手・左肩と比べる)/ [1] = 画面の右(右手・右肩と比べる)/ [2] = 画面の上の中央(背中と比べる)。
        // 中身は付け先を選ぶ表示の文字列と一緒に作り直す
        const int PickCharts = 3;
        readonly bool[] pickChartOn = new bool[PickCharts];
        readonly DuelRadar.Pair[] pickChartPair = new DuelRadar.Pair[PickCharts];
        readonly string[] pickChartHead = new string[PickCharts], pickChartLegend = new string[PickCharts];

        /// <summary>
        /// 付け先を選ぶ表示のグラフの中身。左の所(左手・左肩)と比べるものを画面の左、右の所と比べるものを画面の右に出し、
        /// 押す前に両方の候補を並べる(どちらのボタンでどちらの今の武装と入れ替わるかが、置き場所とボタン名で分かるように)。
        /// 両手持ちは両手の今の武装それぞれと比べる(R1/L1 のどちらでも両手に付くのでボタン名は出さない)。今も両手持ちなら1つだけ(右)。
        /// 片手の武装を、両手持ちを付けたまま付けるときは、左右どちらも今の両手持ちと比べる(どちらでも両手持ちが外れる)。
        /// 背中は画面の上の中央に1つ(今の背中と比べる。見出しは ✗ のボタン名)。w が null(選んでいない)なら出さない。
        /// </summary>
        void BuildPickCharts(DuelWeapon w, DuelSlot[] to, DuelWeapon a, DuelWeapon b, bool curTwo)
        {
            for (int i = 0; i < PickCharts; i++) pickChartOn[i] = false;
            if (w == null) return;
            if (to.Length == 1)
            {
                SetPickChart(2, w, a, "<b>[" + ProtoInput.L(ProtoInput.Btn.Fly) + "] " + DuelWeapon.SlotName(to[0]) + "に付けると</b>");
                return;
            }
            if (w.IsTwoHand)
            {
                if (curTwo)
                {
                    SetPickChart(1, w, a, "<b>両手に付けると</b>  <color=#AAAAAA>今の両手持ちと入れ替わる</color>");
                    return;
                }
                SetPickChart(0, w, b, "<b>両手に付けると</b>  <color=#AAAAAA>今の左手と比べて</color>");
                SetPickChart(1, w, a, "<b>両手に付けると</b>  <color=#AAAAAA>今の右手と比べて</color>");
                return;
            }
            string note = curTwo && w.IsHand ? "  <color=#FFB070>両手持ちが外れる</color>" : "";
            SetPickChart(0, w, b, "<b>[" + SlotButton(to[1]) + "] " + DuelWeapon.SlotName(to[1]) + "に付けると</b>" + note);
            SetPickChart(1, w, a, "<b>[" + SlotButton(to[0]) + "] " + DuelWeapon.SlotName(to[0]) + "に付けると</b>" + note);
        }

        void SetPickChart(int side, DuelWeapon w, DuelWeapon cur, string head)
        {
            var pair = DuelRadar.Get(w, cur);
            pickChartOn[side] = true;
            pickChartPair[side] = pair;
            pickChartHead[side] = head;
            pickChartLegend[side] = ChartLegend(pair, "新", cur != null ? "今" : null, "今は空き");
        }

        /// <summary>
        /// 凡例の1行:「■ 新:名前  ━ 今:名前」。相手が空きなら emptyText、仲間が違えば「今:名前(種類。仲間が違うので比べない)」。
        /// mainTag / otherTag は名前の前に付ける言葉(otherTag が null なら相手は空き)。
        /// </summary>
        static string ChartLegend(DuelRadar.Pair p, string mainTag, string otherTag, string emptyText)
        {
            string main = "<color=" + DuelRadar.MainHex + ">■ " + mainTag + ":" + p.main.name + "</color>";
            if (otherTag == null || p.other == null) return main + "   <color=#999999>" + emptyText + "</color>";
            if (p.Same) return main + "   <color=" + DuelRadar.OtherHex + ">━ " + otherTag + ":同じ" + p.other.name + "(性能は同じ)</color>";
            if (p.otherDifferent)
                return main + "   <color=#AAAAAA>" + otherTag + ":" + p.other.name + "(" + DuelWeapon.KindName(p.other.kind) + "。仲間が違うので比べない)</color>";
            return main + "   <color=" + DuelRadar.OtherHex + ">━ " + otherTag + ":" + p.other.name + "</color>";
        }

        /// <summary>
        /// 付け先を選ぶ表示のグラフ:拾う案内の枠(画面の下の中央)の上で、画面の左右の端に寄せて出す(真ん中の機体と敵を隠さない)。
        /// 背中だけは左右の別がないので、画面の上の中央(上の敵のゲージの下)に出す(左右の枠と同時には出ない)。
        /// 1つの枠に、見出し(どのボタンでどこに付けるか)・凡例・レーダーチャート(仲間が違えば出さない)・帯グラフ。
        /// </summary>
        /// <summary>背中のグラフの上端(上の中央の敵の HP・怯みのゲージ(〜76)の下)。</summary>
        const float BackChartTop = 82f;

        void DrawPickCharts(float boxY)
        {
            const float pw = 440f, ph = 190f, pad = 8f, radarW = 150f, gap = 16f;   // gap:レーダーの横の軸名が帯グラフの名前に重ならない間
            float py = boxY - 8f - ph;
            for (int side = 0; side < PickCharts; side++)
            {
                if (!pickChartOn[side]) continue;
                var pair = pickChartPair[side];
                float px = side == 0 ? 16f : side == 1 ? SW - 16f - pw : (SW - pw) / 2f;
                if (side == 2) py = BackChartTop;
                GUI.color = new Color(0f, 0f, 0f, 0.62f);
                GUI.DrawTexture(new Rect(px, py, pw, ph), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(px + pad, py + 3f, pw - pad * 2f, 22f), pickChartHead[side], chartHeadStyle);
                GUI.Label(new Rect(px + pad, py + 24f, pw - pad * 2f, 20f), pickChartLegend[side], chartLegendStyle);
                float cy = py + 46f, ch = ph - 46f - 4f;
                if (pair.ShowRadar)
                {
                    DuelRadar.DrawRadar(new Rect(px + pad, cy, radarW, ch), uiScale, pair);
                    DuelRadar.DrawBars(new Rect(px + pad + radarW + gap, cy, pw - pad * 2f - radarW - gap, ch), uiScale, pair);
                }
                else DuelRadar.DrawBars(new Rect(px + pad, cy, pw - pad * 2f, ch), uiScale, pair, 80f, 120f);
                GUI.color = Color.white;
            }
        }

        /// <summary>操作説明。ボタン名は使っているコントローラー(キーボード / PlayStation / Xbox)に合わせる。変わったときだけ作り直す。</summary>
        string Help()
        {
            var dev = ProtoInput.Current;
            bool arms = player != null && player.Arms != null;
            var two = arms && player.Arms.TwoHanded ? player.Arms.Get(DuelSlot.RightHand) : null;
            bool reload = arms && player.Arms.AnyReloadable;   // 弾倉のある銃を付けているときだけ「△+撃つボタン リロード」を出す
            var key = (dev, arms, two, reload);
            if (helpKey == key && helpText != null) return helpText;
            helpKey = key;
            static string B(ProtoInput.Btn b) => ProtoInput.L(b);
            bool kb = dev == ProtoInput.Device.Keyboard;
            var sb = new System.Text.StringBuilder();
            if (arms)
            {
                if (two != null)
                {
                    // 両手持ち:R1 が主の技、L1 が別の技
                    sb.Append(B(ProtoInput.Btn.R1)).Append(' ').Append(two.MainMoveName);
                    sb.Append("  ").Append(B(ProtoInput.Btn.L1)).Append(' ').Append(two.altMove).Append(two.alt is { charge: > 0f } ? "(溜めて離す)" : "");
                    if (two.IsBlade) sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append('+').Append(B(ProtoInput.Btn.R1)).Append('/').Append(B(ProtoInput.Btn.L1)).Append(" 強攻撃");
                }
                else
                {
                    // ステージ:手のボタンはその手の武装(剣=攻撃 / 盾=ガード / 銃=射撃)
                    sb.Append(B(ProtoInput.Btn.R1)).Append('/').Append(B(ProtoInput.Btn.L1)).Append(" 右手/左手の武装");
                    sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append('+').Append(B(ProtoInput.Btn.R1)).Append('/').Append(B(ProtoInput.Btn.L1)).Append(" 強攻撃");
                }
                if (reload) sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append("+撃つボタン リロード");
                sb.Append("  ").Append(B(ProtoInput.Btn.R2)).Append('/').Append(B(ProtoInput.Btn.L2)).Append(" 右肩/左肩");
                sb.Append("  ").Append(B(ProtoInput.Btn.Dodge)).Append(" 回避");
                sb.Append("  ").Append(B(ProtoInput.Btn.Fly)).Append("長押し 飛行");
                sb.Append("  ").Append(B(ProtoInput.Btn.Boost)).Append(" ブースト");
                sb.Append("  ").Append(B(ProtoInput.Btn.EquipNow)).Append(" 拾う→付ける所のボタン(背中は ").Append(B(ProtoInput.Btn.Fly)).Append(')');
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
        string viewFoot, viewDropHint, viewDropConfirm, viewDropConfirm2;

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
            // 下に性能のグラフの枠(EquipChartH)を足した全体を、画面の中央に
            float bx = (SW - wd) / 2f, by = Mathf.Max(4f, (SH - h - EquipChartGap - EquipChartH) / 2f);
            // 画面全体を少し暗くし、その上にパネル
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "装備", viewTitleStyle);

            float y = by + pad + titleH;
            float tx = bx + pad + headW, tw = wd - pad * 2f - headW;
            // 両手持ちは右手・左手の2行で1つ:どちらを選んでも2行とも選んでいる帯にし、捨てる確認も2行とも
            bool two = arms.TwoHanded;
            bool selTwo = two && viewSel <= (int)DuelSlot.LeftHand;
            for (int i = 0; i < ArmRows; i++)
            {
                var w = arms.Get((DuelSlot)i);
                bool sel = i == viewSel || (selTwo && i <= (int)DuelSlot.LeftHand);
                bool armed = sel && dropArmed && w != null;
                if (sel)
                {
                    // 選んでいる所:帯と左の縦線(捨てる確認の途中は赤)
                    GUI.color = armed ? new Color(0.9f, 0.25f, 0.2f, 0.28f) : new Color(1f, 1f, 1f, 0.09f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, wd - pad * 2f + 16f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = armed ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.8f, 1f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, 4f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (w != null && block == 0 && !armed && i == viewSel) GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewDropHint, richRight);
                }
                // 区切りの細い線(両手持ちの右手と左手の間は引かない)
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                if (!(two && i == (int)DuelSlot.LeftHand)) GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(bx + pad, y + 6f, headW, 26f), viewHead[i], viewHeadStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewName[i], richSmall);
                if (w != null) GUI.Label(new Rect(tx, y + 29f, tw, 22f), viewStats[i], richSmall);
                // 3行目:使うボタン(捨てる確認の途中は、その案内に替える)
                GUI.color = armed ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(tx, y + (w != null ? 51f : 29f), tw, 22f), armed ? (selTwo ? viewDropConfirm2 : viewDropConfirm) : viewUse[i], richSmall);
                GUI.color = Color.white;
                y += rowH;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(bx, y + 6f, wd, 24f), viewFoot, center);
            GUI.color = Color.white;

            DrawEquipChart(arms, bx, by + h + EquipChartGap, wd);
        }

        const float EquipChartH = 196f, EquipChartGap = 8f;
        // 装備の確認のグラフ:選んでいる所・その武装・比べる相手が変わったときだけ作り直す
        (int sel, DuelWeapon w, DuelWeapon other) viewChartKey = (-1, null, null);
        DuelRadar.Pair viewChartPair;
        string viewChartNote;

        /// <summary>比べる相手の所(右手⇔左手、右肩⇔左肩。背中はなし)。</summary>
        static int PartnerSlot(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => (int)DuelSlot.LeftHand,
            DuelSlot.LeftHand => (int)DuelSlot.RightHand,
            DuelSlot.RightShoulder => (int)DuelSlot.LeftShoulder,
            DuelSlot.LeftShoulder => (int)DuelSlot.RightShoulder,
            _ => -1,
        };

        /// <summary>
        /// 装備の確認の下の枠:選んでいる所の武装のレーダーチャートと帯グラフ。比べる相手は同じ仲間のもう片方の所(右手⇔左手、右肩⇔左肩)。
        /// 相手が空き・背中・両手持ちなら単独で出す。相手の仲間が違えば(剣と盾など)レーダーは出さず、帯グラフも選んでいる武装だけ。
        /// 右の欄に凡例と読み方。選んでいる所が空きなら、その旨だけ。
        /// </summary>
        void DrawEquipChart(DuelArms arms, float bx, float y, float wd)
        {
            var slot = (DuelSlot)viewSel;
            var w = arms.Get(slot);
            int ps = PartnerSlot(slot);
            var other = w == null || w.IsTwoHand || ps < 0 ? null : arms.Get((DuelSlot)ps);
            var key = (viewSel, w, other);
            if (viewChartKey != key)
            {
                viewChartKey = key;
                viewChartPair = DuelRadar.Get(w, other);
                if (w != null)
                {
                    string me = w.IsTwoHand ? "両手" : DuelWeapon.SlotName(slot);
                    string them = ps >= 0 ? DuelWeapon.SlotName((DuelSlot)ps) : "";
                    string why = w.IsTwoHand ? "両手持ちなので、比べる相手はない"
                        : ps < 0 ? "背中は1つだけなので、比べる相手はない"
                        : other == null ? them + "は空き"
                        : viewChartPair.otherDifferent ? them + "の" + other.name + "は" + DuelWeapon.KindName(other.kind) + "で、仲間が違うので比べない"
                        : "";
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<color=").Append(DuelRadar.MainHex).Append(">■ ").Append(me).Append(":").Append(w.name).Append("</color>\n");
                    if (viewChartPair.Same)
                        sb.Append("<color=").Append(DuelRadar.OtherHex).Append(">━ ").Append(them).Append(":同じ").Append(other.name).Append("</color>\n\n")
                          .Append("<color=#BBBBBB>").Append(them).Append("と同じ武装なので、性能は同じ</color>\n");
                    else if (viewChartPair.Compared)
                        sb.Append("<color=").Append(DuelRadar.OtherHex).Append(">━ ").Append(them).Append(":").Append(other.name).Append("</color>\n\n")
                          .Append("<color=#BBBBBB>数値は ").Append(them).Append("→").Append(me).Append("。")
                          .Append("<color=#7CFC8A>緑</color>は").Append(me).Append("のほうが良い、<color=#FF7A6A>赤</color>は劣る</color>\n");
                    else sb.Append("<color=#999999>").Append(why).Append("</color>\n\n");
                    sb.Append("<color=#999999>外側(長い)ほど良い。目盛りは同じ仲間の武装の中での位置</color>");
                    viewChartNote = sb.ToString();
                }
            }

            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, y, wd, EquipChartH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (viewChartPair == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(bx, y, wd, EquipChartH), "空き ― 武装を付けると、ここに性能のグラフを出す", center);
                GUI.color = Color.white;
                return;
            }
            const float pad = 18f, noteW = 250f;
            float rs = EquipChartH - 14f;
            float rx = bx + pad, ry = y + 7f;
            float noteX = bx + wd - pad - noteW;
            if (viewChartPair.ShowRadar)
            {
                DuelRadar.DrawRadar(new Rect(rx, ry, rs, rs), uiScale, viewChartPair);
                float barX = rx + rs + 14f;
                DuelRadar.DrawBars(new Rect(barX, ry + 4f, noteX - 18f - barX, rs - 8f), uiScale, viewChartPair, 84f, 130f);
            }
            else DuelRadar.DrawBars(new Rect(rx, ry + 4f, noteX - 18f - rx, rs - 8f), uiScale, viewChartPair, 84f, 130f);
            GUI.Label(new Rect(noteX, ry + 6f, noteW, rs - 12f), viewChartNote, chartNoteStyle);
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
                if (w.IsTwoHand && slot == DuelSlot.LeftHand)
                {
                    // 両手持ちの左手:右手の行の武装の続き(1つの武装が2つの所にまたがる)。L1 の技を出す
                    string r1 = SlotButton(DuelSlot.RightHand);
                    viewName[i] = "<color=#" + col + ">┗ 両手持ち(" + r1 + " の " + name + " と合わせて1つ)</color>";
                    viewStats[i] = AltStatLine(w);
                    viewUse[i] = w.IsBlade ? $"{btn} {w.altMove}(回避の後も)  {aim}+{btn} 強攻撃  ガードはできない"
                        : $"{btn} {w.altMove}" + (w.alt is { charge: > 0f } ? "(押して溜め、離して撃つ)" : "")
                          + (w.IsGun && w.magazine > 0 ? $"  {aim}+{btn} 主の銃のリロード" : "") + "  ガードはできない";
                    continue;
                }
                viewName[i] = "<size=19><b><color=#" + col + ">" + (w.IsTwoHand ? "┏ " : "") + name + "</color></b></size>   <color=#C8C8C8>" + DuelWeapon.KindName(w.kind)
                              + (w.feel.Length > 0 ? " ― " + w.feel : "") + "</color>";
                viewStats[i] = StatLine(w);
                viewUse[i] = w.kind switch
                {
                    DuelKind.Sword => $"{btn} 攻撃(連撃{w.LightMoves.Length}段)  {aim}+{btn} 強攻撃  回避の後に {btn} 回避斬り",
                    DuelKind.TwoHandMelee => $"{btn} 連撃{w.LightMoves.Length}段  {aim}+{btn} 強攻撃  回避の後に {btn} 回避斬り",
                    DuelKind.TwoHandGun => $"{btn} {w.MainMoveName}({w.FireMode})",
                    DuelKind.Shield => $"{btn} 押している間ガード",
                    DuelKind.Back => "ボタンなし(付けているだけで効く。倍率は素の性能に掛かる)",
                    _ => $"{btn} {w.FireMode}",
                };
                if (w.IsGun && w.magazine > 0) viewUse[i] += $"  {aim}+{btn} リロード";
            }
            string close = kb ? "I / Backspace" : ProtoInput.L(ProtoInput.Btn.EquipView) + " / " + ProtoInput.L(ProtoInput.Btn.Back);
            string drop = ProtoInput.L(ProtoInput.Btn.Discard), back = kb ? "Backspace" : ProtoInput.L(ProtoInput.Btn.Back);
            viewDropHint = $"<color=#AAAAAA>{drop} 床に捨てる</color>";
            viewDropConfirm = $"<color=#FF8A70><b>もう一度 {drop} で足もとに捨てる</b>  {back} やめる</color>";
            viewDropConfirm2 = $"<color=#FF8A70><b>もう一度 {drop} で両手の武装を足もとに捨てる(両手が空く)</b>  {back} やめる</color>";
            string sel = kb ? "↑↓/W・S" : "↑↓";
            viewFoot = block switch
            {
                1 => $"一時停止中 ― {sel} 選ぶ  撃墜中は捨てられない  {close} 閉じる",
                2 => $"一時停止中 ― {sel} 選ぶ  ボスを倒した後は拾い直せないので、捨てられない  {close} 閉じる",
                3 => $"一時停止中 ― {sel} 選ぶ  真下に床がない(穴の上・空中)ので、ここでは捨てられない  {close} 閉じる",
                _ => $"一時停止中 ― {sel} 選ぶ  {drop} 床に捨てる(もう一度押して決める。拾い直せる)  {close} 閉じる",
            };
        }

        /// <summary>両手持ちの L1 の技の1行(近接は一振りの威力・届く範囲・幅・EN、射撃は撃ち方)。</summary>
        static string AltStatLine(DuelWeapon w)
        {
            if (!w.IsBlade) return "L1 " + w.altMove + ":" + w.AltShotText();
            var alt = w.AltMoves;
            if (alt == null) return "";
            // 「L1 突き → 斬り上げ:威力 27(±30°・15.5m) → 31(±80°・14.7m)  EN 14」
            var sb = new System.Text.StringBuilder("L1 ").Append(w.altMove).Append(":威力 ");
            for (int k = 0; k < alt.Length; k++)
            {
                var m = alt[k];
                if (k > 0) sb.Append(" → ");
                sb.Append($"{m.damage * w.damageMul:0}(±{Mathf.Min(180f, m.arc + w.arcAdd):0}°・{m.reach * w.reachMul:0.#}m)");
            }
            sb.Append($"  EN {alt[0].cost:0}");
            return sb.ToString();
        }

        /// <summary>主な性能の1行。拾うときの比較(DuelWeapon.Stats)と同じ値に、銃は連射・リロードを足す(両手持ちの L1 は左手の行に出すので除く)。</summary>
        static string StatLine(DuelWeapon w)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in DuelWeapon.Stats(w))
            {
                if (w.IsTwoHand && s.name is "L1" or "L1技") continue;
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
