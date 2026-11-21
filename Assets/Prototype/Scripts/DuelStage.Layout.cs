using UnityEngine;

namespace Prototype
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

            // ── 5. 合流:機関区画(床160m)。中央に大きな機関。群れが待つ(拾った武装でまとめて倒す、無双の場所。拡張前はボスの前だった) ──
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
            Save("機関区画", 0, 160, 449, 0f);    // 主通路への口の手前(どちらの道から来ても通る)
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

            // ── 6. 主通路(上り坂。20°):機関区画の北口から、傾いた主通路を上る。崩れた隔壁の名残が左右に互い違いに残る(物陰) ──
            Region("主通路", -19, 19, 157, 222, 460, 556);
            RampZ(-16, 16, 463, 160, 547, 192, FloorColor, 3f, floor: true);
            Slab(-16, 16, 547, 556, 192f, FloorColor, 3f, floor: true);   // 上りきった所(格納庫の入口)
            RampZ(-19, 19, 463, 186, 547, 218, WallColor);                // 天井(床と平行)
            Slab(-19, 19, 547, 556, 218f, WallColor);
            WallZ(-17.5f, 460, 556, 157, 222, WallColor);
            WallZ(17.5f, 460, 556, 157, 222, WallColor);
            Solid(-16, -5, Sp(490) - 2f, Sp(494) + 7f, 490, 494, WallColor);   // 崩れた隔壁(左)
            Solid(5, 16, Sp(520) - 2f, Sp(524) + 7f, 520, 524, WallColor);     // 崩れた隔壁(右)
            G(9, Sp(505), 505, 180);
            G(-9, Sp(535), 535, 200);

            // ── 7. 格納庫(床192m・天井250m):広い。倒れたコンテナの山と、壁沿いのキャットウォーク(高さ22m)。倒れたクレーンの桁 ──
            Region("格納庫", -70, 70, 189, 250, 553, 683);
            Slab(-70, 70, 556, 683, 192f, FloorColor, 3f, floor: true);
            Slab(-70, 70, 553, 683, 250f, WallColor);
            WallX(554.5f, -70, 70, 189, 250, WallColor, -16, 16, 189, 215);   // 主通路からの入口
            WallX(681.5f, -70, 70, 189, 250, WallColor, -14, 14, 189, 214);   // 昇降機の竪穴への口
            WallZ(-68.5f, 553, 683, 189, 250, WallColor);
            WallZ(68.5f, 553, 683, 189, 250, WallColor);
            Slab(-67, -50, 600, 680, 214f, FloorColor);                      // キャットウォーク(西)
            Solid(-53, -50, 192, 211, 620, 623, MachineColor);
            Solid(-53, -50, 192, 211, 660, 663, MachineColor);
            Slab(50, 67, 630, 680, 214f, FloorColor);                        // キャットウォーク(東)
            Solid(50, 53, 192, 211, 640, 643, MachineColor);
            Solid(50, 53, 192, 211, 670, 673, MachineColor);
            Solid(-58, -38, 192, 202, 572, 592, FurnitureColor);   // コンテナの山
            Solid(-56, -42, 202, 212, 574, 588, DebrisColor);
            Solid(28, 46, 192, 201, 568, 584, FurnitureColor);
            Solid(-22, -6, 192, 201, 603, 620, DebrisColor);
            Solid(-20, -9, 201, 210, 606, 617, FurnitureColor);
            Solid(28, 48, 192, 204, 612, 640, FurnitureColor);
            Solid(-40, -24, 192, 200, 645, 660, HullColor);
            RampZ(10, 16, 630, 192, 672, 224, MachineColor);       // 倒れたクレーンの桁
            Decal(-67, -66.9f, 230, 232, 560, 680, LampColor);     // 壁を走る照明
            Decal(66.9f, 67, 230, 232, 560, 680, LampColor);
            Decal(-14, 14, 192, 192.06f, 600, 606, DoorColor);     // 床の誘導線
            G(-49, 212, 581, 90, leash: 2f);    // コンテナの山の上:x -56〜-42・z 574〜588
            G(-58, 214, 640, 90, leash: 4f);    // キャットウォーク(西):x -67〜-50
            G(58, 214, 655, 270, leash: 4f);    // キャットウォーク(東):x 50〜67
            G(38, 204, 626, 180, leash: 4f);    // コンテナの上:x 28〜48・z 612〜640
            G(0, 192, 592, 180);
            G(20, 192, 600, 210);
            D(-8, 192, 640, 180);
            D(25, 192, 660, 200);

            // ── 8. 昇降機の竪穴(床192m〜天井333m):細く高い。張り出した足場と、途中で止まった昇降機の箱を、ブースト飛行で上る ──
            Region("昇降機の竪穴", -30, 30, 189, 335, 680, 740);
            Slab(-30, 30, 683, 740, 192f, FloorColor, 3f, floor: true);
            Slab(-30, 30, 680, 740, 333f, WallColor);
            WallX(681.5f, -30, 30, 247, 333, WallColor);                       // 格納庫の天井より上
            WallX(738.5f, -30, 30, 189, 333, WallColor, -12, 12, 302, 324);    // 上の口(船首の外板へ)
            WallZ(-28.5f, 680, 740, 189, 333, WallColor);
            WallZ(28.5f, 680, 740, 189, 333, WallColor);
            Solid(-27, -25, 192, 333, 708, 712, MachineColor);   // 昇降機のレール
            Solid(25, 27, 192, 333, 708, 712, MachineColor);
            Solid(-10, 12, 192, 197, 688, 703, DebrisColor);     // 底の瓦礫
            Slab(-27, -10, 683, 705, 215f, FloorColor);          // 足場1(南西)
            Slab(10, 27, 700, 737, 238f, FloorColor);            // 足場2(北東)
            Solid(-14, 8, 252, 262, 705, 725, MachineColor);     // 止まった昇降機の箱(上に乗れる)
            Solid(6, 7, 262, 299, 722, 724, PipeColor);          // 箱を吊るケーブル(箱の角から、上の踊り場の下まで)
            Slab(8, 27, 683, 700, 280f, FloorColor);             // 足場3(南東)
            Slab(-27, 27, 720, 737, 302f, FloorColor);           // 上の踊り場(北)
            G(-18, 215, 694, 45, leash: 4f);    // 足場1:x -27〜-10・z 683〜705
            G(18, 238, 712, 225, leash: 4f);    // 足場2:x 10〜27・z 700〜737
            G(-3, 262, 714, 180, leash: 4f);    // 昇降機の箱:x -14〜8・z 705〜725
            G(17, 280, 691, 315, leash: 3f);    // 足場3:x 8〜27・z 683〜700
            Save("昇降機の上", 0, 302, 726, 0f);   // 上の口の手前

            // ── 9. 船首の外板(開けている):横倒しの船首の外板の上を、斜面(18°)で上る。めくれた外板・砲塔の残骸・アンテナの柱が物陰 ──
            Region("船首の外板", -63, 63, 290, 380, 737, 893);
            Slab(-60, 60, 740, 770, 302f, HullColor, 6f, floor: true);
            RampZ(-60, 60, 770, 302, 860, 332, HullColor, 6f, floor: true);
            Slab(-60, 60, 860, 890, 332f, HullColor, 6f, floor: true);
            Solid(-63, -30, 296, 345, 737, 740, HullColor, barrier: true);   // 昇降機の竪穴の脇の船体
            Solid(30, 63, 296, 345, 737, 740, HullColor, barrier: true);
            Barrier(-30, 30, 737, 740, 333f);                                // 竪穴の屋根の上から戻れないように
            // 縁:低い縁取り+見えない壁(落ちない)
            Solid(60, 63, 296, 305, 740, 770, HullColor);
            RampZ(60, 63, 770, 305, 860, 335, HullColor, 9f);
            Solid(60, 63, 326, 335, 860, 890, HullColor);
            Solid(-63, -60, 296, 305, 740, 770, HullColor);
            RampZ(-63, -60, 770, 305, 860, 335, HullColor, 9f);
            Solid(-63, -60, 326, 335, 860, 890, HullColor);
            Barrier(60, 63, 737, 893, 290f);
            Barrier(-63, -60, 737, 893, 290f);
            Solid(-35, -25, 302, 309, 750, 758, DebrisColor);                        // めくれた外板
            Solid(20, 32, Bow(790) - 3f, Bow(796) + 8f, 790, 796, HullColor);
            Solid(-40, -28, Bow(815) - 3f, Bow(830) + 10f, 815, 830, MachineColor);  // 砲塔の残骸
            Solid(-35, -33, Bow(830) + 6f, Bow(830) + 8f, 830, 852, MachineColor);   // 砲身
            Solid(30, 34, Bow(840) - 3f, Bow(844) + 40f, 840, 844, MachineColor);    // アンテナの柱
            Solid(5, 18, 332, 338, 868, 878, DebrisColor);
            G(-20, Bow(785), 785, 180);
            G(15, Bow(810), 810, 200);
            G(42, Bow(825), 825, 230);
            D(-10, Bow(848), 848, 180);
            G(-45, 332, 872, 135);
            G(30, 332, 880, 200);

            // ── 10. 艦橋の下(床332m):ボスの前の群れ(拾った武装でまとめて倒す、無双の場所)。北の口から大ホールへの坂 ──
            Region("艦橋の下", -50, 50, 329, 365, 890, 970);
            Slab(-50, 50, 893, 970, 332f, FloorColor, 3f, floor: true);
            Slab(-50, 50, 890, 970, 365f, WallColor);
            WallX(891.5f, -63, 63, 326, 375, HullColor, -16, 16, 332, 356, barrier: true);   // 外板からの入口
            WallX(968.5f, -50, 50, 329, 365, WallColor, -14, 14, 332, 355);                  // 大ホールへの口
            WallZ(-48.5f, 890, 970, 329, 365, WallColor);
            WallZ(48.5f, 890, 970, 329, 365, WallColor);
            Solid(-26, -20, 332, 362, 915, 921, WallColor);     // 柱
            Solid(20, 26, 332, 362, 915, 921, WallColor);
            Solid(-42, -32, 332, 338, 935, 950, MachineColor);  // 計器盤
            Solid(32, 42, 332, 338, 935, 950, MachineColor);
            Decal(-47, -46.9f, 350, 352, 900, 965, LampColor);
            Decal(46.9f, 47, 350, 352, 900, 965, LampColor);
            Save("艦橋の下", 0, 332, 956, 0f);    // 大ホールへの坂の手前
            // 群れ:兵卒5・剣士2。入口(南)のほうを向いて立つ。セーブポイントと再開地点からは 30m 以上離し、背を向けている
            G(-30, 332, 905, 180);
            G(-12, 332, 902, 180);
            G(12, 332, 904, 180);
            G(30, 332, 908, 180);
            G(0, 332, 918, 180);
            D(-15, 332, 928, 200);
            D(15, 332, 928, 160);

            // ── 11. 頭頂部の大ホール(ボス)。形は拡張前のまま、艦橋の下の北の口の先へ移す ──
            BossHall(332f - 160f, 970f - 463f);

            StartPos = saves[0].RespawnPos;
            StartRot = saves[0].RespawnRot;
        }

        /// <summary>
        /// 頭頂部の大ホール(床176m):広い。天井が大きく裂けている。四隅寄りに柱(突進を柱にぶつけられる)。
        /// 座標は拡張前(機関区画の北の口の先にあったとき)のまま書き、dy・dz だけ上・奥へ移す。
        /// 手前の部屋の北の壁(z 460〜463 に当たる所。口は x -14〜14・高さ 0〜23m)は手前の部屋が持つ。
        /// 大ホールの入口(坂を上りきった所の壁の開口)には、ボス戦の間だけ閉じる扉がある(bossGate)。
        /// </summary>
        void BossHall(float dy, float dz)
        {
            Region("大ホール", -75, 75, 155 + dy, 280 + dy, 463 + dz, 625 + dz);
            RampZ(-14, 14, 463 + dz, 160 + dy, 497 + dz, 176 + dy, FloorColor, 3f, floor: true);   // 手前の部屋からの坂(25°)
            WallZ(-15.5f, 460 + dz, 499 + dz, 157 + dy, 200 + dy, WallColor);
            WallZ(15.5f, 460 + dz, 499 + dz, 157 + dy, 200 + dy, WallColor);
            Slab(-17, 17, 460 + dz, 499 + dz, 200f + dy, WallColor);
            Slab(-73, 73, 496 + dz, 622 + dz, 176f + dy, FloorColor, 4f, floor: true);
            WallX(497.5f + dz, -73, 73, 172 + dy, 236 + dy, HullColor, -14, 14, 176 + dy, 197 + dy, barrier: true);
            WallX(620.5f + dz, -73, 73, 172 + dy, 236 + dy, HullColor, barrier: true);
            WallZ(-71.5f, 496 + dz, 622 + dz, 172 + dy, 236 + dy, HullColor, barrier: true);
            WallZ(71.5f, 496 + dz, 622 + dz, 172 + dy, 236 + dy, HullColor, barrier: true);
            Slab(-73, 73, 496 + dz, 528 + dz, 236f + dy, HullColor);       // 天井(手前)
            Slab(-73, 73, 592 + dz, 622 + dz, 236f + dy, HullColor);       // 天井(奥)
            Slab(-73, -30, 528 + dz, 592 + dz, 236f + dy, HullColor);      // 天井(西)。残りは裂けて空が見える
            RampZ(-10, 30, 528 + dz, 236 + dy, 548 + dz, 214 + dy, HullColor);  // 裂けて垂れ下がった天井板
            Solid(-50, -42, 176 + dy, 236 + dy, 526 + dz, 534 + dz, WallColor);
            Solid(42, 50, 176 + dy, 236 + dy, 526 + dz, 534 + dz, WallColor);
            Solid(-50, -42, 176 + dy, 236 + dy, 586 + dz, 594 + dz, WallColor);
            Solid(42, 50, 176 + dy, 236 + dy, 586 + dz, 594 + dz, WallColor);
            bossRoom = new Bounds(new Vector3(0f, 225f + dy, 559.5f + dz), new Vector3(140f, 110f, 119f));   // 入口の坂を上りきったら
            // ボス戦の入口の扉:上の入口の壁(z 496〜499 に当たる所)の開口(x -14〜14・高さ 0〜21m)を、壁の厚みの中ほどでふさぐ。
            // 開口より GateInset だけ大きくして、縁を左右の壁・床・上の壁に埋める(すき間を作らない)。作るのは DuelStage.BuildBossGate
            bossGate.SetMinMax(new Vector3(-14f - GateInset, 176f + dy - GateInset, 497.5f + dz - GateThick / 2f),
                               new Vector3(14f + GateInset, 197f + dy + GateInset, 497.5f + dz + GateThick / 2f));
            bossSpawn = Add(Kind.Boss, 0, 176 + dy, 575 + dz, 180, 0f);
        }

        /// <summary>居住区の部屋3の床の高さ(奥へ 8° 上る)。</summary>
        static float R3(float z) => 45f + (z - 196f) * 8f / 58f;
        /// <summary>外殻の斜面の高さ。</summary>
        static float Outer(float z) => 124f + Mathf.Clamp(z - 332f, 0f, 100f) * 0.36f;
        /// <summary>配管の道のダクト2の床の高さ。</summary>
        static float D2(float z) => 124f + Mathf.Clamp(z - 326f, 0f, 72f) * 0.25f;
        /// <summary>主通路の床の高さ(機関区画の北の口から 20° で上る)。</summary>
        static float Sp(float z) => 160f + Mathf.Clamp(z - 463f, 0f, 84f) * 32f / 84f;
        /// <summary>船首の外板の斜面の高さ。</summary>
        static float Bow(float z) => 302f + Mathf.Clamp(z - 770f, 0f, 90f) / 3f;

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
            Gizmos.color = new Color(1f, 0.5f, 0.2f);
            Gizmos.DrawWireCube(bossGate.center, bossGate.size);   // ボス戦の入口の扉(閉じたとき)
        }
    }
}
