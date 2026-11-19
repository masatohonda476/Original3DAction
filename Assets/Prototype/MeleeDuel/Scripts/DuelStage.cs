using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作のステージ:墜落して地面に斜めに突き刺さった、横倒しの巨大な船の中を、下から上へ登る。区画の区切りはなく、ひと続きの地形。
    ///   地面の裂け目(入口・最下層。開けている)→ 横倒しの居住区(元の壁が床になった狭い部屋が3つ。ドアの穴が床・天井にある)→
    ///   大きな竪穴(張り出した足場・折れた梁をブースト飛行で上る。足場に敵)→ 分岐:外殻を伝う外の道(開けた斜面)/ 内部の配管の道(狭いダクト)→
    ///   合流(機関区画。ボスの前に群れが待つ。拾った武装で暴れる所)→ 頭頂部の大ホール(天井が裂けている。ボス)。
    /// 敵は持ち場で立ち番し、発覚の仕組み(DuelSenses)で気づいてから戦う。倒した敵は復活しない。
    /// セーブポイント(光る柱)に触れると再開地点が変わる。撃墜されたら最後に触れたセーブポイントから再開し、
    /// 生きている敵は HP を戻して持ち場へ帰す(発覚・警戒も切る)。F1 は最初から。ボスを倒すと「ステージクリア」。
    /// 地形と敵の配置はこのクラスの Define() に書き、実行時に組み立てる(シーンには DuelGame とこれを置くだけ)。
    /// 「重厚→無双」の弧ひとつ分:倒した敵がときどき武装を落とし(上ほどレア度の高い武装)、十字右で付け替えながら強くなる(DuelArms)。
    /// 撃墜されても武装・所持品・共通の弾は残る。ボスを倒すと爆発で武装も所持品も全部消し飛び、弾も0になってクリア。
    /// 地形と敵の配置(Define)とシーンビューのギズモは DuelStage.Layout.cs。
    /// </summary>
    public partial class DuelStage : MonoBehaviour
    {
        [Header("見た目(シーンを組み立てるときに入る。なければ仮の色)")]
        public Material groundMat;
        public Material wallMat;
        [Tooltip("地形が影を落とすか。オフなら天井のある部屋の中にも日が当たって見やすい")]
        public bool terrainShadows;

        [Header("発覚(敵が自機に気づく仕組み)")]
        public DuelSenses senses = new();
        [Tooltip("ロックオンできる距離(m)。壁・床ごしの敵はロックオンしない")]
        public float lockRange = 130f;
        [Tooltip("自動でロックオンする距離(m)。ロックオンがオンで狙う敵がいないとき、画面に映っていて壁・床ごしでなく、この距離の内の敵がいれば自動で付く" +
                 "(画面の中央に近く、近い敵ほど先。敵が気づいたかどうかは見ない)。撃破・見失ったあとの移り先も同じ条件。オンにした瞬間・切り替えでは lockRange まで付けられる")]
        public float autoLockRange = 40f;

        [Header("マップ")]
        [Tooltip("ステージに入ったときに一度だけ画面の中央に出すマップ名(場所ごとの名前は出さない)")]
        public string mapName = "墜落船";

        [Header("セーブポイント")]
        [Tooltip("触れたとみなす水平の距離(m)")]
        public float saveRadius = 14f;
        [Tooltip("触れたら HP とガードを満タンにする(作者に未確認。切るなら外す)")]
        public bool saveHeals = true;
        [Tooltip("撃墜されてから、セーブポイントで再開するまでの秒数")]
        public float respawnDelay = 2.5f;

        /// <summary>
        /// 落ちる武装の一覧(付ける所・レア度・性能)。シリアライズせず、いつも DuelWeapon.Defaults() から作る(数値はコードで直す)。
        /// シリアライズすると、シーンを開いたまま DuelWeapon の形が変わったときにメモリ上の古い一覧が残り、
        /// 新しい項目が既定値のまま(例:kind が 0=剣)になって、スラスターや肩の銃が剣として落ちたため。
        /// </summary>
        [System.NonSerialized] public List<DuelWeapon> arsenal = DuelWeapon.Defaults();
        [Header("武装の落とし物(弧ひとつ分の試作)")]
        [Tooltip("倒した敵が武装を落とす確率:兵卒 / 剣士")]
        [Range(0f, 1f)] public float gruntDropChance = 0.45f;
        [Range(0f, 1f)] public float duelistDropChance = 1f;
        [Tooltip("この数だけ続けて落ちなかったら、次に倒した敵は必ず落とす")]
        public int pityKills = 2;
        [Tooltip("空いている所(最初は左手・背中・両肩)に付けられる武装の出やすさ(倍)。最初は左手が空なので、盾・手の銃・剣が出やすい")]
        public float emptySlotWeight = 2f;
        [Tooltip("今付けているのと同じ武装の出やすさ(倍)")]
        public float sameWeaponWeight = 0.3f;
        [Tooltip("高さごとのレア度の重み(倒した所の高さで選ぶ。上ほど強い武装)")]
        public DuelDropBand[] dropBands =
        {
            new(0f, 1f, 0f, 0f),        // 地面の裂け目・居住区の下:コモンだけ
            new(30f, 0.65f, 0.35f, 0f), // 居住区の上・竪穴の下
            new(85f, 0.3f, 0.55f, 0.15f),// 竪穴の上
            new(120f, 0.15f, 0.5f, 0.35f),// 外殻・配管
            new(155f, 0f, 0.35f, 0.65f), // 機関区画(ボスの前の群れ)
        };
        [Tooltip("照準の枠・重さと所持・共通の弾・分解の決まり(数値は仮)")]
        public DuelArmsRules armsRules = new();
        [Tooltip("ボスを倒したときの爆発の大きさ(m)。爆発で武装(所持品も)が全部消し飛び、共通の弾も0になる(画面は揺らさない)")]
        public float bossBlastRadius = 45f;

        [Header("ボス(大剣士)")]
        [Tooltip("武装で強くなった自機と渡り合える硬さ(仮。武装なしの頃は182)")]
        public float bossHP = 480f;
        [Tooltip("射撃(肩の武装)でボスが受けるダメージと怯みの倍率(厚い装甲。1なら減らない)。ミサイル・銃が全部ボスに集まって溶けないように")]
        [Range(0f, 1f)] public float bossRangedTaken = 0.4f;
        [Tooltip("機体の大きさの倍率(剣士は1.5)")]
        public float bossSize = 2.5f;
        [Tooltip("怯みゲージの最大(剣士は25)。大きいほど怯みにくい")]
        public float bossMaxStagger = 70f;
        [Tooltip("ボスが歩いて出ない、持ち場からの半径(m)。0 なら制限なし(大ホールは見えない壁で囲まれ、入口はボスの背より低いので出られない)")]
        public float bossLeash;
        [Tooltip("自機・敵がこの高さ(m)より下へ落ちたら、自機は最後のセーブポイントから再開、敵は持ち場へ戻す(地形のすき間から落ちたときの保険)")]
        public float fallLimitY = -30f;
        public DuelEnemy.BossMoves bossMoves = new();

        public enum Kind { Grunt, Duelist, Boss }

        /// <summary>壁の厚み。</summary>
        const float T = 3f;
        /// <summary>見えない天井の高さ。見えない壁はここまで伸びる(飛んで越えられない)。</summary>
        const float CeilingY = 280f;

        static readonly Color HullColor = new(0.36f, 0.38f, 0.42f), WallColor = new(0.45f, 0.46f, 0.48f),
            FloorColor = new(0.34f, 0.35f, 0.36f), DebrisColor = new(0.5f, 0.46f, 0.4f), FurnitureColor = new(0.55f, 0.5f, 0.42f),
            PipeColor = new(0.42f, 0.5f, 0.46f), MachineColor = new(0.5f, 0.47f, 0.52f), RockColor = new(0.36f, 0.33f, 0.3f),
            DoorColor = new(0.16f, 0.17f, 0.19f), LampColor = new(0.85f, 0.85f, 0.7f);

        struct Block
        {
            public Vector3 center, size;
            public Quaternion rot;
            public Color color;
            /// <summary>上に見えない壁を天井まで伸ばす(飛んで越えられない。回していない箱だけ)。</summary>
            public bool barrier;
            /// <summary>当たり判定なし(床・天井に描いたドアの印など)。</summary>
            public bool decal;
            /// <summary>見えない(見えない壁そのもの)。</summary>
            public bool invisible;
            /// <summary>床に使う(groundMat を貼る)。</summary>
            public bool floor;
        }

        class Spawn
        {
            public Kind kind;
            public Vector3 pos;
            public float yaw, leash;
            public DuelEnemy enemy;
        }

        /// <summary>場所のまとまり(名前は画面に出さない。シーンビューのギズモで範囲を見るだけ)。</summary>
        class Area
        {
            public string name;
            public Bounds bounds;
        }

        class SavePoint
        {
            public string name;
            public Vector3 pos;
            public float yaw;
            public Renderer glow;
            public Light light;
            public bool inside;
            public Vector3 RespawnPos => pos + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 7f;
            public Quaternion RespawnRot => Quaternion.Euler(0f, yaw, 0f);
        }

        readonly List<Block> blocks = new();
        readonly List<Spawn> spawns = new();
        readonly List<Area> places = new();
        readonly List<SavePoint> saves = new();
        Bounds bossRoom;

        public Vector3 StartPos { get; private set; }
        public Quaternion StartRot { get; private set; } = Quaternion.identity;

        DuelGame game;
        DuelPlayer player;
        Spawn bossSpawn;
        SavePoint currentSave;
        float respawnAt = -1f, beganAt;
        bool built;

        /// <summary>ボスを倒した。</summary>
        public bool Cleared { get; private set; }
        /// <summary>ボスを倒すまでにかかった秒数。</summary>
        public float ClearTime { get; private set; }
        /// <summary>画面の中央に少しの間出す知らせ(場所の名前・セーブなど)と、出した時刻。</summary>
        public string Banner { get; private set; }
        public float BannerAt { get; private set; } = -10f;

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

        /// <summary>敵を全部、持ち場に(まだ気づいていない状態で)置き、セーブポイントを立てて始める。</summary>
        public void Begin(DuelPlayer p)
        {
            player = p;
            foreach (var s in spawns) PlaceEnemy(s);
            foreach (var sp in saves) BuildSaveMarker(sp);
            SetCurrentSave(saves[0]);
            saves[0].inside = true;   // 出撃地点で「セーブしました」を出さない
            beganAt = Time.time;
            ShowBanner(mapName);
        }

        void BuildGeometry()
        {
            var root = new GameObject("Stage").transform;
            var mats = new Dictionary<Color, Material>();
            Material Mat(Color c)
            {
                if (!mats.TryGetValue(c, out var m)) mats[c] = m = ProtoFx.Lit(c);
                return m;
            }

            var all = new Bounds();
            bool first = true;
            foreach (var b in blocks)
            {
                if (b.invisible)
                {
                    var bar = new GameObject("Barrier") { layer = DuelFighter.FighterLayer };
                    bar.transform.SetParent(root, false);
                    bar.transform.position = b.center;
                    bar.AddComponent<BoxCollider>().size = b.size;
                    continue;
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = b.decal ? "Mark" : b.floor ? "Floor" : "Block";
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(b.center, b.rot);
                go.transform.localScale = b.size;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = b.floor && groundMat != null ? groundMat : b.color == WallColor && wallMat != null ? wallMat : Mat(b.color);
                if (!terrainShadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (b.decal)
                {
                    DestroyImmediate(go.GetComponent<Collider>());   // 同じフレームの床の高さのレイに当たらないように、すぐ消す
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    continue;
                }
                if (first) { all = new Bounds(b.center, b.size); first = false; }
                else all.Encapsulate(new Bounds(b.center, b.size));
                if (b.barrier)
                {
                    // 箱の上から見えない天井まで、見えない壁を伸ばす
                    float top = b.center.y + b.size.y / 2f;
                    if (top < CeilingY)
                    {
                        var bar = new GameObject("Barrier") { layer = DuelFighter.FighterLayer };
                        bar.transform.SetParent(go.transform, false);
                        var bc = bar.AddComponent<BoxCollider>();
                        float ly = (CeilingY - top) / b.size.y;   // 親の大きさで割った、ローカルの高さ
                        bc.center = new Vector3(0f, 0.5f + ly / 2f, 0f);
                        bc.size = new Vector3(1f, ly, 1f);
                    }
                }
            }
            // 見えない天井
            var ceil = new GameObject("Ceiling") { layer = DuelFighter.FighterLayer };
            ceil.transform.SetParent(root, false);
            ceil.transform.position = new Vector3(all.center.x, CeilingY + 1f, all.center.z);
            ceil.AddComponent<BoxCollider>().size = new Vector3(all.size.x + 40f, 2f, all.size.z + 40f);
            Physics.SyncTransforms();   // 置いたばかりの地形に、すぐレイ(足もとの高さ・敵の目線)が当たるように
        }

        /// <summary>セーブポイントの目印:細く光る柱(当たり判定なし)と、まわりを照らす小さな明かり。</summary>
        void BuildSaveMarker(SavePoint sp)
        {
            var root = new GameObject($"セーブポイント {sp.name}").transform;
            root.position = sp.pos;
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root, false);
            pillar.transform.localPosition = Vector3.up * 5f;
            pillar.transform.localScale = new Vector3(1.6f, 5f, 1.6f);
            sp.glow = pillar.GetComponent<Renderer>();
            sp.glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var foot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(foot.GetComponent<Collider>());
            foot.transform.SetParent(root, false);
            foot.transform.localPosition = Vector3.up * 0.15f;
            foot.transform.localScale = new Vector3(6f, 0.15f, 6f);
            foot.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(new Color(0.3f, 0.34f, 0.38f));
            var lg = new GameObject("Light").AddComponent<Light>();
            lg.transform.SetParent(root, false);
            lg.transform.localPosition = Vector3.up * 8f;
            lg.type = LightType.Point;
            lg.range = 30f;
            lg.shadows = LightShadows.None;
            sp.light = lg;
            Paint(sp, false);
        }

        static void Paint(SavePoint sp, bool active)
        {
            var c = active ? new Color(0.55f, 1f, 0.85f) : new Color(0.25f, 0.55f, 0.6f);
            if (sp.glow != null) sp.glow.sharedMaterial = ProtoFx.Unlit(c);
            if (sp.light != null)
            {
                sp.light.color = c;
                sp.light.intensity = active ? 3f : 1.2f;
            }
        }

        // ───────── 敵 ─────────

        /// <summary>敵を持ち場に置く(足もとの床の高さに合わせ、決まった向きで立ち番。まだ気づいていない)。</summary>
        void PlaceEnemy(Spawn s)
        {
            Vector3 pos = s.pos;
            if (Physics.Raycast(pos + Vector3.up * 8f, Vector3.down, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            var rot = Quaternion.Euler(0f, s.yaw, 0f);
            var go = new GameObject($"{KindName(s.kind)}");
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.2f, rot);
            var e = go.AddComponent<DuelEnemy>();
            e.Init(game, player, s.kind switch
            {
                Kind.Duelist => DuelEnemy.Duelist(game.enemyHP),
                Kind.Boss => DuelEnemy.Boss(bossHP, bossMoves, bossSize, bossMaxStagger),
                _ => DuelEnemy.Grunt(game.gruntHP),
            });
            e.home = pos + Vector3.up * 0.2f;
            e.homeRotation = rot;
            e.leashRadius = s.kind == Kind.Boss ? bossLeash : s.leash;
            e.ignoreLeashRange = s.kind == Kind.Boss;   // ボスは持ち場から離れすぎで発覚を切らない(見失いだけで切る)
            if (s.kind == Kind.Boss) e.rangedTakenMul = bossRangedTaken;
            e.UseSenses(senses);
            s.enemy = e;
            game.Enemies.Add(e);
        }

        static string KindName(Kind k) => k switch { Kind.Duelist => "剣士", Kind.Boss => "大剣士", _ => "兵卒" };

        // ───────── 進行 ─────────

        void Update()
        {
            if (game == null || player == null) return;
            if (DuelGame.Paused)
            {
                // 装備の確認を開いている間:実時間で数える再開・知らせの時計も止める
                float udt = Time.unscaledDeltaTime;
                if (respawnAt > 0f) respawnAt += udt;
                BannerAt += udt;
                return;
            }
            if (respawnAt > 0f && Time.unscaledTime >= respawnAt)
            {
                respawnAt = -1f;
                Respawn();
            }
            // 地形のすき間から落ちた:敵は持ち場へ、自機は最後のセーブポイントから再開
            foreach (var sp0 in spawns)
                if (sp0.enemy != null && !sp0.enemy.Dead && sp0.enemy.transform.position.y < fallLimitY) sp0.enemy.ResetToHome();
            if (player.Dead) return;
            Vector3 p = player.transform.position;
            if (p.y < fallLimitY && respawnAt < 0f)
            {
                game.Log("落下した");
                Respawn();
                return;
            }

            // セーブポイント:近づいたら触れたことにする(離れて戻ればもう一度)
            foreach (var sp in saves)
            {
                Vector3 d = p - sp.pos;
                float flat = new Vector2(d.x, d.z).magnitude;
                bool near = flat < saveRadius && Mathf.Abs(d.y) < 12f;
                if (near && !sp.inside) Touch(sp);
                if (flat > saveRadius * 1.5f || Mathf.Abs(d.y) > 18f) sp.inside = false;
                else if (near) sp.inside = true;
            }

            // ボス:大ホールに入っている間は、気づいて戦う(見失わない)
            var boss = bossSpawn != null ? bossSpawn.enemy : null;
            if (boss != null && !boss.Dead && bossRoom.Contains(p)) boss.KeepEngaged();
        }

        void Touch(SavePoint sp)
        {
            SetCurrentSave(sp);
            if (saveHeals) player.RestoreHP();
            ShowBanner("セーブしました");
        }

        void SetCurrentSave(SavePoint sp)
        {
            if (currentSave != null) Paint(currentSave, false);
            currentSave = sp;
            Paint(sp, true);
        }

        void ShowBanner(string text)
        {
            Banner = text;
            BannerAt = Time.unscaledTime;
        }

        public void OnPlayerDied() => respawnAt = Time.unscaledTime + respawnDelay;

        /// <summary>
        /// 最後に触れたセーブポイントから再開する。倒した敵はそのまま。生きている敵は HP を戻して持ち場へ帰し、発覚・警戒を切る
        /// (ボス戦の途中でも、ボスは HP 満タンで持ち場へ戻る)。
        /// </summary>
        void Respawn()
        {
            foreach (var s in spawns)
                if (s.enemy != null && !s.enemy.Dead) s.enemy.ResetToHome();
            var sp = currentSave ?? saves[0];
            player.Respawn(sp.RespawnPos, sp.RespawnRot);
            foreach (var other in saves) other.inside = other == sp;   // 再開した所で、もう一度触れたことにしない
            var cam = Camera.main != null ? Camera.main.GetComponent<DuelCamera>() : null;
            if (cam != null) cam.Snap();   // カメラを補間せずに合わせる(大きく流れて向きが急に回らないように)
            ShowBanner("セーブポイントから再開");
            game.Log("セーブポイントから再開");
        }

        /// <summary>敵が倒れた(DuelGame から)。ボスならステージクリア。</summary>
        public void OnEnemyDied(DuelEnemy e)
        {
            bool boss = bossSpawn != null && e == bossSpawn.enemy;
            if (!boss) { TryDrop(e); return; }
            if (Cleared) return;
            Cleared = true;
            ClearTime = Time.time - beganAt;
            ShowBanner("ステージクリア");
            game.Log("ボスを倒した");
            DuelLoot.ClearAll();   // 床の武装も消す(消し飛んだ後にまた付けられないように)
            StartCoroutine(BossBlast(e.Chest, e.Size));
        }

        /// <summary>クリアの画面に添える一言(武装が消し飛んだとき)。</summary>
        public string ClearNote { get; private set; }

        /// <summary>
        /// ボスの撃墜の爆発(撃墜の演出の爆発の後)に巻き込まれ、武装が全部消し飛ぶ。画面は揺らさない(大きな爆発・火花・短いヒットストップだけ)。
        /// </summary>
        IEnumerator BossBlast(Vector3 at, float size)
        {
            yield return new WaitForSeconds(0.75f);
            ProtoFx.Blast(at, bossBlastRadius, new Color(1f, 0.55f, 0.15f), 0.9f);
            ProtoFx.Blast(at, bossBlastRadius * 0.45f, new Color(1f, 0.95f, 0.75f), 0.4f);
            DuelSpark.Burst(at, Vector3.zero, new Color(1f, 0.7f, 0.3f), 80, 2.5f * size);
            game.HitStop(0.15f);
            bool hadAmmo = player != null && player.Arms != null && player.Arms.Ammo > 0f;
            int lost = player != null && player.Arms != null ? player.Arms.LoseAll() : 0;   // 所持品も消し飛び、共通の弾も0に戻る
            if (lost > 0 || hadAmmo)
            {
                ClearNote = "ボスの爆発に巻き込まれ、武装も所持品も弾も、すべて消し飛んだ";
                game.Log(ClearNote);
            }
        }

        // ───────── 武装の落とし物 ─────────

        int dryKills;

        /// <summary>倒した敵が武装を落とすか決め、落とすなら倒した所の高さのレア度の重みで1つ選んで落とす。</summary>
        void TryDrop(DuelEnemy e)
        {
            if (arsenal == null || arsenal.Count == 0 || e == null || Cleared) return;   // ボスを倒した後は落とさない
            var kind = Kind.Grunt;
            foreach (var s in spawns) if (s.enemy == e) { kind = s.kind; break; }
            float chance = kind == Kind.Duelist ? duelistDropChance : gruntDropChance;
            if (Random.value >= chance && dryKills < pityKills)
            {
                dryKills++;
                return;
            }
            dryKills = 0;
            var w = PickWeapon(BandAt(e.transform.position.y).RollRarity());
            if (w == null) return;
            Vector2 k = Random.insideUnitCircle * 5f;
            DuelLoot.Drop(w, e.Chest, new Vector3(k.x, 0f, k.y));
            if (w.rarity >= 1) game.Log($"{w.RarityName}の武装が落ちた:{w.name}");
        }

        DuelDropBand BandAt(float y)
        {
            DuelDropBand best = null;
            if (dropBands != null)
                foreach (var b in dropBands)
                    if (b != null && y >= b.fromY && (best == null || b.fromY >= best.fromY)) best = b;
            return best ?? new DuelDropBand();
        }

        /// <summary>
        /// そのレア度の武装から1つ選ぶ(なければ近いレア度から)。空いている所に付けられる武装を出やすく(emptySlotWeight 倍)、
        /// 今付けているのと同じ武装は出にくく(sameWeaponWeight 倍)する。武装ごとの出やすさ(DuelWeapon.dropWeight。両手持ちは少なめ)も掛ける。
        /// </summary>
        DuelWeapon PickWeapon(int rarity)
        {
            var arms = player != null ? player.Arms : null;
            foreach (int r in new[] { rarity, rarity - 1, rarity + 1, rarity - 2, rarity + 2 })
            {
                float sum = 0f;
                foreach (var w in arsenal) if (w != null && w.rarity == r) sum += Weight(w, arms);
                if (sum <= 0f) continue;
                float pick = Random.value * sum;
                DuelWeapon last = null;
                foreach (var w in arsenal)
                {
                    if (w == null || w.rarity != r) continue;
                    last = w;
                    pick -= Weight(w, arms);
                    if (pick < 0f) return w;
                }
                return last;
            }
            return null;
        }

        float Weight(DuelWeapon w, DuelArms arms) => Mathf.Max(0f, w.dropWeight) * SlotWeight(w, arms);

        /// <summary>空いている所に付けられるなら出やすく、今付けているのと同じなら出にくく(両手持ちは、どちらかの手が空いていれば空きとみなす)。</summary>
        float SlotWeight(DuelWeapon w, DuelArms arms)
        {
            if (arms == null) return 1f;
            bool empty = false, same = false;
            foreach (var s in DuelWeapon.SlotsFor(w.kind))
            {
                var cur = arms.Get(s);
                if (cur == null) empty = true;
                else if (cur == w) same = true;
            }
            return empty ? emptySlotWeight : same ? sameWeaponWeight : 1f;
        }

        // ───────── 画面に出す情報 ─────────

        /// <summary>全体の残りの敵(ボスを除く)。</summary>
        public string Status
        {
            get
            {
                if (Cleared) return "ステージクリア";
                int alive = 0, total = 0;
                foreach (var s in spawns)
                {
                    if (s.kind == Kind.Boss) continue;
                    total++;
                    if (s.enemy != null && !s.enemy.Dead) alive++;
                }
                return $"残りの敵 {alive} / {total}";
            }
        }

        /// <summary>戦っているボス(いなければ null)。</summary>
        public DuelEnemy ActiveBoss =>
            bossSpawn != null && bossSpawn.enemy != null && !bossSpawn.enemy.Dead && bossSpawn.enemy.Engaged ? bossSpawn.enemy : null;
    }
}
