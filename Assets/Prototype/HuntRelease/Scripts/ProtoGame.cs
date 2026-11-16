using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HuntProto
{
    /// <summary>Burst = 剥いだ部位をラックに溜め、Rで一気にまとう / Gradual = 剥いだらその場で装備</summary>
    public enum ProtoMode { Burst, Gradual }
    /// <summary>Drop = 倒すと付いていた武装からランダムで落とす / Harvest = 部位を斬り落として剥ぎ取る</summary>
    public enum LootRule { Drop, Harvest }
    public enum Phase { Hunt, HordeIncoming, Horde, Monster, Clear }

    /// <summary>
    /// 「狩り → 群れ → バケモノ」の1弧を回し、区切りごとの時間を記録する。
    /// F2(SHARE+□)でモード切替(再スタート)、F1(SHARE+△)で再スタート。
    /// </summary>
    public class ProtoGame : MonoBehaviour
    {
        public static ProtoGame I;
        public static ProtoMode Mode = ProtoMode.Gradual;
        static bool modeInitialized;

        [Header("機体アセット(ビルダーが設定)")]
        [Tooltip("リグ付き機体。未設定なら下の固定メッシュを使う")]
        public GameObject mechPrefab;
        public Mesh mechMesh;
        public Material[] mechMaterials;
        public float mechHeight = 9f;
        public Mesh swordMesh;
        public Material[] swordMaterials;

        [Header("部位")]
        public PartDefinition[] partPool;

        [Header("モードの初期値(F2 / SHARE+□で切替)")]
        public ProtoMode defaultMode = ProtoMode.Gradual;

        [Header("進行")]
        [Tooltip("ステージ攻略型(区画を順に制圧して進む)。切ると従来の平地の「狩り→群れ→バケモノ」")]
        public bool useStage = true;

        [Header("武装の入手")]
        [Tooltip("Drop:倒すと付いていた武装からランダムで落とす(部位破壊した部位は追加で落ちることがある) / Harvest:部位を斬り落として剥ぐ")]
        public LootRule lootRule = LootRule.Drop;
        [Tooltip("Drop のとき、群れの敵が武装を落とす確率(獲物は必ず1つ、バケモノは2つ)")]
        [Range(0f, 1f)] public float hordeDropChance = 0.7f;
        [Tooltip("Drop のとき、獲物がもう1つ武装を落とす確率")]
        [Range(0f, 1f)] public float huntExtraDropChance = 0.3f;
        [Tooltip("Drop のとき、部位破壊した部位を(通常のドロップとは別に)追加で落とす確率")]
        [Range(0f, 1f)] public float brokenExtraDropChance = 0.5f;
        [Tooltip("ステージ攻略型の通常の敵が武装を1つ落とす確率")]
        [Range(0f, 1f)] public float normalDropChance = 0.75f;
        [Tooltip("ステージ攻略型の通常の敵が部位を2つ付けている確率(残りは1つ)")]
        [Range(0f, 1f)] public float normalTwoPartChance = 0.5f;
        [Range(0f, 1f)] public float healDropNormal = 0.2f;
        [Tooltip("回復アイテムを落とす確率:群れ / 獲物 / バケモノ")]
        [Range(0f, 1f)] public float healDropHorde = 0.12f;
        [Range(0f, 1f)] public float healDropHunt = 0.3f;
        [Range(0f, 1f)] public float healDropMonster = 1f;

        [Header("構成")]
        public int huntEnemyCount = 4;
        public int partsPerHuntEnemy = 2;
        public int hordeWaves = 2;
        public int hordePerWave = 9;
        [Tooltip("群れの部位も剥ぎ取れる(使い切りの武器を戦いながら補充する)")]
        public bool hordeHarvestable = true;
        [Tooltip("群れの部位の硬さ(狩りの敵の何倍か)")]
        public float hordePartHpMul = 0.6f;
        [Tooltip("バケモノの部位も剥ぎ取れる")]
        public bool monsterHarvestable = true;
        public float monsterScale = 2.2f;
        public float arenaHalfSize = 200f;
        public Renderer ground;

        [Header("敵の性能")]
        public float enemyHP = 220f;
        public float monsterHP = 2600f;
        public Color enemyTint = new(0.6f, 0.3f, 0.28f);
        public Color monsterTint = new(0.4f, 0.15f, 0.45f);

        [Header("部位の取付位置(機体高さ9.06m基準・前が+Z)")]
        public Vector3 mountRightArm = new(3.6f, 7.5f, 0.6f);
        public Vector3 mountLeftArm = new(-3.6f, 7.5f, 0.6f);
        public Vector3 mountBack = new(0f, 6.6f, -1.6f);
        public Vector3 mountShoulder = new(1.6f, 8.9f, -0.2f);

        public Phase CurrentPhase { get; private set; }
        public IReadOnlyList<ProtoEnemy> Alive => alive;
        public float Elapsed => Time.time - startTime;

        [HideInInspector] public int lostParts;
        int deaths, harvested, huntSpawned, huntKilled, wave;
        float startTime, nextSpawnAt, phaseTimer;
        float? firstHarvest, huntEnd, releaseTime, hordeStart, hordeEnd, monsterEnd;
        int releasedCount;

        readonly List<ProtoEnemy> alive = new();
        readonly List<(float t, string msg)> log = new();
        readonly Queue<PartDefinition[]> huntLoadouts = new();
        ProtoPlayer player;
        ProtoStage stage;
        float warpUntil, warpScale = 1f;
        GUIStyle label, big;

        void Awake()
        {
            I = this;
            if (!modeInitialized) { Mode = defaultMode; modeInitialized = true; }
            Time.timeScale = 1f;
        }

        void Start()
        {
            SetupGroundGrid();
            if (useStage)
            {
                // 平地の地面と遠景は使わない(ステージの地形と重なるため)
                if (ground != null) ground.gameObject.SetActive(false);
                var horizon = GameObject.Find("Horizon");
                if (horizon != null) horizon.SetActive(false);
                stage = new GameObject("Stage").AddComponent<ProtoStage>();
                stage.Build(this);
            }

            var go = new GameObject("Player");
            go.transform.position = stage != null ? stage.StartPosition : Vector3.zero;
            var cc = go.AddComponent<CharacterController>();
            cc.height = mechHeight;
            cc.radius = 2f;
            cc.center = new Vector3(0f, mechHeight * 0.5f + 0.1f, 0f);
            cc.skinWidth = 0.2f;
            cc.stepOffset = 0.6f;
            player = go.AddComponent<ProtoPlayer>();
            player.Init(this);

            var cam = Camera.main.GetComponent<ProtoCamera>();
            if (cam != null)
            {
                cam.target = go.transform;
                cam.scale = mechHeight / 2.1f;   // 電脳斬禅(人間サイズ)のカメラを機体の大きさへ
            }

            BuildHuntLoadouts();
            startTime = Time.time;
            nextSpawnAt = Time.time + 1f;
            CurrentPhase = Phase.Hunt;
            Log(Mode == ProtoMode.Burst ? "モード:切り替え(拾った武装をラックに溜め、解放して在庫へ)" : "モード:段階的(拾った武装は在庫へ)");
        }

        // ───────── 部位の割り振り:各種類が2回ずつ、1体に同じ種類は付けない ─────────

        void BuildHuntLoadouts()
        {
            int total = huntEnemyCount * partsPerHuntEnemy;
            var bag = new List<PartDefinition>();
            while (bag.Count < total) bag.AddRange(partPool);
            bag.RemoveRange(total, bag.Count - total);

            for (int attempt = 0; attempt < 100; attempt++)
            {
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
                bool ok = true;
                for (int e = 0; e < huntEnemyCount && ok; e++)
                {
                    var seen = new HashSet<PartType>();
                    for (int k = 0; k < partsPerHuntEnemy; k++)
                        if (!seen.Add(bag[e * partsPerHuntEnemy + k].type)) ok = false;
                }
                if (ok) break;
            }
            for (int e = 0; e < huntEnemyCount; e++)
                huntLoadouts.Enqueue(bag.GetRange(e * partsPerHuntEnemy, partsPerHuntEnemy).ToArray());
        }

        /// <summary>部位の取付先。リグがあればボーンに追従させる。</summary>
        public Transform MountParent(MechRig rig, Transform root, MountSlot slot, int index, out Vector3 localPos)
        {
            if (rig != null) return rig.Mount(slot, index, out localPos);
            localPos = MountPos(slot, index);
            return root;
        }

        public Vector3 MountPos(MountSlot slot, int index)
        {
            float k = mechHeight / 9.06f;
            Vector3 p;
            switch (slot)
            {
                case MountSlot.RightArm: p = mountRightArm + new Vector3(1.3f * index, 0f, 0f); break;
                case MountSlot.LeftArm: p = mountLeftArm + new Vector3(-1.3f * index, 0f, 0f); break;
                case MountSlot.Back:
                    float side = index == 0 ? 0f : (index % 2 == 1 ? 1f : -1f) * 1.4f * ((index + 1) / 2);
                    p = mountBack + new Vector3(side, 0f, 0f);
                    break;
                default:
                    p = mountShoulder + new Vector3(0f, 1.2f * (index / 2), 0f);
                    if (index % 2 == 1) p.x = -p.x;
                    break;
            }
            return p * k;
        }

        // ───────── 進行 ─────────

        void Update()
        {
            ProtoInput.TrackDevice();
            if (ProtoInput.ToggleMode)
            {
                Mode = Mode == ProtoMode.Burst ? ProtoMode.Gradual : ProtoMode.Burst;
                Restart();
                return;
            }
            if (ProtoInput.Restart) { Restart(); return; }

            Time.timeScale = player != null && player.MenuOpen ? 0f : Time.unscaledTime < warpUntil ? warpScale : 1f;
            if (Time.unscaledTime >= warpUntil) warpScale = 1f;
            if (stage != null) return;   // ステージ攻略型は ProtoStage が進行を回す

            switch (CurrentPhase)
            {
                case Phase.Hunt:
                    if (alive.Count == 0 && huntSpawned < huntEnemyCount && Time.time >= nextSpawnAt)
                    {
                        huntSpawned++;
                        Spawn(ProtoEnemy.Kind.Hunt, RandomAround(45f), huntLoadouts.Dequeue(), true, 1f, enemyHP, enemyTint);
                        Log($"狩りの獲物 {huntSpawned}/{huntEnemyCount} が現れた");
                    }
                    if (huntKilled >= huntEnemyCount)
                    {
                        huntEnd = Elapsed;
                        CurrentPhase = Phase.HordeIncoming;
                        phaseTimer = 3f;
                        Log("狩り完了。群れが来る");
                    }
                    break;

                case Phase.HordeIncoming:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f)
                    {
                        CurrentPhase = Phase.Horde;
                        hordeStart = Elapsed;
                        SpawnWave();
                    }
                    break;

                case Phase.Horde:
                    if (alive.Count <= 2 && wave < hordeWaves) SpawnWave();
                    if (alive.Count == 0 && wave >= hordeWaves)
                    {
                        hordeEnd = Elapsed;
                        CurrentPhase = Phase.Monster;
                        var all = new List<PartDefinition>(partPool).ToArray();
                        Spawn(ProtoEnemy.Kind.Monster, RandomAround(60f), all, monsterHarvestable, monsterScale, monsterHP, monsterTint);
                        Log("バケモノが現れた");
                    }
                    break;

                case Phase.Monster:
                    if (alive.Count == 0)
                    {
                        monsterEnd = Elapsed;
                        CurrentPhase = Phase.Clear;
                        Log("クリア");
                    }
                    break;
            }
        }

        void SpawnWave()
        {
            wave++;
            for (int i = 0; i < hordePerWave; i++)
            {
                float ang = (i / (float)hordePerWave) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector3 pos = player.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Random.Range(40f, 55f);
                var def = partPool[Random.Range(0, partPool.Length)];
                Spawn(ProtoEnemy.Kind.Horde, ClampArena(pos), new[] { def }, hordeHarvestable, 1f, enemyHP, enemyTint);
            }
            Log($"群れ 第{wave}波({hordePerWave}体)");
        }

        /// <summary>ステージの区画から呼ぶ:種類に応じた部位を付けて敵を置く。type を渡すとその部位1つ(砲台役など)。</summary>
        public ProtoEnemy SpawnForStage(ProtoEnemy.Kind kind, Vector3 pos, PartType? type)
        {
            switch (kind)
            {
                case ProtoEnemy.Kind.Hunt:
                    var loadout = huntLoadouts.Count > 0 ? huntLoadouts.Dequeue()
                        : new[] { partPool[Random.Range(0, partPool.Length)], partPool[Random.Range(0, partPool.Length)] };
                    return Spawn(kind, pos, loadout, true, 1f, enemyHP, enemyTint);
                case ProtoEnemy.Kind.Monster:
                    return Spawn(kind, pos, new List<PartDefinition>(partPool).ToArray(), monsterHarvestable, monsterScale, monsterHP, monsterTint);
                case ProtoEnemy.Kind.Normal:
                {
                    // 通常の敵:部位は1つか2つ(種類は重ならない)。砲台役は指定の部位を必ず持つ
                    var list = new List<PartDefinition>();
                    if (type.HasValue) list.Add(System.Array.Find(partPool, d => d.type == type.Value));
                    else list.Add(partPool[Random.Range(0, partPool.Length)]);
                    if (Random.value < normalTwoPartChance)
                    {
                        var others = System.Array.FindAll(partPool, d => d.type != list[0].type);
                        if (others.Length > 0) list.Add(others[Random.Range(0, others.Length)]);
                    }
                    return Spawn(kind, pos, list.ToArray(), true, 1f, enemyHP, enemyTint);
                }
                default:
                    var def = type.HasValue ? System.Array.Find(partPool, d => d.type == type.Value) : partPool[Random.Range(0, partPool.Length)];
                    return Spawn(kind, pos, new[] { def }, hordeHarvestable, 1f, enemyHP, enemyTint);
            }
        }

        /// <summary>ステージのボス(大きさ・体力・色を指定)。部位は全種類。</summary>
        public ProtoEnemy SpawnBoss(Vector3 pos, float scale, float hp, Color tint)
            => Spawn(ProtoEnemy.Kind.Monster, pos, new List<PartDefinition>(partPool).ToArray(), monsterHarvestable, scale, hp, tint);

        public void OnStageClear()
        {
            monsterEnd = Elapsed;
            CurrentPhase = Phase.Clear;
            Log("ステージクリア");
        }

        ProtoEnemy Spawn(ProtoEnemy.Kind kind, Vector3 pos, PartDefinition[] parts, bool harvestable, float scale, float hp, Color tint)
        {
            var go = new GameObject(kind.ToString());
            go.transform.position = pos;
            Vector3 look = player.transform.position - pos;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(look);
            var e = go.AddComponent<ProtoEnemy>();
            // ドロップ制では部位を斬り落としても剥げない(壊すと能力を失い、落とす候補からも外れる)
            e.drops = lootRule == LootRule.Drop && harvestable;
            if (lootRule == LootRule.Drop) harvestable = false;
            e.Init(this, kind, parts, harvestable, scale, hp, tint);
            alive.Add(e);
            return e;
        }

        Vector3 RandomAround(float dist)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            return ClampArena(player.transform.position + new Vector3(d.x, 0f, d.y) * dist);
        }

        Vector3 ClampArena(Vector3 p)
        {
            float lim = arenaHalfSize - 20f;
            return new Vector3(Mathf.Clamp(p.x, -lim, lim), 0f, Mathf.Clamp(p.z, -lim, lim));
        }

        void Restart()
        {
            Time.timeScale = 1f;
            string path = SceneManager.GetActiveScene().path;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
#endif
        }

        // ───────── 通知 ─────────

        public void TimeWarp(float scale, float realDuration)
        {
            if (Time.unscaledTime >= warpUntil || scale < warpScale) warpScale = scale;
            warpUntil = Mathf.Max(warpUntil, Time.unscaledTime + realDuration);
        }

        public void Log(string msg)
        {
            log.Add((Time.unscaledTime, msg));
            if (log.Count > 6) log.RemoveAt(0);
        }

        public void OnEnemyDied(ProtoEnemy e)
        {
            alive.Remove(e);
            if (e.EnemyKind == ProtoEnemy.Kind.Hunt)
            {
                huntKilled++;
                nextSpawnAt = Time.time + 2.5f;
            }
        }

        public void OnHarvest(PartDefinition def)
        {
            harvested++;
            firstHarvest ??= Elapsed;
        }

        public void OnRelease(int count)
        {
            releaseTime ??= Elapsed;
            releasedCount += count;
            Log($"解放!{count}個の武装をまとった");
        }

        public void OnPlayerDeath()
        {
            deaths++;
            Log("撃墜された(装備はそのまま復帰)");
        }

        // ───────── 表示 ─────────

        void SetupGroundGrid()
        {
            if (ground == null) return;
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, true);
            var baseC = new Color(0.36f, 0.37f, 0.38f);
            var line = new Color(0.24f, 0.25f, 0.26f);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    tex.SetPixel(x, y, (x < 3 || y < 3) ? line : baseC);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 8;
            var m = ground.material;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            float tiles = arenaHalfSize * 2f / 10f;
            m.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
        }

        static string FormatTime(float t) => $"{(int)(t / 60f):00}:{t % 60f:00.0}";
        static string T(float? t) => t.HasValue ? FormatTime(t.Value) : "--";

        string PhaseText()
        {
            if (stage != null) return stage.StatusText;
            switch (CurrentPhase)
            {
                case Phase.Hunt: return $"狩り {huntKilled}/{huntEnemyCount}";
                case Phase.HordeIncoming: return "群れ接近中…";
                case Phase.Horde: return $"群れ 第{wave}/{hordeWaves}波 残り{alive.Count}";
                case Phase.Monster: return "バケモノ";
                default: return "クリア";
            }
        }

        static string Summarize(IReadOnlyList<PartDefinition> list)
        {
            if (list.Count == 0) return "なし";
            var counts = new Dictionary<string, int>();
            foreach (var d in list) { counts.TryGetValue(d.displayName, out int n); counts[d.displayName] = n + 1; }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add(kv.Value > 1 ? $"{kv.Key}×{kv.Value}" : kv.Key);
            return string.Join(" / ", parts);
        }

        // ───────── 普段のHUD(必要なものだけ) ─────────

        GUIStyle small, tiny, head;
        float uiScale = 1f;
        float SW => Screen.width / uiScale;
        float SH => Screen.height / uiScale;
        void EnsureStyles()
        {
            if (small != null) return;
            small = new GUIStyle(label) { fontSize = 14 };
            small.normal.textColor = new Color(0.8f, 0.82f, 0.85f);
            tiny = new GUIStyle(label) { fontSize = 12 };
            tiny.normal.textColor = new Color(0.7f, 0.72f, 0.75f);
            head = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        /// <summary>左上:今の区画と残り、HP・エネルギー。</summary>
        void DrawStatus()
        {
            float x = 20f, y = 16f;
            GUI.Label(new Rect(x, y, 500f, 26f), PhaseText(), head);
            y += 30f;
            Bar(new Rect(x, y + 5f, 260f, 10f), player.HP / player.maxHP,
                player.HP < player.maxHP * 0.3f ? new Color(0.95f, 0.35f, 0.3f) : new Color(0.35f, 0.85f, 0.45f));
            GUI.Label(new Rect(x + 268f, y - 2f, 120f, 22f), $"HP {Mathf.CeilToInt(player.HP)}", small);
            y += 20f;
            Color en = player.Overheated ? new Color(1f, 0.35f, 0.25f) : player.Boosting ? new Color(1f, 0.75f, 0.2f) : new Color(0.35f, 0.75f, 1f);
            Bar(new Rect(x, y + 5f, 260f, 10f), player.Energy / player.maxEnergy, en);
            string state = player.Overheated ? "回復待ち" : (player.Boosting ? "ブースト " : "") + (player.Flying ? "飛行" : "");
            GUI.Label(new Rect(x + 268f, y - 2f, 160f, 22f), state.Length > 0 ? state : "EN", small);
            y += 22f;
            if (player.HealItems > 0)
            {
                Chip(new Rect(x, y + 6f, 10f, 10f), new Color(0.3f, 1f, 0.5f));
                GUI.Label(new Rect(x + 16f, y, 300f, 22f), $"回復 ×{player.HealItems}  [{K(ProtoInput.Btn.Heal)}]", small);
            }
        }

        /// <summary>右下:4つの武装を肩ボタンの並び(左=L・右=R、上=2・下=1)で。残り回数は細いバー。</summary>
        void DrawWeapons()
        {
            const float w = 200f, h = 44f, gap = 8f;
            float x1 = SW - 20f - w, x0 = x1 - gap - w;
            float yb = SH - 20f;
            // 一番下:背中と在庫
            float yBack = yb - 26f;
            var back = player.SlotItem(Hardpoint.Back);
            GUI.Label(new Rect(x0, yBack, w * 2f + gap, 24f),
                $"背  {(back != null ? ProtoPlayer.ItemText(back) : "―")}", small);
            string stock = Mode == ProtoMode.Burst
                ? (player.Rack.Count > 0 ? $"ラック {player.Rack.Count}  [{K(ProtoInput.Btn.Menu)}]解放" : "")
                : (player.Stock.Count > 0 ? $"在庫 {player.Stock.Count}  [{K(ProtoInput.Btn.Menu)}]" : "");
            if (stock.Length > 0)
            {
                var st = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
                st.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(x0, yBack, w * 2f + gap, 24f), stock, st);
            }
            float yRow1 = yBack - 6f - h, yRow2 = yRow1 - gap - h;
            WeaponCell(new Rect(x0, yRow2, w, h), Hardpoint.LeftShoulder, ProtoInput.Btn.L2);
            WeaponCell(new Rect(x1, yRow2, w, h), Hardpoint.RightShoulder, ProtoInput.Btn.R2);
            WeaponCell(new Rect(x0, yRow1, w, h), Hardpoint.LeftHand, ProtoInput.Btn.L1);
            WeaponCell(new Rect(x1, yRow1, w, h), Hardpoint.RightHand, ProtoInput.Btn.R1);
        }

        void WeaponCell(Rect r, Hardpoint at, ProtoInput.Btn btn)
        {
            var it = player.SlotItem(at);
            bool empty = it == null && at != Hardpoint.RightHand;
            Chip(r, new Color(0f, 0f, 0f, empty ? 0.25f : 0.5f));
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, 90f, 16f), K(btn), tiny);
            string name = it != null ? it.def.displayName : at == Hardpoint.RightHand ? "剣" : "―";
            if (it != null) Chip(new Rect(r.x + 8f, r.y + 23f, 10f, 10f), it.def.GradeColor);
            GUI.color = empty ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            GUI.Label(new Rect(r.x + 24f, r.y + 16f, r.width - 30f, 24f), name, label);
            GUI.color = Color.white;
            if (it != null && it.uses >= 0 && it.def.uses > 0)
            {
                float k = Mathf.Clamp01(it.uses / (float)it.def.uses);
                Chip(new Rect(r.x, r.yMax - 3f, r.width, 3f), new Color(1f, 1f, 1f, 0.12f));
                Chip(new Rect(r.x, r.yMax - 3f, r.width * k, 3f), k < 0.25f ? new Color(1f, 0.4f, 0.3f) : new Color(0.9f, 0.9f, 0.9f));
                var st = new GUIStyle(tiny) { alignment = TextAnchor.UpperRight };
                GUI.Label(new Rect(r.x, r.y + 3f, r.width - 8f, 16f), $"{it.uses}", st);
            }
        }

        /// <summary>右上:新しい知らせだけを短く(数秒で消える)。</summary>
        void DrawLog()
        {
            float y = 16f;
            var st = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
            for (int i = Mathf.Max(0, log.Count - 4); i < log.Count; i++)
            {
                float age = Time.unscaledTime - log[i].t;
                if (age > 6f) continue;
                var c = Color.white;
                c.a = Mathf.Clamp01((6f - age) / 1.5f);
                GUI.color = c;
                GUI.Label(new Rect(SW - 520f, y, 500f, 22f), log[i].msg, st);
                y += 22f;
            }
            GUI.color = Color.white;
        }

        static string SlotName(int i)
        {
            ProtoInput.Btn[] b = { ProtoInput.Btn.R1, ProtoInput.Btn.L1, ProtoInput.Btn.R2, ProtoInput.Btn.L2 };
            string[] n = { "右手", "左手", "右肩", "左肩" };
            return i < 4 ? $"{n[i]}  ({ProtoInput.L(b[i])})" : "背中";
        }

        static string K(ProtoInput.Btn b) => ProtoInput.L(b);

        /// <summary>武装の性能の一言(品質の比較用)。</summary>
        static string Stat(PartDefinition d) => d.type switch
        {
            PartType.Blade => $"剣の威力×{d.multiplier:0.##}  リーチ×{1f + (d.multiplier - 1f) * (ProtoPlayer.I != null ? ProtoPlayer.I.bladeReachScale : 0.5f):0.##}",
            PartType.Thruster => $"ブースト中:速さ・回避×{d.multiplier:0.##}  上昇×{1f + (d.multiplier - 1f) * 0.5f:0.##}",
            PartType.Cannon => $"威力{d.damage:0.#}  爆風{d.radius:0.#}m",
            _ => $"威力{d.damage:0.#}  {1f / Mathf.Max(0.01f, d.interval):0.#}発/秒",
        };

        void Chip(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Panel(Rect r, float alpha) => Chip(r, new Color(0f, 0f, 0f, alpha));

        /// <summary>
        /// 装備メニュー(2段):左で場所を選んで×、右でその場所に付けるものを選んで×。○で戻る。
        /// </summary>
        void DrawEquipMenu()
        {
            var slots = ProtoPlayer.MenuSlots;
            bool picking = player.MenuPicking;
            const float colW = 360f, rowH = 54f, gap = 16f, helpW = 300f;
            float w = colW * 2f + helpW + gap * 4f, h = 90f + slots.Length * (rowH + 6f) + 120f;
            float x0 = (SW - w) / 2f, y0 = (SH - h) / 2f;
            Chip(new Rect(0f, 0f, SW, SH), new Color(0f, 0f, 0f, 0.45f));
            Panel(new Rect(x0, y0, w, h), 0.9f);

            float lx = x0 + gap, rx = lx + colW + gap, top = y0 + 52f;
            GUI.Label(new Rect(lx, y0 + 14f, colW, 30f), "装備する場所", head);
            GUI.Label(new Rect(rx, y0 + 14f, colW, 30f), picking ? "付けるもの" : "候補", head);

            // 左:場所
            for (int i = 0; i < slots.Length; i++)
            {
                var r = new Rect(lx, top + i * (rowH + 6f), colW, rowH);
                bool sel = i == player.MenuRow;
                Chip(r, sel ? (picking ? new Color(0.3f, 0.5f, 0.8f, 0.35f) : new Color(0.3f, 0.6f, 1f, 0.55f)) : new Color(1f, 1f, 1f, 0.06f));
                if (sel) Chip(new Rect(r.x, r.y, 4f, r.height), new Color(0.5f, 0.8f, 1f));
                GUI.Label(new Rect(r.x + 12f, r.y + 4f, 220f, 20f), SlotName(i), small);
                var m = player.SlotItem(slots[i]);
                if (m != null) Chip(new Rect(r.x + 14f, r.y + 30f, 12f, 12f), m.def.GradeColor);
                GUI.Label(new Rect(r.x + 34f, r.y + 24f, colW - 40f, 26f), player.SlotText(slots[i]), label);
                int n = player.CandidateCount(slots[i]);
                if (n > 0) GUI.Label(new Rect(r.x + colW - 70f, r.y + 4f, 64f, 20f), $"候補 {n}", small);
            }

            // 右:候補
            var cands = player.MenuCandidates();
            float ry = top;
            if (cands.Count == 0)
            {
                GUI.Label(new Rect(rx + 8f, ry + 6f, colW - 16f, 50f), "付けられるものがありません\n(敵の部位を斬り落とすと入手)", small);
            }
            else
            {
                int maxRows = 6;
                int first = picking ? Mathf.Clamp(player.MenuItem - maxRows + 1, 0, Mathf.Max(0, cands.Count - maxRows)) : 0;
                for (int i = first; i < cands.Count && i < first + maxRows; i++)
                {
                    var r = new Rect(rx, ry, colW, 46f);
                    bool sel = picking && i == player.MenuItem;
                    Chip(r, sel ? new Color(0.3f, 0.6f, 1f, 0.55f) : new Color(1f, 1f, 1f, picking ? 0.06f : 0.03f));
                    if (sel) Chip(new Rect(r.x, r.y, 4f, r.height), new Color(0.5f, 0.8f, 1f));
                    var it = cands[i];
                    GUI.color = picking ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    if (it == null)
                    {
                        GUI.Label(new Rect(r.x + 34f, r.y + 10f, colW - 40f, 26f), "外す(素の武装に戻す)", label);
                    }
                    else
                    {
                        Chip(new Rect(r.x + 14f, r.y + 17f, 12f, 12f), it.def.GradeColor);
                        GUI.Label(new Rect(r.x + 34f, r.y + 2f, colW - 40f, 24f), it.def.displayName, label);
                        string uses = it.uses < 0 ? "外れない" : $"残り {it.uses}回";
                        GUI.Label(new Rect(r.x + 34f, r.y + 24f, colW - 40f, 20f), $"{Stat(it.def)}   {uses}", small);
                    }
                    GUI.color = Color.white;
                    ry += 52f;
                }
                if (cands.Count > maxRows) GUI.Label(new Rect(rx + 8f, ry, colW, 20f), $"全{cands.Count}件", small);
            }

            // 右:操作方法(普段の画面には出さず、ここでだけ見せる)
            float hx = rx + colW + gap;
            Chip(new Rect(hx - gap * 0.5f, y0 + 12f, 1f, h - 60f), new Color(1f, 1f, 1f, 0.15f));
            GUI.Label(new Rect(hx, y0 + 14f, helpW, 30f), "操作", head);
            var help = new (ProtoInput.Btn b, string what)[]
            {
                (ProtoInput.Btn.Move, "移動"),
                (ProtoInput.Btn.R1, "右手"),
                (ProtoInput.Btn.L1, "左手"),
                (ProtoInput.Btn.R2, "右肩"),
                (ProtoInput.Btn.L2, "左肩"),
                (ProtoInput.Btn.Dodge, "回避"),
                (ProtoInput.Btn.Boost, "ブースト 入/切"),
                (ProtoInput.Btn.Fly, "長押しで飛行"),
                (ProtoInput.Btn.Aim, "長押しで照準(カメラを自由に)"),
                (ProtoInput.Btn.Lock, "ロックオン"),
                (ProtoInput.Btn.Switch, "ロックオン切替"),
                (ProtoInput.Btn.Heal, "回復アイテムを使う"),
                (ProtoInput.Btn.Menu, "装備メニュー"),
                (ProtoInput.Btn.Restart, "やり直し"),
                (ProtoInput.Btn.ModeToggle, "モード切替"),
            };
            float hy = top;
            var keyStyle = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
            keyStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            var whatStyle = new GUIStyle(small);
            whatStyle.normal.textColor = Color.white;
            foreach (var (b, what) in help)
            {
                GUI.Label(new Rect(hx, hy, 130f, 22f), K(b), keyStyle);
                GUI.Label(new Rect(hx + 140f, hy, helpW - 140f, 22f), what, whatStyle);
                hy += 22f;
            }
            GUI.Label(new Rect(hx, hy + 4f, helpW, 22f), "手の武装:剣は連打で3段、銃は長押し", tiny);
            hy += 20f;
            if (ProtoInput.Current == ProtoInput.Device.Keyboard)
                GUI.Label(new Rect(hx, hy + 4f, helpW, 22f), "クリックでマウス固定・Escで解除", tiny);

            // 下:記録とモード
            string modeName = Mode == ProtoMode.Burst ? "切り替え(溜めて解放)" : "段階的(拾ったら在庫へ)";
            GUI.Label(new Rect(x0 + gap, y0 + h - 60f, w - gap * 2f, 22f),
                $"経過 {FormatTime(Elapsed)}    入手した武装 {harvested}    撃墜された回数 {deaths}    モード {modeName}", small);

            // 下:操作
            string selK = K(ProtoInput.Btn.Select), ok = K(ProtoInput.Btn.Confirm), back = K(ProtoInput.Btn.Back), close = K(ProtoInput.Btn.Menu);
            string guide = picking ? $"{selK} 選ぶ    {ok} 付ける    {back} 戻る    {close} 閉じる" : $"{selK} 場所を選ぶ    {ok} 決定    {back}・{close} 閉じる";
            GUI.Label(new Rect(x0 + gap, y0 + h - 34f, w - gap * 2f, 24f), guide + "        時間停止中", small);
        }

        void Bar(Rect r, float k, Color c)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnGUI()
        {
            if (player == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 17 };
                label.normal.textColor = Color.white;
                big = new GUIStyle(label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            }
            // UIは 1280x720 を基準に作り、画面の大きさに合わせて拡大縮小する
            uiScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            EnsureStyles();
            // 装備メニュー中は普段のHUDを隠して、メニューだけを出す
            if (player.MenuOpen) { DrawEquipMenu(); return; }

            // 敵と部位のHP
            var cam = Camera.main;
            foreach (var e in alive)
            {
                if (e.Dead) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (mechHeight + 1.5f) * e.Scale) / uiScale;
                if (sp.z < 0f || sp.z * uiScale > 160f) continue;
                Bar(new Rect(sp.x - 50f, SH - sp.y, 100f, 8f), e.HP / e.MaxHP, new Color(0.9f, 0.3f, 0.25f));
                foreach (var p in e.Parts)
                {
                    if (p.Severed || p.Broken) continue;
                    Vector3 pp = cam.WorldToScreenPoint(p.transform.position) / uiScale;
                    if (pp.z < 0f) continue;
                    // 壊せる部位は橙(ドロップ制は全部位、剥ぎ取り方式は剥げる部位だけ)
                    Color c = p.Harvestable || lootRule == LootRule.Drop ? new Color(1f, 0.65f, 0.15f) : new Color(0.6f, 0.6f, 0.6f);
                    Bar(new Rect(pp.x - 22f, SH - pp.y - 4f, 44f, 6f), p.HP / p.MaxHP, c);
                }
            }

            // ロックオンの印
            var tgt = player.Target;
            if (tgt != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(tgt.transform.position + Vector3.up * mechHeight * 0.6f * tgt.Scale) / uiScale;
                if (sp.z > 0f)
                {
                    float r = Mathf.Clamp(900f / (sp.z * uiScale), 18f, 60f);
                    float cx = sp.x, cy = SH - sp.y;
                    GUI.color = new Color(1f, 0.85f, 0.2f);
                    foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    {
                        GUI.DrawTexture(new Rect(cx + dx * r - (dx < 0 ? 0 : 14), cy + dy * r - (dy < 0 ? 0 : 3), 14, 3), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(cx + dx * r - (dx < 0 ? 0 : 3), cy + dy * r - (dy < 0 ? 0 : 14), 3, 14), Texture2D.whiteTexture);
                    }
                    GUI.color = Color.white;
                }
            }

            // 照準(△長押し)の間だけ、射撃が飛ぶ先(機体の向き+カメラの高さ)に小さな照準を出す
            if (player.Aiming)
            {
                Vector3 ap = cam.WorldToScreenPoint(player.AimPreview()) / uiScale;
                if (ap.z > 0f)
                {
                    float cx = ap.x, cy = SH - ap.y;
                    var col = new Color(1f, 1f, 1f, 0.8f);
                    Chip(new Rect(cx - 9f, cy - 1f, 6f, 2f), col);
                    Chip(new Rect(cx + 3f, cy - 1f, 6f, 2f), col);
                    Chip(new Rect(cx - 1f, cy - 9f, 2f, 6f), col);
                    Chip(new Rect(cx - 1f, cy + 3f, 2f, 6f), col);
                }
            }

            DrawStatus();
            DrawWeapons();
            DrawLog();

            if (CurrentPhase == Phase.HordeIncoming)
                GUI.Label(new Rect(0f, SH * 0.3f, SW, 50f), "群れが来る", big);
            if (player.IsDead)
                GUI.Label(new Rect(0f, SH * 0.4f, SW, 50f), stage != null ? "撃墜 ― 装備はそのまま、区画の入口から復帰します" : "撃墜 ― 装備はそのまま復帰します", big);

            if (CurrentPhase == Phase.Clear)
            {
                string mode = Mode == ProtoMode.Burst ? "切り替え(溜めて解放)" : "段階的(拾ったら在庫へ)";
                float w = 560f, h = stage != null ? 150f + 27f * new List<(string, float)>(stage.ClearTimes()).Count : 330f;
                var r = new Rect((SW - w) / 2f, (SH - h) / 2f, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                float yy = r.y + 16f;
                void Line(string s) { GUI.Label(new Rect(r.x + 20f, yy, w - 40f, 26f), s, label); yy += 27f; }
                Line($"クリア ― {mode}");
                if (stage != null)
                {
                    foreach (var (n, t) in stage.ClearTimes()) Line($"{n} 制圧  {T(t >= 0f ? t : (float?)null)}");
                    Line($"入手した武装 {harvested} / 撃墜された回数 {deaths}");
                    Line("");
                    Line($"{K(ProtoInput.Btn.Restart)} 同じモードでもう一度 / {K(ProtoInput.Btn.ModeToggle)} もう一方のモードで");
                    return;
                }
                Line($"最初の剥ぎ取り   {T(firstHarvest)}");
                Line($"狩り完了         {T(huntEnd)}");
                Line($"解放             {T(releaseTime)}");
                Line($"群れ開始 → 全滅  {T(hordeStart)} → {T(hordeEnd)}");
                Line($"バケモノ撃破     {T(monsterEnd)}");
                Line($"剥ぎ取り {harvested} / 失った部位 {lostParts} / 撃墜 {deaths}");
                Line("");
                Line($"{K(ProtoInput.Btn.Restart)} 同じモードでもう一度 / {K(ProtoInput.Btn.ModeToggle)} もう一方のモードで");
            }
        }
    }
}
