using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 自機の武装(ステージだけ。部屋のモードでは付かない)。付ける所は5つ:右手(R1)/ 左手(L1)/ 背中 / 右肩(R2)/ 左肩(L2)。どれも1つずつ。
    ///   手:剣・盾・手の銃のどれでも(左右どちらにも)。剣ならそのボタンで攻撃(技は剣ごとの表)、盾なら押している間ガード、銃なら撃つ。
    ///       最初は右手に素の剣、左手は空(盾を拾うまでガードできない)。
    ///   背中:スラスター。走り・ブースト・回避の距離が伸び、回避・ブースト・飛行のENが減る。
    ///   肩:銃・砲・ミサイルのどれでも(左右どちらにも)。R2 で右肩、L2 で左肩を撃つ。撃ち方(押している間 / 押すたび)は武装ごと。
    /// 近接攻撃の間(振りかぶり〜戻り)と回避の間は撃てない(DuelPlayer.CanShoot)。
    /// 狙い:ロックオン中はその敵(射程の外・壁ごしなら、画面の中央に近い敵 → いなければ画面の中央の先)。
    /// 拾う:落ちている武装の近くで十字右(V)。背中はそのまま付ける。手・肩は付け先を選ぶ表示が出て、R1/L1(手)・R2/L2(肩)で決める
    ///   (選んでいる間、R1/L1/R2/L2 は攻撃・射撃・ガードにならない。決めた・取り消した時に押していたボタンは、離すまで効かない)。
    ///   付け先を選ぶ・背中に付けるのは、自機が動ける(Free)かガード中だけ。攻撃・回避・怯み・ガードで受けた・崩れたら取り消し。もう一度十字右・離れるでも取り消し。
    ///   同じ所の今の武装は、拾った武装のあった所に置く(拾い直せる。素の剣も)。在庫・メニューはない。
    /// 撃墜されても武装は残る。ボスを倒すと爆発で全部消し飛ぶ(LoseAll。右手は素の剣、左手は空に戻る)。自機の素の性能(HP・EN・怯み)は変えない。画面は揺らさない。
    /// 毎フレームの処理は DuelPlayer.Update の始めに Tick() で呼ぶ(ボタンの横取りを、自機が読むより先に決めるため)。
    /// </summary>
    public class DuelArms : MonoBehaviour
    {
        const int SlotCount = 5;
        /// <summary>押すたびに撃つ武装で、撃てない間に押したのを覚えておく秒数。</summary>
        const float PressBuffer = 0.2f;

        DuelPlayer player;
        DuelGame game;
        MechRig rig;
        readonly DuelWeapon[] slots = new DuelWeapon[SlotCount];
        readonly Transform[] visuals = new Transform[SlotCount];
        Vector3 swordBaseScale = Vector3.one, swordLBaseScale = Vector3.one;
        // 撃つ武装の、所ごとの状態
        readonly float[] cd = new float[SlotCount], cdMax = new float[SlotCount], reload = new float[SlotCount];
        readonly float[] lastFire = new float[SlotCount], pressedAt = new float[SlotCount];
        readonly int[] mag = new int[SlotCount];
        /// <summary>ボタン(0=R1 1=L1 2=R2 3=L2)を、離すまで使わない(付け先を決めたボタンが、そのまま攻撃・射撃にならないように)。</summary>
        readonly bool[] suppress = new bool[4];

        /// <summary>拾える範囲にある一番近い武装(なければ null)。</summary>
        public DuelLoot Nearby { get; private set; }
        /// <summary>付け先を選んでいる武装(選んでいなければ null)。</summary>
        public DuelLoot Choosing { get; private set; }

        public void Init(DuelPlayer p, DuelGame g, MechRig r)
        {
            player = p;
            game = g;
            rig = r;
            if (rig != null && rig.sword != null) swordBaseScale = rig.sword.localScale;
            if (rig != null && rig.swordL != null) swordLBaseScale = rig.swordL.localScale;
            for (int i = 0; i < SlotCount; i++) lastFire[i] = pressedAt[i] = -10f;
            slots[(int)DuelSlot.RightHand] = DuelWeapon.BaseSword;
            for (int i = 0; i < SlotCount; i++) Refresh((DuelSlot)i);
        }

        public DuelWeapon Get(DuelSlot s) => slots[(int)s];

        // ───────── 自機から見る値 ─────────

        /// <summary>その手(0=右 / 1=左)の武装(空なら null)。</summary>
        public DuelWeapon Hand(int hand) => slots[hand == 0 ? (int)DuelSlot.RightHand : (int)DuelSlot.LeftHand];
        public bool IsSword(int hand) => Hand(hand) is { kind: DuelKind.Sword };

        public DuelWeapon BackW => slots[(int)DuelSlot.Back];
        /// <summary>走りの倍率:スラスター × 持っている盾の重さ。</summary>
        public float RunMul
        {
            get
            {
                float m = BackW != null ? BackW.runMul : 1f;
                for (int h = 0; h < 2; h++)
                    if (Hand(h) is { kind: DuelKind.Shield } s) m *= s.runMul;
                return m;
            }
        }
        public float DashMul => BackW != null ? BackW.dashMul : 1f;
        public float DodgeMul => BackW != null ? BackW.dodgeMul : 1f;
        public float EnCostMul => BackW != null ? BackW.enCostMul : 1f;

        /// <summary>
        /// ガードのゲージの最大(持っている盾のうち大きいほう。盾がなければ0=ガードできない)。
        /// 盾が2枚でもゲージは1本を共有する(作者に未確認の仮)。最大はこの値で、削れ方・通す傷・構え歩き・固まる長さは構えている盾の値
        /// (DuelPlayer.guardShield。両手で構えたら大きい盾)。
        /// </summary>
        public float GuardMax
        {
            get
            {
                float m = 0f;
                for (int h = 0; h < 2; h++)
                    if (Hand(h) is { kind: DuelKind.Shield } s) m = Mathf.Max(m, s.guardMax);
                return m;
            }
        }

        /// <summary>盾を持つ手のボタンを押しているか。押していれば、その盾と手(0=右 / 1=左)を返す(両手なら大きい盾)。</summary>
        public bool GuardHeld(out DuelWeapon shield, out int hand)
        {
            shield = null;
            hand = 0;
            for (int h = 0; h < 2; h++)
            {
                var w = Hand(h);
                if (w == null || w.kind != DuelKind.Shield || !Held(h == 0 ? DuelSlot.RightHand : DuelSlot.LeftHand)) continue;
                if (shield != null && w.guardMax <= shield.guardMax) continue;
                shield = w;
                hand = h;
            }
            return shield != null;
        }

        // ───────── ボタン(R1=右手 / L1=左手 / R2=右肩 / L2=左肩) ─────────

        static int Btn(DuelSlot s) => s switch
        {
            DuelSlot.RightHand => 0,
            DuelSlot.LeftHand => 1,
            DuelSlot.RightShoulder => 2,
            DuelSlot.LeftShoulder => 3,
            _ => -1,
        };

        static bool RawHeld(int b) => b switch
        {
            0 => ProtoInput.RightHandHeld,
            1 => ProtoInput.LeftHandHeld,
            2 => ProtoInput.RightShoulderHeld,
            _ => ProtoInput.LeftShoulderHeld,
        };

        static bool RawPressed(int b) => b switch
        {
            0 => ProtoInput.Attack,
            1 => ProtoInput.LeftHandPressed,
            2 => ProtoInput.RightShoulderPressed,
            _ => ProtoInput.LeftShoulderPressed,
        };

        /// <summary>その所のボタンを押している(付け先を選んでいる間・決めたボタンを離すまでは数えない)。</summary>
        public bool Held(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && Choosing == null && !suppress[b] && RawHeld(b);
        }

        /// <summary>その所のボタンをこのフレームに押した(付け先を選んでいる間・決めたボタンを離すまでは数えない)。</summary>
        public bool Pressed(DuelSlot s)
        {
            int b = Btn(s);
            return b >= 0 && Choosing == null && !suppress[b] && RawPressed(b);
        }

        // ───────── 画面に出す情報 ─────────

        /// <summary>その所の銃の弾倉の残り(弾倉のない銃・銃でなければ -1)。</summary>
        public int MagLeft(DuelSlot s) => Get(s) is { IsGun: true, magazine: > 0 } ? mag[(int)s] : -1;
        /// <summary>その所の銃のリロードの残り(秒。0ならリロードしていない)。</summary>
        public float ReloadLeft(DuelSlot s) => Mathf.Max(0f, reload[(int)s]);
        /// <summary>その所の砲・ミサイルが次に撃てるまでの残り(0〜1。0で撃てる)。</summary>
        public float Cooldown01(DuelSlot s) => cd[(int)s] > 0f ? Mathf.Clamp01(cd[(int)s] / Mathf.Max(0.01f, cdMax[(int)s])) : 0f;
        /// <summary>その所で、最近(秒)撃ったか(手の銃の構えの見た目用)。</summary>
        public bool ShotRecently(DuelSlot s, float sec) => Time.time - lastFire[(int)s] < sec;

        // ───────── 毎フレーム(DuelPlayer.Update の始めから) ─────────

        public void Tick()
        {
            float dt = Time.deltaTime, now = Time.time;
            for (int i = 0; i < SlotCount; i++)
            {
                cd[i] -= dt;
                var w = slots[i];
                if (w == null || !w.IsGun) continue;
                if (reload[i] > 0f)
                {
                    reload[i] -= dt;
                    if (reload[i] <= 0f) mag[i] = w.magazine;
                }
                // 撃たずにリロードの時間の1.5倍たったら、弾倉を満タンに戻す(撃ちかけの弾倉が残り続けないように)
                else if (w.magazine > 0 && mag[i] < w.magazine && now - lastFire[i] > w.reload * 1.5f) mag[i] = w.magazine;
            }
            for (int b = 0; b < suppress.Length; b++)
                if (suppress[b] && !RawHeld(b)) suppress[b] = false;

            if (player == null || player.Dead)
            {
                Nearby = null;
                if (Choosing != null) EndChoice();
                return;
            }
            // ボスを倒した後(武装が消し飛んだ後)は拾えない
            Nearby = game.stage != null && game.stage.Cleared ? null : DuelLoot.Nearest(player);
            UpdateChoice();

            bool can = player.CanShoot;
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (w == null || !w.IsShooter) continue;
                var s = (DuelSlot)i;
                if (Pressed(s)) pressedAt[i] = now;
                if (!can) pressedAt[i] = -10f;   // 斬っている間・回避の間に押したのは覚えておかない
                bool want = can && (w.hold ? Held(s) : now - pressedAt[i] <= PressBuffer);
                if (!want)
                {
                    cd[i] = Mathf.Max(cd[i], 0f);   // 撃っていない間に端数を溜めない(押し始めにまとめて出ないように)
                    continue;
                }
                bool fired = w.kind == DuelKind.ShoulderHeavy ? FireHeavy(w, s) : FireGun(w, s);
                if (fired) pressedAt[i] = -10f;
            }
        }

        // ───────── 付ける・外す ─────────

        /// <summary>
        /// 十字右:背中の武装はそのまま付ける。手・肩の武装は付け先を選ぶ表示にする。
        /// 選んでいる間は R1/L1(手)・R2/L2(肩)で決める。もう一度十字右・武装から離れる・動ける状態(Free・ガード)でなくなると取り消し。
        /// 選ぶ・付けるのは動ける状態のときだけ(振っている最中・回避の間に手の武装が替わって、当たりと見た目がずれないように)。
        /// </summary>
        void UpdateChoice()
        {
            bool ready = player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;
            if (Choosing != null)
            {
                if (Choosing != Nearby || ProtoInput.EquipNow || !ready)
                {
                    EndChoice();
                    return;
                }
                foreach (var s in DuelWeapon.SlotsFor(Choosing.Weapon.kind))
                {
                    int b = Btn(s);
                    if (b < 0 || !RawPressed(b)) continue;
                    var loot = Choosing;
                    EndChoice();
                    Equip(loot, s);
                    return;
                }
                return;
            }
            if (!ready || Nearby == null || !ProtoInput.EquipNow) return;
            var to = DuelWeapon.SlotsFor(Nearby.Weapon.kind);
            if (to.Length == 1) Equip(Nearby, to[0]);
            else Choosing = Nearby;
        }

        /// <summary>
        /// 付け先を選ぶ表示を終える(決めた・取り消した)。その時に押している R1/L1/R2/L2 は全部、離すまで攻撃・射撃・ガードにしない
        /// (決めたボタンと同時に押したボタンや、選んでいる間に押し続けていたボタンが、終わった瞬間に効かないように)。
        /// </summary>
        void EndChoice()
        {
            Choosing = null;
            for (int b = 0; b < suppress.Length; b++)
                if (RawHeld(b) || RawPressed(b)) suppress[b] = true;
        }

        /// <summary>付け先を選ぶ表示の途中なら取り消す(装備の確認を開いたとき)。</summary>
        public void CancelChoice()
        {
            if (Choosing != null) EndChoice();
        }

        /// <summary>落ちている武装をその所に付ける。今の武装は、拾った武装のあった所にそのまま置く(その場で入れ替え。拾い直せる)。</summary>
        void Equip(DuelLoot loot, DuelSlot slot)
        {
            var w = loot.Weapon;
            int i = (int)slot;
            var old = slots[i];
            slots[i] = w;
            Vector3 at = loot.transform.position;
            loot.Take();
            if (old != null) DuelLoot.Place(old, at);
            mag[i] = w.magazine;
            reload[i] = 0f;
            pressedAt[i] = -10f;
            cd[i] = cdMax[i] = w.IsShooter ? 0.3f : 0f;
            Refresh(slot);
            if (w.kind == DuelKind.Shield || old is { kind: DuelKind.Shield }) player.OnShieldsChanged(w.kind == DuelKind.Shield);
            game.Log($"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた");
        }

        /// <summary>
        /// 全部の武装を失う(ボスの爆発)。付けていた武装は火花を散らして吹き飛び、全部の所を空にしてから右手に素の剣を入れ直す
        /// (左手に拾い直した素の剣も吹き飛ぶ)。失った数を返す(素の剣は数えない)。
        /// </summary>
        public int LoseAll()
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (w == null) continue;
                bool baseSword = w == DuelWeapon.BaseSword;
                if (baseSword && i == (int)DuelSlot.RightHand) continue;   // 右手の素の剣はそのまま(下で入れ直す)
                if (!baseSword) n++;
                slots[i] = null;
                var blade = rig == null || w.kind != DuelKind.Sword ? null : i == (int)DuelSlot.RightHand ? rig.sword : rig.swordL;
                if (blade != null)
                {
                    // 手の剣:機体の剣は素に戻し(左手は隠し)、同じ形の刃を吹き飛ばす
                    var copy = CreateVisual(w, null);
                    copy.SetPositionAndRotation(blade.position, blade.rotation);
                    Fling(copy);
                }
                var v = visuals[i];
                visuals[i] = null;
                if (v != null) Fling(v);
            }
            if (rig != null && rig.sword != null && n > 0)
            {
                DuelSpark.Burst(rig.sword.position, Vector3.up, new Color(1f, 0.8f, 0.4f), 24, 1.3f);
                ProtoFx.Blast(rig.sword.position, 3f, new Color(1f, 0.7f, 0.3f), 0.25f);
            }
            slots[(int)DuelSlot.RightHand] = DuelWeapon.BaseSword;
            for (int i = 0; i < SlotCount; i++)
            {
                Refresh((DuelSlot)i);
                mag[i] = 0;
                reload[i] = cd[i] = 0f;
                pressedAt[i] = -10f;
            }
            if (Choosing != null) EndChoice();
            player.OnShieldsChanged(false);
            return n;
        }

        /// <summary>
        /// 外れた武装を、回しながら外へ吹き飛ばして少しして消す。当たり判定は付けず自前で動かす
        /// (カメラの壁よけ・足もとのレイ・敵の見通しに当たらないよう、機体と同じ Ignore Raycast の層にも置く)。
        /// </summary>
        void Fling(Transform v)
        {
            v.SetParent(null, true);
            foreach (var c in v.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            foreach (var tr in v.GetComponentsInChildren<Transform>()) tr.gameObject.layer = DuelFighter.FighterLayer;
            ProtoFx.Blast(v.position, 3f, new Color(1f, 0.6f, 0.2f), 0.25f);
            DuelSpark.Burst(v.position, Vector3.up, new Color(1f, 0.75f, 0.4f), 12, 1f);
            Vector3 out_ = v.position - player.Chest;
            out_.y = 0f;
            Vector3 vel = (out_.sqrMagnitude > 0.01f ? out_.normalized : Random.onUnitSphere) * 14f + Vector3.up * 16f;
            StartCoroutine(Debris(v, vel, Random.insideUnitSphere * 360f, 2.5f));
        }

        /// <summary>破片の動き:重力で落ち、床で弾んで勢いを失い、life 秒で消える(床は真下へのレイで、移動量を含めて調べる)。</summary>
        static IEnumerator Debris(Transform v, Vector3 vel, Vector3 spin, float life)
        {
            float t = 0f;
            while (t < life && v != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                vel.y -= DuelFighter.Gravity * dt;
                Vector3 p = v.position, next = p + vel * dt;
                if (vel.y < 0f && Physics.Raycast(new Vector3(next.x, p.y, next.z), Vector3.down, out var hit, p.y - next.y + 0.5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    next.y = hit.point.y + 0.5f;
                    vel = new Vector3(vel.x * 0.5f, -vel.y * 0.3f, vel.z * 0.5f);
                    spin *= 0.5f;
                }
                v.position = next;
                v.Rotate(spin * dt, Space.World);
                yield return null;
            }
            if (v != null) Destroy(v.gameObject);
        }

        /// <summary>その所の見た目を作り直す。</summary>
        void Refresh(DuelSlot s)
        {
            int i = (int)s;
            var w = slots[i];
            if (visuals[i] != null) Destroy(visuals[i].gameObject);
            visuals[i] = null;
            float k = game.mechHeight / 9.06f;

            if (s is DuelSlot.RightHand or DuelSlot.LeftHand)
            {
                bool right = s == DuelSlot.RightHand;
                bool sword = w is { kind: DuelKind.Sword };
                var blade = rig == null ? null : right ? rig.sword : rig.swordL;
                if (blade != null)
                {
                    // 剣:機体の剣を出し、握りはそのままで長さを届く範囲に合わせ、レア以上はレア度の色に塗る
                    blade.gameObject.SetActive(sword);
                    if (sword)
                    {
                        Vector3 bs = right ? swordBaseScale : swordLBaseScale;
                        blade.localScale = new Vector3(bs.x, bs.y * w.reachMul, bs.z);
                        MaterialPropertyBlock block = null;
                        if (w.rarity >= 1)
                        {
                            block = new MaterialPropertyBlock();
                            block.SetColor("_BaseColor", w.Color);
                        }
                        foreach (var r in blade.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
                        return;
                    }
                }
                if (w == null) return;
                // 盾・手の銃(剣の握り点がない機体の剣も):前腕に付ける。前腕の取付点は Z が手の向き、X が外側(右腕は +、左腕は -)
                var mount = rig == null ? null : right ? rig.mountRightArm : rig.mountLeftArm;
                float side = right ? 1f : -1f;
                if (mount != null)
                {
                    visuals[i] = CreateVisual(w, mount);
                    visuals[i].localPosition = w.kind == DuelKind.Shield ? new Vector3(0.75f * side, 0f, 0.2f) * k : new Vector3(0f, -0.45f, 1.2f) * k;
                }
                else
                {
                    visuals[i] = CreateVisual(w, transform);
                    visuals[i].localPosition = new Vector3(2.6f * side, game.mechHeight * 0.5f, 0.8f);
                }
                return;
            }

            if (w == null) return;
            Transform m = rig == null ? null : s switch
            {
                DuelSlot.Back => rig.mountBack,
                DuelSlot.RightShoulder => rig.mountShoulderR,
                _ => rig.mountShoulderL,
            };
            if (m != null) visuals[i] = CreateVisual(w, m);
            else
            {
                // 取付点がない機体:胸の高さに仮に付ける
                visuals[i] = CreateVisual(w, transform);
                float sx = s == DuelSlot.RightShoulder ? 2f : s == DuelSlot.LeftShoulder ? -2f : 0f;
                visuals[i].localPosition = new Vector3(sx, game.mechHeight * 0.85f, s == DuelSlot.Back ? -1.8f : 0f);
            }
        }

        /// <summary>武装の仮の見た目(レア度の色)。剣は細長い刃(長さは届く範囲に合わせる)、盾は板、手の銃は細長い箱。parent が null なら親なし。</summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            var root = new GameObject(w.name).transform;
            if (parent != null) root.SetParent(parent, false);
            bool blade = w.kind == DuelKind.Sword;
            var prim = GameObject.CreatePrimitive(blade ? PrimitiveType.Cube : w.shape);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            prim.transform.localScale = blade ? new Vector3(0.4f, 1f, 4.5f * w.reachMul) : w.scale;
            prim.transform.localRotation = Quaternion.Euler(blade ? Vector3.zero : w.euler);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(w.Color);
            return root;
        }

        // ───────── 狙い ─────────

        /// <summary>撃ち出す所(その所の武装の先。なければ胸・肩の高さの左右)。</summary>
        Vector3 Muzzle(DuelSlot s)
        {
            var v = visuals[(int)s];
            Vector3 f = player.transform.forward;
            if (v != null) return v.position + f * 1.5f;
            bool hand = s is DuelSlot.RightHand or DuelSlot.LeftHand;
            float side = s is DuelSlot.RightShoulder or DuelSlot.RightHand ? 2f : -2f;
            return player.transform.position + Vector3.up * game.mechHeight * (hand ? 0.55f : 0.85f) + player.transform.right * side + f * 1.5f;
        }

        /// <summary>
        /// 撃つ相手:ロックオン中の敵(射程の内で見通せれば)。なければ、画面の中央から30°以内で見通せる、射程の内の敵(中央に近く近いもの)。
        /// </summary>
        DuelEnemy AimTarget(float range, Vector3 from)
        {
            var t = player.Target;
            if (t != null && !t.Dead && Vector3.Distance(t.Chest, from) <= range && DuelSenses.ClearSight(from, t.Chest)) return t;
            var cam = Camera.main;
            Vector3 camF = cam != null ? cam.transform.forward : player.transform.forward;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                Vector3 to = e.Chest - from;
                float d = to.magnitude;
                if (d > range) continue;
                float ang = Vector3.Angle(camF, to);
                if (ang > 30f) continue;
                float score = d * (1f + ang / 15f);
                if (score >= bestScore || !DuelSenses.ClearSight(from, e.Chest)) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        /// <summary>狙う相手がいないとき:画面の中央の先(最初に当たる地形・敵。なければ遠く)。</summary>
        Vector3 FreeAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return player.Chest + player.transform.forward * 100f;
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            const float far = 250f;
            float best = far;
            if (Physics.Raycast(o, d, out var hit, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) best = hit.distance;
            if (RayHitsEnemy(o, d, best, out _, out float ed)) best = ed;
            return o + d * best;
        }

        /// <summary>画面の中央の先の点を、from から射程(range)までに切ったもの(狙う敵がいないとき、射程より遠くへ飛ばさない)。</summary>
        Vector3 FreeAimPoint(Vector3 from, float range)
        {
            Vector3 p = FreeAimPoint();
            Vector3 d = p - from;
            return d.magnitude > range ? from + d.normalized * range : p;
        }

        /// <summary>from から dir へ len まで飛ばして、最初に当たる敵(機体を縦長のカプセルとみなす)。</summary>
        bool RayHitsEnemy(Vector3 from, Vector3 dir, float len, out DuelEnemy hitEnemy, out float dist)
        {
            hitEnemy = null;
            dist = len;
            Vector3 to = from + dir * len;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float r = e.Radius, h = game.mechHeight * e.Size;
                Vector3 a = e.transform.position + Vector3.up * r, b = e.transform.position + Vector3.up * Mathf.Max(r, h - r);
                if (SegSegDistance(from, to, a, b, out float s) > r) continue;
                if (s * len >= dist) continue;
                dist = s * len;
                hitEnemy = e;
            }
            return hitEnemy != null;
        }

        /// <summary>線分どうしの最短距離。s は1本目の線分の上の、いちばん近い点の位置(0〜1)。</summary>
        static float SegSegDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float t;
            if (a <= 1e-6f && e <= 1e-6f) { s = 0f; return r.magnitude; }
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }

        /// <summary>向きを最大 deg 度の範囲でランダムにぶらす。</summary>
        static Vector3 Jitter(Vector3 dir, float deg)
        {
            if (deg <= 0f) return dir;
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            Vector3 tilted = Quaternion.AngleAxis(Random.Range(0f, deg), perp.normalized) * dir;
            return Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * tilted;
        }

        // ───────── 銃(手・肩) ─────────

        static readonly Color BulletColor = new(1f, 0.85f, 0.35f), BeamColor = new(1f, 0.45f, 0.95f);

        /// <summary>銃を撃つ(撃てる間隔・弾倉の残りがあれば)。撃ったら true。</summary>
        bool FireGun(DuelWeapon w, DuelSlot s)
        {
            int i = (int)s;
            if (cd[i] > 0f || reload[i] > 0f) return false;
            if (w.magazine > 0 && mag[i] <= 0) { reload[i] = w.reload; return false; }
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            var t = AimTarget(w.range, from);
            Vector3 aim = t != null ? t.Chest : FreeAimPoint(from, w.range);
            Vector3 dir = (aim - from).normalized;
            Color c = w.magazine <= 0 ? BeamColor : BulletColor;
            int n = Mathf.Max(1, w.pellets);
            // 間隔の端数は持ち越す(1フレームより短い間隔なら、そのフレームに何発か出す。最大4回。押すたびに撃つ銃は1回)
            int bursts = w.hold ? 4 : 1;
            for (int k = 0; k < bursts && cd[i] <= 0f; k++)
            {
                cd[i] += Mathf.Max(0.01f, w.interval);
                for (int p = 0; p < n; p++) Shot(from, Jitter(dir, w.spread), w, c, n > 1);
                if (w.magazine > 0 && --mag[i] <= 0) { reload[i] = w.reload; break; }   // 撃ち切ったらリロード
            }
            cd[i] = Mathf.Max(cd[i], 0f);
            return true;
        }

        /// <summary>銃の1発:まっすぐ飛び、最初に当たった地形・敵で止まる。</summary>
        void Shot(Vector3 from, Vector3 dir, DuelWeapon w, Color c, bool pellet)
        {
            float len = w.range;
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            Vector3 end = from + dir * len;
            if (RayHitsEnemy(from, dir, len, out var e, out float d))
            {
                end = from + dir * d;
                e.TakeRangedHit(w.damage, w.stagger, Flat(dir) * w.knock, 0.3f);
                DuelSpark.Burst(end, -dir, c, pellet ? 2 : 3, 0.6f);
            }
            ProtoFx.Tracer(from, end, c, pellet ? 0.15f : 0.25f, 0.05f);
        }

        // ───────── 砲・ミサイル(肩) ─────────

        static readonly Color ShellColor = new(0.8f, 1f, 0.35f), MissileColor = new(1f, 0.6f, 0.25f), BlastColor = new(1f, 0.6f, 0.25f);

        /// <summary>砲・ミサイルを撃つ(次に撃てるまでの時間がたっていれば)。撃ったら true。</summary>
        bool FireHeavy(DuelWeapon w, DuelSlot s)
        {
            int i = (int)s;
            if (cd[i] > 0f) return false;
            cd[i] = cdMax[i] = w.interval;
            lastFire[i] = Time.time;
            Vector3 from = Muzzle(s);
            if (w.IsMissile)
            {
                // ミサイル:狙える敵に順番に割り振る(ロックオン中の敵が先、あとは近い順)。いなければ画面の中央の先へ
                var targets = MissileTargets(w.range);
                Vector3 point = FreeAimPoint(from, w.range);
                for (int j = 0; j < w.salvo; j++)
                {
                    var t = targets.Count > 0 ? targets[j % targets.Count] : null;
                    Vector2 sc = Random.insideUnitCircle * w.radius;
                    StartCoroutine(Missile(from, t, point + new Vector3(sc.x, 0f, sc.y), w, j * 0.06f));
                }
                return true;
            }
            var target = AimTarget(w.range, from);
            Vector3 aim = target != null ? target.Chest : FreeAimPoint(from, w.range);
            Vector3 dir = (aim - from).normalized;
            float len = Mathf.Min(Vector3.Distance(from, aim), w.range);
            // 間を地形・ほかの敵がさえぎっていれば、そこで爆発する
            if (Physics.Raycast(from, dir, out var hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) len = hit.distance;
            if (RayHitsEnemy(from, dir, len, out _, out float ed)) len = ed;
            StartCoroutine(Shell(from, from + dir * len, w));
            return true;
        }

        List<DuelEnemy> MissileTargets(float range)
        {
            var list = new List<DuelEnemy>();
            Vector3 eye = player.LockPoint;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                if (!DuelSenses.ClearSight(eye, e.Chest)) continue;
                list.Add(e);
            }
            var t = player.Target;
            list.Sort((a, b) =>
            {
                if (a == b) return 0;
                if (a == t) return -1;
                if (b == t) return 1;
                return (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude);
            });
            return list;
        }

        /// <summary>弾の見た目:光る球と、尾を引く線。</summary>
        static Transform Projectile(Vector3 at, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Shot";
            go.transform.position = at;
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoFx.Unlit(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.18f;
            tr.startWidth = size * 0.7f;
            tr.endWidth = 0f;
            tr.sharedMaterial = ProtoFx.Unlit(c);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>砲弾:まっすぐ飛んで、着いた所で爆発する。</summary>
        IEnumerator Shell(Vector3 from, Vector3 to, DuelWeapon w)
        {
            const float speed = 160f;
            var p = Projectile(from, ShellColor, 1.2f + w.radius * 0.05f);
            float dur = Vector3.Distance(from, to) / speed, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                p.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.001f, dur)));
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(to, w, true);
        }

        /// <summary>ミサイル1発:上へ打ち上げてから、狙った敵へ曲がって飛ぶ(敵が動いても追いかける)。着いたら小さく爆発する。</summary>
        IEnumerator Missile(Vector3 from, DuelEnemy target, Vector3 point, DuelWeapon w, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Vector3 lift = from + Vector3.up * 12f - player.transform.forward * 3f + Random.insideUnitSphere * 7f;
            Vector3 goal = point;
            var p = Projectile(from, MissileColor, 0.8f);
            float dur = Random.Range(0.55f, 0.75f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                if (target != null && !target.Dead) goal = target.Chest;
                float k = Mathf.Clamp01(t / dur);
                p.position = Vector3.Lerp(Vector3.Lerp(from, lift, k), Vector3.Lerp(lift, goal, k), k);
                yield return null;
            }
            Destroy(p.gameObject);
            Explode(goal, w, false);
        }

        /// <summary>爆発:半径の中の敵に当てる(上下に離れすぎた敵には当たらない)。自機には当たらない。</summary>
        void Explode(Vector3 at, DuelWeapon w, bool shell)
        {
            ProtoFx.Blast(at, w.radius, shell ? ShellColor : BlastColor, shell ? 0.35f : 0.25f);
            DuelSpark.Burst(at, Vector3.zero, new Color(1f, 0.75f, 0.4f), shell ? 20 : 6, shell ? 1.2f : 0.7f);
            bool any = false;
            var list = game.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Dead) continue;
                Vector3 d = e.transform.position - at;
                float dy = at.y - e.transform.position.y;
                d.y = 0f;
                if (d.magnitude - e.Radius > w.radius) continue;
                if (dy < -w.radius || dy > game.mechHeight * e.Size + w.radius) continue;
                e.TakeRangedHit(w.damage, w.stagger, (d.sqrMagnitude > 0.01f ? d.normalized : player.transform.forward) * w.knock, 0.6f);
                any = true;
            }
            if (any && shell) game.HitStop(0.05f);   // 砲だけ。ミサイルは何発も当たるので止めない
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }
    }
}
