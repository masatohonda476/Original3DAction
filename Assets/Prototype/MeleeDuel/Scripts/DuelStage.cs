using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作のステージ:区画を進む一本道。途中で2本(A・B)に分かれ、ボスの手前で合流する。
    /// 地形と敵の配置はこのクラスの Define() に書き、実行時に組み立てる(シーンには DuelGame とこれを置くだけ)。
    /// 区画の流れ(扉は前の試作の形で、仮):
    ///   自機が区画に入る → 入口の扉が閉じ、待機していた敵が動き出す → 区画の敵を全滅させる → 次への扉が開く。
    /// 敵は区画ごとに位置が決まっていて、波で湧かず、倒したら復活しない。
    /// 一度に動き出す数を絞るため、敵ごとに「近づいたら動き出す」「残りが n 体まで減ったら動き出す」を付けられる。
    /// 撃墜されたら、いる区画の入口からやり直す(その区画の敵は置き直す)。F1 は最初から。
    /// ボスを倒すと「ステージクリア」。
    /// </summary>
    public class DuelStage : MonoBehaviour
    {
        [Header("見た目(シーンを組み立てるときに入る。なければ仮の色)")]
        public Material groundMat;
        public Material wallMat;

        [Header("区画")]
        [Tooltip("区画の縁からこの距離(m)だけ内側へ入ったら、区画に入ったとみなす")]
        public float enterMargin = 5f;
        [Tooltip("区画に入ったら入口の扉を閉じる(ボス区画は常に閉じる)")]
        public bool closeEntryOnEnter = true;
        [Tooltip("撃墜されてから、区画の入口でやり直すまでの秒数")]
        public float respawnDelay = 2.5f;
        [Tooltip("やり直した後、敵が動き出すまでの秒数")]
        public float respawnGrace = 1.2f;

        [Header("ボス(大剣士)")]
        public float bossHP = 520f;
        [Tooltip("機体の大きさの倍率(剣士は1.5)")]
        public float bossSize = 2.5f;
        [Tooltip("怯みゲージの最大(剣士は25)。大きいほど怯みにくい")]
        public float bossMaxStagger = 70f;
        public DuelEnemy.BossMoves bossMoves = new();

        public enum Kind { Grunt, Duelist, Boss }

        /// <summary>区画と区画の間の帯の幅(両側の壁の厚み)。扉はこの帯に立つ。</summary>
        const float Band = 4f;
        const float WallThick = Band * 0.5f;
        /// <summary>見えない天井の高さ。見えない壁はここまで伸びる(飛んで越えられない)。</summary>
        const float CeilingY = 120f;

        static readonly Color WallColor = new(0.42f, 0.44f, 0.47f), PillarColor = new(0.5f, 0.5f, 0.53f),
            CoverColor = new(0.55f, 0.5f, 0.42f), LedgeColor = new(0.55f, 0.5f, 0.38f), DoorColor = new(0.8f, 0.28f, 0.16f),
            GroundColor = new(0.32f, 0.33f, 0.31f);

        enum Side { North, South, East, West }
        enum ZoneState { Waiting, Active, Cleared }

        class Spawn
        {
            public Kind kind;
            public Vector3 pos;
            /// <summary>自機がこの距離(m)まで近づいたら動き出す。0 以下なら使わない。</summary>
            public float wakeDistance;
            /// <summary>区画の残り(これと同じく残りを待っている敵を除く)がこの数以下になったら動き出す。負なら使わない。</summary>
            public int wakeWhenLeft = -1;
            /// <summary>持ち場の半径(0 なら制限なし)。</summary>
            public float leash;
            public DuelEnemy enemy;
            /// <summary>どちらの条件もなければ、区画に入った時点で動き出す。</summary>
            public bool WakeOnEntry => wakeDistance <= 0f && wakeWhenLeft < 0;
        }

        class Zone
        {
            public string name;
            public float x0, x1, z0, z1, wallHeight;
            public bool boss;
            /// <summary>区画に入ってから敵が動き出すまでの秒数(ボスの登場の間)。</summary>
            public float wakeDelay;
            public readonly List<Spawn> spawns = new();
            public ZoneState state;
            public float wakeAt;
            public Vector3 respawnPos;
            public Quaternion respawnRot = Quaternion.identity;

            public bool Contains(Vector3 p, float margin) =>
                p.x > x0 + margin && p.x < x1 - margin && p.z > z0 + margin && p.z < z1 - margin;
            public Vector3 Center => new((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
        }

        /// <summary>区画 a から b への扉(a の side の壁にある)。</summary>
        class Link
        {
            public Zone a, b;
            public Side side;
            public float center, width;
            public DuelDoor door;
        }

        struct Block
        {
            public Vector3 min, max;
            public Color color;
            /// <summary>上に見えない壁を天井まで伸ばす(柱の上に乗れない)。</summary>
            public bool tall;
        }

        /// <summary>+z へ上る坂。</summary>
        struct Ramp
        {
            public float x0, x1, z0, z1, height;
        }

        readonly List<Zone> zones = new();
        readonly List<Link> links = new();
        readonly List<Block> blocks = new();
        readonly List<Ramp> ramps = new();

        public Vector3 StartPos { get; private set; }
        public Quaternion StartRot { get; private set; } = Quaternion.identity;

        DuelGame game;
        DuelPlayer player;
        Zone current;
        Spawn bossSpawn;
        float respawnAt = -1f, beganAt;
        bool built;

        /// <summary>ボスを倒した。</summary>
        public bool Cleared { get; private set; }
        /// <summary>ボスを倒すまでにかかった秒数。</summary>
        public float ClearTime { get; private set; }
        /// <summary>画面の中央に少しの間出す知らせ(区画名など)と、出した時刻。</summary>
        public string Banner { get; private set; }
        public float BannerAt { get; private set; } = -10f;

        // ───────── 地形と敵の配置 ─────────

        /// <summary>
        /// ステージの形。座標は x が左右、z が奥。区画どうしは Band(4m)の帯をはさんで並べ、帯に扉を置く。
        /// 敵の書き方:G = 兵卒、D = 剣士、Boss = ボス。wake = 近づいたら動き出す距離、left = 残りがこの数まで減ったら動き出す、leash = 持ち場の半径。
        /// </summary>
        void Define()
        {
            zones.Clear();
            links.Clear();
            blocks.Clear();
            ramps.Clear();
            bossSpawn = null;
            StartPos = new Vector3(0f, 0f, 8f);
            StartRot = Quaternion.identity;

            // 第1区画:広め。柱2本。兵卒2体は近づくと動き出し、3体目は1体まで減ったら来る
            var z1 = AddZone("第1区画", -30, 30, 0, 60);
            Pillar(-14, 30, 6, 6);
            Pillar(14, 40, 6, 6);
            G(z1, -8, 44, wake: 30);
            G(z1, 10, 50, wake: 30);
            G(z1, 0, 56, left: 1);

            // 第2区画:狭い通路(幅22m)。奥で剣士が1体待つ
            var z2 = AddZone("第2区画", -11, 11, 64, 144);
            Connect(z1, z2, 0);
            D(z2, 0, 128);

            // 第3区画:高低差。左奥に高さ5mの台(手前の左端に坂)。床に兵卒2、台の上に持ち場から動かない兵卒2
            var z3 = AddZone("第3区画", -40, 40, 148, 218);
            Connect(z2, z3, 0);
            Platform(-40, -2, 180, 218, 5f);
            RampZ(-40, -28, 162, 180, 5f);
            Pillar(31, 180, 6, 6);
            Cover(6, 168, 14, 3);
            G(z3, 10, 190);
            G(z3, 26, 204);
            G(z3, -14, 200, y: 5f, leash: 8f);
            G(z3, -28, 208, y: 5f, leash: 8f);

            // 分岐点:敵はいない(一息)。左右に扉
            var z4 = AddZone("分岐点", -10, 50, 222, 262);
            Connect(z3, z4, 20);

            // 分岐A:兵卒の群れが多い、開けた道。低い遮蔽物の間で群れをさばく
            var a1 = AddZone("分岐A-1", -104, -14, 222, 302);
            Connect(z4, a1, 242);
            Cover(-40, 236, 3, 14);
            Cover(-70, 260, 14, 3);
            Cover(-50, 284, 3, 12);
            Pillar(-88, 246, 6, 6);
            G(a1, -50, 245);
            G(a1, -56, 267);
            G(a1, -44, 275);
            G(a1, -90, 272, left: 1);
            G(a1, -85, 232, left: 1);

            var a2 = AddZone("分岐A-2", -100, -30, 306, 366);
            Connect(a1, a2, -60);
            Pillar(-78, 330, 6, 6);
            Pillar(-52, 344, 6, 6);
            G(a2, -70, 340);
            G(a2, -45, 330);
            G(a2, -88, 356, left: 1);
            G(a2, -42, 358, left: 1);

            // 分岐B:剣士が待つ狭い道(幅22m)。壁から張り出した柱で、ところどころさらに狭い
            var b1 = AddZone("分岐B-1", 54, 134, 232, 254);
            Connect(z4, b1, 242);
            Pillar(100, 251, 6, 6);
            D(b1, 120, 243);

            var b2 = AddZone("分岐B-2", 112, 134, 258, 338);
            Connect(b1, b2, 123);
            Pillar(115, 290, 6, 6);
            Pillar(131, 312, 6, 6);
            D(b2, 123, 300);
            D(b2, 123, 326, left: 0);   // 1体目を倒したら2体目

            // 合流点:敵はいない(ボスの前の一息)。A・B どちらからも入れる
            var m = AddZone("合流点", -26, 140, 342, 382);
            Connect(a2, m, 354);
            Connect(b2, m, 123);
            Pillar(30, 362, 6, 6);
            Pillar(84, 362, 6, 6);

            // ボス区画:広い四角の部屋。四隅に柱(突進を柱にぶつけられる)。入ると入口が閉じ、少しして動き出す
            var bz = AddZone("ボス区画", 7, 107, 386, 486, wallHeight: 30f, boss: true, wakeDelay: 1.5f);
            Connect(m, bz, 57);
            Pillar(20, 399, 8, 8, 30f);
            Pillar(94, 399, 8, 8, 30f);
            Pillar(20, 473, 8, 8, 30f);
            Pillar(94, 473, 8, 8, 30f);
            bossSpawn = Add(bz, Kind.Boss, 57, 450, 0f, 0f, -1, 0f);
        }

        Zone AddZone(string name, float x0, float x1, float z0, float z1, float wallHeight = 14f, bool boss = false, float wakeDelay = 0f)
        {
            var z = new Zone { name = name, x0 = x0, x1 = x1, z0 = z0, z1 = z1, wallHeight = wallHeight, boss = boss, wakeDelay = wakeDelay };
            z.respawnPos = z.Center;
            zones.Add(z);
            return z;
        }

        /// <summary>区画 a から b への扉。center は扉のある壁に沿った位置(南北の壁なら x、東西の壁なら z)。</summary>
        void Connect(Zone a, Zone b, float center, float width = 16f)
        {
            Side s;
            if (Mathf.Abs(a.z1 + Band - b.z0) < 0.01f) s = Side.North;
            else if (Mathf.Abs(a.z0 - Band - b.z1) < 0.01f) s = Side.South;
            else if (Mathf.Abs(a.x1 + Band - b.x0) < 0.01f) s = Side.East;
            else if (Mathf.Abs(a.x0 - Band - b.x1) < 0.01f) s = Side.West;
            else
            {
                Debug.LogError($"[DuelStage] {a.name} と {b.name} が帯をはさんで隣り合っていません");
                return;
            }
            links.Add(new Link { a = a, b = b, side = s, center = center, width = width });
        }

        void G(Zone z, float x, float zz, float wake = 0f, int left = -1, float leash = 0f, float y = 0f) => Add(z, Kind.Grunt, x, zz, y, wake, left, leash);
        void D(Zone z, float x, float zz, float wake = 0f, int left = -1) => Add(z, Kind.Duelist, x, zz, 0f, wake, left, 0f);

        Spawn Add(Zone z, Kind kind, float x, float zz, float y, float wake, int left, float leash)
        {
            var s = new Spawn { kind = kind, pos = new Vector3(x, y, zz), wakeDistance = wake, wakeWhenLeft = left, leash = leash };
            z.spawns.Add(s);
            return s;
        }

        /// <summary>天井まで届く柱(上に乗れない)。x・z は中心。</summary>
        void Pillar(float x, float z, float w, float d, float h = 14f) =>
            blocks.Add(new Block { min = new Vector3(x - w / 2, 0f, z - d / 2), max = new Vector3(x + w / 2, h, z + d / 2), color = PillarColor, tall = true });

        /// <summary>低い遮蔽物(高さ4m。機体の半分ほど)。x・z は中心。</summary>
        void Cover(float x, float z, float w, float d, float h = 4f) =>
            blocks.Add(new Block { min = new Vector3(x - w / 2, 0f, z - d / 2), max = new Vector3(x + w / 2, h, z + d / 2), color = CoverColor });

        /// <summary>上に乗れる台。</summary>
        void Platform(float x0, float x1, float z0, float z1, float h) =>
            blocks.Add(new Block { min = new Vector3(x0, 0f, z0), max = new Vector3(x1, h, z1), color = LedgeColor });

        void RampZ(float x0, float x1, float z0, float z1, float h) =>
            ramps.Add(new Ramp { x0 = x0, x1 = x1, z0 = z0, z1 = z1, height = h });

        // ───────── 組み立て ─────────

        /// <summary>地形を組み立てる(敵はまだ置かない)。自機を出す前に DuelGame から呼ぶ。</summary>
        public void Prepare(DuelGame g)
        {
            game = g;
            if (built) return;
            built = true;
            Define();
            BuildGeometry();
        }

        /// <summary>敵を(待機したまま)全部置いて始める。</summary>
        public void Begin(DuelPlayer p)
        {
            player = p;
            foreach (var z in zones)
                foreach (var s in z.spawns) Place(s, z);
            beganAt = Time.time;
        }

        void BuildGeometry()
        {
            var root = new GameObject("Stage").transform;
            var wallM = wallMat != null ? wallMat : ProtoFx.Lit(WallColor);

            // 床:全区画を覆う1枚(上面が高さ0)
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var z in zones)
            {
                minX = Mathf.Min(minX, z.x0); maxX = Mathf.Max(maxX, z.x1);
                minZ = Mathf.Min(minZ, z.z0); maxZ = Mathf.Max(maxZ, z.z1);
            }
            minX -= 20f; maxX += 20f; minZ -= 20f; maxZ += 20f;
            Box("Ground", new Vector3((minX + maxX) / 2f, -0.5f, (minZ + maxZ) / 2f), new Vector3(maxX - minX, 1f, maxZ - minZ),
                groundMat != null ? groundMat : ProtoFx.Lit(GroundColor), false, root);
            // 見えない天井
            var ceil = new GameObject("Ceiling") { layer = DuelFighter.FighterLayer };
            ceil.transform.SetParent(root, false);
            ceil.transform.position = new Vector3((minX + maxX) / 2f, CeilingY + 1f, (minZ + maxZ) / 2f);
            ceil.AddComponent<BoxCollider>().size = new Vector3(maxX - minX, 2f, maxZ - minZ);

            // 区画の壁(扉のところは空ける)
            foreach (var z in zones)
            {
                var zr = new GameObject(z.name).transform;
                zr.SetParent(root, false);
                foreach (Side side in System.Enum.GetValues(typeof(Side))) BuildWall(z, side, wallM, zr);
            }

            // 扉(閉じた状態)
            foreach (var l in links)
            {
                float h = Mathf.Max(l.a.wallHeight, l.b.wallHeight);
                Vector3 c = DoorPos(l);
                bool ns = l.side is Side.North or Side.South;
                var go = Box($"Door {l.a.name}→{l.b.name}", new Vector3(c.x, h / 2f, c.z),
                    ns ? new Vector3(l.width, h, Band) : new Vector3(Band, h, l.width), ProtoFx.Lit(DoorColor), true, root);
                l.door = go.AddComponent<DuelDoor>();
                l.door.Init(h);
            }

            foreach (var b in blocks)
                Box(b.tall ? "Pillar" : "Block", (b.min + b.max) / 2f, b.max - b.min, ProtoFx.Lit(b.color), b.tall, root);

            foreach (var r in ramps)
            {
                // 上面が (z0, 高さ0) から (z1, height) へ上る板
                float run = r.z1 - r.z0;
                float len = Mathf.Sqrt(run * run + r.height * r.height);
                var rot = Quaternion.Euler(-Mathf.Atan2(r.height, run) * Mathf.Rad2Deg, 0f, 0f);
                Vector3 topMid = new((r.x0 + r.x1) / 2f, r.height / 2f, (r.z0 + r.z1) / 2f);
                var go = Box("Ramp", topMid - rot * Vector3.up * 0.5f, new Vector3(r.x1 - r.x0, 1f, len), ProtoFx.Lit(LedgeColor), false, root);
                go.transform.rotation = rot;
            }
            Physics.SyncTransforms();   // 置いたばかりの地形に、すぐレイ(足もとの高さ)が当たるように
        }

        /// <summary>区画の1辺の壁。壁は区画の外側(帯の半分)に立て、扉の幅だけ空ける。南北の壁は角を埋めるため両端へ伸ばす。</summary>
        void BuildWall(Zone z, Side side, Material mat, Transform parent)
        {
            bool ns = side is Side.North or Side.South;
            float from = ns ? z.x0 - WallThick : z.z0;
            float to = ns ? z.x1 + WallThick : z.z1;
            float line = side switch
            {
                Side.North => z.z1 + WallThick / 2f,
                Side.South => z.z0 - WallThick / 2f,
                Side.East => z.x1 + WallThick / 2f,
                _ => z.x0 - WallThick / 2f,
            };
            var gaps = new List<(float lo, float hi)>();
            foreach (var l in links)
            {
                if ((l.a == z && l.side == side) || (l.b == z && Opposite(l.side) == side))
                    gaps.Add((l.center - l.width / 2f, l.center + l.width / 2f));
            }
            gaps.Sort((p, q) => p.lo.CompareTo(q.lo));
            float h = z.wallHeight;
            float cursor = from;
            void Segment(float lo, float hi)
            {
                if (hi - lo < 0.01f) return;
                float mid = (lo + hi) / 2f, len = hi - lo;
                Box("Wall", ns ? new Vector3(mid, h / 2f, line) : new Vector3(line, h / 2f, mid),
                    ns ? new Vector3(len, h, WallThick) : new Vector3(WallThick, h, len), mat, true, parent);
            }
            foreach (var (lo, hi) in gaps)
            {
                Segment(cursor, lo);
                cursor = Mathf.Max(cursor, hi);
            }
            Segment(cursor, to);
        }

        static Side Opposite(Side s) => s switch { Side.North => Side.South, Side.South => Side.North, Side.East => Side.West, _ => Side.East };

        /// <summary>扉の真ん中(床の高さ)。</summary>
        static Vector3 DoorPos(Link l) => l.side switch
        {
            Side.North => new Vector3(l.center, 0f, l.a.z1 + Band / 2f),
            Side.South => new Vector3(l.center, 0f, l.a.z0 - Band / 2f),
            Side.East => new Vector3(l.a.x1 + Band / 2f, 0f, l.center),
            _ => new Vector3(l.a.x0 - Band / 2f, 0f, l.center),
        };

        /// <summary>扉から区画 b の中へ向かう向き。</summary>
        static Vector3 Inward(Link l) => l.side switch
        {
            Side.North => Vector3.forward,
            Side.South => Vector3.back,
            Side.East => Vector3.right,
            _ => Vector3.left,
        };

        /// <summary>箱を置く。barrier なら、見える部分の上から天井まで見えない壁を伸ばす(機体だけが当たり、レイ・カメラは素通り)。</summary>
        static GameObject Box(string name, Vector3 center, Vector3 size, Material mat, bool barrier, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (barrier)
            {
                float top = center.y + size.y / 2f;
                float extra = CeilingY - top;
                if (extra > 0f)
                {
                    var b = new GameObject("Barrier") { layer = DuelFighter.FighterLayer };
                    b.transform.SetParent(go.transform, false);
                    var bc = b.AddComponent<BoxCollider>();
                    float ly = extra / size.y;   // 親の大きさで割った、ローカルの高さ
                    bc.center = new Vector3(0f, 0.5f + ly / 2f, 0f);
                    bc.size = new Vector3(1f, ly, 1f);
                }
            }
            return go;
        }

        // ───────── 敵 ─────────

        /// <summary>敵を待機した状態で置く(区画の入口のほうを向く)。</summary>
        void Place(Spawn s, Zone z)
        {
            Vector3 lookAt = StartPos;
            foreach (var l in links)
                if (l.b == z) { lookAt = DoorPos(l); break; }
            Vector3 dir = lookAt - s.pos;
            dir.y = 0f;
            var go = new GameObject($"{z.name} {KindName(s.kind)}");
            go.transform.SetPositionAndRotation(s.pos, dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity);
            var e = go.AddComponent<DuelEnemy>();
            e.Init(game, player, s.kind switch
            {
                Kind.Duelist => DuelEnemy.Duelist(game.enemyHP),
                Kind.Boss => DuelEnemy.Boss(bossHP, bossMoves, bossSize, bossMaxStagger),
                _ => DuelEnemy.Grunt(game.gruntHP),
            });
            e.Dormant = true;
            e.home = s.pos;
            e.leashRadius = s.leash;
            s.enemy = e;
        }

        static string KindName(Kind k) => k switch { Kind.Duelist => "剣士", Kind.Boss => "大剣士", _ => "兵卒" };

        void WakeUp(Spawn s)
        {
            s.enemy.Wake();
            game.Enemies.Add(s.enemy);
            player.AutoLock(s.enemy);   // ロックオンしていなければ、最初に動き出した敵に合わせる
        }

        // ───────── 進行 ─────────

        void Update()
        {
            if (game == null || player == null) return;
            if (respawnAt > 0f && Time.unscaledTime >= respawnAt)
            {
                respawnAt = -1f;
                RespawnInZone();
            }
            if (player.Dead) return;

            Vector3 p = player.transform.position;
            foreach (var z in zones)
                if (z.state == ZoneState.Waiting && z.Contains(p, enterMargin)) { EnterZone(z); break; }
            if (current != null && current.state == ZoneState.Active) UpdateZone(current);
        }

        void EnterZone(Zone z)
        {
            current = z;
            // やり直す場所:通ってきた入口の扉のすぐ内側(入口のない最初の区画は出撃地点)
            Link entry = null;
            float best = float.MaxValue;
            foreach (var l in links)
            {
                if (l.b != z) continue;
                float d = (DoorPos(l) - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; entry = l; }
            }
            if (entry != null)
            {
                z.respawnPos = DoorPos(entry) + Inward(entry) * (Band / 2f + 8f);
                z.respawnRot = Quaternion.LookRotation(Inward(entry));
            }
            else
            {
                z.respawnPos = StartPos;
                z.respawnRot = StartRot;
            }
            if (closeEntryOnEnter || z.boss)
                foreach (var l in links)
                    if (l.b == z && l.door != null) l.door.Close();
            ShowBanner(z.name);
            if (z.spawns.Count == 0) { ClearZone(z); return; }
            z.state = ZoneState.Active;
            z.wakeAt = Time.time + z.wakeDelay;
            game.Log(z.boss ? $"{z.name}:入口が閉じた" : $"{z.name}:敵 {z.spawns.Count} 体");
        }

        void UpdateZone(Zone z)
        {
            if (Time.time < z.wakeAt) return;
            int alive = 0, others = 0;
            foreach (var s in z.spawns)
            {
                if (s.enemy == null || s.enemy.Dead) continue;
                alive++;
                if (!(s.enemy.Dormant && s.wakeWhenLeft >= 0)) others++;   // 残りを待っている敵どうしは数えない
            }
            if (alive == 0) { ClearZone(z); return; }

            Vector3 p = player.transform.position;
            foreach (var s in z.spawns)
            {
                if (s.enemy == null || s.enemy.Dead || !s.enemy.Dormant) continue;
                Vector3 d = s.pos - p;
                d.y = 0f;
                bool wake = s.WakeOnEntry
                            || (s.wakeDistance > 0f && d.magnitude <= s.wakeDistance)
                            || (s.wakeWhenLeft >= 0 && others <= s.wakeWhenLeft);
                if (!wake) continue;
                WakeUp(s);
                if (s.wakeWhenLeft >= 0) others++;
            }
        }

        void ClearZone(Zone z)
        {
            z.state = ZoneState.Cleared;
            foreach (var l in links)
                if (l.a == z && l.door != null) l.door.Open();
            if (z.boss)
            {
                Cleared = true;
                ClearTime = Time.time - beganAt;
                ShowBanner("ステージクリア");
                game.Log("ボスを倒した");
                return;
            }
            if (z.spawns.Count > 0) game.Log($"{z.name}を制圧 ― 扉が開いた");
            else
            {
                int exits = 0;
                foreach (var l in links) if (l.a == z) exits++;
                if (exits > 1) game.Log("道が分かれている");
            }
        }

        void ShowBanner(string text)
        {
            Banner = text;
            BannerAt = Time.unscaledTime;
        }

        public void OnPlayerDied() => respawnAt = Time.unscaledTime + respawnDelay;

        /// <summary>いる区画の入口からやり直す。区画が終わっていなければ、その区画の敵を全部置き直す。</summary>
        void RespawnInZone()
        {
            var z = current;
            if (z == null) return;
            if (z.state == ZoneState.Active)
            {
                foreach (var s in z.spawns)
                {
                    if (s.enemy != null)
                    {
                        game.Enemies.Remove(s.enemy);
                        Destroy(s.enemy.gameObject);
                    }
                    Place(s, z);
                }
                z.wakeAt = Time.time + respawnGrace;
            }
            player.Respawn(z.respawnPos, z.respawnRot);
            ShowBanner(z.name);
            game.Log($"{z.name}の入口からやり直し");
        }

        // ───────── 画面に出す情報 ─────────

        public string ZoneName => current != null ? current.name : "";

        /// <summary>区画の残りの敵の数と様子。</summary>
        public string ZoneStatus
        {
            get
            {
                if (current == null) return "";
                if (current.state == ZoneState.Cleared)
                    return Cleared ? "ステージクリア" : current.spawns.Count > 0 ? "制圧 ― 次の扉が開いた" : "敵なし";
                int n = 0;
                foreach (var s in current.spawns) if (s.enemy != null && !s.enemy.Dead) n++;
                return $"残りの敵 {n}";
            }
        }

        /// <summary>動いているボス(いなければ null)。</summary>
        public DuelEnemy ActiveBoss =>
            bossSpawn != null && bossSpawn.enemy != null && !bossSpawn.enemy.Dead && !bossSpawn.enemy.Dormant ? bossSpawn.enemy : null;

        // ───────── シーンビューでの確認 ─────────

        void OnDrawGizmos()
        {
            if (zones.Count == 0) Define();
            foreach (var z in zones)
            {
                Gizmos.color = z.boss ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 0.9f, 1f);
                Gizmos.DrawWireCube(new Vector3((z.x0 + z.x1) / 2f, z.wallHeight / 2f, (z.z0 + z.z1) / 2f), new Vector3(z.x1 - z.x0, z.wallHeight, z.z1 - z.z0));
                foreach (var s in z.spawns)
                {
                    Gizmos.color = s.kind switch { Kind.Boss => Color.red, Kind.Duelist => new Color(1f, 0.6f, 0.2f), _ => Color.yellow };
                    float r = s.kind switch { Kind.Boss => 5f, Kind.Duelist => 3f, _ => 2f };
                    Gizmos.DrawWireSphere(s.pos + Vector3.up * r, r);
                    if (s.leash > 0f) Gizmos.DrawWireCube(s.pos, new Vector3(s.leash * 2f, 0.1f, s.leash * 2f));
                }
            }
            Gizmos.color = new Color(1f, 0.5f, 0.2f);
            foreach (var l in links) Gizmos.DrawCube(DoorPos(l) + Vector3.up * 3f, new Vector3(3f, 6f, 3f));
            foreach (var b in blocks)
            {
                Gizmos.color = b.color;
                Gizmos.DrawWireCube((b.min + b.max) / 2f, b.max - b.min);
            }
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(StartPos + Vector3.up * 2f, 2f);
        }
    }
}
