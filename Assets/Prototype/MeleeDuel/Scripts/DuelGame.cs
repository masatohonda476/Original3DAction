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

        [Header("崩し(銃で崩しにいき、剣で仕留める。部屋・ステージ・ボス共通)")]
        [Tooltip("射撃で溜められる敵の怯みゲージの上限(最大の何割)。射撃だけでは怯ませられず、最後に崩す(怯ませ切る)のは近接だけ。" +
                 "怯みのバーはこの少し手前から白く点滅する。1 にすると射撃でも怯む(元の動き)")]
        [Range(0f, 1f)] public float rangedStaggerCap = 0.9f;
        [Tooltip("崩れている(怯んでいる)敵に近接を当てたときのダメージの倍率(射撃には掛からない)")]
        public float brokenMeleeDamageMul = 2f;
        [Tooltip("近接で崩したときの怯みの最短の秒数。技の怯みの時間がこれより短ければ延ばす(崩した後に一太刀入れる間)。0 なら技の値のまま")]
        public float breakFlinchMin = 1f;
        [Tooltip("射撃の1発ごとに軽く押し返す速さ(武装の knock に掛ける倍率)。怯ませず、技も止めない。0 なら押さない")]
        public float rangedPushMul = 1f;
        [Tooltip("武装の knock がこれ以上の射撃だけ1発ごとに押し返す(連射の銃で、寄ってくる敵を引き止め続けないように)")]
        public float rangedPushMin = 8f;

        /// <summary>怯みのバーを白く点滅させ始める割合(8割か、射撃の上限の少し手前の低いほう)。射撃で止まった所が「斬りにいけ」の合図になる。</summary>
        public float StaggerBlinkFrom => Mathf.Min(0.8f, rangedStaggerCap - 0.02f);

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
        /// 開いている間は ↑↓(十字・左スティック・↑↓・W/S)で所と所持品を選ぶ(5つの所の下に所持品が続く)。
        ///   付けている武装:△ / C で外して所持品へ。所持品:その所のボタン(R1/L1・R2/L2・背中は ×)で付ける(今の武装は所持品へ)。
        ///   どちらも □・X / X キーで処分の選択になり、もう一度 □ で足もとの床に捨てる、← で分解して弾、→ で分解して回復(○/Backspace・選び直すでやめる)。
        ///   撃墜中・ボスを倒した後・真下に床がない所は捨てられない(分解はできる。回復は撃墜中はできない)。
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
                for (int i = 0; i < ArmRows; i++) dropFloor[i] = player.Arms.HasFloorBelow((DuelSlot)i);   // 所持品は背中と同じ所に置く
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

        /// <summary>選んでいる行(0〜4 = DuelSlot、5〜 = 所持品の番号 + 5)と、処分(捨てる・分解)を選んでいる途中か。</summary>
        int viewSel;
        bool dropArmed;
        /// <summary>所持品の行を選んでいる。</summary>
        bool StashSelected => viewSel >= ArmRows;
        int StashSel => viewSel - ArmRows;
        /// <summary>選んでいる行の武装(空き・範囲外なら null)。</summary>
        DuelWeapon SelectedWeapon(DuelArms arms) => StashSelected
            ? (StashSel < arms.Stash.Count ? arms.Stash[StashSel].w : null)
            : arms.Get((DuelSlot)viewSel);

        /// <summary>選んでいる所の武装を置く所の真下に床があるか(開いたときに測る)。</summary>
        readonly bool[] dropFloor = new bool[ArmRows];

        /// <summary>
        /// 今は捨てられない理由(0 = 捨てられる / 1 = 撃墜中 / 2 = ボスを倒した後で拾えない / 3 = 真下に床がない(穴の上・空中。落ちて消えるので) /
        /// 4 = 攻撃・回避・怯みなどの途中(動ける・ガード中でない。DuelArms.CanRearrange))。1 と 4 は付け外し・分解もできない。
        /// </summary>
        int DropBlock() => player == null || player.Dead ? 1 : player.Arms != null && !player.Arms.CanRearrange ? 4 : stage != null && stage.Cleared ? 2
            : !dropFloor[StashSelected ? (int)DuelSlot.Back : Mathf.Clamp(viewSel, 0, ArmRows - 1)] ? 3 : 0;

        /// <summary>付け外し・分解ができない(撃墜中か、動ける・ガード中でない)。</summary>
        static bool Frozen(int block) => block is 1 or 4;

        /// <summary>所持品を付けるときに押すボタンの番号(ProtoInput.MenuSlotPressed。手・肩はその所のボタン、背中は ×)。</summary>
        static int MenuSlotBtn(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => 0,
            DuelSlot.LeftHand => 1,
            DuelSlot.RightShoulder => 2,
            DuelSlot.LeftShoulder => 3,
            _ => 4,
        };

        void EquipViewInput(DuelArms arms, bool up, bool down)
        {
            int rows = ArmRows + arms.Stash.Count;
            viewSel = Mathf.Clamp(viewSel, 0, rows - 1);   // 所持品が減った
            int d = (down ? 1 : 0) - (up ? 1 : 0);
            if (d != 0)
            {
                viewSel = (viewSel + d + rows) % rows;
                dropArmed = false;
            }
            var w = SelectedWeapon(arms);
            if (w == null)
            {
                dropArmed = false;
                return;
            }
            bool stashRow = StashSelected;
            int si = StashSel;
            var slot = (DuelSlot)Mathf.Clamp(viewSel, 0, ArmRows - 1);
            int block = DropBlock();
            if (block == 4 && (ProtoInput.MenuDiscard || ProtoInput.MenuStore || ProtoInput.MenuLeft || ProtoInput.MenuRight))
                Log("攻撃・回避・怯みなどの途中は、付け外し・処分できない(閉じて動けるようになってから)");
            if (dropArmed)
            {
                // 処分:もう一度 □ で捨てる / ← で弾 / → で回復
                if (ProtoInput.MenuDiscard)
                {
                    if (block != 0) return;
                    dropArmed = false;
                    if (stashRow) arms.DropStash(si);
                    else arms.Drop(slot);
                }
                else if (ProtoInput.MenuLeft || ProtoInput.MenuRight)
                {
                    // 分解は十字キー・矢印キー(A/D)だけ(左スティックでは受けない。ProtoInput.MenuLeft)
                    bool heal = !ProtoInput.MenuLeft;
                    if (Frozen(block)) return;
                    // 弾が上限・HP が満タンなら分解しない(DuelArms が知らせる。処分の選択はそのまま)
                    bool done = stashRow ? arms.SalvageStash(si, heal) : arms.SalvageSlot(slot, heal);
                    if (done) dropArmed = false;
                }
                return;
            }
            if (ProtoInput.MenuDiscard)
            {
                dropArmed = true;   // 1回目:処分を選ぶ
                return;
            }
            if (!stashRow)
            {
                if (ProtoInput.MenuStore) arms.StoreSlot(slot);
                return;
            }
            // 所持品:付ける所のボタンで付ける(R1/L1・R2/L2・背中は ×)。付けたら、付けた所の行を選ぶ
            // (外した武装は同じ番号の所持品に入れ替わるが、選択を所持品に残すと、もう一度押したときに付け直してしまうので)
            foreach (var s in DuelWeapon.SlotsFor(w.kind))
            {
                if (!ProtoInput.MenuSlotPressed(MenuSlotBtn(s))) continue;
                if (arms.EquipFromStash(si, s)) viewSel = (int)(w.IsTwoHand ? DuelSlot.RightHand : s);
                return;
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
            if (player.Arms != null && !equipOpen)
            {
                DrawAimFrames(player.Arms, cam);   // 照準の枠は、ほかの表示より奥に描く
                DrawHitMark(player.Arms);
            }

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
            // ENが0以下でビーム・レーザーを撃てなかった直後は、文字を赤く光らせる(弾切れの表示と同じ合図)
            if (player.Arms != null && Time.time - player.Arms.NoEnergyAt < 1f) GUI.color = new Color(1f, 0.55f, 0.45f);
            GUI.Label(new Rect(x + 308f, y - 7f, 300f, 24f),
                (minus ? $"EN {Mathf.FloorToInt(player.Energy)}(回復待ち)" : $"EN {Mathf.FloorToInt(player.Energy)}") + (player.BoostOn ? "  ブースト" : ""), small);
            GUI.color = Color.white;
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
                // 両手の銃・砲の溜め撃ち:照準の少し下に溜まり具合(溜まったら白)
                float c = player.Arms.Charge01;
                Vector2 cc = CursorScreen(player.Arms);
                if (c > 0f && !equipOpen) Bar(new Rect(cc.x - 60f, cc.y + 46f, 120f, 6f), c, c >= 1f ? Color.white : new Color(1f, 0.8f, 0.4f));
            }

            // ロックオンの印:敵の胸の上のほうに、目立ちすぎない細い白の十字
            var e = player.Target;
            if (e != null && !e.Dead && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(e.LockPoint);
                // 画面の外(敵が背中側へ回り、カメラが上限の速さで向き直っている途中など):画面の端に、その方向を示す小さな印(見失わないように)
                if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) DrawOffscreenLock(cam, e.LockPoint);
                else
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
            DrawLockCorners(cam);   // ロックオンのオン・オフの照準枠

            // 上の中央:ロックオンしている敵のHPと怯みゲージ(ステージでは、ロックオンしていなくてもボスは出す)
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
            // 真ん中の十字:4本の短い線の開きが、今のばらつき(spread + 反動の広がり)。連射すると開き、やめると閉じる
            if (spread >= 0f)
            {
                float g = Mathf.Clamp(spread * k, 3f, SH * 0.3f);
                var sc = FrameIdle;
                sc.a = spreadHot ? 0.85f : 0.4f;
                GUI.color = sc;
                const float len = 7f, th = 2f;
                var t = Texture2D.whiteTexture;
                GUI.DrawTexture(new Rect(cx - g - len, cy - th / 2f, len, th), t);
                GUI.DrawTexture(new Rect(cx + g, cy - th / 2f, len, th), t);
                GUI.DrawTexture(new Rect(cx - th / 2f, cy - g - len, th, len), t);
                GUI.DrawTexture(new Rect(cx - th / 2f, cy + g, th, len), t);
            }
            var mc = FrameLocked;
            mc.a = 0.95f;
            foreach (var e in arms.Marks)
            {
                if (e == null || e.Dead) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.Chest);
                if (sp.z <= 0f) continue;
                float x = sp.x / uiScale, y = SH - sp.y / uiScale;
                Brackets(new Rect(x - 11f, y - 11f, 22f, 22f), 6f, 2f, mc);
            }
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
            if (player.Dead || equipOpen)
            {
                lockOnAt = lockOffAt = -1f;
                lockCornersShown = false;
                lockFollow = 0f;
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
                    lockFollow = 0f;
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

            // 画面の縁寄りの枠:縦は上の敵のゲージ・右上の知らせと、下の自機のゲージ・武装の欄の手前まで(低い画面では縮める。なめらかに)
            float bottom = Mathf.Min(leftTop, player.Arms != null ? ArmsTop : SH) - 12f;
            float wantH = Mathf.Max(53f, Mathf.Min(LockCornerHalfH, bottom - SH * 0.5f));
            lockHalfH = dt <= 0f ? wantH : Mathf.MoveTowards(lockHalfH, wantH, 267f * dt);
            float baseW = Mathf.Max(80f, Mathf.Min(LockCornerHalfW, SW * 0.5f - LockCornerEdge));
            float hw = baseW + lockOffset, hh = lockHalfH + lockOffset * (lockHalfH / baseW);
            hw = Mathf.Lerp(hw, lockFollowHalf.x + lockOffset * 0.35f, e);
            hh = Mathf.Lerp(hh, lockFollowHalf.y + lockOffset * 0.35f, e);
            Vector2 c = new Vector2(SW * 0.5f, SH * 0.5f) + lockFollowCenter * e;
            var col = lockInk;
            if (e > 0f) col.a = Mathf.Min(1f, col.a * Mathf.Lerp(1f, LockFollowAlpha / LockRestAlpha, e));   // 敵に重なる間は薄く
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
            if (stage != null && !DuelSenses.ClearSight(player.LockPoint, target.LockPoint)) return false;
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

        // 武装の表示の文字列は、中身が変わったときだけ作り直す(OnGUI は1フレームに何度も呼ばれる)
        /// <summary>付ける所の数(DuelSlot の数。装備の確認の行数も同じ)。</summary>
        const int ArmRows = 5;
        // 弾・HP は分解を選んでいる間だけ値で、ほかは上限・満タンかどうかだけ入れる(撃つ・被弾するたびに作り直さないように)
        (DuelLoot loot, DuelLoot choosing, DuelLoot breaking, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev, int ammo, int hp, int weight) pickupKey;
        // 付け先を選ぶ表示のグラフは、武装・比べる相手・機器が変わったときだけ作り直す
        (DuelLoot loot, DuelLoot choosing, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev) pickChartKey;
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

        const float CellW = 200f, CellH = 44f, CellGap = 8f, BackRowH = 24f, ArmsMargin = 20f, WeightRowH = 20f;
        /// <summary>右下の武装の欄の上端(重さの1行を含む。拾う案内をこれより上に出す)。</summary>
        float ArmsTop => SH - ArmsMargin - BackRowH - 6f - CellH * 2f - CellGap - WeightRowH;
        /// <summary>右下の武装の欄の左端(操作説明をこれより左で折り返す)。</summary>
        float ArmsLeft => SW - ArmsMargin - CellW * 2f - CellGap;

        /// <summary>セルの右上に出す状態。</summary>
        enum CellState { None, Ammo, Energy, Reload, Loading, Charging, Charged, TwoHand, Starved, NoEnergy }

        // セルの文字列(添字は DuelSlot)。名前・ボタンは武装か機器が、状態は武装・状態・残り(弾数か0.1秒単位)が変わったときだけ作り直す
        readonly string[] cellBtn = new string[ArmRows], cellName = new string[ArmRows], cellStatus = new string[ArmRows];
        readonly (DuelWeapon w, ProtoInput.Device dev, bool set)[] cellKey = new (DuelWeapon, ProtoInput.Device, bool)[ArmRows];
        readonly (DuelWeapon w, CellState st, int val, bool set)[] cellStatusKey = new (DuelWeapon, CellState, int, bool)[ArmRows];

        static readonly Color GaugeOrange = new(1f, 0.6f, 0.15f, 0.35f), StatusOrange = new(1f, 0.75f, 0.4f);

        /// <summary>
        /// 右下:付けている武装を肩ボタンの並び(左=L・右=R、上の段=肩・下の段=手)の 2×2 のセルで、その下に背中の1行。
        /// セルにはボタン名・レア度の色の四角・武装名。右上に状態:銃は「弾 12/20」(残りが弾倉の1/4以下で赤っぽく)、エネルギー武器は「EN 20/秒」(ENが0以下で撃てない間は「EN切れ」)。
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

            // 一番下の右:共通の弾(足りなくて撃てなかった直後は赤く光る。残りが上限の2割以下で橙)
            var r = arms.Rules;
            int ammo = Mathf.FloorToInt(arms.Ammo + 0.0001f);
            if (ammoKey != (ammo, true))
            {
                ammoKey = (ammo, true);
                ammoText = $"弾 {ammo}/{r.ammoMax:0}";
            }
            var ar = new Rect(x1, yBack, CellW, BackRowH);
            bool starve = Time.time - arms.NoAmmoAt < 1f;
            Chip(ar, starve ? new Color(0.6f, 0.1f, 0.08f, 0.6f) : new Color(0f, 0f, 0f, 0.5f));
            Chip(new Rect(ar.x + 8f, ar.y + 9f, 10f, 6f), new Color(1f, 0.85f, 0.35f));
            GUI.color = starve ? new Color(1f, 0.55f, 0.45f) : ammo <= r.ammoMax * 0.2f ? StatusOrange : Color.white;
            GUI.Label(new Rect(ar.x + 24f, ar.y + 2f, CellW - 30f, 20f), ammoText, cellBack);

            // 武装の欄の上:所持品の数と重さ・推力(重すぎて持てなかった直後は赤く光る。上限の9割を超えたら橙)
            float total = arms.TotalWeight, max = arms.CarryMax;
            var wk = (arms.Stash.Count, Mathf.RoundToInt(total), Mathf.RoundToInt(max), Mathf.RoundToInt(arms.Mobility * 100f));
            if (weightKey != wk || weightText == null)
            {
                weightKey = wk;
                weightText = $"所持 {wk.Item1}   重さ {wk.Item2}/{wk.Item3}   推力 ×{wk.Item4 / 100f:0.00}";   // 推力:ブースト移動・回避・上昇(歩き・走りには効かない)
            }
            bool heavyFlash = Time.unscaledTime - arms.TooHeavyAt < 1.2f;
            GUI.color = heavyFlash ? new Color(1f, 0.45f, 0.35f) : total > max * 0.9f ? StatusOrange : new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(x0, yRow2 - WeightRowH, CellW * 2f + CellGap - 4f, 18f), weightText, cellTinyRight);
            GUI.color = Color.white;
        }

        (int, bool) ammoKey;
        string ammoText, weightText;
        (int, int, int, int) weightKey;

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
                if (arms.Starved(slot)) st = CellState.Starved;   // 共通の弾が足りなくて撃てない
                else if (arms.EnergyStarved(slot)) st = CellState.NoEnergy;   // ENが0以下で撃てない(エネルギー武器)
                else if (w.IsGun)
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
                    else st = CellState.Energy;   // 弾倉のない銃 = エネルギー武器(使うEN)
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
                if (arms.AltStarved && c <= 0f) st = CellState.Starved;
                else if (arms.AltEnergyStarved) st = CellState.NoEnergy;
                else if (c > 0f)
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
                        CellState.Energy => w.UsesEnergy ? w.EnText : "弾数∞",
                        CellState.NoEnergy => "EN切れ",
                        CellState.Reload => $"リロード {val / 10f:0.0}秒",
                        CellState.Loading => $"装填 {val / 10f:0.0}秒",
                        CellState.Charging => "溜め中",
                        CellState.Charged => "溜まった・離して撃つ",
                        CellState.Starved => w.IsGun && w.magazine > 0 && !twoL ? "弾 0 ・弾切れ" : "弾切れ",
                        _ => "両手持ち",
                    };
                }
                GUI.color = st switch
                {
                    CellState.Ammo => val <= w.magazine / 4 ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.87f, 0.9f),
                    CellState.Energy => new Color(1f, 0.6f, 0.95f),
                    CellState.Reload or CellState.Loading => StatusOrange,
                    CellState.Charging => new Color(1f, 0.85f, 0.5f),
                    CellState.Charged => Color.white,
                    CellState.Starved or CellState.NoEnergy => new Color(1f, 0.45f, 0.35f),
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
            var loot = arms.Choosing != null ? arms.Choosing : arms.Breaking != null ? arms.Breaking : arms.Nearby;
            if (loot == null || player.Dead) return;
            var w = loot.Weapon;
            var to = DuelWeapon.SlotsFor(w.kind);
            var a = arms.Get(to[0]);
            var b = to.Length > 1 ? arms.Get(to[1]) : null;
            bool ammoFull = arms.SalvageUseless(false), hpFull = arms.SalvageUseless(true);
            bool breaking = arms.Breaking != null;
            var key = (loot, arms.Choosing, arms.Breaking, a, b, ProtoInput.Current,
                       breaking ? Mathf.FloorToInt(arms.Ammo + 0.0001f) : ammoFull ? -2 : -1, breaking ? Mathf.CeilToInt(player.HP) : hpFull ? -2 : -1,
                       arms.Choosing == null && !breaking ? Mathf.RoundToInt(arms.TotalWeight * 10f) : 0);
            if (pickupKey != key || pickupLines[0] == null)
            {
                pickupKey = key;
                string col = ColorUtility.ToHtmlStringRGB(w.Color);
                string name = "<color=#" + col + ">" + w.name + "〈" + w.RarityName + "〉</color>";
                string mode = w.FireMode.Length > 0 ? "・" + w.FireMode : "";
                string equip = "[" + ProtoInput.L(ProtoInput.Btn.EquipNow) + "]";
                string keepBtn = "[" + ProtoInput.L(ProtoInput.Btn.Stash) + "]", breakBtn = "[" + ProtoInput.L(ProtoInput.Btn.Break) + "]";
                bool curTwo = a is { IsTwoHand: true };   // 今、両手持ちを付けている(右手と左手に同じ武装)
                int salAmmo = arms.SalvageAmmo(w), salHeal = arms.SalvageHeal(w);
                if (arms.Breaking != null)
                {
                    // 分解を選ぶ:十字上で弾、十字下で回復、十字右でやめる
                    var rules = arms.Rules;
                    float a1 = Mathf.Min(rules.ammoMax, arms.Ammo + salAmmo), h1 = Mathf.Min(player.MaxHP, player.HP + salHeal);
                    // 弾が上限・HP が満タンの選択肢は赤く(押しても分解しない。武装だけ消えないように)
                    string ammoOpt = ammoFull
                        ? "<color=#FF7A6A>" + breakBtn + " 弾は上限(" + rules.ammoMax.ToString("0") + ")</color>"
                        : breakBtn + " 弾にする +" + salAmmo + "(弾 " + Mathf.FloorToInt(arms.Ammo + 0.0001f) + "→" + Mathf.FloorToInt(a1 + 0.0001f) + "/" + rules.ammoMax.ToString("0") + ")";
                    string healOpt = hpFull
                        ? "<color=#FF7A6A>" + keepBtn + " HP は満タン</color>"
                        : keepBtn + " 回復にする +" + salHeal + "(HP " + Mathf.CeilToInt(player.HP) + "→" + Mathf.CeilToInt(h1) + "/" + player.MaxHP.ToString("0") + ")";
                    pickupLines[0] = name + " を分解する?   " + ammoOpt + "    " + healOpt + "    " + equip + " やめる";
                    pickupLines[1] = "<color=#AAAAAA>分解した武装はなくなる(弾は上限まで・回復は HP の最大まで)</color>";
                    pickupLineCount = 2;
                }
                else if (w.IsTwoHand)
                {
                    // 両手持ち:付け先を選ぶ表示で R1/L1 のどちらを押しても両手に付き、今の右手・左手の武装は両方とも置かれる
                    string r1 = SlotButton(DuelSlot.RightHand), l1 = SlotButton(DuelSlot.LeftHand);
                    var cmp = DuelWeapon.SameFamily(a, w) ? a : DuelWeapon.SameFamily(b, w) ? b : null;
                    if (arms.Choosing == null)
                    {
                        pickupLines[0] = "拾える:" + name + "(" + DuelWeapon.KindName(w.kind) + "・両手持ち)  ― " + w.feel;
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
                        pickupLines[0] = "拾える:" + name + "(" + DuelWeapon.KindName(w.kind) + ")  ― " + w.feel + "(今:" + CurName(a) + ")";
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
                    pickupLines[0] = "拾える:" + name + "(" + DuelWeapon.KindName(w.kind) + mode + ")  ― " + w.feel;
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
                if (arms.Choosing == null && arms.Breaking == null && pickupLineCount < pickupLines.Length)
                {
                    // 拾う案内の操作:付ける(付け先を選ぶ)/ 持つ(所持品へ。重さの上限を超えるなら持てない)/ 分解(弾か回復を選ぶ)
                    float after = arms.WeightIfStashed(w), max = arms.CarryMax;
                    string keep = after <= max + 0.001f
                        ? keepBtn + " 持つ(重さ " + arms.TotalWeight.ToString("0") + "→" + after.ToString("0") + "/" + max.ToString("0") + ")"
                        : "<color=#FF7A6A>" + keepBtn + " 持つ:重すぎる(" + after.ToString("0") + "/" + max.ToString("0") + ")</color>";
                    string sal = ammoFull && hpFull ? "<color=#FF7A6A>" + breakBtn + " 分解(弾は上限・HP は満タン)</color>"
                        : breakBtn + " 分解(" + (ammoFull ? "<color=#FF7A6A>弾は上限</color>" : "弾 +" + salAmmo) + " / " + (hpFull ? "<color=#FF7A6A>HP は満タン</color>" : "回復 +" + salHeal) + ")";
                    pickupLines[pickupLineCount++] = equip + " 付ける    " + keep + "    " + sal;
                }
                var ck = (loot, arms.Choosing, a, b, ProtoInput.Current);
                if (pickChartKey != ck)
                {
                    pickChartKey = ck;
                    BuildPickCharts(arms.Choosing != null ? w : null, to, a, b, curTwo);
                }
            }
            // 右下の武装の欄と左下の HP・操作説明に重ならない高さ(行が多いときは上へずらす)
            float h = 6f + 26f * pickupLineCount, wd = 1200f, bx = (SW - wd) / 2f;
            float by = Mathf.Min(SH * 0.56f, Mathf.Min(ArmsTop, leftTop) - 8f - h);
            GUI.color = new Color(0f, 0f, 0f, arms.Choosing != null || arms.Breaking != null ? 0.7f : 0.55f);
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
            // ロックオンのボタンはオン・オフの切り替え(電脳斬禅と同じ)。狙う敵の切り替え:撃っていないときに右スティック(マウスは左右に振る)を左右に弾く。
            // R3 / Tab でも切り替わる(オフのときに押すとオンに戻る)。ロックオン中のカメラは敵を追い続け、右スティック・マウスでは回らない
            string lockSwitch = kb ? "  ロックオン中にマウスを左右に振る/Tab 狙う敵を変更" : "  ロックオン中に右スティックを左右に弾く/R3 狙う敵を変更";
            // 銃・砲のボタンを押している間(と撃ってから少しの間)は、右スティック・マウスで画面の上の照準を動かす(カメラは回さない)
            string aimMove = kb ? "  撃つボタンを押している間 マウス 照準を動かす" : "  撃つボタンを押している間 右スティック 照準を動かす";
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
                sb.Append(aimMove);
                sb.Append("  ").Append(B(ProtoInput.Btn.R2)).Append('/').Append(B(ProtoInput.Btn.L2)).Append(" 右肩/左肩");
                sb.Append("  ").Append(B(ProtoInput.Btn.Dodge)).Append(" 回避");
                sb.Append("  ").Append(B(ProtoInput.Btn.Fly)).Append("長押し 飛行");
                sb.Append("  ").Append(B(ProtoInput.Btn.Boost)).Append(" ブースト");
                sb.Append("  ").Append(B(ProtoInput.Btn.EquipNow)).Append(" 付ける→付ける所のボタン(背中は ").Append(B(ProtoInput.Btn.Fly)).Append(')');
                sb.Append("  ").Append(B(ProtoInput.Btn.Stash)).Append(" 持つ  ").Append(B(ProtoInput.Btn.Break)).Append(" 分解(弾か回復)");
                sb.Append("  ").Append(B(ProtoInput.Btn.Lock)).Append(" ロックオン切替").Append(lockSwitch);
                sb.Append("  ").Append(B(ProtoInput.Btn.EquipView)).Append(" 装備・所持品");
                helpText = sb.ToString();
                return helpText;
            }
            sb.Append(B(ProtoInput.Btn.R1)).Append(" 通常攻撃(回避の後で回避斬り)");
            sb.Append("  ").Append(B(ProtoInput.Btn.Aim)).Append('+').Append(B(ProtoInput.Btn.R1)).Append(" 強攻撃");
            sb.Append("  ").Append(B(ProtoInput.Btn.Dodge)).Append(" 回避");
            sb.Append("  ").Append(B(ProtoInput.Btn.Fly)).Append("長押し 飛行");
            sb.Append("  ").Append(B(ProtoInput.Btn.Boost)).Append(" ブースト切替");
            sb.Append("  ").Append(B(ProtoInput.Btn.L1)).Append(" ガード");
            sb.Append("  ").Append(B(ProtoInput.Btn.Lock)).Append(" ロックオン切替");
            sb.Append(lockSwitch);
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
        string viewFoot, viewDropHint;

        /// <summary>
        /// 画面の中央:5つの所それぞれの武装(名前・レア度の色・種類・一言)、主な性能(拾うときの比較と同じ値)、使うボタン。空きは「なし」。
        /// 選んでいる所は明るい帯と左の縦線で目立たせ、右上に操作(△ 外して持つ・□ 処分)を出す(処分の途中は赤い帯と、捨てる・弾・回復の選び方)。
        /// 右に所持品の欄(重さ・弾・所持品の一覧)。下に選んでいる武装の性能のグラフ。
        /// </summary>
        void DrawEquipView(DuelArms arms)
        {
            var dev = ProtoInput.Current;
            int block = DropBlock();
            bool changed = !viewBuilt || viewKeyDev != dev || viewKeyBlock != block;
            for (int i = 0; i < ArmRows && !changed; i++) changed = viewKeyW[i] != arms.Get((DuelSlot)i);
            if (changed) BuildEquipView(arms, dev, block);

            const float wd = 900f, sideW = 330f, gapX = 10f, rowH = 76f, headW = 130f, pad = 18f, titleH = 46f, footH = 34f;
            float h = pad + titleH + rowH * ArmRows + footH + 6f;
            // 右の所持品の欄と、下の性能のグラフの枠(EquipChartH)を足した全体を、画面の中央に
            float bx = Mathf.Max(4f, (SW - wd - gapX - sideW) / 2f), by = Mathf.Max(4f, (SH - h - EquipChartGap - EquipChartH) / 2f);
            // 画面全体を少し暗くし、その上にパネル
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "装備", viewTitleStyle);

            float y = by + pad + titleH;
            float tx = bx + pad + headW, tw = wd - pad * 2f - headW;
            // 両手持ちは右手・左手の2行で1つ:どちらを選んでも2行とも選んでいる帯にし、処分の選択も2行とも
            bool two = arms.TwoHanded;
            bool onSlots = !StashSelected;
            bool selTwo = onSlots && two && viewSel <= (int)DuelSlot.LeftHand;
            for (int i = 0; i < ArmRows; i++)
            {
                var w = arms.Get((DuelSlot)i);
                bool sel = onSlots && (i == viewSel || (selTwo && i <= (int)DuelSlot.LeftHand));
                bool armed = sel && dropArmed && w != null;
                if (sel)
                {
                    // 選んでいる所:帯と左の縦線(処分の途中は赤)
                    GUI.color = armed ? new Color(0.9f, 0.25f, 0.2f, 0.28f) : new Color(1f, 1f, 1f, 0.09f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, wd - pad * 2f + 16f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = armed ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.8f, 1f);
                    GUI.DrawTexture(new Rect(bx + pad - 8f, y + 1f, 4f, rowH - 1f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (w != null && !armed && i == viewSel) GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewDropHint, richRight);
                }
                // 区切りの細い線(両手持ちの右手と左手の間は引かない)
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                if (!(two && i == (int)DuelSlot.LeftHand)) GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(bx + pad, y + 6f, headW, 26f), viewHead[i], viewHeadStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewName[i], richSmall);
                if (w != null) GUI.Label(new Rect(tx, y + 29f, tw, 22f), viewStats[i], richSmall);
                // 3行目:使うボタン(処分の途中は、その選び方に替える)
                GUI.color = armed ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(tx, y + (w != null ? 51f : 29f), tw, 22f), armed ? DisposeLine(arms, w, block, selTwo) : viewUse[i], richSmall);
                GUI.color = Color.white;
                y += rowH;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(bx, y + 6f, wd, 24f), viewFoot, center);
            GUI.color = Color.white;

            DrawStashPanel(arms, bx + wd + gapX, by, sideW, h, block);
            DrawEquipChart(arms, bx, by + h + EquipChartGap, wd + gapX + sideW);
        }

        // 処分の1行(捨てる・弾にする・回復にする・やめる)。武装・捨てられない理由・機器が変わったときだけ作る
        (DuelWeapon w, int block, bool two, ProtoInput.Device dev, bool ammoFull, bool hpFull) disposeKey;
        string disposeText;

        /// <summary>処分の選び方:「処分  [□] 床に捨てる  [←] 弾にする +24  [→] 回復にする +13  [○] やめる」(捨てられない・回復できないときは灰色で理由)。</summary>
        string DisposeLine(DuelArms arms, DuelWeapon w, int block, bool two)
        {
            bool ammoFull = arms.SalvageUseless(false), hpFull = arms.SalvageUseless(true);
            var key = (w, block, two, ProtoInput.Current, ammoFull, hpFull);
            if (disposeText != null && disposeKey == key) return disposeText;
            disposeKey = key;
            bool kb = ProtoInput.Current == ProtoInput.Device.Keyboard;
            string drop = ProtoInput.L(ProtoInput.Btn.Discard), back = kb ? "Backspace" : ProtoInput.L(ProtoInput.Btn.Back);
            string left = ProtoInput.L(ProtoInput.Btn.MenuLeft), right = ProtoInput.L(ProtoInput.Btn.MenuRight);
            if (Frozen(block))
            {
                string frozen = block == 1 ? "撃墜中は処分できない" : "攻撃・回避・怯みなどの途中は処分できない";
                disposeText = $"<color=#FF8A70><b>処分</b></color>  <color=#888888>{frozen}</color>   [{back}] やめる";
                return disposeText;
            }
            string why = block switch { 2 => "ボスの後は捨てられない", 3 => "真下に床がない", _ => "" };
            string dropPart = block == 0 ? $"[{drop}] {(two ? "両手の武装を" : "")}床に捨てる" : $"<color=#888888>[{drop}] {why}</color>";
            // 弾が上限・HP が満タンなら、分解しても何も得ないので赤く「上限」「満タン」(押しても分解しない)
            string ammoPart = ammoFull ? $"<color=#FF7A6A>[{left}] 弾は上限</color>" : $"[{left}] 弾にする +{arms.SalvageAmmo(w)}";
            string healPart = hpFull ? $"<color=#FF7A6A>[{right}] HP は満タン</color>" : $"[{right}] 回復にする +{arms.SalvageHeal(w)}";
            disposeText = $"<color=#FF8A70><b>処分</b></color>  {dropPart}   {ammoPart}   {healPart}   [{back}] やめる";
            return disposeText;
        }

        // 所持品の欄の文字列(所持品の中身・機器が変わったときだけ作る)
        readonly List<DuelArms.Carried> stashRowKey = new();
        readonly List<string> stashRowName = new(), stashRowWeight = new();
        ProtoInput.Device stashRowDev;
        (DuelWeapon w, bool armed, int block, ProtoInput.Device dev) stashHintKey;
        string stashHint, stashHead, stashTitle, stashEmpty, stashRange;
        int stashTitleN = -1;
        ProtoInput.Device stashEmptyDev;
        (int, int, int) stashRangeKey;
        (int n, int total, int max, int mob, int ammo, int ammoMax) stashHeadKey;

        /// <summary>
        /// 右の所持品の欄:重さ(上限)と推力・共通の弾、その下に所持品の一覧(レア度の色・名前・種類・重さ)。選んでいる所持品は帯と縦線。
        /// 一番下に、選んでいる所持品の操作(付ける所のボタン・処分)か、処分の選び方。
        /// </summary>
        void DrawStashPanel(DuelArms arms, float x, float y, float w, float h, int block)
        {
            const float pad = 14f, rowH = 28f, hintH = 92f, listTop = 78f;
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var stash = arms.Stash;
            var r = arms.Rules;
            var hk = (stash.Count, Mathf.RoundToInt(arms.TotalWeight), Mathf.RoundToInt(arms.CarryMax), Mathf.RoundToInt(arms.Mobility * 100f),
                      Mathf.FloorToInt(arms.Ammo + 0.0001f), Mathf.RoundToInt(r.ammoMax));
            if (stashHead == null || stashHeadKey != hk)
            {
                stashHeadKey = hk;
                stashHead = $"重さ {hk.Item2}/{hk.Item3}   推力 ×{hk.Item4 / 100f:0.00}\n弾 {hk.Item5}/{hk.Item6}";
            }
            if (stashTitle == null || stashTitleN != stash.Count)
            {
                stashTitleN = stash.Count;
                stashTitle = $"所持品  {stash.Count}";
            }
            GUI.Label(new Rect(x + pad, y + 10f, w - pad * 2f, 26f), stashTitle, viewHeadStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(x + pad, y + 36f, w - pad * 2f, 40f), stashHead, small);
            GUI.color = Color.white;

            // 一覧の文字列
            bool rebuild = stashRowKey.Count != stash.Count || stashRowDev != ProtoInput.Current;
            for (int i = 0; i < stash.Count && !rebuild; i++) rebuild = stashRowKey[i] != stash[i];
            if (rebuild)
            {
                stashRowKey.Clear();
                stashRowName.Clear();
                stashRowWeight.Clear();
                stashRowDev = ProtoInput.Current;
                foreach (var c in stash)
                {
                    stashRowKey.Add(c);
                    string kind = DuelWeapon.KindName(c.w.kind);
                    string ammo = c.w.IsGun && c.w.magazine > 0 && c.memo.mag >= 0 && c.memo.mag < c.w.magazine ? $" 弾{c.memo.mag}/{c.w.magazine}" : "";
                    stashRowName.Add($"<color=#{ColorUtility.ToHtmlStringRGB(c.w.Color)}>{c.w.name}</color> <color=#9A9A9A>{kind}{ammo}</color>");
                    stashRowWeight.Add($"重 {c.w.weight:0.#}");
                }
            }

            float listY = y + listTop, listH = h - listTop - hintH;
            int visible = Mathf.Max(1, Mathf.FloorToInt(listH / rowH));
            int sel = StashSelected ? StashSel : -1;
            if (stash.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                if (stashEmpty == null || stashEmptyDev != ProtoInput.Current)
                {
                    stashEmptyDev = ProtoInput.Current;
                    stashEmpty = $"なし ― 落ちている武装の近くで {ProtoInput.L(ProtoInput.Btn.Stash)} で持つ。装備の行で {ProtoInput.L(ProtoInput.Btn.PickUp)} で外して持つ";
                }
                GUI.Label(new Rect(x + pad, listY + 4f, w - pad * 2f, 60f), stashEmpty, chartNoteStyle);
                GUI.color = Color.white;
            }
            else
            {
                // 選んでいる行が見えるように送る
                int first = Mathf.Clamp((sel >= 0 ? sel : 0) - visible / 2, 0, Mathf.Max(0, stash.Count - visible));
                for (int k = 0; k < visible && first + k < stash.Count; k++)
                {
                    int i = first + k;
                    float ry = listY + k * rowH;
                    bool isSel = i == sel;
                    if (isSel)
                    {
                        bool armed = dropArmed;
                        GUI.color = armed ? new Color(0.9f, 0.25f, 0.2f, 0.28f) : new Color(1f, 1f, 1f, 0.09f);
                        GUI.DrawTexture(new Rect(x + 6f, ry, w - 12f, rowH - 2f), Texture2D.whiteTexture);
                        GUI.color = armed ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.8f, 1f);
                        GUI.DrawTexture(new Rect(x + 6f, ry, 4f, rowH - 2f), Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }
                    Chip(new Rect(x + pad + 2f, ry + 9f, 10f, 10f), stash[i].w.Color);
                    GUI.Label(new Rect(x + pad + 18f, ry + 3f, w - pad * 2f - 70f, 22f), stashRowName[i], richSmall);
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                    GUI.Label(new Rect(x + w - pad - 60f, ry + 3f, 60f, 22f), stashRowWeight[i], rightSmall);
                    GUI.color = Color.white;
                }
                if (stash.Count > visible)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    var rk = (first, Mathf.Min(stash.Count, first + visible), stash.Count);
                    if (stashRange == null || stashRangeKey != rk)
                    {
                        stashRangeKey = rk;
                        stashRange = $"{rk.Item1 + 1}〜{rk.Item2} / {rk.Item3}";
                    }
                    GUI.Label(new Rect(x + pad, listY + visible * rowH - 4f, w - pad * 2f, 18f), stashRange, cellTinyRight);
                    GUI.color = Color.white;
                }
            }

            // 一番下:選んでいる所持品の操作
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(x + pad, y + h - hintH, w - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var sw = sel >= 0 && sel < stash.Count ? stash[sel].w : null;
            var key = (sw, dropArmed, block, ProtoInput.Current);
            if (stashHint == null || stashHintKey != key)
            {
                stashHintKey = key;
                if (sw == null) stashHint = $"<color=#999999>{(ProtoInput.Current == ProtoInput.Device.Keyboard ? "↓/S" : "↓")} で所持品を選ぶ</color>";
                else if (dropArmed) stashHint = DisposeLine(arms, sw, block, false);
                else
                {
                    var sb = new System.Text.StringBuilder();
                    if (block == 1) sb.Append("<color=#888888>撃墜中は付けられない</color>");
                    else if (block == 4) sb.Append("<color=#888888>攻撃・回避・怯みなどの途中は付けられない</color>");
                    else if (sw.IsTwoHand)
                        sb.Append('[').Append(SlotButton(DuelSlot.RightHand)).Append("]/[").Append(SlotButton(DuelSlot.LeftHand)).Append("] 両手に付ける");
                    else
                    {
                        var to = DuelWeapon.SlotsFor(sw.kind);
                        for (int k = 0; k < to.Length; k++)
                        {
                            if (k > 0) sb.Append(" / ");
                            string b = to[k] == DuelSlot.Back ? ProtoInput.L(ProtoInput.Btn.Fly) : SlotButton(to[k]);
                            sb.Append('[').Append(b).Append("] ").Append(DuelWeapon.SlotName(to[k]));
                        }
                        sb.Append(" に付ける");
                    }
                    if (!Frozen(block)) sb.Append("<color=#AAAAAA>(今の武装は所持品へ)</color>");
                    sb.Append("\n[").Append(ProtoInput.L(ProtoInput.Btn.Discard)).Append("] 処分(捨てる・分解して弾か回復)");
                    stashHint = sb.ToString();
                }
            }
            GUI.Label(new Rect(x + pad, y + h - hintH + 6f, w - pad * 2f, hintH - 10f), stashHint, chartNoteStyle);
        }

        const float EquipChartH = 196f, EquipChartGap = 8f;
        // 装備の確認のグラフ:選んでいる行・その武装・比べる相手が変わったときだけ作り直す
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
        /// 装備の確認の下の枠:選んでいる武装のレーダーチャートと帯グラフ。
        /// 付けている所なら、比べる相手は同じ仲間のもう片方の所(右手⇔左手、右肩⇔左肩)。相手が空き・背中・両手持ちなら単独で出す。
        /// 所持品なら、付けられる所の今の武装(同じ仲間がいればそれ、なければ1つ目の所)と比べる。
        /// 相手の仲間が違えば(剣と盾など)レーダーは出さず、帯グラフも選んでいる武装だけ。右の欄に凡例と読み方。選んでいる所が空きなら、その旨だけ。
        /// </summary>
        void DrawEquipChart(DuelArms arms, float bx, float y, float wd)
        {
            var w = SelectedWeapon(arms);
            DuelWeapon other = null;
            string me = "", them = "", why = "";
            if (w != null && StashSelected)
            {
                var to = DuelWeapon.SlotsFor(w.kind);
                var os = to[0];
                foreach (var s in to)
                    if (DuelWeapon.SameFamily(arms.Get(s), w)) { os = s; break; }
                other = arms.Get(os);
                me = "所持品";
                them = other is { IsTwoHand: true } && os is DuelSlot.RightHand or DuelSlot.LeftHand ? "両手" : DuelWeapon.SlotName(os);
                why = other == null ? them + "は空き" : "";
            }
            else if (w != null)
            {
                var slot = (DuelSlot)viewSel;
                int ps = PartnerSlot(slot);
                other = w.IsTwoHand || ps < 0 ? null : arms.Get((DuelSlot)ps);
                me = w.IsTwoHand ? "両手" : DuelWeapon.SlotName(slot);
                them = ps >= 0 ? DuelWeapon.SlotName((DuelSlot)ps) : "";
                why = w.IsTwoHand ? "両手持ちなので、比べる相手はない"
                    : ps < 0 ? "背中は1つだけなので、比べる相手はない"
                    : other == null ? them + "は空き" : "";
            }
            var key = (viewSel, w, other);
            if (viewChartKey != key)
            {
                viewChartKey = key;
                viewChartPair = DuelRadar.Get(w, other);
                if (w != null)
                {
                    if (other != null && viewChartPair.otherDifferent)
                        why = them + "の" + other.name + "は" + DuelWeapon.KindName(other.kind) + "で、仲間が違うので比べない";
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
            viewDropHint = $"<color=#AAAAAA>{ProtoInput.L(ProtoInput.Btn.PickUp)} 外して持つ   {drop} 処分(捨てる・分解)</color>";
            string sel = kb ? "↑↓/W・S" : "↑↓";
            string note = block switch
            {
                1 => "(撃墜中は捨てられない)",
                2 => "(ボスの後は捨てられない)",
                3 => "(真下に床がないので、ここでは捨てられない)",
                4 => "(攻撃・回避・怯みなどの途中は付け外し・処分できない)",
                _ => "",
            };
            viewFoot = $"一時停止中 ― {sel} 選ぶ(下に所持品)  {drop} 処分:もう一度 {drop} 捨てる・← 弾・→ 回復{note}  {close} 閉じる";
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
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(s.name).Append(' ').Append(s.text);
            }
            if (w.IsGun)
            {
                sb.Append("   連射 ").Append((1f / Mathf.Max(0.01f, w.interval)).ToString("0.#")).Append("発/秒");
                if (w.magazine > 0) sb.Append("   リロード ").Append(w.reload.ToString("0.0")).Append("秒");
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
