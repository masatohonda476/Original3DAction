using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelGame の武装の表示:右下の武装の欄・拾う案内・付け先を選ぶ表示の性能のグラフ(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
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

        // 拾う案内の文字列は、中身が変わったときだけ作り直す(OnGUI は1フレームに何度も呼ばれる)。
        // 弾・HP は分解を選んでいる間だけ値で、ほかは上限・満タンかどうかだけ入れる(撃つ・被弾するたびに作り直さないように)
        (DuelLoot loot, DuelLoot choosing, DuelLoot breaking, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev, int ammo, int hp, int weight) pickupKey;
        // 付け先を選ぶ表示のグラフは、武装・比べる相手・機器が変わったときだけ作り直す
        (DuelLoot loot, DuelLoot choosing, DuelWeapon a, DuelWeapon b, ProtoInput.Device dev) pickChartKey;
        readonly string[] pickupLines = new string[4];
        int pickupLineCount;

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
