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
    /// 撃墜されても武装は残る。ボスを倒すと爆発で武装が全部消し飛んでクリア。
    /// </summary>
    public class DuelStage : MonoBehaviour
    {
        [Header("見た目(シーンを組み立てるときに入る。なければ仮の色)")]
        public Material groundMat;
        public Material wallMat;
        [Tooltip("地形が影を落とすか。オフなら天井のある部屋の中にも日が当たって見やすい")]
        public bool terrainShadows;

        [Header("発覚(敵が自機に気づく仕組み)")]
        public DuelSenses senses = new();
        [Tooltip("ロックオンできる距離(m)。壁・床ごしの敵はロックオンしない")]
        public float lockRange = 80f;

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

        [Header("武装の落とし物(弧ひとつ分の試作)")]
        /// <summary>
        /// 落ちる武装の一覧(付ける所・レア度・性能)。シリアライズせず、いつも DuelWeapon.Defaults() から作る(数値はコードで直す)。
        /// シリアライズすると、シーンを開いたまま DuelWeapon の形が変わったときにメモリ上の古い一覧が残り、
        /// 新しい項目が既定値のまま(例:kind が 0=剣)になって、スラスターや肩の銃が剣として落ちたため。
        /// </summary>
        [System.NonSerialized] public List<DuelWeapon> arsenal = DuelWeapon.Defaults();
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
        [Tooltip("ボスを倒したときの爆発の大きさ(m)。爆発で武装が全部消し飛ぶ(画面は揺らさない)")]
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

        class Area
        {
            public string name;
            public Bounds bounds;
            public bool visited;
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
        Area currentPlace;
        float respawnAt = -1f, beganAt;
        bool built;

        /// <summary>ボスを倒した。</summary>
        public bool Cleared { get; private set; }
        /// <summary>ボスを倒すまでにかかった秒数。</summary>
        public float ClearTime { get; private set; }
        /// <summary>画面の中央に少しの間出す知らせ(場所の名前・セーブなど)と、出した時刻。</summary>
        public string Banner { get; private set; }
        public float BannerAt { get; private set; } = -10f;

        // ───────── 地形と敵の配置 ─────────

        /// <summary>
        /// ステージの形。座標は x が左右、z が奥(船の頭のほう)、y が上。船は奥へ行くほど高い。
        /// 敵:G = 兵卒、D = 剣士、Boss = ボス。yaw は立ち番の向き(0 = 奥(+z)、90 = 右(+x)、180 = 手前)。leash は歩いて出ない半径(足場の敵)。
        /// </summary>
        void Define()
        {
            blocks.Clear();
            spawns.Clear();
            places.Clear();
            saves.Clear();
            bossSpawn = null;

            // ── 1. 地面の裂け目(入口・最下層):開けた窪地。正面に船の外板がそびえ、瓦礫の斜面を登って裂け目から中へ入る ──
            Region("地面の裂け目", -96, 96, -10, 80, -46, 112);
            Slab(-96, 96, -46, 113, 0f, FloorColor, 3f, floor: true);
            Solid(-96, -90, 0, 40, -46, 113, RockColor, barrier: true);   // 窪地の崖
            Solid(90, 96, 0, 40, -46, 113, RockColor, barrier: true);
            Solid(-96, 96, 0, 40, -46, -40, RockColor, barrier: true);
            // 船の外板(下端)。裂け目は高さ14〜34m
            WallX(113.5f, -90, 90, -3, 60, HullColor, -12, 12, 14, 34, barrier: true);
            RampZ(-14, 14, 70, 0, 112, 14, DebrisColor, 4f);   // 瓦礫の斜面(19°)
            Solid(-24, -14, 0, 8, 90, 112, DebrisColor);         // 斜面の脇の瓦礫
            Solid(14, 22, 0, 10, 96, 112, DebrisColor);
            Decal(-22, 22, 0, 0.06f, 56, 70, DoorColor);          // 地面の裂け目
            // 剥がれ落ちた外板・折れた桁(物陰)
            Solid(-44, -26, 0, 7, 24, 28, HullColor);
            Solid(22, 30, 0, 6, 38, 50, HullColor);
            Solid(-40, -34, 0, 5, 60, 86, DebrisColor);
            Solid(40, 60, 0, 14, 70, 90, HullColor);
            Save("入口", 0, 0, -6, 0f);
            G(-30, 0, 44, 90);    // 横を向いて立つ(物陰づたいに近づける)
            G(34, 0, 64, 200);
            G(6, 12, 104, 180);   // 裂け目の見張り(斜面を見下ろす)

            // ── 2. 横倒しの居住区:元の壁が床になった狭い部屋が3つ。寝台・ロッカーが壁から横に突き出し、ドアの穴が床と天井にある ──
            Region("居住区", -27, 27, 8, 82, 112, 251);
            // 部屋1(床14m):奥の天井にドアの穴(部屋2へ)。下に倒れた寝台の山を踏み台に、飛んで抜ける
            Slab(-27, 27, 112, 161, 14f, FloorColor, 3f, floor: true);
            WallZ(-25.5f, 112, 200, 11, 63, WallColor);
            WallZ(25.5f, 112, 200, 11, 63, WallColor);
            WallX(161.5f, -27, 27, 11, 37, WallColor);
            SlabHole(-27, 27, 112, 199, 37f, -22, -4, 142, 158, FloorColor);   // 部屋1の天井 = 部屋2の床(ドアの穴)
            Solid(-24, -12, 14, 22, 144, 158, FurnitureColor);    // 倒れた寝台の山(踏み台)
            Solid(16, 24, 14, 19, 118, 136, FurnitureColor);      // 横倒しのロッカー
            Decal(-6, 6, 14, 14.06f, 120, 128, DoorColor);        // 床のドア(元は横の部屋への扉)
            Decal(6, 16, 33.94f, 34f, 120, 130, DoorColor);       // 天井のドア
            Decal(-24, -23.9f, 22, 23, 116, 140, LampColor);      // 壁を走る照明(元は天井の照明)
            G(12, 14, 146, 270);
            // 部屋2(床37m):剣士が1体。奥の扉の穴へ瓦礫の坂
            WallX(138.5f, -27, 27, 37, 63, WallColor);
            WallX(197.5f, -27, 27, 34, 81, WallColor, -10, 10, 45, 58);   // 部屋3の天井(78〜81m)まで閉じる(部屋2の屋根へ出られないように)
            Slab(-27, 27, 137, 199, 63f, WallColor);
            Solid(14, 24, 37, 43, 150, 186, FurnitureColor);      // 横倒しのロッカーの列
            Solid(-24, -20, 37, 52, 168, 184, WallColor);         // 倒れかかった仕切り
            RampZ(-10, 10, 178, 37, 196, 45, DebrisColor);        // 瓦礫の坂(24°)
            Decal(-18, -6, 59.94f, 60f, 170, 180, DoorColor);
            Decal(0, 10, 37, 37.06f, 150, 158, DoorColor);
            D(2, 37, 166, 120);
            // 部屋3(元の通路):床が奥へ少し上る(8°)。天井にドアの穴が並ぶ。奥の裂け目から竪穴へ
            RampZ(-27, 27, 196, 45, 254, 53, FloorColor, 3f, floor: true);
            WallZ(-25.5f, 196, 254, 40, 81, WallColor);
            WallZ(25.5f, 196, 254, 40, 81, WallColor);
            Slab(-27, 27, 196, 254, 81f, WallColor);
            RampZ(-24, -18, 204, R3(204) + 5f, 246, R3(246) + 5f, FurnitureColor, 5f);   // 横倒しのロッカー
            RampZ(18, 24, 214, R3(214) + 4f, 236, R3(236) + 4f, FurnitureColor, 4f);     // 横倒しの寝台
            Decal(-6, 6, 77.94f, 78f, 214, 222, DoorColor);
            Decal(-6, 6, 77.94f, 78f, 232, 240, DoorColor);
            G(-8, R3(222), 222, 0);     // 奥(竪穴のほう)を向く
            G(12, R3(240), 240, 270);

            // ── 3. 大きな竪穴:縦に長い吹き抜け(床30m〜天井160m)。張り出した足場と折れた梁を、ブースト飛行で上る ──
            Region("竪穴", -48, 48, 25, 165, 251, 327);
            Slab(-48, 48, 251, 327, 30f, FloorColor, 3f, floor: true);
            Slab(-48, 48, 251, 327, 163f, WallColor);
            WallX(252.5f, -48, 48, 27, 163, WallColor, -14, 14, 53, 72);   // 居住区からの裂け目
            WallX(325.5f, -48, 48, 27, 163, WallColor);
            WallZ(-46.5f, 251, 327, 27, 163, WallColor, 309, 323, 124, 140);                // 西:配管の道へ
            WallZ(46.5f, 251, 327, 27, 163, HullColor, 309, 323, 124, 142, barrier: true);  // 東:外殻の外へ
            Solid(-20, 5, 30, 38, 280, 300, DebrisColor);   // 底の瓦礫
            Solid(10, 30, 30, 34, 300, 320, DebrisColor);
            Slab(-14, 14, 254, 265, 53f, FloorColor);       // 足場0(入ったところ)
            Slab(22, 45, 258, 290, 70f, FloorColor);        // 足場1(東)
            RampZ(25, 45, 290, 70, 302, 62, FloorColor);    // 足場1の先の折れた床(垂れ下がる)
            Solid(-20, 22, 75, 78, 282, 288, DebrisColor);  // 折れた梁(足場1 → 足場2 の中継)
            Slab(-45, -20, 272, 306, 88f, FloorColor);      // 足場2(西)
            Solid(-20, 15, 94, 97, 293, 299, DebrisColor);  // 折れた梁(足場2 → 足場3 の中継)
            Slab(15, 45, 280, 302, 106f, FloorColor);       // 足場3(東)
            Slab(-45, 45, 306, 324, 124f, FloorColor);      // 竪穴の上の踊り場(分岐)
            // 足場の敵の持ち場の円は、足場の縁から機体の半径+2m(4m)以上内側に収める(回り込みで落ちないように)。壁側・折れた床側は落ちない
            G(33, 70, 270, 270, leash: 7f);    // 足場1:x 22〜45・z 258〜290(z 290 の先は折れた床の坂)
            G(40, 70, 282, 200, leash: 9f);
            G(-33, 88, 290, 90, leash: 8f);    // 足場2:x -45〜-20・z 272〜306
            G(28, 106, 291, 180, leash: 7f);   // 足場3:x 15〜45・z 280〜302
            G(39, 106, 289, 270, leash: 5f);
            Save("竪穴の上", 30, 124, 316, 270f);   // 足場3から上がったところ(東へも西へも、この脇を通る)

            // ── 4a. 外殻を伝う外の道(開けている):横倒しの船の側面(いまは上面)の外板の上を、斜面(20°)で上る ──
            Region("外殻", 43, 155, 100, 280, 285, 466);
            Slab(48, 150, 290, 332, 124f, HullColor, 6f, floor: true);              // 出たところの外板
            RampZ(48, 150, 332, 124, 432, 160, HullColor, 6f, floor: true);         // 外板の斜面
            Slab(43, 150, 432, 462, 160f, HullColor, 6f, floor: true);              // 上の外板(機関区画の入口)
            // 縁:低い縁取り+見えない壁(落ちない)
            Solid(150, 153, 118, 127, 290, 332, HullColor);
            RampZ(150, 153, 332, 127, 432, 163, HullColor, 9f);
            Solid(150, 153, 154, 163, 432, 462, HullColor);
            Solid(48, 153, 118, 127, 287, 290, HullColor);
            Barrier(150, 153, 287, 465, 100f);
            Barrier(48, 153, 287, 290, 100f);
            Solid(43, 153, 154, 176, 462, 465, HullColor, barrier: true);
            Solid(43, 48, 100, 200, 326, 432, HullColor, barrier: true);   // 船体の張り出し(内側の壁)
            // めくれた外板・アンテナの柱・突き出た残骸(物陰)
            Solid(70, 80, 124, 130, 300, 308, DebrisColor);
            Solid(66, 82, Outer(356) - 3f, Outer(360) + 6f, 356, 360, HullColor);
            Solid(100, 104, Outer(372) - 3f, Outer(388) + 5f, 372, 388, HullColor);
            Solid(128, 132, Outer(400) - 3f, Outer(404) + 45f, 400, 404, MachineColor);
            Solid(60, 76, Outer(404) - 3f, Outer(418) + 8f, 404, 418, MachineColor);
            Solid(95, 110, 160, 166, 440, 446, DebrisColor);
            // 斜面の中ほどに3体、上の外板に3体(群れ)
            G(75, Outer(372), 372, 180);
            G(92, Outer(395), 395, 210);
            G(122, Outer(380), 380, 250);
            G(62, 160, 452, 90);
            G(88, 160, 447, 180);
            G(125, 160, 455, 225);

            // ── 4b. 内部の配管の道(狭い):ダクトを西へ、折れて北へ上り、ポンプ室を抜けて機関区画へ ──
            Region("配管", -135, -40, 119, 192, 300, 445);
            // ダクト1(西へ。幅20m・高さ16m)
            Slab(-119, -48, 303, 329, 124f, FloorColor, 3f, floor: true);
            Slab(-119, -45, 303, 329, 143f, WallColor);
            WallX(304.5f, -119, -48, 121, 143, PipeColor);
            WallX(327.5f, -96, -48, 121, 143, PipeColor);
            Solid(-60, -52, 124, 130, 306, 316, PipeColor);   // 床を這う配管
            // ダクト2(北へ上る。14°)。壁沿いの配管の束で、さらに狭い
            RampZ(-116, -96, 326, 124, 398, 142, FloorColor, 3f, floor: true);
            RampZ(-119, -93, 326, 145, 400, 163, WallColor);   // 天井(床と平行)
            WallZ(-117.5f, 303, 400, 121, 165, PipeColor);
            WallZ(-94.5f, 326, 400, 121, 165, PipeColor);
            RampZ(-116, -111, 330, D2(330) + 5f, 396, D2(396) + 5f, PipeColor, 5f);   // 配管の束
            Solid(-111, -96, D2(352) - 1f, D2(356) + 3.5f, 352, 356, PipeColor);       // 床を横切る太い配管(跳び越える)
            G(-90, 124, 316, 0);
            D(-103, D2(385), 385, 160);
            // ポンプ室(床142m)
            Slab(-133, -77, 397, 443, 142f, FloorColor, 3f, floor: true);
            Slab(-133, -77, 397, 443, 173f, WallColor);
            WallX(398.5f, -133, -77, 139, 173, PipeColor, -116, -96, 142, 160);
            WallX(441.5f, -133, -77, 139, 173, PipeColor);
            WallZ(-131.5f, 397, 443, 139, 173, PipeColor);
            WallZ(-78.5f, 397, 443, 139, 173, PipeColor, 410, 430, 142, 165);
            Solid(-112, -106, 142, 170, 412, 418, PipeColor);   // 縦の配管(柱)
            Solid(-98, -92, 142, 170, 424, 430, PipeColor);
            Solid(-128, -118, 142, 150, 426, 440, MachineColor);   // ポンプ
            G(-88, 142, 432, 270);
            G(-122, 142, 408, 90);
            // ダクト3(東へ上る。26°)→ 機関区画
            RampX(-80, 142, -43, 160, 410, 430, FloorColor, 3f, floor: true);
            RampX(-80, 168, -40, 187.5f, 407, 433, WallColor);
            WallX(408.5f, -80, -40, 139, 190, PipeColor);
            WallX(431.5f, -80, -40, 139, 190, PipeColor);

            // ── 5. 合流:機関区画(床160m)。中央に大きな機関。ボスの前に群れが待つ(拾った武装でまとめて倒す、無双の場所) ──
            Region("機関区画", -40, 43, 155, 195, 400, 463);
            Slab(-43, 43, 402, 463, 160f, FloorColor, 3f, floor: true);
            Slab(-43, 43, 402, 463, 191f, WallColor);
            WallZ(-41.5f, 402, 463, 157, 191, WallColor, 410, 430, 160, 180);
            WallZ(41.5f, 402, 463, 157, 191, HullColor, 435, 455, 160, 178, barrier: true);
            WallX(403.5f, -43, 43, 157, 191, WallColor);
            WallX(461.5f, -43, 43, 157, 191, WallColor, -14, 14, 160, 183);
            Solid(-12, 12, 160, 174, 425, 445, MachineColor);   // 機関
            Solid(-34, -26, 160, 188, 446, 454, PipeColor);
            Solid(26, 34, 160, 188, 408, 416, PipeColor);
            Save("機関区画", 0, 160, 449, 0f);    // 大ホールへの坂の手前(どちらの道から来ても通る)
            // 群れ:兵卒5・剣士3。機関の手前(南の半分)に固まって、西口・東口のほうを向いて立つ。
            // セーブポイント(北の坂の手前)とその再開地点からは 30m 以上離し、機関の陰になり、背を向けている
            // (撃墜されて再開しても群れの真ん中にならない。倒した敵はそのままなので、ボスに負けても群れはやり直しにならない)
            G(-26, 160, 410, 270);
            G(-12, 160, 409, 240);
            G(10, 160, 409, 120);
            G(20, 160, 421, 90);
            G(-22, 160, 424, 270);
            D(-35, 160, 420, 270);   // 西口(配管の道から)を向く
            D(30, 160, 430, 60);     // 東口(外殻の道から)を向く
            D(0, 160, 417, 180);

            // ── 6. 頭頂部の大ホール(床176m):広い。天井が大きく裂けている。四隅寄りに柱(突進を柱にぶつけられる) ──
            Region("大ホール", -75, 75, 155, 280, 463, 625);
            RampZ(-14, 14, 463, 160, 497, 176, FloorColor, 3f, floor: true);   // 機関区画からの坂(25°)
            WallZ(-15.5f, 460, 499, 157, 200, WallColor);
            WallZ(15.5f, 460, 499, 157, 200, WallColor);
            Slab(-17, 17, 460, 499, 200f, WallColor);
            Slab(-73, 73, 496, 622, 176f, FloorColor, 4f, floor: true);
            WallX(497.5f, -73, 73, 172, 236, HullColor, -14, 14, 176, 197, barrier: true);
            WallX(620.5f, -73, 73, 172, 236, HullColor, barrier: true);
            WallZ(-71.5f, 496, 622, 172, 236, HullColor, barrier: true);
            WallZ(71.5f, 496, 622, 172, 236, HullColor, barrier: true);
            Slab(-73, 73, 496, 528, 236f, HullColor);       // 天井(手前)
            Slab(-73, 73, 592, 622, 236f, HullColor);       // 天井(奥)
            Slab(-73, -30, 528, 592, 236f, HullColor);      // 天井(西)。残りは裂けて空が見える
            RampZ(-10, 30, 528, 236, 548, 214, HullColor);  // 裂けて垂れ下がった天井板
            Solid(-50, -42, 176, 236, 526, 534, WallColor);
            Solid(42, 50, 176, 236, 526, 534, WallColor);
            Solid(-50, -42, 176, 236, 586, 594, WallColor);
            Solid(42, 50, 176, 236, 586, 594, WallColor);
            bossRoom = new Bounds(new Vector3(0f, 225f, 559.5f), new Vector3(140f, 110f, 119f));   // 入口の坂を上りきったら
            bossSpawn = Add(Kind.Boss, 0, 176, 575, 180, 0f);

            StartPos = saves[0].RespawnPos;
            StartRot = saves[0].RespawnRot;
        }

        /// <summary>居住区の部屋3の床の高さ(奥へ 8° 上る)。</summary>
        static float R3(float z) => 45f + (z - 196f) * 8f / 58f;
        /// <summary>外殻の斜面の高さ。</summary>
        static float Outer(float z) => 124f + Mathf.Clamp(z - 332f, 0f, 100f) * 0.36f;
        /// <summary>配管の道のダクト2の床の高さ。</summary>
        static float D2(float z) => 124f + Mathf.Clamp(z - 326f, 0f, 72f) * 0.25f;

        // ───────── 配置の書き方 ─────────

        void Region(string name, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
            places.Add(new Area { name = name, bounds = b });
        }

        void Save(string name, float x, float y, float z, float yaw) =>
            saves.Add(new SavePoint { name = name, pos = new Vector3(x, y, z), yaw = yaw });

        void G(float x, float y, float z, float yaw, float leash = 0f) => Add(Kind.Grunt, x, y, z, yaw, leash);
        void D(float x, float y, float z, float yaw) => Add(Kind.Duelist, x, y, z, yaw, 0f);

        Spawn Add(Kind kind, float x, float y, float z, float yaw, float leash)
        {
            var s = new Spawn { kind = kind, pos = new Vector3(x, y, z), yaw = yaw, leash = leash };
            spawns.Add(s);
            return s;
        }

        /// <summary>軸にそろった箱(x・y・z の範囲)。</summary>
        void Solid(float x0, float x1, float y0, float y1, float z0, float z1, Color c, bool barrier = false, bool floor = false)
        {
            var min = new Vector3(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Min(z0, z1));
            var max = new Vector3(Mathf.Max(x0, x1), Mathf.Max(y0, y1), Mathf.Max(z0, z1));
            if ((max - min).x < 0.01f || (max - min).y < 0.01f || (max - min).z < 0.01f) return;
            blocks.Add(new Block { center = (min + max) / 2f, size = max - min, rot = Quaternion.identity, color = c, barrier = barrier, floor = floor });
        }

        /// <summary>床・天井の板(上面の高さ top、厚み thick)。</summary>
        void Slab(float x0, float x1, float z0, float z1, float top, Color c, float thick = 3f, bool floor = false) =>
            Solid(x0, x1, top - thick, top, z0, z1, c, floor: floor);

        /// <summary>四角い穴の空いた板(穴の周りを4枚で組む)。</summary>
        void SlabHole(float x0, float x1, float z0, float z1, float top, float hx0, float hx1, float hz0, float hz1, Color c, float thick = 3f)
        {
            Slab(x0, x1, z0, hz0, top, c, thick);
            Slab(x0, x1, hz1, z1, top, c, thick);
            Slab(x0, hx0, hz0, hz1, top, c, thick);
            Slab(hx1, x1, hz0, hz1, top, c, thick);
        }

        /// <summary>
        /// x に沿った壁(z の位置が中心、厚み T)。開口(ox0〜ox1、高さ oy0〜oy1)があれば、その周りを組む。
        /// barrier なら上へ見えない壁を伸ばす(開口の下の部分には付けない)。
        /// </summary>
        void WallX(float z, float x0, float x1, float y0, float y1, Color c, float ox0 = 0f, float ox1 = 0f, float oy0 = 0f, float oy1 = 0f, bool barrier = false)
        {
            float z0 = z - T / 2f, z1 = z + T / 2f;
            if (ox1 <= ox0) { Solid(x0, x1, y0, y1, z0, z1, c, barrier); return; }
            Solid(x0, ox0, y0, y1, z0, z1, c, barrier);
            Solid(ox1, x1, y0, y1, z0, z1, c, barrier);
            Solid(ox0, ox1, y0, oy0, z0, z1, c);
            Solid(ox0, ox1, oy1, y1, z0, z1, c, barrier);
        }

        /// <summary>z に沿った壁(x の位置が中心、厚み T)。開口は oz0〜oz1、高さ oy0〜oy1。</summary>
        void WallZ(float x, float z0, float z1, float y0, float y1, Color c, float oz0 = 0f, float oz1 = 0f, float oy0 = 0f, float oy1 = 0f, bool barrier = false)
        {
            float x0 = x - T / 2f, x1 = x + T / 2f;
            if (oz1 <= oz0) { Solid(x0, x1, y0, y1, z0, z1, c, barrier); return; }
            Solid(x0, x1, y0, y1, z0, oz0, c, barrier);
            Solid(x0, x1, y0, y1, oz1, z1, c, barrier);
            Solid(x0, x1, y0, oy0, oz0, oz1, c);
            Solid(x0, x1, oy1, y1, oz0, oz1, c, barrier);
        }

        /// <summary>z に沿った斜めの板。上面が (z0, y0) から (z1, y1) へ(z0 &lt; z1)。</summary>
        void RampZ(float x0, float x1, float z0, float y0, float z1, float y1, Color c, float thick = 3f, bool floor = false)
        {
            float run = z1 - z0, rise = y1 - y0;
            var rot = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            Vector3 topMid = new((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
            blocks.Add(new Block
            {
                center = topMid - rot * Vector3.up * (thick / 2f), size = new Vector3(Mathf.Abs(x1 - x0), thick, Mathf.Sqrt(run * run + rise * rise)),
                rot = rot, color = c, floor = floor,
            });
        }

        /// <summary>x に沿った斜めの板。上面が (x0, y0) から (x1, y1) へ(x0 &lt; x1)。</summary>
        void RampX(float x0, float y0, float x1, float y1, float z0, float z1, Color c, float thick = 3f, bool floor = false)
        {
            float run = x1 - x0, rise = y1 - y0;
            var rot = Quaternion.Euler(0f, 0f, Mathf.Atan2(rise, run) * Mathf.Rad2Deg);
            Vector3 topMid = new((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
            blocks.Add(new Block
            {
                center = topMid - rot * Vector3.up * (thick / 2f), size = new Vector3(Mathf.Sqrt(run * run + rise * rise), thick, Mathf.Abs(z1 - z0)),
                rot = rot, color = c, floor = floor,
            });
        }

        /// <summary>見えない壁(y0 から見えない天井まで。機体だけが当たり、レイ・カメラは素通り)。</summary>
        void Barrier(float x0, float x1, float z0, float z1, float y0) =>
            blocks.Add(new Block
            {
                center = new Vector3((x0 + x1) / 2f, (y0 + CeilingY) / 2f, (z0 + z1) / 2f), size = new Vector3(x1 - x0, CeilingY - y0, z1 - z0),
                rot = Quaternion.identity, invisible = true,
            });

        /// <summary>当たり判定のない薄い印(床・天井・壁に描いたドア・照明)。</summary>
        void Decal(float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        {
            Solid(x0, x1, y0, y1, z0, z1, c);
            var b = blocks[^1];
            b.decal = true;
            blocks[^1] = b;
        }

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

            // 今いる場所(名前は画面に出さない。Gizmos と内部の判定用)
            Area now = null;
            foreach (var pl in places)
                if (pl.bounds.Contains(p)) { now = pl; break; }
            if (now != null && now != currentPlace)
            {
                currentPlace = now;
                now.visited = true;
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
            int lost = player != null && player.Arms != null ? player.Arms.LoseAll() : 0;
            if (lost > 0)
            {
                ClearNote = "ボスの爆発に巻き込まれ、武装がすべて消し飛んだ";
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

        public string PlaceName => currentPlace != null ? currentPlace.name : "";

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

        // ───────── シーンビューでの確認 ─────────

        void OnDrawGizmos()
        {
            if (blocks.Count == 0) Define();
            foreach (var b in blocks)
            {
                if (b.decal) continue;
                Gizmos.color = b.invisible ? new Color(1f, 0.3f, 0.3f, 0.3f) : b.color;
                Gizmos.matrix = Matrix4x4.TRS(b.center, b.rot, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, b.size);
            }
            Gizmos.matrix = Matrix4x4.identity;
            foreach (var s in spawns)
            {
                Gizmos.color = s.kind switch { Kind.Boss => Color.red, Kind.Duelist => new Color(1f, 0.6f, 0.2f), _ => Color.yellow };
                float r = s.kind switch { Kind.Boss => 5f, Kind.Duelist => 3f, _ => 2f };
                Vector3 c = s.pos + Vector3.up * r;
                Gizmos.DrawWireSphere(c, r);
                Gizmos.DrawLine(c, c + Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward * r * 3f);   // 立ち番の向き
                if (s.leash > 0f) Gizmos.DrawWireCube(s.pos, new Vector3(s.leash * 2f, 0.1f, s.leash * 2f));
            }
            Gizmos.color = new Color(0.4f, 1f, 0.85f);
            foreach (var sp in saves)
            {
                Gizmos.DrawWireCube(sp.pos + Vector3.up * 5f, new Vector3(2f, 10f, 2f));
                Gizmos.DrawWireSphere(sp.RespawnPos + Vector3.up * 2f, 2f);
            }
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            foreach (var pl in places) Gizmos.DrawWireCube(pl.bounds.center, pl.bounds.size);
        }
    }
}
