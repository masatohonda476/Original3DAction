using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// ステージ攻略型の進行。区画(エリア)を順に進み、区画の敵を全滅させると出口の扉が開く。
    /// 地形は仮の箱で組む(高低差・閉所あり)。見た目は後で差し替える前提で、寸法と進行だけを決めている。
    ///   1 谷の入口(開けた谷)→ 2 坑道(狭い通路・天井あり)→ 3 大広間(段差・上の足場は飛ばないと届かない)
    ///   → 4 縦坑(飛んで登る)→ 5 頂上(バケモノ)
    /// </summary>
    public class ProtoStage : MonoBehaviour
    {
        public static ProtoStage I;

        /// <summary>区画1つ分。入ると敵が現れ、全滅で出口が開く。</summary>
        class Area
        {
            public string name;
            public int stage;
            public Bounds bounds;
            public Vector3 checkpoint;
            public System.Action spawn;
            public GameObject door;
            public bool entered, cleared;
            public float clearTime = -1f;
            public readonly List<ProtoEnemy> enemies = new();
        }

        readonly List<Area> areas = new();
        Transform root;
        ProtoGame game;
        int current;
        Material gridTex;

        public Vector3 StartPosition => new(0f, 0.2f, 0f);
        public bool Cleared { get; private set; }

        // 色(仮)
        static readonly Color Rock = new(0.45f, 0.42f, 0.38f), RockDark = new(0.34f, 0.32f, 0.3f),
            Metal = new(0.4f, 0.44f, 0.48f), MetalDark = new(0.29f, 0.31f, 0.34f), Floor = new(0.37f, 0.38f, 0.39f),
            Ledge = new(0.55f, 0.5f, 0.38f), Door = new(0.8f, 0.28f, 0.16f);

        // ───────── 地形の問い合わせ(敵・カメラ・飛行で使う) ─────────

        public static bool IsStage(Collider c) => I != null && c != null && c.transform.IsChildOf(I.root);

        /// <summary>真下の足場の高さ。ステージでなければ 0。</summary>
        public static float GroundHeight(Vector3 p)
        {
            if (I == null) return 0f;
            float best = -1000f;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
                if (IsStage(h.collider) && h.point.y > best) best = h.point.y;
            return best;
        }

        /// <summary>a から b の間を地形がさえぎっているか。</summary>
        public static bool Blocked(Vector3 a, Vector3 b)
        {
            if (I == null) return false;
            Vector3 d = b - a;
            foreach (var h in Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (IsStage(h.collider)) return true;
            return false;
        }

        /// <summary>from から dir へ球を動かして、地形に当たるまでの距離(当たらなければ maxDist)。カメラのめり込み防止用。</summary>
        public static float CastDistance(Vector3 from, Vector3 dir, float maxDist, float radius)
        {
            if (I == null) return maxDist;
            float best = maxDist;
            foreach (var h in Physics.SphereCastAll(from, radius, dir, maxDist, ~0, QueryTriggerInteraction.Ignore))
                if (IsStage(h.collider) && h.distance > 0f && h.distance < best) best = h.distance;
            return best;
        }

        // ───────── 組み立て ─────────

        public void Build(ProtoGame g)
        {
            I = this;
            game = g;
            root = new GameObject("StageGeometry").transform;
            root.SetParent(transform, false);
            BuildGrid();

            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 900f;

            // 1 谷の入口:開けた谷。岩を盾にしながら最初の獲物を狩る
            Solid(-60, 60, -2, 0, -40, 154, Floor, floor: true);
            Solid(-66, -60, 0, 300, -46, 154, Rock);
            Solid(60, 66, 0, 300, -46, 154, Rock);
            Solid(-66, 66, 0, 300, -46, -40, Rock);
            Solid(-30, -18, 0, 8, 40, 52, RockDark);
            Solid(15, 30, 0, 12, 80, 92, RockDark);
            Solid(-58, -36, 0, 10, 96, 140, Ledge);
            Ramp(-16, -36, 104, 132, 0, 10, Ledge, sideways: true);
            var door1 = WallZ(150, -66, 66, 0, 300, -14, 14, 0, 26, Rock);
            AddArea("谷の入口", new Bounds(new Vector3(0, 30, 55), new Vector3(120, 80, 190)), new Vector3(0, 0.2f, 0), door1, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 0, 100));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-20, 0, 70));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(22, 0, 120));
            });

            // 2 坑道:幅28m・天井26mの通路。柱の陰から出てくる。奥は坂を登って一段高い出口へ
            Solid(-14, 14, -2, 0, 154, 290, Floor, floor: true);
            Solid(-18, -14, 0, 30, 154, 290, Metal);
            Solid(14, 18, 0, 30, 154, 290, Metal);
            Solid(-18, 18, 26, 30, 154, 294, MetalDark, shadow: false);
            Solid(-14, -7, 0, 26, 186, 194, MetalDark);
            Solid(7, 14, 0, 26, 212, 220, MetalDark);
            Solid(-14, -7, 0, 26, 238, 246, MetalDark);
            Ramp(-14, 14, 256, 276, 0, 6, Ledge);
            Solid(-14, 14, 0, 6, 276, 290, Ledge);
            var door2 = WallZ(290, -74, 74, 0, 74, -14, 14, 6, 26, Metal);
            AddArea("坑道", new Bounds(new Vector3(0, 13, 222), new Vector3(28, 30, 136)), new Vector3(0, 0.2f, 160), door2, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-6, 0, 205));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(6, 0, 230));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 0, 252));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 6, 284), PartType.Gatling, hold: true);
            });

            // 3 大広間:床・右の段(14m・坂で登れる)・左奥の崖(30m・飛ばないと届かない)。出口は30mの高さ
            Solid(-70, 70, -2, 0, 294, 470, Floor, floor: true);
            Solid(-74, -70, 0, 74, 290, 474, Rock);
            Solid(70, 74, 0, 74, 290, 474, Rock);
            Solid(-74, 74, 70, 74, 294, 474, RockDark, shadow: false);
            Solid(-14, 14, 0, 6, 294, 312, Ledge);                 // 入口のバルコニー(ここから飛び降りる)
            Solid(35, 70, 0, 14, 330, 470, Ledge);                 // 右の段
            Ramp(20, 35, 300, 330, 0, 14, Ledge, sideways: false, alongX: false);
            Solid(-70, -36, 0, 30, 390, 470, Rock);                // 左奥の崖
            Solid(-36, 35, 0, 30, 444, 470, Rock);                 // 奥の高台(出口の前)
            Solid(-22, -8, 0, 10, 352, 366, RockDark);
            Solid(4, 16, 0, 6, 398, 410, RockDark);
            var door3 = WallZ(470, -74, 74, 0, 74, -14, 14, 30, 56, Rock);
            AddArea("大広間", new Bounds(new Vector3(0, 36, 382), new Vector3(140, 74, 176)), new Vector3(0, 6.2f, 300), door3, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-12, 0, 380));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(14, 0, 350));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(52, 14, 400));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-54, 30, 425), PartType.Cannon, hold: true);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 30, 457), PartType.Gatling, hold: true);
            });

            // 4 縦坑:上が開いた縦穴。左右の足場を飛び移って80mの出口へ
            Solid(-22, 22, 0, 30, 474, 530, RockDark);
            Solid(-26, -22, 0, 300, 470, 534, Rock);
            Solid(22, 26, 0, 300, 470, 534, Rock);
            Solid(8, 22, 30, 46, 488, 530, Ledge);                 // 右の足場 46m
            Solid(-22, -6, 30, 62, 500, 530, Ledge);               // 左の足場 62m
            Solid(-14, 14, 30, 80, 520, 530, Ledge);               // 出口の敷居 80m
            var door4 = WallZ(530, -84, 84, 0, 300, -14, 14, 80, 106, Rock);
            AddArea("縦坑", new Bounds(new Vector3(0, 150, 500), new Vector3(44, 300, 52)), new Vector3(0, 30.2f, 480), door4, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 30, 496));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(15, 46, 512), PartType.Gatling, hold: true);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-14, 62, 518), PartType.Cannon, hold: true);
            });

            // 5 頂上:高さ80mの台地。柱を盾にバケモノと戦う
            Solid(-80, 80, 0, 80, 534, 724, RockDark, floor: true);
            Solid(-84, -80, 80, 300, 530, 728, Rock);
            Solid(80, 84, 80, 300, 530, 728, Rock);
            var door5 = WallZ(724, -84, 84, 80, 300, -14, 14, 80, 106, Rock);   // ボスを倒すとステージ2へ
            Solid(-42, -30, 80, 112, 600, 612, Metal);
            Solid(30, 42, 80, 112, 636, 648, Metal);
            Solid(-10, 10, 80, 88, 684, 694, MetalDark);
            AddArea("頂上", new Bounds(new Vector3(0, 220, 629), new Vector3(160, 290, 190)), new Vector3(0, 80.2f, 545), door5, () =>
            {
                Spawn(ProtoEnemy.Kind.Monster, new Vector3(0, 80, 665));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-30, 80, 630));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(30, 80, 610));
            });

            BuildStage2();
            PlaceChests();
        }

        // ───────── ステージ2(ステージ1と同じくらいの広さ。最後に2体目のボス) ─────────
        //   6 断崖の橋(足場が途切れた橋・落ちると復帰)→ 7 格納庫(閉所・両脇に高い通路)→ 8 段々の谷(下り段差)
        //   → 9 地下水路(天井の低いS字の通路)→ 10 溶鉱炉跡(2体目のボス)
        void BuildStage2()
        {
            stageNo = 2;

            // 6 断崖の橋:谷底はない(落ちると区画の入口へ戻る)。橋は途中で16m途切れていて、飛ぶかブーストで越える
            Solid(-30, 30, 0, 80, 728, 762, RockDark);
            Solid(-8, 8, 76, 80, 762, 812, Metal);
            Solid(-8, 8, 76, 80, 828, 880, Metal);
            Solid(-48, -32, 0, 96, 790, 808, Rock);                // 左の柱(上に砲台)
            Solid(32, 48, 0, 96, 840, 858, Rock);                  // 右の柱(上に砲台)
            Solid(-40, 40, 0, 80, 880, 904, RockDark);
            Solid(-74, -70, 0, 300, 728, 908, Rock);
            Solid(70, 74, 0, 300, 728, 908, Rock);
            var door6 = WallZ(904, -74, 74, 0, 300, -14, 14, 80, 106, Rock);
            AddArea("断崖の橋", new Bounds(new Vector3(0, 150, 816), new Vector3(140, 300, 176)), new Vector3(0, 80.2f, 740), door6, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 80, 790));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-20, 80, 892));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(18, 80, 890));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-40, 96, 799), PartType.Gatling, hold: true);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(40, 96, 849), PartType.Cannon, hold: true);
            });

            // 7 格納庫:天井40mの屋内。両脇に高さ18mの通路(左は坂で上がれる、右は飛んで上がる)。コンテナを盾に
            Solid(-60, 60, 0, 80, 908, 1084, MetalDark, floor: true);
            Solid(-64, -60, 80, 124, 904, 1088, Metal);
            Solid(60, 64, 80, 124, 904, 1088, Metal);
            Solid(-64, 64, 120, 124, 908, 1084, MetalDark, shadow: false);
            Solid(40, 60, 96, 98, 920, 1076, Ledge);
            Solid(-60, -40, 96, 98, 920, 1076, Ledge);
            Ramp(0, -40, 960, 976, 80, 98, Ledge, sideways: true);
            Solid(-20, -6, 80, 92, 930, 950, Metal);
            Solid(10, 26, 80, 88, 1000, 1016, Metal);
            Solid(-30, -14, 80, 96, 1040, 1056, MetalDark);
            var door7 = WallZ(1084, -64, 64, 80, 124, -14, 14, 80, 106, Metal);
            AddArea("格納庫", new Bounds(new Vector3(0, 102, 996), new Vector3(120, 44, 176)), new Vector3(0, 80.2f, 915), door7, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-10, 80, 990));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(20, 80, 1030));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 80, 1062));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(50, 98, 1020), PartType.Gatling, hold: true);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-50, 98, 1050), PartType.Cannon, hold: true);
            });

            // 8 段々の谷:10mずつ下がる4段(80→50m)。上から撃ち下ろし、下から撃ち上げられる
            Solid(-60, 60, 0, 80, 1088, 1132, Floor, floor: true);
            Solid(-60, 60, 0, 70, 1132, 1176, Floor, floor: true);
            Solid(-60, 60, 0, 60, 1176, 1220, Floor, floor: true);
            Solid(-60, 60, 0, 50, 1220, 1264, Floor, floor: true);
            Solid(-64, -60, 0, 300, 1084, 1268, Rock);
            Solid(60, 64, 0, 300, 1084, 1268, Rock);
            Solid(-30, -18, 70, 80, 1150, 1160, RockDark);
            Solid(20, 32, 60, 72, 1190, 1200, RockDark);
            var door8 = WallZ(1264, -64, 64, 0, 300, -14, 14, 50, 76, Rock);
            AddArea("段々の谷", new Bounds(new Vector3(0, 150, 1176), new Vector3(120, 300, 176)), new Vector3(0, 80.2f, 1095), door8, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 70, 1150));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-25, 60, 1200));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(20, 50, 1235));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(40, 50, 1250), PartType.Cannon, hold: true);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-40, 50, 1245), PartType.Gatling, hold: true);
            });

            // 9 地下水路:天井26mの低い屋内。仕切りでS字に折れ曲がる通路
            Solid(-50, 50, 0, 50, 1268, 1404, MetalDark, floor: true);
            Solid(-54, -50, 50, 80, 1264, 1408, Metal);
            Solid(50, 54, 50, 80, 1264, 1408, Metal);
            Solid(-54, 54, 76, 80, 1268, 1404, MetalDark, shadow: false);
            Solid(-50, 20, 50, 76, 1305, 1311, Metal);            // 1つ目の仕切り(右端が通れる)
            Solid(-20, 50, 50, 76, 1350, 1356, Metal);            // 2つ目の仕切り(左端が通れる)
            var door9 = WallZ(1404, -54, 54, 50, 80, -14, 14, 50, 76, Metal);
            AddArea("地下水路", new Bounds(new Vector3(0, 63, 1336), new Vector3(100, 26, 136)), new Vector3(0, 50.2f, 1275), door9, () =>
            {
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-30, 50, 1290));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 50, 1330));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-35, 50, 1340));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(0, 50, 1380));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(30, 50, 1392), PartType.Gatling, hold: true);
            });

            // 10 溶鉱炉跡:2体目のボス。左右に高さ12mの台、中央に柱
            Solid(-90, 90, 0, 50, 1408, 1600, RockDark, floor: true);
            Solid(-94, -90, 50, 300, 1404, 1604, Rock);
            Solid(90, 94, 50, 300, 1404, 1604, Rock);
            Solid(-94, 94, 50, 300, 1600, 1604, Rock);
            Solid(-80, -50, 50, 62, 1440, 1480, Ledge);
            Solid(50, 80, 50, 62, 1520, 1560, Ledge);
            Solid(-20, -8, 50, 90, 1500, 1512, Metal);
            Solid(8, 20, 50, 90, 1540, 1552, Metal);
            AddArea("溶鉱炉跡", new Bounds(new Vector3(0, 175, 1504), new Vector3(180, 250, 192)), new Vector3(0, 50.2f, 1415), null, () =>
            {
                var boss = game.SpawnBoss(new Vector3(0, 50, 1560), 2.6f, 3600f, new Color(0.15f, 0.35f, 0.42f));
                boss.moveSpeed = 5.2f;      // 1体目より大きく、速く、振りが早い
                boss.meleeWindup = 1.0f;
                spawningArea.enemies.Add(boss);
                ProtoFx.Blast(boss.transform.position + Vector3.up * game.mechHeight * boss.Scale * 0.5f, 14f, new Color(0.5f, 0.8f, 1f), 0.5f);
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(-62, 62, 1460));
                Spawn(ProtoEnemy.Kind.Normal, new Vector3(62, 62, 1540));
            });
        }

        // ───────── 宝箱:区画ごとに1〜2個、歩ける(か飛んで届く)平らな場所にランダムで置く ─────────

        void PlaceChests()
        {
            Physics.SyncTransforms();
            var placed = new List<Vector3>();
            foreach (var a in areas)
            {
                int want = Random.Range(1, 3);
                for (int tries = 0; tries < 60 && want > 0; tries++)
                {
                    Bounds b = a.bounds;
                    float x = Random.Range(b.min.x + 8f, b.max.x - 8f), z = Random.Range(b.min.z + 8f, b.max.z - 8f);
                    var cands = new List<Vector3>();
                    foreach (var h in Physics.RaycastAll(new Vector3(x, b.max.y + 1f, z), Vector3.down, b.size.y + 2f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (!IsStage(h.collider) || h.collider.name == "Door" || h.normal.y < 0.95f) continue;
                        if (h.point.y < b.min.y - 1f || h.point.y > b.max.y - 4f) continue;   // 区画の外(天井の上など)には置かない
                        if (Blocked(h.point + Vector3.up * 0.3f, h.point + Vector3.up * 10f)) continue;   // 上が詰まっている(床の中)
                        cands.Add(h.point);
                    }
                    if (cands.Count == 0) continue;
                    Vector3 pt = cands[Random.Range(0, cands.Count)];
                    if (Vector3.Distance(pt, a.checkpoint) < 20f || placed.Exists(q => Vector3.Distance(q, pt) < 15f)) continue;
                    placed.Add(pt);
                    ProtoChest.Create(pt, transform);
                    want--;
                }
            }
        }

        int stageNo = 1;

        void AddArea(string name, Bounds b, Vector3 checkpoint, GameObject door, System.Action spawn)
        {
            areas.Add(new Area { name = name, stage = stageNo, bounds = b, checkpoint = checkpoint, door = door, spawn = spawn });
        }

        Area spawningArea;

        void Spawn(ProtoEnemy.Kind kind, Vector3 pos, PartType? type = null, bool hold = false)
        {
            var e = game.SpawnForStage(kind, pos, type);
            if (e == null) return;
            e.hold = hold;
            spawningArea.enemies.Add(e);
            ProtoFx.Blast(pos + Vector3.up * game.mechHeight * 0.5f * e.Scale, 6f * e.Scale, new Color(0.5f, 0.8f, 1f), 0.4f);
        }

        // ───────── 進行 ─────────

        void Update()
        {
            var p = ProtoPlayer.I;
            if (p == null || current >= areas.Count) return;
            var a = areas[current];
            if (!a.entered)
            {
                if (!a.bounds.Contains(p.transform.position + Vector3.up * 2f)) return;
                a.entered = true;
                p.SetCheckpoint(a.checkpoint);
                spawningArea = a;
                a.spawn?.Invoke();
                game.Log($"エリア {current + 1}/{areas.Count}:{a.name}");
                return;
            }
            a.enemies.RemoveAll(e => e == null || e.Dead);
            if (a.enemies.Count > 0) return;
            a.cleared = true;
            a.clearTime = game.Elapsed;
            if (a.door != null)
            {
                StartCoroutine(OpenDoor(a.door));
                game.Log($"{a.name} 制圧 ― 扉が開いた");
            }
            current++;
            if (current < areas.Count && areas[current].stage != a.stage)
                game.Log($"ステージ{a.stage} クリア ― 扉の先はステージ{areas[current].stage}");
            if (current >= areas.Count)
            {
                Cleared = true;
                game.OnStageClear();
            }
        }

        IEnumerator OpenDoor(GameObject door)
        {
            // 上へ縮みながら消える(壁の上側に吸い込まれる見た目)
            var t = door.transform;
            Vector3 s0 = t.localScale, p0 = t.position;
            float top = p0.y + s0.y * 0.5f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 1.2f)
            {
                float h = s0.y * (1f - k);
                t.localScale = new Vector3(s0.x, Mathf.Max(0.01f, h), s0.z);
                t.position = new Vector3(p0.x, top - h * 0.5f, p0.z);
                yield return null;
            }
            Destroy(door);
        }

        public string StatusText
        {
            get
            {
                if (Cleared) return "クリア";
                var a = areas[Mathf.Min(current, areas.Count - 1)];
                int first = areas.FindIndex(x => x.stage == a.stage);
                int count = areas.FindAll(x => x.stage == a.stage).Count;
                string n = $"ステージ{a.stage}  エリア {current - first + 1}/{count} {a.name}";
                return a.entered ? $"{n}  残り{a.enemies.Count}" : n;
            }
        }

        /// <summary>結果画面用:区画ごとの制圧時刻。</summary>
        public IEnumerable<(string name, float time)> ClearTimes()
        {
            foreach (var a in areas) yield return (a.name, a.clearTime);
        }

        // ───────── 箱の組み立て ─────────

        void BuildGrid()
        {
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, true);
            var line = new Color(0.72f, 0.72f, 0.72f);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    tex.SetPixel(x, y, (x < 3 || y < 3) ? line : Color.white);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 8;
            gridTex = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gridTex.SetTexture("_BaseMap", tex);
        }

        /// <summary>最小・最大の座標で箱を置く。10m ごとの格子を貼って、大きさと速さが分かるようにする。</summary>
        GameObject Solid(float x0, float x1, float y0, float y1, float z0, float z1, Color c, bool floor = false, bool shadow = true)
        {
            var center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            var size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
            return Box(center, size, Quaternion.identity, c, shadow);
        }

        GameObject Box(Vector3 center, Vector3 size, Quaternion rot, Color c, bool shadow = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Block";
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = gridTex;
            if (!shadow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            float u = Mathf.Max(size.x, size.z) / 10f, v = Mathf.Max(Mathf.Min(size.x, size.z), size.y) / 10f;
            mpb.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(1f, u), Mathf.Max(1f, v), 0f, 0f));
            r.SetPropertyBlock(mpb);
            return go;
        }

        /// <summary>
        /// 坂。既定は z0→z1 に向かって y0→y1 へ登る(alongX=false)。
        /// sideways=true のときは x0→x1 に向かって登る(横から登る坂)。
        /// </summary>
        void Ramp(float x0, float x1, float z0, float z1, float y0, float y1, Color c, bool sideways = false, bool alongX = false)
        {
            float rise = y1 - y0;
            if (sideways)
            {
                float run = x1 - x0;
                float len = Mathf.Sqrt(run * run + rise * rise);
                float ang = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
                var center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f - 1f, (z0 + z1) * 0.5f);
                Box(center, new Vector3(len, 2f, Mathf.Abs(z1 - z0)), Quaternion.Euler(0f, 0f, ang), c);
            }
            else
            {
                float run = z1 - z0;
                float len = Mathf.Sqrt(run * run + rise * rise);
                float ang = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
                var center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f - 1f, (z0 + z1) * 0.5f);
                Box(center, new Vector3(Mathf.Abs(x1 - x0), 2f, len), Quaternion.Euler(-ang, 0f, 0f), c);
            }
        }

        /// <summary>z の位置に立つ壁(厚さ4m)。開口部(出口)を残して組み、開口部には扉を置いて返す。</summary>
        GameObject WallZ(float z, float x0, float x1, float y0, float y1, float ox0, float ox1, float oy0, float oy1, Color c)
        {
            float za = z, zb = z + 4f;
            Solid(x0, ox0, y0, y1, za, zb, c);
            Solid(ox1, x1, y0, y1, za, zb, c);
            if (oy0 > y0) Solid(ox0, ox1, y0, oy0, za, zb, c);
            if (oy1 < y1) Solid(ox0, ox1, oy1, y1, za, zb, c);
            var door = Solid(ox0, ox1, oy0, oy1, za + 1f, zb - 1f, Door);
            door.name = "Door";
            return door;
        }
    }
}
