using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelArms の付ける・外す・所持品・見た目(partial。本体と全体の説明は DuelArms.cs)
    public partial class DuelArms
    {
        // ───────── 付ける・外す ─────────

        /// <summary>
        /// 十字右:付け先を選ぶ表示にする(手・肩・背中・両手持ちとも同じ流れ。付け先が空きでもふさがっていても同じ)。
        /// 選んでいる間は R1/L1(手)・R2/L2(肩)・✗(背中)で決める(背中は R1〜L2 では付かず、手・肩は ✗ では付かない)。
        /// ✗ は押し始めだけ数える(飛んでいて押し続けていた ✗ では付かない)。両手持ちは R1/L1 のどちらでも両手に付く(R2/L2 では付かない。Equip)。
        /// 選ぶ・付けるのは動ける状態のときだけ(振っている最中・回避の間に手の武装が替わって、当たりと見た目がずれないように)。
        /// 拾う案内(選んでいない間)では、十字右で付け先を選ぶ表示、十字下でそのまま所持品へ(重さの上限を超えるなら持てない)、
        /// 十字上で分解を選ぶ表示。分解を選ぶ表示では、十字左(←)で弾・十字右(→)で回復。
        /// 十字上を続けて押しても分解されない(作者が選んだ形:押し間違いで武装が消えないように)。
        /// 取り消し(どちらの選ぶ表示も、閉じるだけで何も付けない・分解しない):○・B / Backspace(ProtoInput.ChoiceCancel。作者「○でキャンセルできるように」)、
        /// 入ったのと同じボタンをもう一度(付け先 = 十字右 / 分解 = 十字上)、武装から離れる、空中に出る、動ける状態(Free・ガード)でなくなる(怯むなど)。
        /// 選ぶ表示の間に効くのは、移動・カメラと、ここで読む選ぶ表示の操作だけ(ChoiceOpen / ChoiceLocksInput。攻撃・射撃・ガード・飛行は Held / Pressed /
        /// FlyBlocked が、回避・ブーストの切り替え・ロックオンのボタンは DuelPlayer が止める)。開くときに、それより前に押して覚えている先行入力も捨てる(BeginChoice)。
        /// 十字左はふだんロックオンの切り替えなので、分解を選ぶ表示を抜けた後も、離すまでは切り替えにしない(LockDpadTaken)。
        /// 付け先を選ぶ表示で、付けると重さの上限を超える所のボタンを押したら、付けずに知らせる(表示はそのまま)。
        /// </summary>
        void UpdateChoice()
        {
            bool ready = player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;
            bool cancel = ProtoInput.ChoiceCancel;   // ○・B / Backspace(選ぶ表示を開いている間だけ見る。ふだんの ○ はブーストの切り替え)
            if (Breaking != null)
            {
                breakLeftHold = true;   // 抜けた後も、十字左を離すまでロックオンの切り替えにしない(Tick で離したら下ろす)
                if (Breaking != Nearby || ProtoInput.BreakNow || cancel || !ready)
                {
                    EndChoice();
                    return;
                }
                bool toAmmo = ProtoInput.BreakAmmoNow, toHeal = !toAmmo && ProtoInput.BreakHealNow;
                if (!toAmmo && !toHeal) return;
                if (SalvageFull(toHeal)) return;   // 弾が上限・HP が満タン:武装だけ消えないよう、分解しない(選ぶ表示はそのまま)
                var loot = Breaking;
                EndChoice();
                var w = loot.Weapon;
                int left = loot.Memo.mag;
                loot.Take();
                Salvage(w, left, toHeal);
                return;
            }
            if (Choosing != null)
            {
                if (Choosing != Nearby || ProtoInput.EquipNow || cancel || !ready)
                {
                    EndChoice();
                    return;
                }
                foreach (var s in DuelWeapon.SlotsFor(Choosing.Weapon.kind))
                {
                    int b = ChoiceBtn(s);
                    if (b < 0 || !RawPressed(b)) continue;
                    var loot = Choosing;
                    float after = WeightIfEquipped(loot.Weapon, s);
                    if (!Fits(after))
                    {
                        TooHeavy(after, "付けられない");
                        return;
                    }
                    EndChoice();
                    Equip(loot, s);
                    return;
                }
                return;
            }
            if (!ready || Nearby == null) return;
            if (ProtoInput.EquipNow)
            {
                Choosing = Nearby;
                BeginChoice();
            }
            else if (ProtoInput.StashNow) StashLoot(Nearby);
            else if (ProtoInput.BreakNow)
            {
                Breaking = Nearby;
                BeginChoice();
            }
        }

        /// <summary>
        /// 選ぶ表示(付け先・分解)を開いた:それより前に押して覚えている攻撃・回避・射撃(先行入力)を捨てる
        /// (ENの回復待ち・撃てるまでの待ちが明けて、選んでいる間に遅れて出ないように)。選んでいる間に押した分は、もとから覚えない。
        /// </summary>
        void BeginChoice()
        {
            ClearBufferedInput();
            player.ClearBufferedInput();
        }

        /// <summary>落ちている武装を所持品に入れる(重さの上限を超えるなら入れない)。弾倉の残りも覚える。</summary>
        void StashLoot(DuelLoot loot)
        {
            var w = loot.Weapon;
            float after = WeightIfStashed(w);
            if (!Fits(after))
            {
                TooHeavy(after, "持てない");
                return;
            }
            stash.Add(new Carried { w = w, memo = loot.Memo });
            loot.Take();
            game.Log($"{w.name}({w.RarityName})を持った(所持 {stash.Count}  重さ {TotalWeight:0}/{CarryMax:0})");
        }

        /// <summary>分解しても何も得ない(弾にする:弾が上限 / 回復にする:HP が満タンか撃墜中)。</summary>
        public bool SalvageUseless(bool heal) => heal ? player == null || player.Dead || player.HP >= player.MaxHP - 0.001f : Ammo >= Rules.ammoMax - 0.001f;

        /// <summary>分解しても何も得ないなら、知らせて true(分解しない)。</summary>
        bool SalvageFull(bool heal)
        {
            if (!SalvageUseless(heal)) return false;
            game.Log(heal ? "HP は満タン ― 回復にしても何も得ない" : $"弾は上限({Rules.ammoMax:0})― 弾にしても何も得ない");
            return true;
        }

        /// <summary>
        /// 武装を分解して、共通の弾(上限まで)か HP の回復にする。量は DuelArmsRules(レア度だけで決まる)。
        /// 弾倉のある銃は、それに加えて弾倉の残り(magLeft。-1=満タン)が共通の弾へ戻る(弾にしても回復にしても。上限まで。MagAmmo)。
        /// </summary>
        void Salvage(DuelWeapon w, int magLeft, bool heal)
        {
            var r = Rules;
            float back = MagAmmo(w, magLeft);
            if (heal)
            {
                float before = player.HP, ammoBefore = Ammo;
                player.Heal(r.BreakHeal(w));
                Refund(back);
                game.Log($"{w.name}を分解して回復 +{player.HP - before:0}(HP {player.HP:0}/{player.MaxHP:0})"
                         + (Ammo > ammoBefore ? $" 弾倉の残りは弾へ +{Ammo - ammoBefore:0.#}" : ""));
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(0.45f, 1f, 0.55f), 14, 0.9f);
            }
            else
            {
                float before = Ammo;
                Ammo = Mathf.Min(r.ammoMax, Ammo + r.BreakAmmo(w) + back);
                game.Log($"{w.name}を分解して弾 +{Ammo - before:0.#}" + (back > 0f ? $"(弾倉の残り {back:0.#} 込み)" : "")
                         + $"(弾 {Ammo:0}/{r.ammoMax:0})" + (Ammo >= r.ammoMax ? " 上限" : ""));
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(1f, 0.85f, 0.35f), 14, 0.9f);
            }
        }

        /// <summary>分解したときの量(弾・回復)。案内に出す。弾倉の残りの分は入らない(MagAmmo を足す)。</summary>
        public int SalvageAmmo(DuelWeapon w) => Rules.BreakAmmo(w);
        public int SalvageHeal(DuelWeapon w) => Rules.BreakHeal(w);

        /// <summary>
        /// 分解したときに共通の弾へ戻る、弾倉の残りの分(残りの数 × 1発の消費。作者「解体時は弾倉の残りは弾薬として戻るようにしてください」)。
        /// left は弾倉の残り(-1=満タン)。敵が落としたばかりの銃の最初の満タンの弾倉も数える(作者「入っている分は全部返す」)。
        /// 弾倉のない武装・弾を使わない銃は 0。
        /// </summary>
        public float MagAmmo(DuelWeapon w, int left) =>
            w is { IsGun: true, magazine: > 0 } && w.ammoCost > 0f ? (left < 0 ? w.magazine : Mathf.Min(left, w.magazine)) * w.ammoCost : 0f;
        /// <summary>落ちている武装を分解したときに戻る、弾倉の残りの分。</summary>
        public float MagAmmo(DuelLoot loot) => loot != null ? MagAmmo(loot.Weapon, loot.Memo.mag) : 0f;
        /// <summary>所持品を分解したときに戻る、弾倉の残りの分。</summary>
        public float MagAmmo(Carried c) => c != null ? MagAmmo(c.w, c.memo.mag) : 0f;
        /// <summary>付けている武装を分解したときに戻る、弾倉の残りの分(両手の銃は右手の所の状態。リロード中に取り置いた分は別に戻る:ResetState)。</summary>
        public float MagAmmo(DuelSlot slot)
        {
            var w = slots[(int)slot];
            return w == null ? 0f : MagAmmo(w, MagOf(w.IsTwoHand && slot is DuelSlot.RightHand or DuelSlot.LeftHand ? R : (int)slot));
        }

        /// <summary>
        /// 選ぶ表示(付け先を選ぶ表示・分解を選ぶ表示)を終える(決めた・分解した・取り消した)。その時に押している R1/L1/R2/L2/✗ は全部、
        /// 離すまで攻撃・射撃・ガード・飛行にしない(決めたボタンと同時に押したボタンや、選んでいる間に押し続けていたボタンが、終わった瞬間に効かないように)。
        /// 終えたフレームは覚えておき、そのフレームの間は DuelPlayer が回避・ブーストの切り替え・ロックオンのボタンを読まない(ChoiceLocksInput)。
        /// </summary>
        void EndChoice()
        {
            Choosing = null;
            Breaking = null;
            choiceEndFrame = Time.frameCount;
            for (int b = 0; b < suppress.Length; b++)
                if (RawHeld(b) || RawPressed(b)) suppress[b] = true;
        }

        /// <summary>選ぶ表示(付け先・分解)の途中なら取り消す(メニューを開いたとき)。L1 の溜めもやめる(閉じた後に、離したことになって撃たないように)。</summary>
        public void CancelChoice()
        {
            StopCharge();
            if (ChoiceOpen) EndChoice();
        }

        /// <summary>先行入力(押してからの受付時間内の射撃)を捨てる。メニューを開いたとき(閉じた直後に撃たないように)。</summary>
        public void ClearBufferedInput()
        {
            for (int i = 0; i < StateCount; i++) pressedAt[i] = -10f;
        }

        /// <summary>捨てた武装を出す所(所ごとに少しずらす。Drop と HasFloorBelow で同じ)。</summary>
        Vector3 DropPos(int i)
        {
            float ang = (i - 2) * 50f;
            Vector3 off = Quaternion.Euler(0f, ang, 0f) * player.transform.forward * (player.Radius * 0.6f);
            return player.transform.position + off + Vector3.up * 2f;
        }

        /// <summary>
        /// その所の武装を捨てたときに置く所の真下に床があるか(穴の上・空中で捨てると、落ちる武装が -200m より下まで落ちて消えるので)。
        /// 下の階の床でもよい(そこまで落ちて拾い直せる)。
        /// </summary>
        public bool HasFloorBelow(DuelSlot slot)
        {
            if (player == null) return false;
            Vector3 p = DropPos((int)slot);
            return Physics.Raycast(p, Vector3.down, p.y + 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// その所の武装を外して、自機の足もとの床に置く(装備の確認の画面から。素の剣も捨てられ、どの所も空にできる)。
        /// 置き方は付け替えで外した武装と同じ DuelLoot.Place(勢いなしで真下の床まで落ちる。拾い直せる)。
        /// 機体の中心から半径の内側(所ごとに少しずらす)の、床から少し上に出す(壁の外・床の下に出ないように)。
        /// 撃墜中・ボスを倒した後(拾い直せない)・真下に床がない所(HasFloorBelow)では捨てない。捨てたら true。
        /// </summary>
        public bool Drop(DuelSlot slot)
        {
            int i = (int)slot;
            if (slots[i] == null || !CanDrop || !HasFloorBelow(slot)) return false;
            var w = Unequip(slot, out var memo, out bool two);   // memo:撃ちかけの弾倉の残り・装填待ち(拾い直しても満タン・待ちなしにしない)
            DuelLoot.Place(w, DropPos(i)).Memo = memo;
            game.Log(two ? $"両手の{w.name}を床に捨てた" : $"{DuelWeapon.SlotName(slot)}の{w.name}を床に捨てた");
            return true;
        }

        /// <summary>
        /// その所の武装を外す(両手持ちなら両手とも空ける)。外した武装・撃つ状態(弾倉の残り・装填待ち)・両手持ちだったかを返す(空なら null)。
        /// リロード中ならやめて、取り置いた弾を返す。盾ならガードを引き直し、剣ならその手の振り・押して覚えている攻撃をやめる。
        /// </summary>
        DuelWeapon Unequip(DuelSlot slot, out Memo memo, out bool two)
        {
            int i = (int)slot;
            var w = slots[i];
            memo = Memo.Fresh;
            two = false;
            if (w == null) return null;
            two = w.IsTwoHand && (i == R || i == L);
            memo = MemoOf(two ? R : i);
            if (two)
            {
                // 両手持ち:右手・左手の両方を空ける
                StopCharge();
                slots[R] = slots[L] = null;
                ResetState(R);
                ResetState(L);
                ResetState(AltIndex);
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
            }
            else
            {
                slots[i] = null;
                ResetState(i);
                Refresh(slot);
            }
            if (w.kind == DuelKind.Shield) player.OnShieldsChanged(false);
            if (two && w.IsBlade)
            {
                player.OnSwordDropped(0);
                player.OnSwordDropped(1);
            }
            else if (w.IsBlade) player.OnSwordDropped(slot == DuelSlot.RightHand ? 0 : 1);
            return w;
        }

        // ───────── 所持品(装備の確認の画面から) ─────────

        /// <summary>
        /// 装備の確認から付け外し・捨てる・分解ができるか:自機が動ける(Free)かガード中だけ(床から拾うときと同じ決まり)。
        /// 撃墜中・攻撃・回避・怯み・ガードで受けて固まっている・崩れている間はできない(止まった時間の中で外して、固まりを解くなどを防ぐ)。
        /// </summary>
        public bool CanRearrange => player != null && !player.Dead && player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;

        /// <summary>今は所持品・装備を床に捨てられるか(付け外しできる間で、ボスを倒した後でない)。</summary>
        bool CanDrop => CanRearrange && !game.stage.Cleared;

        /// <summary>付けている武装を外して所持品にする(重さの合計は増えない。付け外しできる間だけ)。外したら true。</summary>
        public bool StoreSlot(DuelSlot slot)
        {
            if (!CanRearrange) return false;
            var w = Unequip(slot, out var memo, out bool two);
            if (w == null) return false;
            stash.Add(new Carried { w = w, memo = memo });
            game.Log(two ? $"両手の{w.name}を外して持った" : $"{DuelWeapon.SlotName(slot)}の{w.name}を外して持った");
            return true;
        }

        /// <summary>付けている武装を分解して、弾か回復にする(付け外しできる間だけ。弾が上限・HP が満タンなら分解しない)。分解したら true。</summary>
        public bool SalvageSlot(DuelSlot slot, bool heal)
        {
            if (!CanRearrange || slots[(int)slot] == null || SalvageFull(heal)) return false;
            var w = Unequip(slot, out var memo, out _);   // memo.mag:外す直前の弾倉の残り
            Salvage(w, memo.mag, heal);
            return true;
        }

        /// <summary>
        /// 所持品をその所に付ける。今その所の武装(両手持ちが絡むときは両手の分)は所持品に入る(1つ目は付けた所持品と同じ番号に入れ替え、2つ目は末尾)。
        /// 付け外しできない間・付けられない所・重さの上限を超える(所持品を軽く数えている場合)なら付けない。付けたら true。
        /// 撃つ状態(弾倉の残り・装填待ち)は外したときのまま戻す。盾のゲージは満タンにせず、今の値を新しい最大で切るだけ(外して付け直して満タンにしないように)。
        /// </summary>
        public bool EquipFromStash(int index, DuelSlot slot)
        {
            if (!CanRearrange || index < 0 || index >= stash.Count) return false;
            var c = stash[index];
            if (System.Array.IndexOf(DuelWeapon.SlotsFor(c.w.kind), slot) < 0) return false;
            float keep = 1f - Rules.stockWeightRate;   // 所持品から付けると、所持品として軽く数えていた分だけ重くなる
            float after = TotalWeight + (c.w.weight - RemovedWeight(c.w, slot)) * keep;
            if (!Fits(after))
            {
                TooHeavy(after, "付けられない");
                return false;
            }
            stash.RemoveAt(index);
            var removed = Mount(c.w, c.memo, slot, false);
            for (int k = 0; k < removed.Count; k++)
            {
                var nc = new Carried { w = removed[k].w, memo = removed[k].memo };
                if (k == 0) stash.Insert(index, nc);   // 入れ替え(所持品の並びが変わらないように)
                else stash.Add(nc);
            }
            game.Log((c.w.IsTwoHand ? $"両手に{c.w.name}を付けた" : $"{DuelWeapon.SlotName(slot)}に{c.w.name}を付けた")
                     + (removed.Count > 0 ? "(外した武装は所持品へ)" : ""));
            return true;
        }

        /// <summary>所持品を足もとの床に捨てる(真下に床がない・撃墜中・ボスの後は捨てない)。捨てたら true。</summary>
        public bool DropStash(int index)
        {
            if (index < 0 || index >= stash.Count || !CanDrop || !HasFloorBelow(DuelSlot.Back)) return false;
            var c = stash[index];
            stash.RemoveAt(index);
            DuelLoot.Place(c.w, DropPos((int)DuelSlot.Back)).Memo = c.memo;
            game.Log($"所持品の{c.w.name}を床に捨てた");
            return true;
        }

        /// <summary>所持品を分解して、弾か回復にする(付け外しできる間だけ。弾が上限・HP が満タンなら分解しない)。分解したら true。</summary>
        public bool SalvageStash(int index, bool heal)
        {
            if (!CanRearrange || index < 0 || index >= stash.Count || SalvageFull(heal)) return false;
            var c = stash[index];
            stash.RemoveAt(index);
            Salvage(c.w, c.memo.mag, heal);
            return true;
        }

        /// <summary>撃つ状態を空に戻す(弾倉・リロード・間隔・押したことの記憶)。リロード中なら取り置いた弾を返す。</summary>
        void ResetState(int i)
        {
            Refund(resAmmo[i]);
            resAmmo[i] = 0f;
            resN[i] = 0;
            mag[i] = 0;
            uid[i] = 0;
            bloom[i] = 0f;
            reload[i] = cd[i] = cdMax[i] = 0f;
            pressedAt[i] = -10f;
        }

        /// <summary>その状態の弾倉の残り(弾倉のある銃でなければ -1=満タン扱い)。</summary>
        int MagOf(int i) => slots[i] is { IsGun: true, magazine: > 0 } ? Mathf.Clamp(mag[i], 0, slots[i].magazine) : -1;

        /// <summary>その状態が次に撃てる時刻(待ちがなければ 0)。</summary>
        float ReadyOf(int i) => cd[i] > 0f ? Time.time + cd[i] : 0f;

        /// <summary>外すときに覚えさせる撃つ状態(弾倉の残り・主の撃ち方と、右手の両手の銃・砲なら L1 の撃ち方の装填待ち・個体の番号)。</summary>
        Memo MemoOf(int i) => new()
        {
            mag = MagOf(i),
            ready = ReadyOf(i),
            altReady = i == R && TwoHandGun != null ? ReadyOf(AltIndex) : 0f,
            id = uid[i],
        };

        /// <summary>
        /// 付けたばかりの撃つ状態(弾倉は left、-1 なら満タンに)。撃てるまで少なくとも 0.3 秒待つ。
        /// 外したときの装填待ち(ready。時刻)が残っていれば、その残りを待つ(外して付け直して待ちが縮まないように)。
        /// id は個体の番号(Memo.id。外す前の番号を引き継ぐ)。
        /// </summary>
        void ArmState(int i, DuelWeapon w, int left = -1, float ready = 0f, int id = 0)
        {
            ResetState(i);
            if (w == null) return;
            uid[i] = id;
            mag[i] = left >= 0 ? Mathf.Min(left, w.magazine) : w.magazine;
            cd[i] = cdMax[i] = w.IsShooter ? 0.3f : 0f;
            lastFire[i] = -10f;
            cdShown[i] = false;   // 付けた直後の 0.3 秒の待ちは、装填のゲージに出さない(CooldownLeft)
            float rest = ready - Time.time;
            if (w.IsShooter && rest > cd[i])
            {
                // 外したときの装填待ちの続き(ゲージにも出す。撃ってはいないので lastFire は入れない。入れると構え扱いになる)
                cd[i] = rest;
                cdMax[i] = Mathf.Max(rest, w.interval);
                cdShown[i] = true;
            }
        }

        // ───────── ボス戦の前の弾(控える・戻す) ─────────

        /// <summary>控えがあるか・控えた共通の弾・控えた弾倉の残り(個体の番号 Memo.id → 残り。-1 = 満タン)。</summary>
        bool ammoKept;
        float keptAmmo;
        readonly Dictionary<int, int> keptMag = new();

        static bool HasMagazine(DuelWeapon w) => w is { IsGun: true, magazine: > 0 };

        /// <summary>
        /// 今の共通の弾と、弾倉のある銃の個体ごとの弾倉の残りを控える(ボス戦の入口の扉が閉じた瞬間。DuelStage.BeginBossFight)。前の控えは捨てる。
        /// 個体は番号(Memo.id / uid)で覚えるので、その後に付け替えても・外して持っても・床に置いても、同じ個体へ戻せる。
        /// 控えるのは、付けている銃・所持品・床に落ちている銃の全部(床の銃も入れるのは、前の挑戦で大ホールの床に置いた銃を拾って詰め直し、
        /// やられて共通の弾だけ戻ると、詰めた分の弾が増えるため)。この後に手に入れた個体(控えにない番号)は、戻すときに触らない。
        /// リロードの途中の銃は詰め終えた残りで控える(取り置いた弾は共通の弾からもう引いてあるので、合計は今と同じ)。
        /// L1 の溜めの途中なら、取り置いた1回分は共通の弾に入れて控える(溜めをやめた扱い)。
        /// </summary>
        public void KeepAmmo()
        {
            ammoKept = true;
            keptAmmo = Ammo + chargeAmmo;
            keptMag.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                if (!HasMagazine(slots[i]) || (i == L && TwoHanded)) continue;   // 両手の銃の状態は右手の所で持つ
                if (uid[i] == 0) uid[i] = ++nextUid;
                keptMag[uid[i]] = Mathf.Min(slots[i].magazine, MagOf(i) + resN[i]);
            }
            foreach (var c in stash)
            {
                if (!HasMagazine(c.w)) continue;
                if (c.memo.id == 0) c.memo.id = ++nextUid;
                keptMag[c.memo.id] = c.memo.mag;
            }
            foreach (var l in DuelLoot.All)
            {
                if (l == null || !HasMagazine(l.Weapon)) continue;
                var m = l.Memo;
                if (m.id == 0)
                {
                    m.id = ++nextUid;
                    l.Memo = m;
                }
                keptMag[m.id] = m.mag;
            }
        }

        /// <summary>
        /// 控えた弾へ戻す(ボス戦の間にやられて・落ちて再開するとき。DuelStage.Respawn)。控えがなければ何もせず false。控えそのものは変えない
        /// (続けてやられても、毎回同じ結果になる)。
        ///   1. 進行中の L1 の溜め・リロードを片付ける(溜めはやめ、リロードは詰め終える。取り置いた弾は返さない:共通の弾は次で控えた値にする)。
        ///   2. 共通の弾を控えた値にする(上限 ammoMax まで)。
        ///   3. 控えた個体の弾倉の残りを、今どこにあっても(付けている・所持品・床)控えた残りにする。
        ///      ボス戦の間に分解した個体は戻らない(分解で得た弾も 2 で消える)。控えにない個体は触らない。
        ///   4. 付けている銃のうち弾倉が満タンでないものを、共通の弾を使ってリロード済みにする(待ち時間なし。
        ///      作者「装備している武装は、リロード前に入っていれば弾薬を使ってリロード済みの状態にしてください」)。使う弾と、足りないときに
        ///      入るだけ入れる決まりは、ふつうのリロードと同じ(StartReload)。弾を取り合うときは付ける所の番号順
        ///      (右手 → 左手 → 右肩 → 左肩。両手の銃は右手の所)。所持品・床の銃はリロードしない。
        /// </summary>
        public bool RestoreKeptAmmo()
        {
            if (!ammoKept) return false;
            chargeAmmo = 0f;
            charge = 0f;
            for (int i = 0; i < StateCount; i++)
                if (reload[i] > 0f) EndReload(i, StateWeapon(i));
            Ammo = Mathf.Clamp(keptAmmo, 0f, Rules.ammoMax);
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (!HasMagazine(w) || (i == L && TwoHanded)) continue;
                if (keptMag.TryGetValue(uid[i], out int left)) mag[i] = left < 0 ? w.magazine : Mathf.Min(left, w.magazine);
            }
            foreach (var c in stash)
                if (HasMagazine(c.w) && keptMag.TryGetValue(c.memo.id, out int left)) c.memo.mag = left;
            foreach (var l in DuelLoot.All)
            {
                if (l == null || !HasMagazine(l.Weapon) || !keptMag.TryGetValue(l.Memo.id, out int left)) continue;
                var m = l.Memo;
                m.mag = left;
                l.Memo = m;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (!HasMagazine(w) || (i == L && TwoHanded)) continue;
                if (StartReload(i, w, true)) EndReload(i, w);
            }
            return true;
        }

        /// <summary>控えた弾を捨てる(ボスを倒した。この後の再開で古い弾が戻らないように)。</summary>
        public void ForgetKeptAmmo()
        {
            ammoKept = false;
            keptMag.Clear();
        }

        /// <summary>
        /// 落ちている武装をその所に付ける。今の武装は、拾った武装のあった所にそのまま置く(その場で入れ替え。拾い直せる)。
        /// 置いた武装は、すぐ拾えるが拾う案内の優先度を下げる(DuelLoot.LowerPriority。両手持ちで2つ置くときは2つとも)。
        /// 両手持ちを付けるときは(R1/L1 のどちらで決めても)両手に付け、右手・左手の今の武装を両方置く(素の剣も。同じ武装を2つ持っていれば2つとも)。
        /// 両手持ちのまま片手の武装を付けるときは、両手持ちを外して置き、選んだ手に付ける(もう片方の手は空になる)。
        /// </summary>
        void Equip(DuelLoot loot, DuelSlot slot)
        {
            var w = loot.Weapon;
            Vector3 at = loot.transform.position;
            loot.Take();
            bool wasTwo = TwoHanded;
            var removed = Mount(w, loot.Memo, slot, true);
            // 外した武装を置く:1つ目は拾った所、2つ目は自機の側へ少し寄せて(重ならないように)
            for (int k = 0; k < removed.Count; k++)
            {
                var placed = DuelLoot.Place(removed[k].w, k == 0 ? at : Beside(at));
                placed.Memo = removed[k].memo;
                placed.LowerPriority();
            }
            game.Log(w.IsTwoHand ? $"両手に{w.name}({w.RarityName})を付けた"
                     : wasTwo && w.IsHand ? $"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた(両手の{removed[0].w.name}は外した)"
                     : $"{DuelWeapon.SlotName(slot)}に{w.name}({w.RarityName})を付けた");
        }

        readonly List<(DuelWeapon w, Memo memo)> mountRemoved = new();

        /// <summary>
        /// w をその所に付け(撃つ状態は memo:弾倉の残り・装填待ち)、外れた武装と撃つ状態を返す(置き場所は呼ぶ側が決める:床か所持品)。
        /// refillShield:盾を付けたらガードのゲージを満タンにする(床から拾ったとき)。false なら今の値を最大で切るだけ(所持品から付けたとき)。
        /// 両手持ちを付けるときは(R1/L1 のどちらで決めても)両手に付け、右手・左手の今の武装を両方外す(同じ武装を2つ持っていれば2つとも)。
        /// 両手持ちのまま片手の武装を付けるときは、両手持ちを外し、選んだ手に付ける(もう片方の手は空になる)。
        /// 外れた手の剣は振り・押して覚えている攻撃をやめ、盾が絡めばガードを引き直す。返すリストは次に呼ぶまで使える。
        /// </summary>
        List<(DuelWeapon w, Memo memo)> Mount(DuelWeapon w, Memo memo, DuelSlot slot, bool refillShield)
        {
            mountRemoved.Clear();
            int i = (int)slot;
            bool hand = i == R || i == L;
            bool shieldBefore = Hand(0) is { kind: DuelKind.Shield } || Hand(1) is { kind: DuelKind.Shield };
            StopCharge();
            if (hand && (w.IsTwoHand || TwoHanded))
            {
                var oldR = slots[R];
                var oldL = slots[L];
                bool oldTwo = oldR is { IsTwoHand: true };
                Memo memR = MemoOf(R), memL = MemoOf(L);
                slots[R] = slots[L] = null;
                if (w.IsTwoHand) slots[R] = slots[L] = w;
                else slots[i] = w;
                if (oldR != null) mountRemoved.Add((oldR, memR));
                if (oldL != null && !oldTwo) mountRemoved.Add((oldL, memL));
                ArmState(R, slots[R], slots[R] == w ? memo.mag : -1, slots[R] == w ? memo.ready : 0f, slots[R] == w ? memo.id : 0);
                ArmState(L, slots[L] != null && slots[L].IsTwoHand ? null : slots[L], slots[L] == w ? memo.mag : -1, slots[L] == w ? memo.ready : 0f,
                    slots[L] == w ? memo.id : 0);
                ArmState(AltIndex, AltFire, -1, w.IsTwoHand ? memo.altReady : 0f);
                Refresh(DuelSlot.RightHand);
                Refresh(DuelSlot.LeftHand);
                if (oldR is { IsBlade: true }) player.OnSwordDropped(0);
                if (oldL is { IsBlade: true }) player.OnSwordDropped(1);
            }
            else
            {
                var old = slots[i];
                var oldMemo = MemoOf(i);
                slots[i] = w;
                if (old != null) mountRemoved.Add((old, oldMemo));
                ArmState(i, w, memo.mag, memo.ready, memo.id);
                Refresh(slot);
                if (hand && old is { IsBlade: true }) player.OnSwordDropped(i == R ? 0 : 1);
            }
            // 盾を付けた・盾を持っていた(外れたかもしれない):ゲージと構えている盾を引き直す
            if (w.kind == DuelKind.Shield || shieldBefore) player.OnShieldsChanged(refillShield && w.kind == DuelKind.Shield);
            return mountRemoved;
        }

        /// <summary>at から自機の方へ少し寄せた所(2つ目の武装を置く。壁の外に出ないよう、拾えた=見通せる向きへ寄せる)。</summary>
        Vector3 Beside(Vector3 at)
        {
            Vector3 d = player.transform.position - at;
            d.y = 0f;
            float len = d.magnitude;
            return len < 0.5f ? at + Vector3.up * 0.5f : at + d / len * Mathf.Min(4f, len * 0.5f);
        }

        /// <summary>
        /// 全部の武装を失う(ボスの爆発)。付けていた武装は火花を散らして吹き飛び、全部の所を空にしてから右手に素の剣を入れ直す
        /// (左手に拾い直した素の剣も吹き飛ぶ)。失った数を返す(素の剣は数えない)。
        /// </summary>
        public int LoseAll()
        {
            int n = 0;
            var twoHand = TwoHanded ? slots[R] : null;   // 両手持ちは1つとして数える(左手の所は右手と同じ武装)
            for (int i = 0; i < SlotCount; i++)
            {
                var w = slots[i];
                if (w == null) continue;
                bool baseSword = w == DuelWeapon.BaseSword;
                if (baseSword && i == (int)DuelSlot.RightHand) continue;   // 右手の素の剣はそのまま(下で入れ直す)
                if (i == L && w == twoHand)
                {
                    slots[i] = null;
                    continue;
                }
                if (!baseSword) n++;
                slots[i] = null;
                var blade = rig == null || !w.IsBlade ? null : i == (int)DuelSlot.RightHand ? rig.sword : rig.swordL;
                if (blade != null)
                {
                    // 手の剣:機体の剣は素に戻し(左手は隠し)、同じ形の刃を吹き飛ばす
                    var copy = CreateVisual(w, null);
                    copy.SetPositionAndRotation(blade.position, blade.rotation);
                    Fling(copy);
                }
                var v = visuals[i];
                visuals[i] = null;
                if (v != null && blade != null) Destroy(v.gameObject);   // 機体の剣の柄(吹き飛ばす刃の写しに柄も入っている)
                else if (v != null) Fling(v);
            }
            if (rig != null && rig.sword != null && n > 0)
            {
                DuelSpark.Burst(rig.sword.position, Vector3.up, new Color(1f, 0.8f, 0.4f), 24, 1.3f);
                ProtoFx.Blast(rig.sword.position, 3f, new Color(1f, 0.7f, 0.3f), 0.25f);
            }
            slots[(int)DuelSlot.RightHand] = DuelWeapon.BaseSword;
            for (int i = 0; i < SlotCount; i++) Refresh((DuelSlot)i);
            StopCharge();
            for (int i = 0; i < StateCount; i++) ResetState(i);
            if (ChoiceOpen) EndChoice();
            player.OnShieldsChanged(false);
            // 所持品も消し飛び、共通の弾も0に戻る(弧の終わりに全部失う。仮置き)
            n += stash.Count;
            stash.Clear();
            Ammo = 0f;
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
                var blade = rig == null ? null : right ? rig.sword : rig.swordL;
                if (!right && w is { IsTwoHand: true })
                {
                    // 両手持ちの左手:見た目は右手の側でまとめて出す(左手の剣は隠す)
                    if (blade != null) blade.gameObject.SetActive(false);
                    return;
                }
                bool sword = w is { IsBlade: true };
                if (blade != null)
                {
                    // 剣:機体の剣を出し、握りはそのままで長さを届く範囲に、太さを武装の太さに合わせ、レア以上はレア度の色に塗る
                    // (両手の近接は太く・長く、握りの後ろに柄を伸ばして両手で持つ感じを出す)
                    blade.gameObject.SetActive(sword);
                    if (sword)
                    {
                        Vector3 bs = right ? swordBaseScale : swordLBaseScale;
                        float th = Mathf.Max(0.1f, w.thickMul);
                        blade.localScale = new Vector3(bs.x * th, bs.y * w.reachMul, bs.z * th);
                        MaterialPropertyBlock block = null;
                        if (w.rarity >= 1)
                        {
                            block = new MaterialPropertyBlock();
                            block.SetColor("_BaseColor", w.Color);
                        }
                        foreach (var r in blade.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
                        if (w.haft > 0f) visuals[i] = Haft(blade, w.haft * k, 0.35f * k);
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
                    visuals[i].localPosition = w.kind == DuelKind.Shield ? new Vector3(0.75f * side, 0f, 0.2f) * k
                        : w.kind == DuelKind.TwoHandGun ? new Vector3(-0.3f, -0.45f, 1.8f) * k   // 両手の銃・砲:大きく、少し体の内側へ(両手で抱える感じ)
                        : new Vector3(0f, -0.45f, 1.2f) * k;
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

        /// <summary>
        /// 武装の仮の見た目(レア度の色)。剣は細長い刃(長さは届く範囲、太さは武装の太さに合わせ、柄があれば後ろに伸ばす)、盾は板、手の銃は細長い箱。
        /// parent が null なら親なし。
        /// </summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            var root = new GameObject(w.name).transform;
            if (parent != null) root.SetParent(parent, false);
            bool blade = w.IsBlade;
            var prim = GameObject.CreatePrimitive(blade ? PrimitiveType.Cube : w.shape);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            float th = Mathf.Max(0.1f, w.thickMul), len = 4.5f * w.reachMul;
            prim.transform.localScale = blade ? new Vector3(0.4f * th, 1f * th, len) : w.scale;
            prim.transform.localRotation = Quaternion.Euler(blade ? Vector3.zero : w.euler);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(w.Color);
            if (blade && w.haft > 0f)
            {
                var haft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(haft.GetComponent<Collider>());
                haft.transform.SetParent(root, false);
                haft.transform.localScale = new Vector3(0.35f, 0.35f, w.haft);
                haft.transform.localPosition = new Vector3(0f, 0f, -(len + w.haft) * 0.5f);
                haft.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(HaftColor);
            }
            return root;
        }

        static readonly Color HaftColor = new(0.25f, 0.25f, 0.28f);

        /// <summary>
        /// 機体の剣の握りの後ろ(剣の -Y)へ伸びる柄(両手の近接)。剣の拡大に引きずられないよう、世界の大きさから逆算して付ける。
        /// 機体と同じ層に置く(カメラの壁よけ・敵の見通しのレイに当たらないように)。
        /// </summary>
        static Transform Haft(Transform blade, float length, float thick)
        {
            var root = new GameObject("Haft").transform;
            root.SetParent(blade, false);
            var prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(prim.GetComponent<Collider>());
            prim.transform.SetParent(root, false);
            Vector3 ls = blade.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y)), sz = Mathf.Max(0.0001f, Mathf.Abs(ls.z));
            prim.transform.localScale = new Vector3(thick / sx, length / sy, thick / sz);
            prim.transform.localPosition = new Vector3(0f, -length * 0.5f / sy, 0f);
            prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(HaftColor);
            root.gameObject.layer = prim.layer = blade.gameObject.layer;
            return root;
        }
    }
}
