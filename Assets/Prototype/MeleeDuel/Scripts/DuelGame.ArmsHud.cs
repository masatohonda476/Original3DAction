using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelGame の武装の表示:右下の武装の欄・拾う案内(カード・付け先を選ぶ表示・分解を選ぶ表示)・付け先を選ぶ表示の性能のグラフ(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
        // ───────── 右下の武装の欄(武装ハントの DrawWeapons / WeaponCell と同じ見せ方) ─────────

        const float CellW = 200f, CellH = 44f, CellGap = 8f, BackRowH = 24f, ArmsMargin = 20f, WeightRowH = 20f;
        /// <summary>右下の武装の欄の上端(重さの1行を含む。拾う案内をこれより上に出す)。</summary>
        float ArmsTop => SH - ArmsMargin - BackRowH - 6f - CellH * 2f - CellGap - WeightRowH;
        /// <summary>右下の武装の欄の左端(左下のメニューの案内は、これより左に収める)。</summary>
        float ArmsLeft => SW - ArmsMargin - CellW * 2f - CellGap;

        /// <summary>セルの右上に出す状態。</summary>
        enum CellState { None, Ammo, Energy, Cost, Reload, Loading, Charging, Charged, TwoHand, Starved, NoEnergy, Guard, Guarding, GuardBroken }

        // セルの文字列(添字は DuelSlot)。名前・ボタンは武装か機器が、状態は武装・状態・残り(弾数か0.1秒単位。盾はガードの残りと最大)が変わったときだけ作り直す
        readonly string[] cellBtn = new string[ArmRows], cellName = new string[ArmRows], cellStatus = new string[ArmRows];
        readonly (DuelWeapon w, ProtoInput.Device dev, bool set)[] cellKey = new (DuelWeapon, ProtoInput.Device, bool)[ArmRows];
        readonly (DuelWeapon w, CellState st, int val, int max, bool set)[] cellStatusKey = new (DuelWeapon, CellState, int, int, bool)[ArmRows];

        static readonly Color GaugeOrange = new(1f, 0.6f, 0.15f, 0.35f), StatusOrange = new(1f, 0.75f, 0.4f);
        // 盾のセルのガードのゲージ:ふだん・構えている間・崩れている間
        static readonly Color GaugeGuard = new(0.4f, 0.6f, 0.85f, 0.35f), GaugeGuarding = new(0.5f, 0.8f, 1f, 0.5f), GaugeGuardBroken = new(1f, 0.4f, 0.2f, 0.4f);

        /// <summary>
        /// 右下:付けている武装を肩ボタンの並び(左=L・右=R、上の段=肩・下の段=手)の 2×2 のセルで、その下に背中の1行。
        /// セルにはボタン名・レア度の色の四角・武装名。右上に状態:銃は「弾 12/20 (1/発)」(弾倉の残りと、1発詰めるごとに使う共通の弾。残りが弾倉の1/4以下で赤っぽく)、
        /// 砲・ミサイルと L1 の実弾の撃ち方は撃てる間「弾 6/回」(1回で使う共通の弾)、エネルギー武器は「EN 20/秒」(ENが0以下で撃てない間は「EN切れ」)。
        /// 銃のリロード中・砲とミサイルの装填中は、セル全体を左から満ちていくオレンジのゲージにし「リロード 1.2秒」「装填 0.8秒」。
        /// 盾はガードのゲージを同じ形で出す(作者「左下は消して、右下の武装欄の盾のところの、リロードとかと同じ感じで」):セルを残りの分だけ左から青く満たし
        /// 「ガード 80/100」。構えている盾は明るい青で「ガード中」、崩れている間は赤っぽく「ガード崩れ」。盾が2枚でもゲージは1本なので、どちらのセルにも同じ量。
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
            int val = 0, max = 0;
            float gauge = 0f;
            Color gaugeColor = GaugeOrange;
            if (w != null && !twoL)
            {
                if (w.kind == DuelKind.Shield)
                {
                    // 盾:ガードのゲージ(残りの分だけセルを満たす。盾が2枚でもゲージは1本なので、どちらのセルにも同じ量)
                    float guardMax = player.MaxGuard;
                    bool held = player.Guarding && player.GuardHand == (slot == DuelSlot.RightHand ? 0 : 1);
                    st = player.Current == DuelPlayer.State.GuardBroken ? CellState.GuardBroken : held ? CellState.Guarding : CellState.Guard;
                    val = Mathf.CeilToInt(player.Guard);
                    max = Mathf.RoundToInt(guardMax);
                    gauge = guardMax > 0f ? player.Guard / guardMax : 0f;
                    gaugeColor = st == CellState.GuardBroken ? GaugeGuardBroken : held ? GaugeGuarding : GaugeGuard;
                }
                else if (arms.Starved(slot)) st = CellState.Starved;   // 共通の弾が足りなくて撃てない
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
                else if (w.PaysPerShot) st = CellState.Cost;   // 弾倉のない実弾(砲・ミサイル):撃てる間は1回で使う共通の弾
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
                else if (arms.AltFire.PaysPerShot) st = CellState.Cost;   // L1 の実弾の撃ち方:撃てる間は1回で使う共通の弾
            }
            if (st == CellState.None && w is { IsTwoHand: true }) st = CellState.TwoHand;
            if (gauge > 0f) Chip(new Rect(r.x, r.y, r.width * Mathf.Clamp01(gauge), r.height), gaugeColor);

            if (st != CellState.None)
            {
                if (cellStatusKey[i] != (w, st, val, max, true))
                {
                    cellStatusKey[i] = (w, st, val, max, true);
                    cellStatus[i] = st switch
                    {
                        CellState.Ammo => w.UsesAmmo ? $"弾 {val}/{w.magazine} ({w.AmmoCostText})" : $"弾 {val}/{w.magazine}",
                        CellState.Energy => w.UsesEnergy ? w.EnText : w.UsesAmmo ? "弾 " + w.AmmoCostText : "弾数∞",
                        CellState.Cost => "弾 " + (twoL ? arms.AltFire : w).AmmoCostText,
                        CellState.NoEnergy => "EN切れ",
                        CellState.Reload => $"リロード {val / 10f:0.0}秒",
                        CellState.Loading => $"装填 {val / 10f:0.0}秒",
                        CellState.Charging => "溜め中",
                        CellState.Charged => "溜まった・離して撃つ",
                        CellState.Starved => w.IsGun && w.magazine > 0 && !twoL ? "弾 0 ・弾切れ" : "弾切れ",
                        CellState.Guard => $"ガード {val}/{max}",
                        CellState.Guarding => $"ガード中 {val}/{max}",
                        CellState.GuardBroken => "ガード崩れ",
                        _ => "両手持ち",
                    };
                }
                GUI.color = st switch
                {
                    CellState.Ammo => val <= w.magazine / 4 ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.87f, 0.9f),
                    CellState.Energy => new Color(1f, 0.6f, 0.95f),
                    CellState.Cost => new Color(0.85f, 0.87f, 0.9f),
                    CellState.Reload or CellState.Loading => StatusOrange,
                    CellState.Charging => new Color(1f, 0.85f, 0.5f),
                    CellState.Charged => Color.white,
                    CellState.Starved or CellState.NoEnergy or CellState.GuardBroken => new Color(1f, 0.45f, 0.35f),
                    CellState.Guard => new Color(0.7f, 0.85f, 1f),
                    CellState.Guarding => new Color(0.8f, 0.95f, 1f),
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

        // 拾う案内の文字列は、中身が変わったときだけ作り直す(OnGUI は1フレームに何度も呼ばれる)。
        // 弾・HP は分解を選んでいる間だけ値で、ほかは上限・満タンかどうかだけ入れる(撃つ・被弾するたびに作り直さないように)
        (DuelLoot loot, DuelLoot choosing, DuelLoot breaking, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev, int ammo, int hp, int weight) pickupKey;
        bool pickupBuilt;
        // 付け先を選ぶ表示のグラフは、武装・比べる相手・機器が変わったときだけ作り直す
        (DuelLoot loot, DuelLoot choosing, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev) pickChartKey;
        readonly string[] pickupLines = new string[4];
        int pickupLineCount;

        const string PickRed = "#FF7A6A", PickGrey = "#AAAAAA";

        /// <summary>
        /// 画面の下の中央:拾える武装があれば、その案内。無視すればそのまま残る。空中では出ない(DuelArms.Nearby が null)。
        ///   まだ何も押していない間:枠のあるカード(BuildPickCard / DrawPickCard)。名前・その武装の数値・付け先ごとの今の武装との差・操作を最初から並べる。
        ///   十字右で付け先を選ぶ表示(付け先が空きでもふさがっていても同じ):手・肩なら左右それぞれの今の武装と、背中なら今の背中と比べる(背中は ✗ で付ける)。
        ///   十字上で分解を選ぶ表示:十字左で弾・十字右で回復・十字上でやめる。
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
            bool card = arms.Choosing == null && !breaking;   // まだ何も押していない:カード
            var key = (loot, arms.Choosing, arms.Breaking, a, b, ProtoInput.Current,
                       breaking ? Mathf.FloorToInt(arms.Ammo + 0.0001f) : ammoFull ? -2 : -1, breaking ? Mathf.CeilToInt(player.HP) : hpFull ? -2 : -1,
                       card ? Mathf.RoundToInt(arms.TotalWeight * 10f) : 0);
            if (pickupKey != key || !pickupBuilt)
            {
                pickupKey = key;
                pickupBuilt = true;
                bool curTwo = a is { IsTwoHand: true };   // 今、両手持ちを付けている(右手と左手に同じ武装)
                if (card) BuildPickCard(arms, w, to, a, b, ammoFull, hpFull);
                else BuildPickLines(arms, w, to, a, b, curTwo, ammoFull, hpFull);
                var ck = (loot, arms.Choosing, a, b, ProtoInput.Current);
                if (pickChartKey != ck)
                {
                    pickChartKey = ck;
                    BuildPickCharts(arms.Choosing != null ? w : null, to, a, b, curTwo);
                }
            }
            // 右下の武装の欄と左下の HP・メニューの案内に重ならない高さ(行が多いときは上へずらす)
            float limit = Mathf.Min(ArmsTop, leftTop) - 8f;
            if (card)
            {
                DrawPickCard(limit);
                return;
            }
            float h = 6f + 26f * pickupLineCount, wd = 1200f, bx = (SW - wd) / 2f;
            float by = Mathf.Min(SH * 0.56f, limit - h);
            CardFrame(new Rect(bx, by, wd, h), w.Color, 0.74f);
            for (int i = 0; i < pickupLineCount; i++)
                GUI.Label(new Rect(bx, by + 4f + i * 26f, wd, 24f), pickupLines[i], richCenter);
            if (arms.Choosing != null) DrawPickCharts(by);
        }

        /// <summary>
        /// 付け先を選ぶ表示・分解を選ぶ表示の文字(中央寄せの行。pickupLines)。
        /// 付け先を選ぶ表示では、付けると重さの上限を超える所に赤で「重すぎる」を添える(押すと断られる。DuelArms.UpdateChoice)。
        /// </summary>
        void BuildPickLines(DuelArms arms, DuelWeapon w, DuelSlot[] to, DuelWeapon a, DuelWeapon b, bool curTwo, bool ammoFull, bool hpFull)
        {
            string name = "<color=#" + ColorUtility.ToHtmlStringRGB(w.Color) + ">" + w.name + "〈" + w.RarityName + "〉</color>";
            string equip = "[" + ProtoInput.L(ProtoInput.Btn.EquipNow) + "]";
            string Heavy(DuelSlot s) => arms.Fits(arms.WeightIfEquipped(w, s)) ? "" : " <color=" + PickRed + ">重すぎる</color>";
            if (arms.Breaking != null)
            {
                // 分解を選ぶ:十字左で弾、十字右で回復(装備の確認の「← 弾 / → 回復」と同じ左右)、十字上(入ったのと同じボタン)でやめる
                string ammoBtn = "[" + ProtoInput.L(ProtoInput.Btn.BreakAmmo) + "]", healBtn = "[" + ProtoInput.L(ProtoInput.Btn.BreakHeal) + "]";
                string breakBtn = "[" + ProtoInput.L(ProtoInput.Btn.Break) + "]";
                int salAmmo = arms.SalvageAmmo(w), salHeal = arms.SalvageHeal(w);
                var rules = arms.Rules;
                float a1 = Mathf.Min(rules.ammoMax, arms.Ammo + salAmmo), h1 = Mathf.Min(player.MaxHP, player.HP + salHeal);
                // 弾が上限・HP が満タンの選択肢は赤く(押しても分解しない。武装だけ消えないように)
                string ammoOpt = ammoFull
                    ? "<color=" + PickRed + ">" + ammoBtn + " 弾は上限(" + rules.ammoMax.ToString("0") + ")</color>"
                    : ammoBtn + " 弾にする +" + salAmmo + "(弾 " + Mathf.FloorToInt(arms.Ammo + 0.0001f) + "→" + Mathf.FloorToInt(a1 + 0.0001f) + "/" + rules.ammoMax.ToString("0") + ")";
                string healOpt = hpFull
                    ? "<color=" + PickRed + ">" + healBtn + " HP は満タン</color>"
                    : healBtn + " 回復にする +" + salHeal + "(HP " + Mathf.CeilToInt(player.HP) + "→" + Mathf.CeilToInt(h1) + "/" + player.MaxHP.ToString("0") + ")";
                pickupLines[0] = name + " を分解する?   " + ammoOpt + "    " + healOpt + "    " + breakBtn + " やめる";
                pickupLines[1] = "<color=" + PickGrey + ">分解した武装はなくなる(弾は上限まで・回復は HP の最大まで)</color>";
                pickupLineCount = 2;
            }
            else if (w.IsTwoHand)
            {
                // 両手持ち:R1/L1 のどちらを押しても両手に付き、今の右手・左手の武装は両方とも置かれる
                string r1 = SlotButton(DuelSlot.RightHand), l1 = SlotButton(DuelSlot.LeftHand);
                var cmp = DuelWeapon.SameFamily(a, w) ? a : DuelWeapon.SameFamily(b, w) ? b : null;
                string off = curTwo ? "両手の" + a.name : "右手の" + (a != null ? a.name : "なし") + "・左手の" + (b != null ? b.name : "なし");
                pickupLines[0] = name + " を両手に付ける?  [" + r1 + "] / [" + l1 + "] どちらでも両手に付く" + Heavy(DuelSlot.RightHand) + "    " + equip + " やめる";
                pickupLines[1] = (a == null && b == null ? "<color=" + PickGrey + ">両手とも空いている</color>"
                                     : "<color=#FFB070>" + off + " は足もとに置かれる(拾い直せる)</color>");
                pickupLines[2] = (cmp != null ? (cmp == a ? (curTwo ? "両手の" : "右手の") : "左手の") + cmp.name + "と比べて  " : "") + DuelWeapon.CompareText(w, cmp);
                pickupLines[3] = TwoHandMoves(w);
                pickupLineCount = 4;
            }
            else if (to.Length == 1)
            {
                // 背中:✗ を押すと付く(✗ は飛行と同じボタン。選んでいる間は飛ばない)
                string fly = "[" + ProtoInput.L(ProtoInput.Btn.Fly) + "]";
                pickupLines[0] = name + " を背中に付ける?  " + fly + " " + DuelWeapon.SlotName(to[0]) + "に付ける(今:" + (a != null ? a.name : "なし") + ")" + Heavy(to[0]) + "    " + equip + " やめる";
                pickupLines[1] = DuelWeapon.SlotName(to[0]) + "と比べて  " + DuelWeapon.CompareText(w, a);
                pickupLines[2] = "<color=" + PickGrey + ">選んでいる間は " + ProtoInput.L(ProtoInput.Btn.Fly) + " で飛ばない</color>";
                pickupLineCount = 3;
            }
            else
            {
                pickupLines[0] = name + " をどちらに付ける?  [" + SlotButton(to[0]) + "] " + DuelWeapon.SlotName(to[0]) + "(今:" + (a != null ? a.name : "なし") + ")" + Heavy(to[0])
                                 + " / [" + SlotButton(to[1]) + "] " + DuelWeapon.SlotName(to[1]) + "(今:" + (b != null ? b.name : "なし") + ")" + Heavy(to[1]) + "    " + equip + " やめる";
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
        }

        // ───────── 拾う案内のカード(まだ何も押していない間) ─────────

        // 中の幅は中身に合わせる(CardMinInner〜CardMaxInner)。項目の多い行(その武装の数値・付け先ごとの差・操作)は、幅を測って項目の切れ目で折り返す(文字を枠で切らない)
        const float CardMaxInner = 860f, CardMinInner = 560f, CardPadX = 14f, CardPadY = 8f, CardLine = 22f, CardAccent = 4f, CardColGap = 14f;
        const float CardTitleH = 26f, CardDivider = 9f, CardActionH = 24f, CardCurMax = 380f;
        /// <summary>カードの文字 1 行を描く枠の高さ。行の送り(CardLine)より高くする(同じ高さだと「↓」の下が切れる。上寄せなので文字の位置は変わらない)。</summary>
        const float CardTextH = 28f;
        const int CardRowMax = 2;
        string cardName, cardKind, cardWeight, cardFeel, cardMoves;
        float cardNameW, cardMovesH, cardInner, cardH, cardSlotW, cardCurW, cardWeightW;
        Color cardColor;
        int cardRowCount;
        /// <summary>一言を見出しの行(種類の右)に続けて出しているか(収まらなければ次の行に出す)。</summary>
        bool cardFeelInline;
        readonly List<string> cardStats = new(), cardActions = new(), cardPack = new();
        readonly string[] cardRowSlot = new string[CardRowMax], cardRowCur = new string[CardRowMax], cardRowWeight = new string[CardRowMax];
        readonly List<string>[] cardRowDiff = { new(), new() };
        static readonly GUIContent cardMeasure = new();

        /// <summary>その文字の1行の幅(リッチテキストのタグは数えない。OnGUI の中で呼ぶ)。</summary>
        static float TextW(GUIStyle style, string text)
        {
            cardMeasure.text = text;
            return style.CalcSize(cardMeasure).x;
        }

        /// <summary>
        /// items を sep でつなぎ、幅 maxW に収まるところで行を分けて lines に入れる(項目の途中では切らない。1つで幅を超える項目はそのまま1行)。
        /// 一番広い行の幅を返す。
        /// </summary>
        static float PackLines(List<string> items, string sep, float maxW, GUIStyle style, List<string> lines)
        {
            lines.Clear();
            float widest = 0f, curW = 0f;
            string cur = null;
            foreach (var it in items)
            {
                if (cur != null)
                {
                    string joined = cur + sep + it;
                    float jw = TextW(style, joined);
                    if (jw <= maxW)
                    {
                        cur = joined;
                        curW = jw;
                        continue;
                    }
                    lines.Add(cur);
                    widest = Mathf.Max(widest, curW);
                }
                cur = it;
                curW = TextW(style, cur);
            }
            if (cur != null)
            {
                lines.Add(cur);
                widest = Mathf.Max(widest, curW);
            }
            return widest;
        }

        /// <summary>
        /// カードの中身を作る(作者が選んだ形:文字の帯をやめ、最初から付け先ごとの差を並べる)。上から:
        ///   見出し:名前〈レア度〉(レア度の色)・種類と撃ち方・一言(見出しの行に収まらなければ次の行)、右に今の重さの合計。両手持ちはその下に R1/L1 の技。
        ///   その武装自身の数値(付け先が全部空きでも性能が分かるように)。
        ///   付け先ごとに1行:所の名前(両手持ちは「両手」)・今そこにある武装(なければ「空き」)・差の短い形(DuelWeapon.CompareShort:差のある項目だけ「威力↑↑」。
        ///     良くなるのは緑・悪くなるのは赤)・付けた後の重さ。仲間が違う今の武装とは比べない(付け先を選ぶ表示のグラフと同じ扱い)。
        ///     付けると重さの上限を超える所は赤で「重すぎる」(外れる武装の重さを引いて、所ごとに判定。DuelArms.WeightIfEquipped)。
        ///   操作:付ける(付け先を選ぶ表示へ。どの所でも重すぎるなら赤)・持つ(重さの変化)・分解(量)。
        /// </summary>
        void BuildPickCard(DuelArms arms, DuelWeapon w, DuelSlot[] to, DuelWeapon a, DuelWeapon b, bool ammoFull, bool hpFull)
        {
            float max = arms.CarryMax, total = arms.TotalWeight;
            string maxText = max.ToString("0"), totalText = total.ToString("0");
            cardColor = w.Color;
            cardName = w.name + "〈" + w.RarityName + "〉";
            cardNameW = TextW(cardTitleStyle, cardName);
            cardKind = DuelWeapon.KindName(w.kind) + (w.IsTwoHand ? "・両手持ち" : w.FireMode.Length > 0 ? "・" + w.FireMode : "");
            cardWeight = "今の重さ " + totalText + "/" + maxText;
            cardFeel = w.feel;
            cardMoves = w.IsTwoHand ? TwoHandMoves(w) : null;
            // 一言は、見出しの行に収まるなら種類の右に続ける(カードを低くして、画面の真ん中を隠しにくくする)。収まらなければ次の行
            float headW = cardNameW + 8f + TextW(richSmall, cardKind) + 24f + TextW(richSmall, cardWeight);
            string kindFeel = cardFeel.Length > 0 ? cardKind + "  ―  " + cardFeel : cardKind;
            float headFeelW = cardNameW + 8f + TextW(richSmall, kindFeel) + 24f + TextW(richSmall, cardWeight);
            cardFeelInline = headFeelW <= CardMaxInner;
            if (cardFeelInline) cardKind = kindFeel;
            float inner = cardFeelInline ? headFeelW : Mathf.Max(headW, TextW(richSmall, cardFeel));
            if (cardMoves != null) inner = Mathf.Max(inner, Mathf.Min(CardMaxInner, TextW(chartNoteStyle, cardMoves)));

            // その武装自身の数値
            cardPack.Clear();
            foreach (var st in DuelWeapon.Stats(w)) cardPack.Add(st.name + " " + st.text);
            inner = Mathf.Max(inner, PackLines(cardPack, "   ", CardMaxInner, richSmall, cardStats));

            // 付け先ごとの行(両手持ち・背中は1行)
            bool curTwo = a is { IsTwoHand: true };
            bool anyFits = false;
            cardRowCount = w.IsTwoHand || to.Length == 1 ? 1 : CardRowMax;
            cardSlotW = cardCurW = cardWeightW = 0f;
            const string emptyPlain = "空き";
            string empty = "<color=" + PickGrey + ">" + emptyPlain + "</color>";
            static string Named(DuelWeapon x, string none) => x == null ? none : "<color=#" + ColorUtility.ToHtmlStringRGB(x.Color) + ">" + x.name + "</color>";
            static string Plain(DuelWeapon x) => x == null ? emptyPlain : x.name;
            for (int i = 0; i < cardRowCount; i++)
            {
                var slot = to[i];
                var cur = i == 0 ? a : b;
                float after = arms.WeightIfEquipped(w, slot);
                bool fits = arms.Fits(after);
                anyFits |= fits;
                string slotName = w.IsTwoHand ? "両手" : DuelWeapon.SlotName(slot);
                cardRowSlot[i] = fits ? slotName : "<color=" + PickRed + ">" + slotName + "</color>";
                string weightPlain = (fits ? "→ 重さ " : "重すぎる ") + after.ToString("0") + "/" + maxText;
                cardRowWeight[i] = "<color=" + (fits ? "#C8C8C8" : PickRed) + ">" + weightPlain + "</color>";
                // 今そこにある武装と、比べる相手。両手持ちを付けるときは右手・左手の両方が外れ、同じ仲間のほう(右手が先)と比べる
                bool bothHands = w.IsTwoHand && !curTwo;
                var cmp = bothHands ? (DuelWeapon.SameFamily(a, w) ? a : DuelWeapon.SameFamily(b, w) ? b : null) : cur;
                bool occupied = bothHands ? a != null || b != null : cur != null;
                string twoNote = curTwo && !w.IsTwoHand ? "(両手持ち)" : "";   // 片手の武装を付けると、今の両手持ちが外れる(どちらの手に付けても)
                cardRowCur[i] = !occupied ? empty
                    : bothHands ? "右 " + Named(a, empty) + " ・ 左 " + Named(b, empty)
                    : Named(cur, empty) + twoNote;
                // 列の幅は、色のタグを抜いた文字で測る
                string curPlain = !occupied ? emptyPlain : bothHands ? "右 " + Plain(a) + " ・ 左 " + Plain(b) : Plain(cur) + twoNote;
                cardPack.Clear();
                // 差の列(空きなら比べる相手がないので、何も出さない)
                if (occupied && cmp == w) cardPack.Add("<color=" + PickGrey + ">同じ武装(性能は同じ)</color>");
                else if (occupied && !DuelWeapon.SameFamily(cmp, w)) cardPack.Add("<color=" + PickGrey + ">仲間が違うので比べない</color>");
                else if (occupied)
                {
                    if (bothHands && a != null && b != null) cardPack.Add("<color=" + PickGrey + ">" + cmp.name + "と比べて</color>");
                    int n0 = cardPack.Count;
                    DuelWeapon.CompareShort(w, cmp, cardPack);
                    if (cardPack.Count == n0) cardPack.Add("<color=" + PickGrey + ">ほぼ同じ</color>");
                }
                cardSlotW = Mathf.Max(cardSlotW, TextW(richSmall, slotName));
                cardCurW = Mathf.Max(cardCurW, Mathf.Min(CardCurMax, TextW(richSmall, curPlain)));
                cardWeightW = Mathf.Max(cardWeightW, TextW(richSmall, weightPlain));
                // 差の項目は、列の幅が全部の行で決まってから詰める(下の回り)。それまで cardRowDiff に預ける
                cardRowDiff[i].Clear();
                cardRowDiff[i].AddRange(cardPack);
            }
            float diffMax = Mathf.Max(120f, CardMaxInner - cardSlotW - cardCurW - cardWeightW - CardColGap * 3f), diffW = 0f;
            for (int i = 0; i < cardRowCount; i++)
            {
                cardPack.Clear();
                cardPack.AddRange(cardRowDiff[i]);
                diffW = Mathf.Max(diffW, PackLines(cardPack, "  ", diffMax, richSmall, cardRowDiff[i]));
            }
            inner = Mathf.Max(inner, cardSlotW + cardCurW + diffW + cardWeightW + CardColGap * 3f);

            // 操作:付ける / 持つ(所持品へ。重さの上限を超えるなら持てない)/ 分解(弾か回復を選ぶ)
            string equipBtn = "[" + ProtoInput.L(ProtoInput.Btn.EquipNow) + "]";
            string keepBtn = "[" + ProtoInput.L(ProtoInput.Btn.Stash) + "]", breakBtn = "[" + ProtoInput.L(ProtoInput.Btn.Break) + "]";
            int salAmmo = arms.SalvageAmmo(w), salHeal = arms.SalvageHeal(w);
            float kept = arms.WeightIfStashed(w);
            cardPack.Clear();
            cardPack.Add(anyFits ? equipBtn + " 付ける" : "<color=" + PickRed + ">" + equipBtn + " 付ける:重すぎる</color>");
            cardPack.Add(arms.Fits(kept)
                ? keepBtn + " 持つ(重さ " + totalText + "→" + kept.ToString("0") + "/" + maxText + ")"
                : "<color=" + PickRed + ">" + keepBtn + " 持つ:重すぎる(" + kept.ToString("0") + "/" + maxText + ")</color>");
            cardPack.Add(ammoFull && hpFull ? "<color=" + PickRed + ">" + breakBtn + " 分解(弾は上限・HP は満タン)</color>"
                : breakBtn + " 分解(" + (ammoFull ? "<color=" + PickRed + ">弾は上限</color>" : "弾 +" + salAmmo) + " / "
                  + (hpFull ? "<color=" + PickRed + ">HP は満タン</color>" : "回復 +" + salHeal) + ")");
            inner = Mathf.Max(inner, PackLines(cardPack, "     ", CardMaxInner, richSmall, cardActions));

            cardInner = Mathf.Clamp(Mathf.Ceil(inner), CardMinInner, CardMaxInner);
            cardMovesH = 0f;
            if (cardMoves != null)
            {
                cardMeasure.text = cardMoves;
                cardMovesH = Mathf.Ceil(chartNoteStyle.CalcHeight(cardMeasure, cardInner));
            }
            float h = CardPadY + CardTitleH + (cardFeelInline ? 0f : CardLine) + cardMovesH + cardStats.Count * CardLine + CardDivider;
            for (int i = 0; i < cardRowCount; i++) h += Mathf.Max(1, cardRowDiff[i].Count) * CardLine;
            cardH = h + CardDivider + cardActions.Count * CardActionH + CardPadY;
        }

        /// <summary>
        /// カードを描く:画面の下の中央、右下の武装の欄・左下の HP とメニューの案内より上(limitY がカードの下端の上限)。
        /// 文字列・幅・高さは BuildPickCard で作ってあるものを並べるだけ(毎フレーム作り直さない)。
        /// </summary>
        void DrawPickCard(float limitY)
        {
            float wd = cardInner + CardPadX * 2f + CardAccent, bx = (SW - wd) / 2f;
            float by = Mathf.Min(SH * 0.56f, limitY - cardH);
            CardFrame(new Rect(bx, by, wd, cardH), cardColor, 0.74f);
            float x = bx + CardAccent + CardPadX, y = by + CardPadY;
            var dim = new Color(1f, 1f, 1f, 0.78f);

            // 見出し:名前〈レア度〉・種類と撃ち方、右に今の重さ
            GUI.color = cardColor;
            GUI.Label(new Rect(x, y, cardNameW, CardTitleH), cardName, cardTitleStyle);
            GUI.color = dim;
            GUI.Label(new Rect(x + cardNameW + 8f, y + 3f, cardInner - cardNameW - 8f, CardTextH), cardKind, richSmall);
            GUI.Label(new Rect(x, y + 3f, cardInner, CardTextH), cardWeight, richRight);
            y += CardTitleH;
            if (!cardFeelInline)
            {
                GUI.Label(new Rect(x, y, cardInner, CardTextH), cardFeel, richSmall);
                y += CardLine;
            }
            if (cardMoves != null)
            {
                GUI.Label(new Rect(x, y, cardInner, cardMovesH), cardMoves, chartNoteStyle);
                y += cardMovesH;
            }
            // その武装自身の数値
            GUI.color = Color.white;
            for (int i = 0; i < cardStats.Count; i++, y += CardLine)
                GUI.Label(new Rect(x, y, cardInner, CardTextH), cardStats[i], richSmall);
            Chip(new Rect(x, y + 4f, cardInner, 1f), new Color(1f, 1f, 1f, 0.16f));
            y += CardDivider;

            // 付け先ごとの行
            float curX = x + cardSlotW + CardColGap, diffX = curX + cardCurW + CardColGap, diffW = x + cardInner - cardWeightW - CardColGap - diffX;
            for (int i = 0; i < cardRowCount; i++)
            {
                var diff = cardRowDiff[i];
                GUI.Label(new Rect(x, y, cardSlotW, CardTextH), cardRowSlot[i], richSmall);
                GUI.Label(new Rect(curX, y, cardCurW, CardTextH), cardRowCur[i], richSmall);
                for (int k = 0; k < diff.Count; k++)
                    GUI.Label(new Rect(diffX, y + k * CardLine, diffW, CardTextH), diff[k], richSmall);
                GUI.Label(new Rect(x + cardInner - cardWeightW, y, cardWeightW, CardTextH), cardRowWeight[i], richRight);
                y += Mathf.Max(1, diff.Count) * CardLine;
            }
            Chip(new Rect(x, y + 4f, cardInner, 1f), new Color(1f, 1f, 1f, 0.16f));
            y += CardDivider;

            // 操作
            for (int i = 0; i < cardActions.Count; i++, y += CardActionH)
                GUI.Label(new Rect(x, y, cardInner, CardActionH), cardActions[i], richSmall);
        }

        /// <summary>カードの地:暗い半透明の地・細い枠線・左端に色の線(武装のレア度の色)。拾う案内のカードと、付け先・分解を選ぶ表示で同じ。</summary>
        void CardFrame(Rect r, Color accent, float alpha)
        {
            Chip(r, new Color(0.03f, 0.04f, 0.06f, alpha));
            var line = new Color(1f, 1f, 1f, 0.3f);
            Chip(new Rect(r.x, r.y, r.width, 1f), line);
            Chip(new Rect(r.x, r.yMax - 1f, r.width, 1f), line);
            Chip(new Rect(r.xMax - 1f, r.y, 1f, r.height), line);
            accent.a = 1f;
            Chip(new Rect(r.x, r.y, CardAccent, r.height), accent);
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

        /// <summary>背中のグラフの上端(上の中央の敵の HP・怯みのゲージ(〜76)の下)。</summary>
        const float BackChartTop = 82f;

        /// <summary>
        /// 付け先を選ぶ表示のグラフ:拾う案内の枠(画面の下の中央)の上で、画面の左右の端に寄せて出す(真ん中の機体と敵を隠さない)。
        /// 背中だけは左右の別がないので、画面の上の中央(上の敵のゲージの下)に出す(左右の枠と同時には出ない)。
        /// 1つの枠に、見出し(どのボタンでどこに付けるか)・凡例・レーダーチャート(仲間が違えば出さない)・帯グラフ。
        /// </summary>
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
    }
}
