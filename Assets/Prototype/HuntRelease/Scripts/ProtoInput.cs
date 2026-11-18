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
    ///   ロックオン: Q・中クリック / 十字キー左(切り替え:Tab / R3、ロックオン中は右スティックを左右に弾く)
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
        public static bool Blocked => ProtoPlayer.I != null && ProtoPlayer.I.MenuBlocksInput;

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
        public static bool Attack => !Blocked && ((Ms != null && MouseLocked && Ms.leftButton.wasPressedThisFrame) || P(p => p.rightShoulder));
        public static bool RightHandHeld => !Blocked && ((Ms != null && MouseLocked && Ms.leftButton.isPressed) || (Pad != null && Pad.rightShoulder.isPressed));
        public static bool LeftHandPressed => !Blocked && ((Ms != null && MouseLocked && Ms.rightButton.wasPressedThisFrame) || P(p => p.leftShoulder));
        public static bool LeftHandHeld => !Blocked && ((Ms != null && MouseLocked && Ms.rightButton.isPressed) || (Pad != null && Pad.leftShoulder.isPressed));
        public static bool RightShoulderHeld => !Blocked && ((Kb != null && Kb.rKey.isPressed) || (Pad != null && Pad.rightTrigger.isPressed));
        public static bool LeftShoulderHeld => !Blocked && ((Kb != null && Kb.eKey.isPressed) || (Pad != null && Pad.leftTrigger.isPressed));
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
            float y = Pad != null ? Pad.leftStick.ReadValue().y : 0f;
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

        public static bool Dodge => !Blocked && (K(k => k.leftCtrlKey) || (!DebugMod && P(p => p.buttonWest)));
        public static bool BoostToggle => !Blocked && (K(k => k.leftShiftKey) || P(p => p.buttonEast));
        public static bool FlyHeld => !Blocked && (DebugFly || (Kb != null && Kb.spaceKey.isPressed) || (Pad != null && Pad.buttonSouth.isPressed));
        /// <summary>動作確認用(外部から飛行ボタンを押しっぱなしにする)。</summary>
        public static bool DebugFly;
        /// <summary>動作確認用(外部から移動スティックを倒しっぱなしにする)。</summary>
        public static Vector2 DebugMove;
        /// <summary>動作確認用(外部から照準ボタンを押しっぱなしにする)。</summary>
        public static bool DebugAim;
        // ───────── 表示するボタン名(最後に触った機器に合わせる) ─────────

        public enum Device { Keyboard, PlayStation, Xbox }
        public enum Btn { R1, L1, R2, L2, Menu, Dodge, Boost, Fly, Confirm, Back, Lock, Switch, Restart, ModeToggle, Select, Move, Heal, Aim, Discard, PickUp, EquipNow, Delete }

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
                _ => new[] { "WASD", "左スティック", "左スティック" },
            };
            return n[i];
        }

        /// <summary>手動照準(△ / C 長押し):ロックオン中でもカメラを動かせ、照準の先へ撃つ。SHARE を押している間は数えない。</summary>
        public static bool AimHeld => !Blocked && (DebugAim || (Kb != null && Kb.cKey.isPressed) || (Pad != null && !DebugMod && Pad.buttonNorth.isPressed));

        /// <summary>足元の武装を拾う(△ / C を押した瞬間。長押しは照準)。パラメータ制のときだけ使う。</summary>
        public static bool PickUp => !Blocked && (K(k => k.cKey) || (Pad != null && !DebugMod && P(p => p.buttonNorth)));

        /// <summary>
        /// 足元の武装をその場で装備する(十字キー右 / V)。武装ハントではパラメータ制のとき(在庫を通さず装備)、
        /// 近接の試作(MeleeDuel)のステージでは、落ちている武装を拾って付けるのに使う。
        /// </summary>
        public static bool EquipNow => !Blocked && (K(k => k.vKey) || P(p => p.dpad.right));

        /// <summary>メニュー内:選んでいる在庫を完全に削除する(△ / C 長押し)。</summary>
        public static bool MenuDeleteHeld => (Kb != null && Kb.cKey.isPressed) || (Pad != null && !DebugMod && Pad.buttonNorth.isPressed);

        /// <summary>回復アイテムを使う(十字キー上 / H)。</summary>
        public static bool Heal => !Blocked && (K(k => k.hKey) || P(p => p.dpad.up));
        public static bool LockToggle => !Blocked && (K(k => k.qKey) || (Ms != null && Ms.middleButton.wasPressedThisFrame) || P(p => p.dpad.left));
        public static bool SwitchTarget => !Blocked && (K(k => k.tabKey) || P(p => p.rightStickButton));
        public static bool Restart => K(k => k.f1Key) || (DebugMod && P(p => p.buttonNorth));
        public static bool ToggleMode => K(k => k.f2Key) || (DebugMod && P(p => p.buttonWest));
    }
}
