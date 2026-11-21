using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HuntProto
{
    /// <summary>
    /// 入力。移動・ロックオンの作法は電脳斬禅に合わせ、武装は肩の4ボタンに割り当てる。
    ///   武装      : R1 右手(剣・連打で3段) / L1 左手(銃・押している間) / R2 右肩 / L2 左肩
    ///               キー:左クリック / 右クリック / R / E
    ///   装備      : OPTIONS・≡ / G で装備メニュー(時間が止まる。↑↓で選ぶ、×/Space 決定、○/Backspace 戻る)
    ///   回避      : □・X / 左Ctrl
    ///   ブースト  : ○・B で入/切 / 左Shift
    ///   飛行      : ×・A 長押しで上昇 / Space 長押し
    ///   ロックオン: Q・中クリック / 十字キー左(切り替え:Tab / R3。武装ハントはロックオン中に右スティックを左右に弾いても切り替わる。
    ///               プロトタイプは Q・中クリック / 十字キー左でロックオンのオン・オフを切り替え、Tab / R3 で狙う敵を変える(オフならオンに戻す)。
    ///               ロックオン中は右スティックを左右に弾く・マウスを左右に大きく振っても変わる。ロックオン中のカメラは敵を追い続け、右スティック・マウスでは回らない)
    ///   メニュー  : OPTIONS・≡ / I(プロトタイプだけ。時間が止まる)。項目は「装備・所持品」「操作方法」「閉じる」。↑↓で選ぶ、×/Space・Enter 決定、
    ///               ○・B / Backspace で1つ前へ戻る(項目のメニューでは閉じる)。OPTIONS・≡ / I はどの段階からでも全部閉じる。
    ///               Esc はカメラのマウスの固定を外すので使わない
    ///   装備の確認: メニューの「装備・所持品」(プロトタイプだけ。↑↓で選び、△ / C で外して持つ、所持品は付ける所のボタンで付ける、
    ///               □・X / X で処分(もう一度で床に捨てる・← で弾・→ で回復))
    ///   拾う案内  : 十字右 / V 付ける、十字下 / G 持つ、十字上 / F 分解(プロトタイプだけ)。
    ///               分解を選んでいる間は 十字左 / ← で弾、十字右 / → で回復、十字上 / F でやめる(十字左はこの間ロックオンの切り替えにならない)。
    ///               付け先・分解を選んでいる間は ○・B / Backspace でもやめる(ChoiceCancel)。その間に効くのは移動・カメラ・選ぶ表示の操作だけ
    ///               (攻撃・射撃・ガード・回避・ブーストの切り替え・飛行・ロックオンのボタンは効かない。止めるのは DuelArms / DuelPlayer の側)
    ///   やり直し  : F1 / SHARE を押しながら△(電脳斬禅のデバッグメニューと同じ)
    ///   モード切替: F2 / SHARE を押しながら□
    /// </summary>
    public static class ProtoInput
    {
        static Gamepad Pad => Gamepad.current;
        static Keyboard Kb => Keyboard.current;
        static Mouse Ms => Mouse.current;
        static bool MouseLocked => Cursor.lockState == CursorLockMode.Locked;

        static bool K(System.Func<Keyboard, KeyControl> key) => Kb != null && key(Kb).wasPressedThisFrame;
        static bool P(System.Func<Gamepad, ButtonControl> b) => Pad != null && b(Pad).wasPressedThisFrame;

        /// <summary>
        /// 装備メニューを開いている間は、戦闘の操作(移動・攻撃・カメラなど)を効かなくする。
        /// メニューの操作(上下・決定・戻る・開閉)とデバッグ操作は効く。時間を止めないメニューのため。
        /// </summary>
        public static bool Blocked => Paused || (ProtoPlayer.I != null && ProtoPlayer.I.MenuBlocksInput);

        // ───────── プロトタイプのメニュー(項目のメニュー・装備の確認・操作方法。時間を止めるメニュー) ─────────

        static bool paused, waitRelease;
        static int waitFrame = -1;
        static bool waitNow;

        /// <summary>
        /// プロトタイプで、メニュー(項目のメニュー・装備の確認・操作方法)を開いている間(DuelGame が立てる。武装ハントでは使わない)。戦闘の操作を全部止める。
        /// 閉じたあとは、その時に押していたボタン(攻撃・回避・飛行など)を全部離すまで、ボタンの操作だけ止める(閉じた瞬間に漏れないように)。
        /// </summary>
        public static bool Paused
        {
            get => paused;
            set
            {
                if (paused && !value)
                {
                    waitRelease = true;
                    waitFrame = -1;
                }
                paused = value;
            }
        }

        /// <summary>ボタンの操作を止めるか(メニュー中、またはメニューを閉じてからボタンを離すまで)。</summary>
        static bool BtnBlocked => Blocked || WaitingRelease;

        static bool WaitingRelease
        {
            get
            {
                if (!waitRelease) return false;
                if (waitFrame == Time.frameCount) return waitNow;
                waitFrame = Time.frameCount;
                waitNow = AnyButtonHeld();
                if (!waitNow) waitRelease = false;
                return waitNow;
            }
        }

        /// <summary>戦闘に使うボタン・キー(移動・カメラは除く)のどれかを押しているか。</summary>
        static bool AnyButtonHeld()
        {
            var p = Pad;
            if (p != null && (p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed || p.buttonNorth.isPressed ||
                              p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.leftTrigger.isPressed || p.rightTrigger.isPressed ||
                              p.dpad.ReadValue().sqrMagnitude > 0.25f || p.rightStickButton.isPressed))
                return true;
            if (Ms != null && (Ms.leftButton.isPressed || Ms.rightButton.isPressed || Ms.middleButton.isPressed)) return true;
            var k = Kb;
            return k != null && (k.spaceKey.isPressed || k.leftCtrlKey.isPressed || k.leftShiftKey.isPressed || k.cKey.isPressed || k.vKey.isPressed ||
                                 k.rKey.isPressed || k.eKey.isPressed || k.qKey.isPressed || k.tabKey.isPressed || k.hKey.isPressed ||
                                 k.fKey.isPressed || k.gKey.isPressed);
        }

        /// <summary>
        /// プロトタイプ:メニュー(項目は「装備・所持品」「操作方法」「閉じる」)を開く/どの段階からでも全部閉じる。
        /// パッド右のメニューボタン(OPTIONS・≡ / +)を押した瞬間、または I キー。
        /// (前は装備の確認を直接開くボタンだったので、名前は EquipView のまま。装備の確認はメニューの一番上の項目)
        /// 左のメニューボタン(SHARE・View)は、押しながら△・□のやり直しなど(DebugMod)だけに使う。
        /// </summary>
        public static bool EquipView => K(k => k.iKey) || P(p => p.startButton);

        /// <summary>
        /// プロトタイプ:装備の確認から1つ前(項目のメニュー)へ戻る(○・B / Backspace。処分の確認の途中なら確認だけ取り消す)。
        /// Esc は DuelCamera がマウスの固定を外すのに使うので含めない。
        /// </summary>
        public static bool EquipViewBack => K(k => k.backspaceKey) || P(p => p.buttonEast);

        public static Vector2 Move
        {
            get
            {
                if (Blocked) return Vector2.zero;
                Vector2 v = DebugMove;
                if (Kb != null)
                {
                    if (Kb.wKey.isPressed) v.y += 1f;
                    if (Kb.sKey.isPressed) v.y -= 1f;
                    if (Kb.dKey.isPressed) v.x += 1f;
                    if (Kb.aKey.isPressed) v.x -= 1f;
                }
                if (Pad != null)
                {
                    Vector2 s = Pad.leftStick.ReadValue();
                    if (s.magnitude > 0.15f) v += s;
                }
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>マウスの移動量(画素)。カーソル固定中だけ。</summary>
        public static Vector2 MouseDelta => !Blocked && Ms != null && MouseLocked ? Ms.delta.ReadValue() : Vector2.zero;
        public static Vector2 RightStick
        {
            get
            {
                if (Pad == null || Blocked) return Vector2.zero;
                Vector2 s = Pad.rightStick.ReadValue();
                return s.magnitude > 0.15f ? s : Vector2.zero;
            }
        }

        // 武装:R1 右手 / L1 左手 / R2 右肩 / L2 左肩
        public static bool Attack => !BtnBlocked && ((Ms != null && MouseLocked && Ms.leftButton.wasPressedThisFrame) || P(p => p.rightShoulder));
        public static bool RightHandHeld => !BtnBlocked && ((Ms != null && MouseLocked && Ms.leftButton.isPressed) || (Pad != null && Pad.rightShoulder.isPressed));
        public static bool LeftHandPressed => !BtnBlocked && ((Ms != null && MouseLocked && Ms.rightButton.wasPressedThisFrame) || P(p => p.leftShoulder));
        public static bool LeftHandHeld => !BtnBlocked && ((Ms != null && MouseLocked && Ms.rightButton.isPressed) || (Pad != null && Pad.leftShoulder.isPressed));
        public static bool RightShoulderHeld => !BtnBlocked && ((Kb != null && Kb.rKey.isPressed) || (Pad != null && Pad.rightTrigger.isPressed));
        public static bool LeftShoulderHeld => !BtnBlocked && ((Kb != null && Kb.eKey.isPressed) || (Pad != null && Pad.leftTrigger.isPressed));
        /// <summary>R2 / R を押した瞬間(プロトタイプで、押すたびに撃つ肩の武装・付け先の選択に使う)。</summary>
        public static bool RightShoulderPressed => !BtnBlocked && (K(k => k.rKey) || P(p => p.rightTrigger));
        /// <summary>L2 / E を押した瞬間(プロトタイプで、押すたびに撃つ肩の武装・付け先の選択に使う)。</summary>
        public static bool LeftShoulderPressed => !BtnBlocked && (K(k => k.eKey) || P(p => p.leftTrigger));
        /// <summary>装備メニューを開く/閉じる(切り替え方式では開くときにラックを解放)。</summary>
        /// <summary>パッド右のメニューボタン(OPTIONS・≡)/ G。</summary>
        public static bool Menu => K(k => k.gKey) || P(p => p.startButton);
        /// <summary>SHARE(View)を押している間は、△□をデバッグ用に回す。</summary>
        static bool DebugMod => Pad != null && Pad.selectButton.isPressed;
        // メニュー内:↑↓(十字キー・左スティック・W/S) で選ぶ / × 決定 / ○ 戻る
        static bool stickUpHeld, stickDownHeld;
        static int stickFrame = -1;
        static bool stickUp, stickDown;
        static void ReadStick()
        {
            if (stickFrame == Time.frameCount) return;
            stickFrame = Time.frameCount;
            Vector2 v = Pad != null ? Pad.leftStick.ReadValue() : Vector2.zero;
            float y = v.y;
            stickUp = y > 0.6f && !stickUpHeld;
            stickDown = y < -0.6f && !stickDownHeld;
            stickUpHeld = y > 0.6f;
            stickDownHeld = y < -0.6f;
        }
        public static bool MenuUp { get { ReadStick(); return stickUp || K(k => k.upArrowKey) || K(k => k.wKey) || P(p => p.dpad.up); } }
        public static bool MenuDown { get { ReadStick(); return stickDown || K(k => k.downArrowKey) || K(k => k.sKey) || P(p => p.dpad.down); } }
        public static bool MenuConfirm => K(k => k.spaceKey) || K(k => k.enterKey) || P(p => p.buttonSouth);
        public static bool MenuBack => K(k => k.backspaceKey) || P(p => p.buttonEast);
        /// <summary>メニュー内:選んでいる武装を捨てる(□ / X)。</summary>
        public static bool MenuDiscard => K(k => k.xKey) || (!DebugMod && P(p => p.buttonWest));
        /// <summary>
        /// プロトタイプのメニュー内:←(十字左・← / A)。分解して弾にする。
        /// 左スティックでは受けない(下の行へ移ろうとして斜めに倒し、誤って分解して武装が消えないように)。
        /// </summary>
        public static bool MenuLeft => K(k => k.leftArrowKey) || K(k => k.aKey) || P(p => p.dpad.left);
        /// <summary>プロトタイプのメニュー内:→(十字右・→ / D)。分解して回復にする。左スティックでは受けない。</summary>
        public static bool MenuRight => K(k => k.rightArrowKey) || K(k => k.dKey) || P(p => p.dpad.right);
        /// <summary>プロトタイプのメニュー内:付けている武装を外して所持品にする(△ / C)。</summary>
        public static bool MenuStore => K(k => k.cKey) || (!DebugMod && P(p => p.buttonNorth));

        /// <summary>
        /// プロトタイプのメニュー内:所持品を付ける所のボタンを押した瞬間(0=R1・左クリック / 1=L1・右クリック / 2=R2・R / 3=L2・E / 4=×・Space(背中))。
        /// メニューの間は戦闘のボタンが止まっている(Paused)ので、ここだけ止めずに読む。
        /// </summary>
        public static bool MenuSlotPressed(int b) => b switch
        {
            0 => (Ms != null && MouseLocked && Ms.leftButton.wasPressedThisFrame) || P(p => p.rightShoulder),
            1 => (Ms != null && MouseLocked && Ms.rightButton.wasPressedThisFrame) || P(p => p.leftShoulder),
            2 => K(k => k.rKey) || P(p => p.rightTrigger),
            3 => K(k => k.eKey) || P(p => p.leftTrigger),
            _ => K(k => k.spaceKey) || P(p => p.buttonSouth),
        };

        public static bool Dodge => !BtnBlocked && (K(k => k.leftCtrlKey) || (!DebugMod && P(p => p.buttonWest)));
        public static bool BoostToggle => !BtnBlocked && (K(k => k.leftShiftKey) || P(p => p.buttonEast));
        public static bool FlyHeld => !BtnBlocked && (DebugFly || (Kb != null && Kb.spaceKey.isPressed) || (Pad != null && Pad.buttonSouth.isPressed));
        /// <summary>動作確認用(外部から飛行ボタンを押しっぱなしにする)。</summary>
        public static bool DebugFly;
        /// <summary>動作確認用(外部から移動スティックを倒しっぱなしにする)。</summary>
        public static Vector2 DebugMove;
        /// <summary>動作確認用(外部から照準ボタンを押しっぱなしにする)。</summary>
        public static bool DebugAim;
        // ───────── 表示するボタン名(最後に触った機器に合わせる) ─────────

        public enum Device { Keyboard, PlayStation, Xbox }
        public enum Btn { R1, L1, R2, L2, Menu, Dodge, Boost, Fly, Confirm, Back, Lock, Switch, Restart, ModeToggle, Select, Move, Heal, Aim, Discard, PickUp, EquipNow, Delete, EquipView,
                          Stash, Break, MenuLeft, MenuRight, BreakAmmo, BreakHeal }

        /// <summary>今使っている機器。パッドに触ればパッド、キーかマウスに触ればキーボードに切り替わる。</summary>
        public static Device Current { get; private set; } = Gamepad.current != null ? PadKind() : Device.Keyboard;
        static int trackedFrame = -1;

        static Device PadKind() => Gamepad.current is UnityEngine.InputSystem.DualShock.DualShockGamepad ? Device.PlayStation : Device.Xbox;

        /// <summary>毎フレーム1回呼ぶ。</summary>
        public static void TrackDevice()
        {
            if (trackedFrame == Time.frameCount) return;
            trackedFrame = Time.frameCount;
            var p = Pad;
            if (p != null && (p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed || p.buttonNorth.isPressed ||
                              p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.leftTrigger.isPressed || p.rightTrigger.isPressed ||
                              p.dpad.ReadValue().sqrMagnitude > 0.25f || p.startButton.isPressed || p.selectButton.isPressed ||
                              p.leftStick.ReadValue().magnitude > 0.5f || p.rightStick.ReadValue().magnitude > 0.5f))
            {
                Current = PadKind();
                return;
            }
            if ((Kb != null && Kb.anyKey.isPressed) ||
                (Ms != null && (Ms.leftButton.isPressed || Ms.rightButton.isPressed || Ms.delta.ReadValue().magnitude > 3f)))
                Current = Device.Keyboard;
        }

        /// <summary>そのボタンの、今の機器での呼び名。</summary>
        public static string L(Btn b)
        {
            int i = (int)Current;   // Keyboard / PlayStation / Xbox
            string[] n = b switch
            {
                Btn.R1 => new[] { "左クリック", "R1", "RB" },
                Btn.L1 => new[] { "右クリック", "L1", "LB" },
                Btn.R2 => new[] { "R", "R2", "RT" },
                Btn.L2 => new[] { "E", "L2", "LT" },
                Btn.Menu => new[] { "G", "OPTIONS", "≡" },
                Btn.Dodge => new[] { "左Ctrl", "□", "X" },
                Btn.Boost => new[] { "Shift", "○", "B" },
                Btn.Fly => new[] { "Space", "×", "A" },
                Btn.Confirm => new[] { "Space", "×", "A" },
                Btn.Back => new[] { "Backspace", "○", "B" },
                Btn.Lock => new[] { "Q", "十字左", "十字左" },
                Btn.Switch => new[] { "Tab", "右スティック弾き", "右スティック弾き" },
                Btn.Restart => new[] { "F1", "SHARE+△", "View+Y" },
                Btn.ModeToggle => new[] { "F2", "SHARE+□", "View+X" },
                Btn.Select => new[] { "W/S", "↑↓", "↑↓" },
                Btn.Heal => new[] { "H", "十字上", "十字上" },
                Btn.Aim => new[] { "C", "△", "Y" },
                Btn.Discard => new[] { "X", "□", "X" },
                Btn.PickUp => new[] { "C", "△", "Y" },
                Btn.EquipNow => new[] { "V", "十字右", "十字右" },
                Btn.Delete => new[] { "C長押し", "△長押し", "Y長押し" },
                Btn.EquipView => new[] { "I", "OPTIONS", "≡" },
                Btn.Stash => new[] { "G", "十字下", "十字下" },
                Btn.Break => new[] { "F", "十字上", "十字上" },
                Btn.MenuLeft => new[] { "←", "←", "←" },
                Btn.MenuRight => new[] { "→", "→", "→" },
                Btn.BreakAmmo => new[] { "←", "十字左", "十字左" },
                Btn.BreakHeal => new[] { "→", "十字右", "十字右" },
                _ => new[] { "WASD", "左スティック", "左スティック" },
            };
            return n[i];
        }

        /// <summary>手動照準(△ / C 長押し):ロックオン中でもカメラを動かせ、照準の先へ撃つ。SHARE を押している間は数えない。</summary>
        public static bool AimHeld => !BtnBlocked && (DebugAim || (Kb != null && Kb.cKey.isPressed) || (Pad != null && !DebugMod && Pad.buttonNorth.isPressed));

        /// <summary>足元の武装を拾う(△ / C を押した瞬間。長押しは照準)。パラメータ制のときだけ使う。</summary>
        public static bool PickUp => !BtnBlocked && (K(k => k.cKey) || (Pad != null && !DebugMod && P(p => p.buttonNorth)));

        /// <summary>
        /// 足元の武装をその場で装備する(十字キー右 / V)。武装ハントではパラメータ制のとき(在庫を通さず装備)、
        /// プロトタイプのステージでは、落ちている武装を拾って付けるのに使う。
        /// </summary>
        public static bool EquipNow => !BtnBlocked && (K(k => k.vKey) || P(p => p.dpad.right));

        /// <summary>プロトタイプ:落ちている武装を所持品に入れる(十字キー下 / G)。</summary>
        public static bool StashNow => !BtnBlocked && (K(k => k.gKey) || P(p => p.dpad.down));

        /// <summary>プロトタイプ:落ちている武装の分解を選ぶ表示にする(十字キー上 / F)。分解を選ぶ表示の間は「やめる」(入ったのと同じボタンで戻る)。</summary>
        public static bool BreakNow => !BtnBlocked && (K(k => k.fKey) || P(p => p.dpad.up));

        /// <summary>
        /// プロトタイプ:分解を選ぶ表示の間の「弾にする」(十字キー左 / ←)。装備の確認の「← 弾 / → 回復」と左右をそろえる。
        /// 十字キー左はふだんロックオンの切り替え(LockToggle)なので、分解を選んでいる間は DuelPlayer が LockToggleNoDpad を読む。
        /// キーボードは ←(ロックオンの Q・中クリックとぶつからない)。
        /// </summary>
        public static bool BreakAmmoNow => !BtnBlocked && (K(k => k.leftArrowKey) || P(p => p.dpad.left));

        /// <summary>プロトタイプ:分解を選ぶ表示の間の「回復にする」(十字キー右 / →)。</summary>
        public static bool BreakHealNow => !BtnBlocked && (K(k => k.rightArrowKey) || P(p => p.dpad.right));

        /// <summary>
        /// プロトタイプ:付け先を選ぶ表示・分解を選ぶ表示を取り消す(○・B / Backspace。メニューの「戻る」MenuBack と同じ組)。
        /// ○・B はふだんブーストのオン・オフ(BoostToggle)なので、選ぶ表示の間と閉じたフレームは DuelPlayer がブーストに読まない(DuelArms.ChoiceLocksInput)。
        /// </summary>
        public static bool ChoiceCancel => !BtnBlocked && (K(k => k.backspaceKey) || P(p => p.buttonEast));

        /// <summary>プロトタイプ:「弾にする」のボタン(十字キー左 / ←)を押している(止めずに読む。分解を抜けた後、離すまでロックオンの切り替えにしないため)。</summary>
        public static bool BreakAmmoHeld => (Kb != null && Kb.leftArrowKey.isPressed) || (Pad != null && Pad.dpad.left.isPressed);

        /// <summary>メニュー内:選んでいる在庫を完全に削除する(△ / C 長押し)。</summary>
        public static bool MenuDeleteHeld => (Kb != null && Kb.cKey.isPressed) || (Pad != null && !DebugMod && Pad.buttonNorth.isPressed);

        /// <summary>回復アイテムを使う(十字キー上 / H)。</summary>
        public static bool Heal => !BtnBlocked && (K(k => k.hKey) || P(p => p.dpad.up));
        public static bool LockToggle => !BtnBlocked && (K(k => k.qKey) || (Ms != null && Ms.middleButton.wasPressedThisFrame) || P(p => p.dpad.left));
        /// <summary>プロトタイプ:十字キー左を除いたロックオンの切り替え(Q・中クリックだけ)。十字キー左を分解の「弾にする」に使っている間に読む。</summary>
        public static bool LockToggleNoDpad => !BtnBlocked && (K(k => k.qKey) || (Ms != null && Ms.middleButton.wasPressedThisFrame));
        public static bool SwitchTarget => !BtnBlocked && (K(k => k.tabKey) || P(p => p.rightStickButton));
        public static bool Restart => K(k => k.f1Key) || (DebugMod && P(p => p.buttonNorth));
        public static bool ToggleMode => K(k => k.f2Key) || (DebugMod && P(p => p.buttonWest));
    }
}
