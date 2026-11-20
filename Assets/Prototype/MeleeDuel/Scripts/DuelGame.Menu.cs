using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelGame のメニュー:OPTIONS / I で開く項目のメニュー(開閉・ページの行き来)・操作方法のページ・左下の短い案内
    // (partial。本体は DuelGame.cs。「装備・所持品」のページの中身は DuelGame.EquipView.cs)
    public partial class DuelGame
    {
        // ───────── メニュー(右のメニューボタン) ─────────

        /// <summary>メニューの段階:閉じている / 項目のメニュー / 「装備・所持品」のページ(装備の確認)/ 「操作方法」のページ。</summary>
        enum MenuPage { Closed, Top, Equip, Help }

        MenuPage menuPage;
        /// <summary>メニューのどれかの段階を開いている。この間は照準・拾う案内・ロックオンの枠を出さない。</summary>
        bool MenuOpen => menuPage != MenuPage.Closed;
        /// <summary>メニュー(項目のメニュー・装備の確認・操作方法)を開いている(時間が止まっている)。自機・敵・ステージの Update はこの間なにもしない。</summary>
        public static bool Paused => I != null && I.MenuOpen;

        /// <summary>項目のメニューの並び(添字は menuSel)。装備へは OPTIONS → × の2押しで行けるよう、装備を一番上にする。</summary>
        static readonly string[] MenuItems = { "装備・所持品", "操作方法", "閉じる" };
        int menuSel;

        /// <summary>
        /// 右のメニューボタン(OPTIONS・≡・+ / I)で項目のメニューを開く。操作方法を画面に出し続けるのをやめ、ここから見る形にした
        /// (作者「操作方法は常に出すのではなく、オプションにメニューを出してください」)。
        /// 項目のメニュー:↑↓(十字・左スティック・↑↓・W/S)で選び、×・A / Space・Enter で決定、○・B / Backspace で閉じる。
        /// 「装備・所持品」「操作方法」のページ:○・B / Backspace で1つ前(項目のメニュー)へ戻る(装備の処分の確認の途中なら、確認だけ取り消す)。
        /// 右のメニューボタン / I は、どの段階からでも全部閉じてゲームに戻る(Esc はカメラのマウスの固定を外すので使わない)。
        /// 開いている間は、どの段階でも時間を止め、戦闘の入力を止める(Paused・ProtoInput.Paused)。
        /// 閉じたあとは、その時に押していたボタン(決定の × = 飛行、戻るの ○ = ブーストなど)を離すまで効かない(ProtoInput 側)。
        /// 1フレームに進む段階は1つだけ(決定・戻るを押したフレームに、移った先のページがその同じ押下を読まないように)。
        /// </summary>
        void UpdateMenu()
        {
            bool toggle = ProtoInput.EquipView;
            bool up = ProtoInput.MenuUp, down = ProtoInput.MenuDown;   // スティックの倒し始めの判定のため、毎フレーム読む
            if (player == null)
            {
                SetMenuPage(MenuPage.Closed);
                return;
            }
            if (toggle)
            {
                SetMenuPage(MenuOpen ? MenuPage.Closed : MenuPage.Top);
                return;
            }
            switch (menuPage)
            {
                case MenuPage.Top:
                {
                    if (ProtoInput.MenuBack)
                    {
                        SetMenuPage(MenuPage.Closed);
                        break;
                    }
                    int d = (down ? 1 : 0) - (up ? 1 : 0);
                    if (d != 0) menuSel = (menuSel + d + MenuItems.Length) % MenuItems.Length;
                    else if (ProtoInput.MenuConfirm)
                        SetMenuPage(menuSel switch { 0 => MenuPage.Equip, 1 => MenuPage.Help, _ => MenuPage.Closed });
                    break;
                }
                case MenuPage.Equip:
                    if (ProtoInput.EquipViewBack)
                    {
                        if (dropArmed) dropArmed = false;   // 捨てる確認の途中なら、確認だけ取り消す
                        else SetMenuPage(MenuPage.Top);
                    }
                    else EquipViewInput(player.Arms, up, down);
                    break;
                case MenuPage.Help:
                    if (ProtoInput.MenuBack) SetMenuPage(MenuPage.Top);
                    break;
            }
        }

        /// <summary>
        /// メニューの段階を変える。ゲームから開くときは先行入力を捨て、項目のメニューの一番上を選ぶ(ページから戻ったときは、入ったときの項目を選んだまま)。
        /// 止める・戻す(ProtoInput.Paused)は「どれかを開いているか」だけで決めるので、ページの行き来では入力の止め方は変わらない。
        /// </summary>
        void SetMenuPage(MenuPage page)
        {
            if (page == menuPage) return;
            if (!MenuOpen)
            {
                player.Arms.CancelChoice();
                // 先行入力(攻撃・回避・射撃)を捨てる(閉じた直後に出ないように)
                player.ClearBufferedInput();
                player.Arms.ClearBufferedInput();
                menuSel = 0;
            }
            if (page == MenuPage.Equip) PrepareEquipPage();
            dropArmed = false;
            menuPage = page;
            ProtoInput.Paused = MenuOpen;
        }

        // ───────── メニューの画面 ─────────

        // 案内の文字列は、機器が変わったときだけ作る(OnGUI は1フレームに何度も呼ばれる)
        ProtoInput.Device menuTextDev;
        bool menuTextBuilt;
        string menuHint, menuFoot, helpFoot;
        float menuFootW;

        /// <summary>案内の文字列(左下の1行・項目のメニューの下・操作方法のページの下)。OnGUI の中から呼ぶ(幅を測るのにスタイルが要る)。</summary>
        void BuildMenuText()
        {
            var dev = ProtoInput.Current;
            if (menuTextBuilt && menuTextDev == dev) return;
            menuTextBuilt = true;
            menuTextDev = dev;
            bool kb = dev == ProtoInput.Device.Keyboard;
            string menu = ProtoInput.L(ProtoInput.Btn.EquipView), back = ProtoInput.L(ProtoInput.Btn.Back);
            string confirm = ProtoInput.L(ProtoInput.Btn.Confirm) + (kb ? "/Enter" : "");
            menuHint = $"{menu} メニュー(装備・操作方法)";
            menuFoot = $"一時停止中 ― {(kb ? "↑↓/W・S" : "↑↓")} 選ぶ  {confirm} 決定  {back} / {menu} 閉じる";
            helpFoot = $"一時停止中 ― {back} 戻る  {menu} 閉じる";
            menuFootW = Mathf.Ceil(center.CalcSize(new GUIContent(menuFoot)).x);
        }

        /// <summary>左下に出す短い案内(「OPTIONS メニュー(装備・操作方法)」。ボタン名は使っている機器に合わせる)。</summary>
        string MenuHint()
        {
            BuildMenuText();
            return menuHint;
        }

        /// <summary>開いている段階の画面を、ほかの表示の上に描く。</summary>
        void DrawMenu(DuelArms arms)
        {
            switch (menuPage)
            {
                case MenuPage.Top: DrawMenuTop(); break;
                case MenuPage.Equip: DrawEquipView(arms); break;
                case MenuPage.Help: DrawHelpPage(arms); break;
            }
        }

        /// <summary>画面全体を少し暗くする(装備の確認と同じ濃さ)。</summary>
        void MenuBackdrop()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>メニューのパネルの地(装備の確認と同じ色)。</summary>
        static void MenuPanel(Rect r)
        {
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 項目のメニュー:画面の中央の小さなパネルに、見出しと項目の行。選んでいる行は装備の確認と同じ明るい帯と左の縦線。
        /// 操作の案内は文字が長い(キーボードの表記)ので、パネルの下に、文字の幅に合わせた別の帯で出す。動かす演出は入れない。
        /// </summary>
        void DrawMenuTop()
        {
            BuildMenuText();
            const float wd = 380f, pad = 18f, titleH = 46f, rowH = 46f, footH = 34f, gap = 8f;
            int n = MenuItems.Length;
            float h = pad + titleH + rowH * n + pad;
            float bx = (SW - wd) / 2f, by = Mathf.Max(4f, (SH - h - gap - footH) / 2f);
            MenuBackdrop();
            MenuPanel(new Rect(bx, by, wd, h));
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "メニュー", viewTitleStyle);
            float y = by + pad + titleH;
            for (int i = 0; i < n; i++)
            {
                bool sel = i == menuSel;
                if (sel) SelectionBand(new Rect(bx + pad - 8f, y + 1f, wd - pad * 2f + 16f, rowH - 1f), false);
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
                GUI.color = sel ? Color.white : new Color(1f, 1f, 1f, 0.7f);
                GUI.Label(new Rect(bx + pad + 10f, y + 10f, wd - pad * 2f - 10f, 28f), MenuItems[i], viewHeadStyle);
                y += rowH;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float footW = Mathf.Min(SW - 8f, Mathf.Max(wd, menuFootW + pad * 2f)), fx = (SW - footW) / 2f, fy = by + h + gap;
            MenuPanel(new Rect(fx, fy, footW, footH));
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(fx, fy + 5f, footW, 24f), menuFoot, center);
            GUI.color = Color.white;
        }

        // ───────── 操作方法のページ ─────────

        // 操作方法の行(ボタン・内容)は、中身(機器・両手持ちの武装・リロードの有無・照準を動かせる設定)が変わったときだけ作り直す
        (ProtoInput.Device dev, DuelWeapon two, bool reload, bool aimMove) helpKey;
        bool helpBuilt;
        readonly List<string> helpBtn = new(), helpWhat = new();
        /// <summary>右の列が始まる行(左の列は動かす・戦う、右の列はロックオン・拾う・メニュー)。</summary>
        int helpSplit;
        /// <summary>列ごとのボタンの欄・内容の欄の幅(文字の幅を測って決める。キーボードの表記は長いので、決め打ちにすると切れる)。</summary>
        readonly float[] helpBtnW = new float[2], helpWhatW = new float[2];
        static readonly Color HelpButtonColor = new(0.62f, 0.86f, 1f);

        void HelpRow(string btn, string what)
        {
            helpBtn.Add(btn);
            helpWhat.Add(what);
        }

        /// <summary>
        /// 操作方法の行を作る。ボタン名は使っているコントローラー(キーボード / PlayStation / Xbox)に合わせ、
        /// 両手持ち・リロードできる銃・照準を動かせる設定で出し分ける(前に左下へ出し続けていた操作説明と同じ出し分け)。
        /// メニューの中の操作(↑↓・決定・戻る・装備の確認の △/□ など)は、各画面の下の案内に任せて、ここには入れない。
        /// OnGUI の中から呼ぶ(幅を測るのにスタイルが要る)。
        /// </summary>
        void BuildHelpRows(DuelArms arms)
        {
            var dev = ProtoInput.Current;
            var two = arms.TwoHanded ? arms.Get(DuelSlot.RightHand) : null;
            bool reload = arms.AnyReloadable;   // 弾倉のある銃を付けているときだけ「△+撃つボタン リロード」を出す
            bool aimMove = DuelArmsRules.Current.cursorMovable;
            var key = (dev, two, reload, aimMove);
            if (helpBuilt && helpKey == key) return;
            helpBuilt = true;
            helpKey = key;
            helpBtn.Clear();
            helpWhat.Clear();
            static string B(ProtoInput.Btn b) => ProtoInput.L(b);
            bool kb = dev == ProtoInput.Device.Keyboard;
            string r1 = B(ProtoInput.Btn.R1), l1 = B(ProtoInput.Btn.L1), aim = B(ProtoInput.Btn.Aim);

            // 左の列:動かす・戦う
            HelpRow(B(ProtoInput.Btn.Move), "移動");
            // ロックオン中のカメラは敵を追い続け、右スティック・マウスでは回らない(狙う敵の切り替えになる。右の列)
            HelpRow(kb ? "マウス" : "右スティック", kb ? "カメラ(クリックで固定・Esc で解除)" : "カメラ");
            if (two != null)
            {
                // 両手持ち:R1 が主の技、L1 が別の技
                HelpRow(r1, two.MainMoveName);
                HelpRow(l1, two.altMove + (two.alt is { charge: > 0f } ? "(溜めて離す)" : ""));
                if (two.IsBlade) HelpRow($"{aim}+{r1}/{l1}", "強攻撃");
            }
            else
            {
                // 手のボタンはその手の武装(剣=攻撃 / 盾=ガード / 銃=射撃)
                HelpRow($"{r1}/{l1}", "右手/左手の武装");
                HelpRow($"{aim}+{r1}/{l1}", "強攻撃");
            }
            if (reload) HelpRow($"{aim}+撃つボタン", "リロード");
            // 銃・砲のボタンを押している間(と撃ってから少しの間)は、右スティック・マウスで画面の上の照準を動かす(カメラは回さない)
            if (aimMove) HelpRow(kb ? "マウス" : "右スティック", "撃つボタンを押している間:照準を動かす");
            HelpRow($"{B(ProtoInput.Btn.R2)}/{B(ProtoInput.Btn.L2)}", "右肩/左肩の武装");
            HelpRow(B(ProtoInput.Btn.Dodge), "回避");
            HelpRow(B(ProtoInput.Btn.Fly) + " 長押し", "飛行");
            HelpRow(B(ProtoInput.Btn.Boost), "ブースト");

            // 右の列:ロックオン・落ちている武装・メニュー
            helpSplit = helpBtn.Count;
            // ロックオンのボタンはオン・オフの切り替え(電脳斬禅と同じ)。狙う敵の切り替えは R3 / Tab(オフのときに押すとオンに戻る)か、
            // 撃っていないときに右スティック(マウスは左右に振る)を左右に弾く
            HelpRow(B(ProtoInput.Btn.Lock), "ロックオン切替");
            HelpRow(kb ? "Tab / マウスを左右に振る" : "R3 / 右スティックを左右に弾く", "ロックオン中:狙う敵を変更");
            HelpRow(B(ProtoInput.Btn.EquipNow), $"武装を付ける → 付ける所のボタン(背中は {B(ProtoInput.Btn.Fly)})");
            HelpRow(B(ProtoInput.Btn.Stash), "武装を持つ(所持品へ)");
            HelpRow(B(ProtoInput.Btn.Break), "武装を分解(弾か回復)");
            HelpRow(B(ProtoInput.Btn.EquipView), "メニュー(装備・所持品/操作方法)");
            HelpRow(B(ProtoInput.Btn.Restart), "最初からやり直す");

            var gc = new GUIContent();
            helpBtnW[0] = helpBtnW[1] = helpWhatW[0] = helpWhatW[1] = 0f;
            for (int i = 0; i < helpBtn.Count; i++)
            {
                int col = i < helpSplit ? 0 : 1;
                gc.text = helpBtn[i];
                helpBtnW[col] = Mathf.Max(helpBtnW[col], Mathf.Ceil(richSmall.CalcSize(gc).x) + 4f);
                gc.text = helpWhat[i];
                helpWhatW[col] = Mathf.Max(helpWhatW[col], Mathf.Ceil(richSmall.CalcSize(gc).x) + 4f);
            }
        }

        /// <summary>
        /// 操作方法のページ:画面の中央のパネルに、見出しと「ボタン ― 内容」の行を2列で並べる(行が多いので1列では縦に収まりにくい)。
        /// パネルの幅は文字の幅から決める。帯・地の色・文字の大きさは装備の確認にそろえる。動かす演出は入れない。
        /// </summary>
        void DrawHelpPage(DuelArms arms)
        {
            BuildMenuText();
            BuildHelpRows(arms);
            const float pad = 18f, titleH = 46f, rowH = 30f, footH = 34f, btnGap = 16f, colGap = 40f;
            int rows = Mathf.Max(helpSplit, helpBtn.Count - helpSplit);
            float colL = helpBtnW[0] + btnGap + helpWhatW[0], colR = helpBtnW[1] + btnGap + helpWhatW[1];
            float wd = Mathf.Min(SW - 8f, pad * 2f + colL + colGap + colR);
            float h = pad + titleH + rowH * rows + footH + 6f;
            float bx = (SW - wd) / 2f, by = Mathf.Max(4f, (SH - h) / 2f);
            MenuBackdrop();
            MenuPanel(new Rect(bx, by, wd, h));
            GUI.Label(new Rect(bx, by + pad - 6f, wd, titleH), "操作方法", viewTitleStyle);

            float top = by + pad + titleH, right = bx + wd - pad;
            for (int i = 0; i < helpBtn.Count; i++)
            {
                int col = i < helpSplit ? 0 : 1;
                float cx = col == 0 ? bx + pad : bx + pad + colL + colGap;
                float ry = top + (col == 0 ? i : i - helpSplit) * rowH;
                float wx = cx + helpBtnW[col] + btnGap;
                // 区切りの細い線(列ごと。ボタンと内容が離れていても、横に目で追えるように)
                GUI.color = new Color(1f, 1f, 1f, 0.1f);
                GUI.DrawTexture(new Rect(cx, ry, Mathf.Min(col == 0 ? colL : colR, right - cx), 1f), Texture2D.whiteTexture);
                GUI.color = HelpButtonColor;
                GUI.Label(new Rect(cx, ry + 4f, Mathf.Min(helpBtnW[col], right - cx), 22f), helpBtn[i], richSmall);
                GUI.color = Color.white;
                GUI.Label(new Rect(wx, ry + 4f, Mathf.Min(helpWhatW[col], right - wx), 22f), helpWhat[i], richSmall);
            }
            float y = top + rows * rowH;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(bx + pad, y, wd - pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(bx, y + 6f, wd, 24f), helpFoot, center);
            GUI.color = Color.white;
        }
    }
}
