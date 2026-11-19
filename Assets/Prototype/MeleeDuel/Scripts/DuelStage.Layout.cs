using UnityEngine;

namespace MeleeDuel
{
    // DuelStage の地形と敵の配置(Define)と、その書き方・シーンビューでの確認(partial。本体と全体の説明は DuelStage.cs)
    public partial class DuelStage
    {
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
