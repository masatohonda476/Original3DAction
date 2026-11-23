using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;
using UnityEngine.Serialization;

namespace Prototype
{
    /// <summary>
    /// プロトタイプのステージ:墜落して地面に斜めに突き刺さった、横倒しの巨大な船の中を、下から上へ登る。区画の区切りはなく、ひと続きの地形。
    ///   地面の裂け目(入口・最下層。開けている)→ 横倒しの居住区(元の壁が床になった狭い部屋が3つ。ドアの穴が床・天井にある)→
    ///   大きな竪穴(張り出した足場・折れた梁をブースト飛行で上る。足場に敵)→ 分岐:外殻を伝う外の道(開けた斜面)/ 内部の配管の道(狭いダクト)→
    ///   合流(機関区画。群れが待つ。拾った武装で暴れる所)→ 主通路(上り坂)→ 格納庫(広い。コンテナの山とキャットウォーク)→
    ///   昇降機の竪穴(細く高い。足場と止まった昇降機の箱を上る)→ 船首の外板(開けた外の斜面)→ 艦橋の下(ボスの前の群れ)→
    ///   頭頂部の大ホール(天井が裂けている。ボス)。
    /// 敵は持ち場で立ち番し、発覚の仕組み(DuelSenses)で気づいてから戦う。倒した敵は復活しない。
    /// セーブポイント(光る柱)に触れると再開地点が変わる(HPは回復しない。作者「セーブポイントでは回復できないようにしてください」)。
    /// 最後に接地した足場から一定以上落ちると、落下ダメージを受けてその足場に戻る。
    /// 撃墜されたら最後に触れたセーブポイントから再開し、
    /// 生きている敵は HP を戻して持ち場へ帰す(発覚・警戒も切る)。F1 は最初から。ボスを倒すと「ステージクリア」。
    /// ボス戦(作者「ボス戦に入ったら入口を閉じ、死亡したら入る前の弾薬の状態で復活するようにしてください」):自機が大ホールに入りきったら入口の扉が閉じ、
    /// そのときのアイテム、共通の弾、武装ごとの弾倉を控える。ボスを倒すか、やられて再開すると扉は開く(入り直すとまた閉じる)。
    /// ボスは扉が閉じている間だけダメージを受ける(作者「ボス部屋の外からの攻撃は0ダメージにしてください」)。
    /// ボス以外の敵は大ホールへ入れず、入口の手前で止まる(作者「敵はボス部屋に入れないようにしてください」。DuelFighter.maxZ)。
    /// 地形と敵の配置はこのクラスの Define() に書き、実行時に組み立てる(シーンには DuelGame とこれを置くだけ)。
    /// 「重厚→無双」のサイクルひとつ分:倒した敵がときどき武装を落とし(上ほどレア度の高い武装)、十字右で付け替えながら強くなる(DuelArms)。
    /// 撃墜されても武装・共通の弾は残る。ただしボス戦の間にやられて再開したときは、共通の弾と弾倉の残りだけを扉が閉じたときに戻し、
    /// 装填は近接に戻ってから進む(装備はやられた時点のまま)。
    /// ボスを倒すと爆発で武装は全部消し飛び、弾も0になってクリア。
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
        [Tooltip("ロックオン距離(m)。ロックオンが付き始める距離は、この1つだけ(作者「1つにそろえて長くする」「ロックオン距離の2倍にしてください」:前の 自動 40m / ボタン 130m の2段をやめ、40m の2倍の 80m)。" +
                 "ロックオンがオンで狙う敵がいないとき、体が照準枠(画面の縁寄りの四つの角の内側)に掛かっていて壁・床ごしでなく、この距離の内の敵がいれば付く" +
                 "(自動・オンにした瞬間・撃破や見失ったあとの次の敵・切り替えボタンのどれも同じ距離。ロックオン中の切り替え先は画面の中ならよいが、距離は同じ)。" +
                 "枠の右辺の目盛りの目立つ刻みがこの距離(上端はこの2倍)。付いたあとは、この1.5倍より遠い・壁ごしが DuelPlayer.lockLoseAfter 秒続くと外れる。0 なら距離も壁も見ない")]
        public float lockOnRange = 80f;   // 前の lockRange(130)・autoLockRange(40)とは別の名前(シーンに古い値が保存されていても読まれない)

        [Header("マップ")]
        [Tooltip("ステージに入ったときに一度だけ画面の中央に出すマップ名(場所ごとの名前は出さない)")]
        public string mapName = "墜落船";

        [Header("セーブポイント")]
        [Tooltip("触れたとみなす水平の距離(m)")]
        public float saveRadius = 14f;
        [Tooltip("撃墜されてから、セーブポイントで再開するまでの秒数")]
        public float respawnDelay = 2.5f;

        /// <summary>
        /// 落ちる武装の一覧(付ける所・レア度・性能)。シリアライズせず、いつも DuelWeapon.Defaults() から作る(数値はコードで直す)。
        /// </summary>
        [System.NonSerialized] public List<DuelWeapon> arsenal = DuelWeapon.Defaults();
        [Header("武装の落とし物(サイクルひとつ分の試作)")]
        [Tooltip("倒した敵が武装を落とす確率(兵卒も剣士も同じ。続けて落ちなかったときの救済はなし。ボスは落とさない)")]
        [Range(0f, 1f)] public float dropChance = 0.3f;
        [Tooltip("今付けているのと同じ武装の出やすさ(倍)")]
        public float sameWeaponWeight = 0.3f;
        [Tooltip("兵卒が落とす武装のレア度の重み(コモン / レア / エピック。倒した所の高さは見ない)")]
        public DuelRarityWeights gruntRarity = new(0.5f, 0.4f, 0.1f);
        [Tooltip("剣士が落とす武装のレア度の重み(コモン / レア / エピック)")]
        public DuelRarityWeights duelistRarity = new(0.1f, 0.45f, 0.45f);
        [Tooltip("照準・装備の重量・弾と装填・解体の決まり(数値は仮)")]
        public DuelArmsRules armsRules = new();
        [Tooltip("ボスを倒したときの爆発の大きさ(m)。爆発で拾った武装とアイテムが消し飛び、共通の弾も0になる(画面は揺らさない)")]
        public float bossBlastRadius = 45f;

        [Header("アイテムの落とし物")]
        [Tooltip("兵卒・剣士を倒したとき、武装の抽選とは別にアイテムが1個落ちる確率")]
        [Range(0f, 1f)] public float itemDropChance = 0.3f;

        [Header("ボス(大剣士)")]
        [Tooltip("ボスの最大HP")]
        public float bossHP = 700f;
        [Tooltip("射撃でボスの怯みゲージがたまる量の倍率")]
        [FormerlySerializedAs("bossRangedTaken")]
        [Range(0f, 1f)] public float bossRangedStaggerMul = 0.4f;
        [Tooltip("機体の大きさの倍率(剣士は1.5)")]
        public float bossSize = 2.5f;
        [Tooltip("怯みゲージの最大(剣士は25)。大きいほど怯みにくい")]
        public float bossMaxStagger = 70f;
        [Tooltip("ボスが歩いて出ない、持ち場からの半径(m)。0 なら制限なし")]
        public float bossLeash;
        public DuelEnemy.BossMoves bossMoves = new();

        [Header("落下復帰")]
        [Tooltip("最後に接地した足場からこの距離(m)落ちたら、ダメージを受けて足場へ戻る")]
        [Min(1f)] public float fallRecoveryDepth = 40f;
        [Tooltip("落下1回で失う最大HPの割合")]
        [Range(0f, 1f)] public float fallDamageFraction = 0.2f;
        [Tooltip("敵がこの高さ(m)より下へ落ちたら、持ち場へ戻す")]
        public float fallLimitY = -30f;

        [Header("ボス戦の入口の扉(大ホールの入口。ボス戦の間だけ閉じる。数値は仮)")]
        [Tooltip("自機が、扉の内側の面から「機体の半径 + この距離(m)」より奥へ入ったら扉を閉じる(閉じた扉に挟まれない・外に取り残されない余白)")]
        public float gateClearance = 1f;
        [Tooltip("扉が下りきる・上がりきるまでの秒数(見た目だけ。機体はこれを待たず、閉じた瞬間から通れない)。0 ならすぐ")]
        public float gateTime = 0.35f;

        public enum Kind { Grunt, Duelist, Boss, FlyingScout }

        /// <summary>壁の厚み。</summary>
        const float T = 3f;
        /// <summary>ボス戦の入口の扉の厚み(壁の厚み T より薄く、壁の中ほどに置く。壁の面と同じ位置に面が来ない)と、開口の縁から壁・床へ埋める幅。</summary>
        const float GateThick = 2f, GateInset = 0.2f;
        /// <summary>機体を扉の面から離しておく余白(m。機体の半径に足す。当たり判定の皮 0.2m より少し広く)。</summary>
        const float GateSkin = 0.3f;

        static readonly Color HullColor = new(0.36f, 0.38f, 0.42f), WallColor = new(0.45f, 0.46f, 0.48f),
            FloorColor = new(0.34f, 0.35f, 0.36f), DebrisColor = new(0.5f, 0.46f, 0.4f), FurnitureColor = new(0.55f, 0.5f, 0.42f),
            PipeColor = new(0.42f, 0.5f, 0.46f), MachineColor = new(0.5f, 0.47f, 0.52f), RockColor = new(0.36f, 0.33f, 0.3f),
            DoorColor = new(0.16f, 0.17f, 0.19f), LampColor = new(0.85f, 0.85f, 0.7f);

        struct Block
        {
            public Vector3 center, size;
            public Quaternion rot;
            public Color color;
            /// <summary>当たり判定なし(床・天井に描いたドアの印など)。</summary>
            public bool decal;
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
        /// <summary>ボス戦の入口の扉が、閉じたときに占める範囲(BossHall で決める)。</summary>
        Bounds bossGate;

        public Vector3 StartPos { get; private set; }
        public Quaternion StartRot { get; private set; } = Quaternion.identity;

        DuelGame game;
        DuelPlayer player;
        Vector3 lastFoothold;
        Spawn bossSpawn;
        SavePoint currentSave;
        float respawnAt = -1f, beganAt;
        bool built;
        /// <summary>
        /// ボス戦の間(入口の扉が閉じている)。自機が大ホールに入りきったら立ち、ボスを倒す・撃墜されて再開すると下りる。
        /// 立っている間の再開だけ、弾と弾倉を扉が閉じたときに戻す。
        /// </summary>
        bool bossFight;
        internal bool PlayerInBossRoom => player != null && bossRoom.Contains(player.transform.position);
        /// <summary>
        /// 扉の見える板(ふつうの壁と同じ層。開いている間は上の壁の中へ引っ込めて消す)と、その当たり判定(閉じきっている間だけ効かせる)、
        /// 機体だけを止める見えない壁(閉じている間だけ出す)。
        /// </summary>
        Transform gateDoor;
        Collider gateDoorCollider;
        GameObject gateBarrier;
        /// <summary>扉の下り具合(0 = 開ききった、1 = 閉じきった)。</summary>
        float gateShut;

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
            SeparateCoplanarFaces();
            BuildGeometry();
        }

        /// <summary>重なった箱の、同じ向きで同じ位置にある面をずらす幅(m)。</summary>
        const float FaceNudge = 0.03f;

        /// <summary>
        /// 壁のちらつき(Z ファイティング)を防ぐ。箱どうしが重なっていて、同じ向きの面が同じ位置にあると
        /// (例:壁の外側の面と床の板の端、壁の上端と天井の板の上面)、どちらを描くかが画素ごとに揺れて縞模様にちらつく。
        /// 回していない箱を置いた順に見て、先に置いた箱と同じ向き・同じ位置で重なる面があれば、後の箱のその面を FaceNudge だけ内側へずらす
        /// (ずらした先がまた別の箱の面と重なれば、もう一度ずらす)。印(デカール)と坂(回した箱)は触らない。
        /// </summary>
        void SeparateCoplanarFaces()
        {
            static bool Plain(Block b) => !b.decal && b.rot == Quaternion.identity;
            for (int j = 0; j < blocks.Count; j++)
            {
                var bj = blocks[j];
                if (!Plain(bj)) continue;
                Vector3 min = bj.center - bj.size / 2f, max = bj.center + bj.size / 2f;
                bool moved = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    for (int side = 0; side < 2; side++)
                    {
                        for (int tries = 0; tries < 8; tries++)
                        {
                            float face = side == 0 ? min[axis] : max[axis];
                            bool hit = false;
                            for (int i = 0; i < j && !hit; i++)
                            {
                                var bi = blocks[i];
                                if (!Plain(bi)) continue;
                                Vector3 imin = bi.center - bi.size / 2f, imax = bi.center + bi.size / 2f;
                                float other = side == 0 ? imin[axis] : imax[axis];
                                if (Mathf.Abs(other - face) > 0.005f) continue;
                                // 面どうしが広さを持って重なっているか(辺で接するだけなら見えない)
                                if (Mathf.Min(max[u], imax[u]) - Mathf.Max(min[u], imin[u]) <= 0.01f) continue;
                                if (Mathf.Min(max[v], imax[v]) - Mathf.Max(min[v], imin[v]) <= 0.01f) continue;
                                hit = true;
                            }
                            if (!hit) break;
                            if (side == 0) min[axis] += FaceNudge; else max[axis] -= FaceNudge;
                            moved = true;
                        }
                    }
                }
                if (!moved) continue;
                bj.center = (min + max) / 2f;
                bj.size = max - min;
                blocks[j] = bj;
            }
        }

        /// <summary>敵を全部、持ち場に(まだ気づいていない状態で)置き、セーブポイントを立てて始める。</summary>
        public void Begin(DuelPlayer p)
        {
            player = p;
            lastFoothold = StartPos;
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

            foreach (var b in blocks)
            {
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
                }
            }
            BuildBossGate(root);
            Physics.SyncTransforms();   // 置いたばかりの地形に、すぐレイ(足もとの高さ・敵の目線)が当たるように
        }

        // ───────── ボス戦の入口の扉 ─────────

        /// <summary>
        /// ボス戦の入口の扉を作る(開いた状態で始める)。Define の箱(blocks)には入れない:開け閉めするので参照が要り、面をずらす処理にも掛けない。
        ///   見える板:ふつうの壁と同じ箱(既定の層・当たり判定つき)。閉じきっている間は、機体・弾・見通し・カメラの壁よけ・透かしがふつうの壁と同じに効く。
        ///     閉じるときは開口の上の壁の中から下り、開くときは上の壁の中へ引っ込む(壁より薄いので、壁の中にある間は見えない)。
        ///     動いている間(下りる・上がる途中)は当たり判定を切り、見た目だけにする(PlaceGate):動く板の底面をカメラの天井よけ
        ///     (DuelCamera.FloorCeilingLimit)が拾って、カメラが急に前へ寄らないように(作者は 3D 酔いする)。その間の弾・見通しは板を素通りする。
        ///   機体用の当たり判定:板が下りきるのを待たず、閉じた瞬間から機体を通さない
        ///     (下りてくる板の下をくぐる・板に挟まれることがない)。
        /// </summary>
        void BuildBossGate(Transform root)
        {
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "BossGate";
            door.transform.SetParent(root, false);
            door.transform.localScale = bossGate.size;
            var r = door.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Lit(DoorColor);
            if (!terrainShadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gateDoor = door.transform;
            gateDoorCollider = door.GetComponent<Collider>();
            gateBarrier = new GameObject("BossGateBarrier") { layer = DuelFighter.FighterLayer };
            gateBarrier.transform.SetParent(root, false);
            gateBarrier.transform.position = bossGate.center;
            gateBarrier.AddComponent<BoxCollider>().size = bossGate.size;
            gateBarrier.SetActive(false);
            gateShut = 0f;
            PlaceGate();
        }

        /// <summary>
        /// 扉の見える板を、下り具合(gateShut)の位置に置く。開ききったら消す。板の当たり判定は、閉じきっていて、ボス戦の間だけ効かせる
        /// (動いている間は見た目だけ。開け始めたら、板が動き出す前でもすぐ切る)。
        /// </summary>
        void PlaceGate()
        {
            gateDoor.position = bossGate.center + Vector3.up * (bossGate.size.y * (1f - gateShut));
            gateDoor.gameObject.SetActive(gateShut > 0f);
            gateDoorCollider.enabled = bossFight && gateShut >= 1f;
        }

        /// <summary>扉の見える板を、閉じる・開くほうへ動かす(gateTime 秒で端から端まで。画面は揺らさない)。</summary>
        void TickGate(float dt)
        {
            float target = bossFight ? 1f : 0f;
            if (gateShut == target) return;
            gateShut = gateTime > 0f ? Mathf.MoveTowards(gateShut, target, dt / gateTime) : target;
            PlaceGate();
        }

        /// <summary>
        /// ボス戦を始める:入口の扉を閉じ、共通の弾・弾倉・アイテムを控える(やられて再開するときに戻す)。
        /// 呼ぶのは、自機が扉の内側の面から「機体の半径 + gateClearance」より奥にいるときだけ(Update)なので、自機は扉に掛からず、必ず内側にいる。
        /// ここからボスにダメージが入る(SetBossInvulnerable)。
        /// 扉に重なっている機体は押し出す(閉じた扉に埋まらないように):入口に寄ってきたボスは今いる側(奥)へ。ボス以外の敵は入口の手前で止まるので
        /// (DuelFighter.maxZ)重ならないはずだが、重なっていたら必ず手前(通路)側へ出す(奥へ出すと、中の自機に重なる)。
        /// </summary>
        void BeginBossFight()
        {
            bossFight = true;
            // 飛び込んだ直後から、落下で閉じた扉の外に戻らないようにする。
            if (!bossRoom.Contains(lastFoothold))
                lastFoothold = new Vector3(bossGate.center.x, bossGate.min.y + GateInset,
                    bossGate.max.z + player.Radius + Mathf.Max(gateClearance, GateSkin));
            gateBarrier.SetActive(true);
            SetBossInvulnerable(false);
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                Vector3 q = e.transform.position;
                float r = e.Radius + GateSkin;
                if (q.x <= bossGate.min.x - r || q.x >= bossGate.max.x + r || q.z <= bossGate.min.z - r || q.z >= bossGate.max.z + r) continue;
                if (q.y >= bossGate.max.y || q.y + game.mechHeight * e.Size <= bossGate.min.y) continue;
                q.z = e.IsBoss && q.z >= bossGate.center.z ? bossGate.max.z + r : bossGate.min.z - r;
                e.Teleport(q, e.transform.rotation);
            }
            player.Arms.KeepBossSupplies();
        }

        /// <summary>
        /// ボス戦を終える(ボスを倒した・やられて再開した):入口の扉を開ける(板の当たり判定はすぐ切る)。
        /// ボスは、次に扉が閉じるまでまたダメージを受けなくなる。
        /// </summary>
        void EndBossFight()
        {
            bossFight = false;
            gateBarrier.SetActive(false);
            gateDoorCollider.enabled = false;
            SetBossInvulnerable(true);
        }

        /// <summary>
        /// ボスがダメージを受けないかを切り替える(作者「ボス部屋の外からの攻撃は0ダメージにしてください」):
        /// 入口の扉が閉じている間(ボス戦の間)だけダメージが入り、それ以外(入る前・入口に立って撃つ・やられて再開してから入り直すまで)は 0。
        /// </summary>
        void SetBossInvulnerable(bool on)
        {
            if (bossSpawn != null && bossSpawn.enemy != null) bossSpawn.enemy.invulnerable = on;
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
            if (s.kind != Kind.FlyingScout && Physics.Raycast(pos + Vector3.up * 8f, Vector3.down, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            var rot = Quaternion.Euler(0f, s.yaw, 0f);
            var go = new GameObject($"{KindName(s.kind)}");
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.2f, rot);
            var e = go.AddComponent<DuelEnemy>();
            e.Init(game, player, s.kind switch
            {
                Kind.Duelist => DuelEnemy.Duelist(game.enemyHP),
                Kind.FlyingScout => DuelEnemy.FlyingScout(),
                Kind.Boss => DuelEnemy.Boss(bossHP, bossMoves, bossSize, bossMaxStagger),
                _ => DuelEnemy.Grunt(game.gruntHP),
            });
            e.home = pos + Vector3.up * 0.2f;
            e.homeRotation = rot;
            e.leashRadius = s.kind == Kind.Boss ? bossLeash : s.leash;
            e.ignoreLeashRange = s.kind == Kind.Boss;   // ボスは持ち場から離れすぎで発覚を切らない(見失いだけで切る)
            if (s.kind == Kind.Boss)
            {
                e.rangedStaggerMul = bossRangedStaggerMul;
                e.invulnerable = true;   // 入口の扉が閉じる(ボス戦が始まる)まではダメージを受けない(SetBossInvulnerable)
            }
            // ボス以外の敵は、大ホールの入口(扉の手前の面)より奥へ入れない(作者「敵はボス部屋に入れないようにしてください」。扉が開いていても)
            else e.maxZ = bossGate.min.z - e.Radius - GateSkin;
            e.UseSenses(senses);
            s.enemy = e;
            game.Enemies.Add(e);
        }

        static string KindName(Kind k) => k switch { Kind.Duelist => "剣士", Kind.Boss => "大剣士", Kind.FlyingScout => "飛行偵察機", _ => "兵卒" };

        // ───────── 進行 ─────────

        void Update()
        {
            if (game == null || player == null) return;
            if (DuelGame.Paused)
            {
                // メニュー(装備の確認・操作方法)を開いている間:実時間で数える再開・知らせの時計も止める
                float udt = Time.unscaledDeltaTime;
                if (respawnAt > 0f) respawnAt += udt;
                BannerAt += udt;
                return;
            }
            TickGate(Time.deltaTime);
            if (respawnAt > 0f && Time.unscaledTime >= respawnAt)
            {
                respawnAt = -1f;
                Respawn();
            }
            // 地形のすき間から落ちた敵は持ち場へ戻す。
            foreach (var sp0 in spawns)
                if (sp0.enemy != null && !sp0.enemy.Dead && sp0.enemy.transform.position.y < fallLimitY) sp0.enemy.ResetToHome();
            if (player.Dead) return;
            TickCombatItems();

            Vector3 p = player.transform.position;
            if (p.y <= lastFoothold.y - fallRecoveryDepth)
            {
                game.Log("落下した");
                player.RecoverFromFall(lastFoothold, player.MaxHP * fallDamageFraction);
                SnapCamera();
                return;
            }
            if (player.OnFoothold) lastFoothold = p;

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
            if (boss == null || boss.Dead || !bossRoom.Contains(p)) return;
            boss.KeepEngaged();
            // 入口の扉より十分奥まで入ったら、扉を閉じてボス戦を始める(入口に立っているだけでは閉じない)
            if (!bossFight && p.z >= bossGate.max.z + player.Radius + gateClearance) BeginBossFight();
        }

        /// <summary>セーブポイントに触れた:再開地点を変えて知らせる(HPは回復しない)。</summary>
        void Touch(SavePoint sp)
        {
            SetCurrentSave(sp);
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

        public void OnPlayerDied()
        {
            ClearCombatItems();
            respawnAt = Time.unscaledTime + respawnDelay;
        }

        /// <summary>
        /// 最後に触れたセーブポイントから再開する。倒した敵はそのまま。生きている敵は HP を戻して持ち場へ帰し、発覚・警戒を切る
        /// (ボス戦の途中でも、ボスは HP 満タンで持ち場へ戻る)。
        /// ボス戦の間(入口の扉が閉じている間)の再開なら、扉を開け(再開地点は扉の外)、アイテム・共通の弾・弾倉を扉が閉じたときに戻す。
        /// 弾倉は再開後も近接に戻っている間に詰まる。ボス戦でなければ、弾はやられた時点のまま。
        /// </summary>
        void Respawn()
        {
            game.CancelEnemyRounds();
            foreach (var s in spawns)
                if (s.enemy != null && !s.enemy.Dead) s.enemy.ResetToHome();
            var sp = currentSave ?? saves[0];
            player.Respawn(sp.RespawnPos, sp.RespawnRot);
            lastFoothold = sp.RespawnPos;
            if (bossFight)
            {
                EndBossFight();
                player.Arms.RestoreBossSupplies();
            }
            foreach (var other in saves) other.inside = other == sp;   // 再開した所で、もう一度触れたことにしない
            SnapCamera();
            ShowBanner("セーブポイントから再開");
            game.Log("セーブポイントから再開");
        }

        static void SnapCamera()
        {
            if (Camera.main != null && Camera.main.TryGetComponent<DuelCamera>(out var cam)) cam.Snap();
        }

        /// <summary>敵が倒れた(DuelGame から)。ボスならステージクリア。</summary>
        public void OnEnemyDied(DuelEnemy e)
        {
            if (e == null || Cleared) return;
            bool boss = bossSpawn != null && e == bossSpawn.enemy;
            if (!boss)
            {
                TryDropWeapon(e);
                TryDropItem(e);
                return;
            }
            Cleared = true;
            ClearCombatItems();
            game.CancelEnemyRounds();
            ClearTime = Time.time - beganAt;
            ShowBanner("ステージクリア");
            game.Log("ボスを倒した");
            EndBossFight();                  // 入口の扉を開ける
            if (player != null) player.Arms.ForgetBossSupplies();
            DuelLoot.ClearAll();
            StartCoroutine(BossBlast(e.Chest, e.Size));
        }

        /// <summary>クリアの画面に添える一言(武装が消し飛んだとき)。</summary>
        public string ClearNote { get; private set; }

        /// <summary>
        /// ボスの撃墜の爆発(撃墜の演出の爆発の後)に巻き込まれ、拾った武装とアイテム、弾が消し飛ぶ。
        /// </summary>
        IEnumerator BossBlast(Vector3 at, float size)
        {
            yield return new WaitForSeconds(0.75f);
            ProtoFx.Blast(at, bossBlastRadius, new Color(1f, 0.55f, 0.15f), 0.9f);
            ProtoFx.Blast(at, bossBlastRadius * 0.45f, new Color(1f, 0.95f, 0.75f), 0.4f);
            DuelSpark.Burst(at, Vector3.zero, new Color(1f, 0.7f, 0.3f), 80, 2.5f * size);
            game.HitStop(0.15f);
            bool hadAmmo = player != null && (player.Arms.Ammo > 0f || player.Arms.Magazine > 0);
            int lost = player != null ? player.Arms.LoseAll() : 0;   // 共通の弾と弾倉も空に戻る
            if (lost > 0 || hadAmmo)
            {
                ClearNote = "ボスの爆発に巻き込まれ、拾った武装・アイテムと弾が消し飛んだ";
                game.Log(ClearNote);
            }
        }

        // ───────── 落とし物 ─────────

        void TryDropItem(DuelEnemy e)
        {
            if (itemDropChance < 1f && Random.value >= itemDropChance) return;
            var item = DuelItems.RollDrop();
            Vector2 k = Random.insideUnitCircle * 5f;
            DuelLoot.Drop(item, e.Chest, new Vector3(k.x, 0f, k.y));
        }

        /// <summary>
        /// 倒した敵が武装を落とすか決め(どの敵も dropChance。救済なし)、落とすなら敵の種類のレア度の重みで1つ選んで落とす。
        /// </summary>
        void TryDropWeapon(DuelEnemy e)
        {
            if (arsenal == null || arsenal.Count == 0) return;
            if (Random.value >= dropChance) return;
            var kind = Kind.Grunt;
            foreach (var s in spawns) if (s.enemy == e) { kind = s.kind; break; }
            var rarity = (kind == Kind.Duelist ? duelistRarity : gruntRarity) ?? new DuelRarityWeights();
            var w = PickWeapon(rarity.RollRarity());
            if (w == null) return;
            Vector2 k = Random.insideUnitCircle * 5f;
            DuelLoot.Drop(w, e.Chest, new Vector3(k.x, 0f, k.y));
            if (w.rarity >= 1) game.Log($"{w.RarityName}の武装が落ちた:{w.name}");
        }

        /// <summary>そのレア度から一つ選ぶ。今付けている武装は出にくくする。</summary>
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

        float Weight(DuelWeapon w, DuelArms arms) =>
            Mathf.Max(0f, w.dropWeight) * (arms != null && arms.IsEquipped(w) ? sameWeaponWeight : 1f);

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
