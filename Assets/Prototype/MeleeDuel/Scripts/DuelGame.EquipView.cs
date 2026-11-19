using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelGame の装備の確認(開閉・操作・画面)(partial。本体は DuelGame.cs)
    public partial class DuelGame
    {
        // ───────── 装備の確認(左のメニューボタン) ─────────

        bool equipOpen;
        /// <summary>装備の確認を開いている(時間が止まっている)。自機・敵・ステージの Update はこの間なにもしない。</summary>
        public static bool Paused => I != null && I.equipOpen;

        /// <summary>
        /// 左のメニューボタン(SHARE・View・− を離す / I)で装備の確認を開く・閉じる。○・B / Backspace でも閉じる(Esc はカメラのマウスの固定を外すので使わない)。
        /// ステージ(武装がある)だけ。開いている間は時間を止め、入力を止める(ProtoInput.Paused)。付け先を選ぶ表示の途中なら取り消す。
        /// 閉じたあとは、その時に押していたボタンを離すまで効かない(ProtoInput 側)。
        /// 開いている間は ↑↓(十字・左スティック・↑↓・W/S)で所と所持品を選ぶ(5つの所の下に所持品が続く)。
        ///   付けている武装:△ / C で外して所持品へ。所持品:その所のボタン(R1/L1・R2/L2・背中は ×)で付ける(今の武装は所持品へ)。
        ///   どちらも □・X / X キーで処分の選択になり、もう一度 □ で足もとの床に捨てる、← で分解して弾、→ で分解して回復(○/Backspace・選び直すでやめる)。
        ///   撃墜中・ボスを倒した後・真下に床がない所は捨てられない(分解はできる。回復は撃墜中はできない)。
        /// </summary>
        void UpdateEquipView()
        {
            bool toggle = ProtoInput.EquipView;   // 組み合わせの判定のため、毎フレーム読む
            bool up = ProtoInput.MenuUp, down = ProtoInput.MenuDown;   // スティックの倒し始めの判定のため、毎フレーム読む
            if (equipOpen)
            {
                if (toggle || player == null || player.Arms == null) SetEquipOpen(false);
                else if (ProtoInput.EquipViewBack)
                {
                    if (dropArmed) dropArmed = false;   // 捨てる確認の途中なら、確認だけ取り消す
                    else SetEquipOpen(false);
                }
                else EquipViewInput(player.Arms, up, down);
                return;
            }
            if (toggle && stage != null && player != null && player.Arms != null) SetEquipOpen(true);
        }

        void SetEquipOpen(bool open)
        {
            if (open == equipOpen) return;
            if (open)
            {
                player.Arms.CancelChoice();
                // 先行入力(攻撃・回避・射撃)を捨てる(閉じた直後に出ないように)
                player.ClearBufferedInput();
                player.Arms.ClearBufferedInput();
                // 捨てた武装を置く所の真下に床があるか(止まっている間は動かないので、開いたときに1回だけ測る)
                for (int i = 0; i < ArmRows; i++) dropFloor[i] = player.Arms.HasFloorBelow((DuelSlot)i);   // 所持品は背中と同じ所に置く
                // 武装が付いている最初の所を選ぶ
                viewSel = 0;
                for (int i = 0; i < ArmRows; i++)
                    if (player.Arms.Get((DuelSlot)i) != null) { viewSel = i; break; }
            }
            dropArmed = false;
            equipOpen = open;
            ProtoInput.Paused = open;
            viewBuilt = false;
        }

        /// <summary>選んでいる行(0〜4 = DuelSlot、5〜 = 所持品の番号 + 5)と、処分(捨てる・分解)を選んでいる途中か。</summary>
        int viewSel;
        bool dropArmed;
        /// <summary>所持品の行を選んでいる。</summary>
        bool StashSelected => viewSel >= ArmRows;
        int StashSel => viewSel - ArmRows;
        /// <summary>選んでいる行の武装(空き・範囲外なら null)。</summary>
        DuelWeapon SelectedWeapon(DuelArms arms) => StashSelected
            ? (StashSel < arms.Stash.Count ? arms.Stash[StashSel].w : null)
            : arms.Get((DuelSlot)viewSel);

        /// <summary>選んでいる所の武装を置く所の真下に床があるか(開いたときに測る)。</summary>
        readonly bool[] dropFloor = new bool[ArmRows];

        /// <summary>
        /// 今は捨てられない理由(0 = 捨てられる / 1 = 撃墜中 / 2 = ボスを倒した後で拾えない / 3 = 真下に床がない(穴の上・空中。落ちて消えるので) /
        /// 4 = 攻撃・回避・怯みなどの途中(動ける・ガード中でない。DuelArms.CanRearrange))。1 と 4 は付け外し・分解もできない。
        /// </summary>
        int DropBlock() => player == null || player.Dead ? 1 : player.Arms != null && !player.Arms.CanRearrange ? 4 : stage != null && stage.Cleared ? 2
            : !dropFloor[StashSelected ? (int)DuelSlot.Back : Mathf.Clamp(viewSel, 0, ArmRows - 1)] ? 3 : 0;

        /// <summary>付け外し・分解ができない(撃墜中か、動ける・ガード中でない)。</summary>
        static bool Frozen(int block) => block is 1 or 4;

        void EquipViewInput(DuelArms arms, bool up, bool down)
        {
            int rows = ArmRows + arms.Stash.Count;
            viewSel = Mathf.Clamp(viewSel, 0, rows - 1);   // 所持品が減った
            int d = (down ? 1 : 0) - (up ? 1 : 0);
            if (d != 0)
            {
                viewSel = (viewSel + d + rows) % rows;
                dropArmed = false;
            }
            var w = SelectedWeapon(arms);
            if (w == null)
            {
                dropArmed = false;
                return;
            }
            bool stashRow = StashSelected;
            int si = StashSel;
            var slot = (DuelSlot)Mathf.Clamp(viewSel, 0, ArmRows - 1);
            int block = DropBlock();
            if (block == 4 && (ProtoInput.MenuDiscard || ProtoInput.MenuStore || ProtoInput.MenuLeft || ProtoInput.MenuRight))
                Log("攻撃・回避・怯みなどの途中は、付け外し・処分できない(閉じて動けるようになってから)");
            if (dropArmed)
            {
                // 処分:もう一度 □ で捨てる / ← で弾 / → で回復
                if (ProtoInput.MenuDiscard)
                {
                    if (block != 0) return;
                    dropArmed = false;
                    if (stashRow) arms.DropStash(si);
                    else arms.Drop(slot);
                }
                else if (ProtoInput.MenuLeft || ProtoInput.MenuRight)
                {
                    // 分解は十字キー・矢印キー(A/D)だけ(左スティックでは受けない。ProtoInput.MenuLeft)
                    bool heal = !ProtoInput.MenuLeft;
                    if (Frozen(block)) return;
                    // 弾が上限・HP が満タンなら分解しない(DuelArms が知らせる。処分の選択はそのまま)
                    bool done = stashRow ? arms.SalvageStash(si, heal) : arms.SalvageSlot(slot, heal);
                    if (done) dropArmed = false;
                }
                return;
            }
            if (ProtoInput.MenuDiscard)
            {
                dropArmed = true;   // 1回目:処分を選ぶ
                return;
            }
            if (!stashRow)
            {
                if (ProtoInput.MenuStore) arms.StoreSlot(slot);
                return;
            }
            // 所持品:付ける所のボタンで付ける(R1/L1・R2/L2・背中は ×)。付けたら、付けた所の行を選ぶ
            // (外した武装は同じ番号の所持品に入れ替わるが、選択を所持品に残すと、もう一度押したときに付け直してしまうので)
            foreach (var s in DuelWeapon.SlotsFor(w.kind))
            {
                if (!ProtoInput.MenuSlotPressed(DuelArms.ChoiceBtn(s))) continue;
                if (arms.EquipFromStash(si, s)) viewSel = (int)(w.IsTwoHand ? DuelSlot.RightHand : s);
                return;
            }
        }

        // ───────── 装備の確認の画面 ─────────

        // 文字列は開いたとき・中身(武装・機器)が変わったときだけ作る
        readonly string[] viewHead = new string[ArmRows], viewName = new string[ArmRows], viewStats = new string[ArmRows], viewUse = new string[ArmRows];
        readonly DuelWeapon[] viewKeyW = new DuelWeapon[ArmRows];
        ProtoInput.Device viewKeyDev;
        int viewKeyBlock;
        bool viewBuilt;
        string viewFoot, viewDropHint;

        /// <summary>
        /// 画面の中央:5つの所それぞれの武装(名前・レア度の色・種類・一言)、主な性能(拾うときの比較と同じ値)、使うボタン。空きは「なし」。
        /// 選んでいる所は明るい帯と左の縦線で目立たせ、右上に操作(△ 外して持つ・□ 処分)を出す(処分の途中は赤い帯と、捨てる・弾・回復の選び方)。
        /// 右に所持品の欄(重さ・弾・所持品の一覧)。下に選んでいる武装の性能のグラフ。
        /// </summary>
        void DrawEquipView(DuelArms arms)
        {
            var dev = ProtoInput.Current;
            int block = DropBlock();
            bool changed = !viewBuilt || viewKeyDev != dev || viewKeyBlock != block;
            for (int i = 0; i < ArmRows && !changed; i++) changed = viewKeyW[i] != arms.Get((DuelSlot)i);
            if (changed) BuildEquipView(arms, dev, block);

            const float wd = 900f, sideW = 330f, gapX = 10f, rowH = 76f, headW = 130f, pad = 18f, titleH = 46f, footH = 34f;
            float h = pad + titleH + rowH * ArmRows + footH + 6f;
            // 右の所持品の欄と、下の性能のグラフの枠(EquipChartH)を足した全体を、画面の中央に
            float bx = Mathf.Max(4f, (SW - wd - gapX - sideW) / 2f), by = Mathf.Max(4f, (SH - h - EquipChartGap - EquipChartH) / 2f);
            // 画面全体を少し暗くし、その上にパネル
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, by, wd, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "装備", viewTitleStyle);

            float y = by + pad + titleH;
            float tx = bx + pad + headW, tw = wd - pad * 2f - headW;
            // 両手持ちは右手・左手の2行で1つ:どちらを選んでも2行とも選んでいる帯にし、処分の選択も2行とも
            bool two = arms.TwoHanded;
            bool onSlots = !StashSelected;
            bool selTwo = onSlots && two && viewSel <= (int)DuelSlot.LeftHand;
            for (int i = 0; i < ArmRows; i++)
            {
                var w = arms.Get((DuelSlot)i);
                bool sel = onSlots && (i == viewSel || (selTwo && i <= (int)DuelSlot.LeftHand));
                bool armed = sel && dropArmed && w != null;
                if (sel)
                {
                    SelectionBand(new Rect(bx + pad - 8f, y + 1f, wd - pad * 2f + 16f, rowH - 1f), armed);
                    if (w != null && !armed && i == viewSel) GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewDropHint, richRight);
                }
                // 区切りの細い線(両手持ちの右手と左手の間は引かない)
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                if (!(two && i == (int)DuelSlot.LeftHand)) GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(bx + pad, y + 6f, headW, 26f), viewHead[i], viewHeadStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(tx, y + 6f, tw, 24f), viewName[i], richSmall);
                if (w != null) GUI.Label(new Rect(tx, y + 29f, tw, 22f), viewStats[i], richSmall);
                // 3行目:使うボタン(処分の途中は、その選び方に替える)
                GUI.color = armed ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(tx, y + (w != null ? 51f : 29f), tw, 22f), armed ? DisposeLine(arms, w, block, selTwo) : viewUse[i], richSmall);
                GUI.color = Color.white;
                y += rowH;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(bx, y + 6f, wd, 24f), viewFoot, center);
            GUI.color = Color.white;

            DrawStashPanel(arms, bx + wd + gapX, by, sideW, h, block);
            DrawEquipChart(arms, bx, by + h + EquipChartGap, wd + gapX + sideW);
        }

        /// <summary>選んでいる行の帯と左の縦線(処分を選んでいる途中は赤)。装備の行・所持品の行で同じ。</summary>
        static void SelectionBand(Rect r, bool armed)
        {
            GUI.color = armed ? new Color(0.9f, 0.25f, 0.2f, 0.28f) : new Color(1f, 1f, 1f, 0.09f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = armed ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // 処分の1行(捨てる・弾にする・回復にする・やめる)。武装・捨てられない理由・機器が変わったときだけ作る
        (DuelWeapon w, int block, bool two, ProtoInput.Device dev, bool ammoFull, bool hpFull) disposeKey;
        string disposeText;

        /// <summary>処分の選び方:「処分  [□] 床に捨てる  [←] 弾にする +24  [→] 回復にする +13  [○] やめる」(捨てられない・回復できないときは灰色で理由)。</summary>
        string DisposeLine(DuelArms arms, DuelWeapon w, int block, bool two)
        {
            bool ammoFull = arms.SalvageUseless(false), hpFull = arms.SalvageUseless(true);
            var key = (w, block, two, ProtoInput.Current, ammoFull, hpFull);
            if (disposeText != null && disposeKey == key) return disposeText;
            disposeKey = key;
            bool kb = ProtoInput.Current == ProtoInput.Device.Keyboard;
            string drop = ProtoInput.L(ProtoInput.Btn.Discard), back = kb ? "Backspace" : ProtoInput.L(ProtoInput.Btn.Back);
            string left = ProtoInput.L(ProtoInput.Btn.MenuLeft), right = ProtoInput.L(ProtoInput.Btn.MenuRight);
            if (Frozen(block))
            {
                string frozen = block == 1 ? "撃墜中は処分できない" : "攻撃・回避・怯みなどの途中は処分できない";
                disposeText = $"<color=#FF8A70><b>処分</b></color>  <color=#888888>{frozen}</color>   [{back}] やめる";
                return disposeText;
            }
            string why = block switch { 2 => "ボスの後は捨てられない", 3 => "真下に床がない", _ => "" };
            string dropPart = block == 0 ? $"[{drop}] {(two ? "両手の武装を" : "")}床に捨てる" : $"<color=#888888>[{drop}] {why}</color>";
            // 弾が上限・HP が満タンなら、分解しても何も得ないので赤く「上限」「満タン」(押しても分解しない)
            string ammoPart = ammoFull ? $"<color=#FF7A6A>[{left}] 弾は上限</color>" : $"[{left}] 弾にする +{arms.SalvageAmmo(w)}";
            string healPart = hpFull ? $"<color=#FF7A6A>[{right}] HP は満タン</color>" : $"[{right}] 回復にする +{arms.SalvageHeal(w)}";
            disposeText = $"<color=#FF8A70><b>処分</b></color>  {dropPart}   {ammoPart}   {healPart}   [{back}] やめる";
            return disposeText;
        }

        // 所持品の欄の文字列(所持品の中身・機器が変わったときだけ作る)
        readonly List<DuelArms.Carried> stashRowKey = new();
        readonly List<string> stashRowName = new(), stashRowWeight = new();
        ProtoInput.Device stashRowDev;
        (DuelWeapon w, bool armed, int block, ProtoInput.Device dev) stashHintKey;
        string stashHint, stashHead, stashTitle, stashEmpty, stashRange;
        int stashTitleN = -1;
        ProtoInput.Device stashEmptyDev;
        (int, int, int) stashRangeKey;
        (int n, int total, int max, int mob, int ammo, int ammoMax) stashHeadKey;

        /// <summary>
        /// 右の所持品の欄:重さ(上限)と推力・共通の弾、その下に所持品の一覧(レア度の色・名前・種類・重さ)。選んでいる所持品は帯と縦線。
        /// 一番下に、選んでいる所持品の操作(付ける所のボタン・処分)か、処分の選び方。
        /// </summary>
        void DrawStashPanel(DuelArms arms, float x, float y, float w, float h, int block)
        {
            const float pad = 14f, rowH = 28f, hintH = 92f, listTop = 78f;
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var stash = arms.Stash;
            var r = arms.Rules;
            var hk = (stash.Count, Mathf.RoundToInt(arms.TotalWeight), Mathf.RoundToInt(arms.CarryMax), Mathf.RoundToInt(arms.Mobility * 100f),
                      Mathf.FloorToInt(arms.Ammo + 0.0001f), Mathf.RoundToInt(r.ammoMax));
            if (stashHead == null || stashHeadKey != hk)
            {
                stashHeadKey = hk;
                stashHead = $"重さ {hk.Item2}/{hk.Item3}   推力 ×{hk.Item4 / 100f:0.00}\n弾 {hk.Item5}/{hk.Item6}";
            }
            if (stashTitle == null || stashTitleN != stash.Count)
            {
                stashTitleN = stash.Count;
                stashTitle = $"所持品  {stash.Count}";
            }
            GUI.Label(new Rect(x + pad, y + 10f, w - pad * 2f, 26f), stashTitle, viewHeadStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(x + pad, y + 36f, w - pad * 2f, 40f), stashHead, small);
            GUI.color = Color.white;

            // 一覧の文字列
            bool rebuild = stashRowKey.Count != stash.Count || stashRowDev != ProtoInput.Current;
            for (int i = 0; i < stash.Count && !rebuild; i++) rebuild = stashRowKey[i] != stash[i];
            if (rebuild)
            {
                stashRowKey.Clear();
                stashRowName.Clear();
                stashRowWeight.Clear();
                stashRowDev = ProtoInput.Current;
                foreach (var c in stash)
                {
                    stashRowKey.Add(c);
                    string kind = DuelWeapon.KindName(c.w.kind);
                    string ammo = c.w.IsGun && c.w.magazine > 0 && c.memo.mag >= 0 && c.memo.mag < c.w.magazine ? $" 弾{c.memo.mag}/{c.w.magazine}" : "";
                    stashRowName.Add($"<color=#{ColorUtility.ToHtmlStringRGB(c.w.Color)}>{c.w.name}</color> <color=#9A9A9A>{kind}{ammo}</color>");
                    stashRowWeight.Add($"重 {c.w.weight:0.#}");
                }
            }

            float listY = y + listTop, listH = h - listTop - hintH;
            int visible = Mathf.Max(1, Mathf.FloorToInt(listH / rowH));
            int sel = StashSelected ? StashSel : -1;
            if (stash.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                if (stashEmpty == null || stashEmptyDev != ProtoInput.Current)
                {
                    stashEmptyDev = ProtoInput.Current;
                    stashEmpty = $"なし ― 落ちている武装の近くで {ProtoInput.L(ProtoInput.Btn.Stash)} で持つ。装備の行で {ProtoInput.L(ProtoInput.Btn.PickUp)} で外して持つ";
                }
                GUI.Label(new Rect(x + pad, listY + 4f, w - pad * 2f, 60f), stashEmpty, chartNoteStyle);
                GUI.color = Color.white;
            }
            else
            {
                // 選んでいる行が見えるように送る
                int first = Mathf.Clamp((sel >= 0 ? sel : 0) - visible / 2, 0, Mathf.Max(0, stash.Count - visible));
                for (int k = 0; k < visible && first + k < stash.Count; k++)
                {
                    int i = first + k;
                    float ry = listY + k * rowH;
                    bool isSel = i == sel;
                    if (isSel) SelectionBand(new Rect(x + 6f, ry, w - 12f, rowH - 2f), dropArmed);
                    Chip(new Rect(x + pad + 2f, ry + 9f, 10f, 10f), stash[i].w.Color);
                    GUI.Label(new Rect(x + pad + 18f, ry + 3f, w - pad * 2f - 70f, 22f), stashRowName[i], richSmall);
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                    GUI.Label(new Rect(x + w - pad - 60f, ry + 3f, 60f, 22f), stashRowWeight[i], rightSmall);
                    GUI.color = Color.white;
                }
                if (stash.Count > visible)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    var rk = (first, Mathf.Min(stash.Count, first + visible), stash.Count);
                    if (stashRange == null || stashRangeKey != rk)
                    {
                        stashRangeKey = rk;
                        stashRange = $"{rk.Item1 + 1}〜{rk.Item2} / {rk.Item3}";
                    }
                    GUI.Label(new Rect(x + pad, listY + visible * rowH - 4f, w - pad * 2f, 18f), stashRange, cellTinyRight);
                    GUI.color = Color.white;
                }
            }

            // 一番下:選んでいる所持品の操作
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(x + pad, y + h - hintH, w - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var sw = sel >= 0 && sel < stash.Count ? stash[sel].w : null;
            var key = (sw, dropArmed, block, ProtoInput.Current);
            if (stashHint == null || stashHintKey != key)
            {
                stashHintKey = key;
                if (sw == null) stashHint = $"<color=#999999>{(ProtoInput.Current == ProtoInput.Device.Keyboard ? "↓/S" : "↓")} で所持品を選ぶ</color>";
                else if (dropArmed) stashHint = DisposeLine(arms, sw, block, false);
                else
                {
                    var sb = new System.Text.StringBuilder();
                    if (block == 1) sb.Append("<color=#888888>撃墜中は付けられない</color>");
                    else if (block == 4) sb.Append("<color=#888888>攻撃・回避・怯みなどの途中は付けられない</color>");
                    else if (sw.IsTwoHand)
                        sb.Append('[').Append(SlotButton(DuelSlot.RightHand)).Append("]/[").Append(SlotButton(DuelSlot.LeftHand)).Append("] 両手に付ける");
                    else
                    {
                        var to = DuelWeapon.SlotsFor(sw.kind);
                        for (int k = 0; k < to.Length; k++)
                        {
                            if (k > 0) sb.Append(" / ");
                            string b = to[k] == DuelSlot.Back ? ProtoInput.L(ProtoInput.Btn.Fly) : SlotButton(to[k]);
                            sb.Append('[').Append(b).Append("] ").Append(DuelWeapon.SlotName(to[k]));
                        }
                        sb.Append(" に付ける");
                    }
                    if (!Frozen(block)) sb.Append("<color=#AAAAAA>(今の武装は所持品へ)</color>");
                    sb.Append("\n[").Append(ProtoInput.L(ProtoInput.Btn.Discard)).Append("] 処分(捨てる・分解して弾か回復)");
                    stashHint = sb.ToString();
                }
            }
            GUI.Label(new Rect(x + pad, y + h - hintH + 6f, w - pad * 2f, hintH - 10f), stashHint, chartNoteStyle);
        }

        const float EquipChartH = 196f, EquipChartGap = 8f;
        // 装備の確認のグラフ:選んでいる行・その武装・比べる相手が変わったときだけ作り直す
        (int sel, DuelWeapon w, DuelWeapon other) viewChartKey = (-1, null, null);
        DuelRadar.Pair viewChartPair;
        string viewChartNote;

        /// <summary>比べる相手の所(右手⇔左手、右肩⇔左肩。背中はなし)。</summary>
        static int PartnerSlot(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => (int)DuelSlot.LeftHand,
            DuelSlot.LeftHand => (int)DuelSlot.RightHand,
            DuelSlot.RightShoulder => (int)DuelSlot.LeftShoulder,
            DuelSlot.LeftShoulder => (int)DuelSlot.RightShoulder,
            _ => -1,
        };

        /// <summary>
        /// 装備の確認の下の枠:選んでいる武装のレーダーチャートと帯グラフ。
        /// 付けている所なら、比べる相手は同じ仲間のもう片方の所(右手⇔左手、右肩⇔左肩)。相手が空き・背中・両手持ちなら単独で出す。
        /// 所持品なら、付けられる所の今の武装(同じ仲間がいればそれ、なければ1つ目の所)と比べる。
        /// 相手の仲間が違えば(剣と盾など)レーダーは出さず、帯グラフも選んでいる武装だけ。右の欄に凡例と読み方。選んでいる所が空きなら、その旨だけ。
        /// </summary>
        void DrawEquipChart(DuelArms arms, float bx, float y, float wd)
        {
            var w = SelectedWeapon(arms);
            DuelWeapon other = null;
            string me = "", them = "", why = "";
            if (w != null && StashSelected)
            {
                var to = DuelWeapon.SlotsFor(w.kind);
                var os = to[0];
                foreach (var s in to)
                    if (DuelWeapon.SameFamily(arms.Get(s), w)) { os = s; break; }
                other = arms.Get(os);
                me = "所持品";
                them = other is { IsTwoHand: true } && os is DuelSlot.RightHand or DuelSlot.LeftHand ? "両手" : DuelWeapon.SlotName(os);
                why = other == null ? them + "は空き" : "";
            }
            else if (w != null)
            {
                var slot = (DuelSlot)viewSel;
                int ps = PartnerSlot(slot);
                other = w.IsTwoHand || ps < 0 ? null : arms.Get((DuelSlot)ps);
                me = w.IsTwoHand ? "両手" : DuelWeapon.SlotName(slot);
                them = ps >= 0 ? DuelWeapon.SlotName((DuelSlot)ps) : "";
                why = w.IsTwoHand ? "両手持ちなので、比べる相手はない"
                    : ps < 0 ? "背中は1つだけなので、比べる相手はない"
                    : other == null ? them + "は空き" : "";
            }
            var key = (viewSel, w, other);
            if (viewChartKey != key)
            {
                viewChartKey = key;
                viewChartPair = DuelRadar.Get(w, other);
                if (w != null)
                {
                    if (other != null && viewChartPair.otherDifferent)
                        why = them + "の" + other.name + "は" + DuelWeapon.KindName(other.kind) + "で、仲間が違うので比べない";
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<color=").Append(DuelRadar.MainHex).Append(">■ ").Append(me).Append(":").Append(w.name).Append("</color>\n");
                    if (viewChartPair.Same)
                        sb.Append("<color=").Append(DuelRadar.OtherHex).Append(">━ ").Append(them).Append(":同じ").Append(other.name).Append("</color>\n\n")
                          .Append("<color=#BBBBBB>").Append(them).Append("と同じ武装なので、性能は同じ</color>\n");
                    else if (viewChartPair.Compared)
                        sb.Append("<color=").Append(DuelRadar.OtherHex).Append(">━ ").Append(them).Append(":").Append(other.name).Append("</color>\n\n")
                          .Append("<color=#BBBBBB>数値は ").Append(them).Append("→").Append(me).Append("。")
                          .Append("<color=#7CFC8A>緑</color>は").Append(me).Append("のほうが良い、<color=#FF7A6A>赤</color>は劣る</color>\n");
                    else sb.Append("<color=#999999>").Append(why).Append("</color>\n\n");
                    sb.Append("<color=#999999>外側(長い)ほど良い。目盛りは同じ仲間の武装の中での位置</color>");
                    viewChartNote = sb.ToString();
                }
            }

            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(bx, y, wd, EquipChartH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (viewChartPair == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(bx, y, wd, EquipChartH), "空き ― 武装を付けると、ここに性能のグラフを出す", center);
                GUI.color = Color.white;
                return;
            }
            const float pad = 18f, noteW = 250f;
            float rs = EquipChartH - 14f;
            float rx = bx + pad, ry = y + 7f;
            float noteX = bx + wd - pad - noteW;
            if (viewChartPair.ShowRadar)
            {
                DuelRadar.DrawRadar(new Rect(rx, ry, rs, rs), uiScale, viewChartPair);
                float barX = rx + rs + 14f;
                DuelRadar.DrawBars(new Rect(barX, ry + 4f, noteX - 18f - barX, rs - 8f), uiScale, viewChartPair, 84f, 130f);
            }
            else DuelRadar.DrawBars(new Rect(rx, ry + 4f, noteX - 18f - rx, rs - 8f), uiScale, viewChartPair, 84f, 130f);
            GUI.Label(new Rect(noteX, ry + 6f, noteW, rs - 12f), viewChartNote, chartNoteStyle);
        }

        void BuildEquipView(DuelArms arms, ProtoInput.Device dev, int block)
        {
            viewBuilt = true;
            viewKeyDev = dev;
            viewKeyBlock = block;
            bool kb = dev == ProtoInput.Device.Keyboard;
            string aim = ProtoInput.L(ProtoInput.Btn.Aim);
            for (int i = 0; i < ArmRows; i++)
            {
                var slot = (DuelSlot)i;
                var w = arms.Get(slot);
                viewKeyW[i] = w;
                string btn = SlotButton(slot);
                viewHead[i] = DuelWeapon.SlotName(slot) + (btn.Length > 0 ? "  " + btn : "");
                if (w == null)
                {
                    viewName[i] = "<color=#999999>なし</color>";
                    viewStats[i] = "";
                    viewUse[i] = slot switch
                    {
                        DuelSlot.RightHand or DuelSlot.LeftHand => "剣・盾・手の銃を付けられる",
                        DuelSlot.Back => "スラスターを付けられる",
                        _ => "肩の銃・砲・ミサイルを付けられる",
                    };
                    continue;
                }
                string col = ColorUtility.ToHtmlStringRGB(w.Color);
                string name = w == DuelWeapon.BaseSword ? w.name : w.name + "〈" + w.RarityName + "〉";
                if (w.IsTwoHand && slot == DuelSlot.LeftHand)
                {
                    // 両手持ちの左手:右手の行の武装の続き(1つの武装が2つの所にまたがる)。L1 の技を出す
                    string r1 = SlotButton(DuelSlot.RightHand);
                    viewName[i] = "<color=#" + col + ">┗ 両手持ち(" + r1 + " の " + name + " と合わせて1つ)</color>";
                    viewStats[i] = AltStatLine(w);
                    viewUse[i] = w.IsBlade ? $"{btn} {w.altMove}(回避の後も)  {aim}+{btn} 強攻撃  ガードはできない"
                        : $"{btn} {w.altMove}" + (w.alt is { charge: > 0f } ? "(押して溜め、離して撃つ)" : "")
                          + (w.IsGun && w.magazine > 0 ? $"  {aim}+{btn} 主の銃のリロード" : "") + "  ガードはできない";
                    continue;
                }
                viewName[i] = "<size=19><b><color=#" + col + ">" + (w.IsTwoHand ? "┏ " : "") + name + "</color></b></size>   <color=#C8C8C8>" + DuelWeapon.KindName(w.kind)
                              + (w.feel.Length > 0 ? " ― " + w.feel : "") + "</color>";
                viewStats[i] = StatLine(w);
                viewUse[i] = w.kind switch
                {
                    DuelKind.Sword => $"{btn} 攻撃(連撃{w.LightMoves.Length}段)  {aim}+{btn} 強攻撃  回避の後に {btn} 回避斬り",
                    DuelKind.TwoHandMelee => $"{btn} 連撃{w.LightMoves.Length}段  {aim}+{btn} 強攻撃  回避の後に {btn} 回避斬り",
                    DuelKind.TwoHandGun => $"{btn} {w.MainMoveName}({w.FireMode})",
                    DuelKind.Shield => $"{btn} 押している間ガード",
                    DuelKind.Back => "ボタンなし(付けているだけで効く。倍率は素の性能に掛かる)",
                    _ => $"{btn} {w.FireMode}",
                };
                if (w.IsGun && w.magazine > 0) viewUse[i] += $"  {aim}+{btn} リロード";
            }
            string close = kb ? "I / Backspace" : ProtoInput.L(ProtoInput.Btn.EquipView) + " / " + ProtoInput.L(ProtoInput.Btn.Back);
            string drop = ProtoInput.L(ProtoInput.Btn.Discard), back = kb ? "Backspace" : ProtoInput.L(ProtoInput.Btn.Back);
            viewDropHint = $"<color=#AAAAAA>{ProtoInput.L(ProtoInput.Btn.PickUp)} 外して持つ   {drop} 処分(捨てる・分解)</color>";
            string sel = kb ? "↑↓/W・S" : "↑↓";
            string note = block switch
            {
                1 => "(撃墜中は捨てられない)",
                2 => "(ボスの後は捨てられない)",
                3 => "(真下に床がないので、ここでは捨てられない)",
                4 => "(攻撃・回避・怯みなどの途中は付け外し・処分できない)",
                _ => "",
            };
            viewFoot = $"一時停止中 ― {sel} 選ぶ(下に所持品)  {drop} 処分:もう一度 {drop} 捨てる・← 弾・→ 回復{note}  {close} 閉じる";
        }

        /// <summary>両手持ちの L1 の技の1行(近接は一振りの威力・届く範囲・幅・EN、射撃は撃ち方)。</summary>
        static string AltStatLine(DuelWeapon w)
        {
            if (!w.IsBlade) return "L1 " + w.altMove + ":" + w.AltShotText();
            var alt = w.AltMoves;
            if (alt == null) return "";
            // 「L1 突き → 斬り上げ:威力 27(±30°・15.5m) → 31(±80°・14.7m)  EN 14」
            var sb = new System.Text.StringBuilder("L1 ").Append(w.altMove).Append(":威力 ");
            for (int k = 0; k < alt.Length; k++)
            {
                var m = alt[k];
                if (k > 0) sb.Append(" → ");
                sb.Append($"{m.damage * w.damageMul:0}(±{Mathf.Min(180f, m.arc + w.arcAdd):0}°・{m.reach * w.reachMul:0.#}m)");
            }
            sb.Append($"  EN {alt[0].cost:0}");
            return sb.ToString();
        }

        /// <summary>主な性能の1行。拾うときの比較(DuelWeapon.Stats)と同じ値に、銃は連射・リロードを足す(両手持ちの L1 は左手の行に出すので除く)。</summary>
        static string StatLine(DuelWeapon w)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in DuelWeapon.Stats(w))
            {
                if (w.IsTwoHand && s.name is "L1" or "L1技") continue;
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(s.name).Append(' ').Append(s.text);
            }
            if (w.IsGun)
            {
                sb.Append("   連射 ").Append((1f / Mathf.Max(0.01f, w.interval)).ToString("0.#")).Append("発/秒");
                if (w.magazine > 0) sb.Append("   リロード ").Append(w.reload.ToString("0.0")).Append("秒");
            }
            return sb.ToString();
        }
    }
}
