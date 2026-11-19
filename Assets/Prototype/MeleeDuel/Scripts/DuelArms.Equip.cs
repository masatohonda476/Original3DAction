using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelArms の付ける・外す・所持品・見た目(partial。本体と全体の説明は DuelArms.cs)
    public partial class DuelArms
    {
        // ───────── 付ける・外す ─────────

        /// <summary>
        /// 十字右:付け先を選ぶ表示にする(手・肩・背中・両手持ちとも同じ流れ)。
        /// 選んでいる間は R1/L1(手)・R2/L2(肩)・✗(背中)で決める(背中は R1〜L2 では付かず、手・肩は ✗ では付かない)。
        /// ✗ は押し始めだけ数える(飛んでいて押し続けていた ✗ では付かない)。両手持ちは R1/L1 のどちらでも両手に付く(R2/L2 では付かない。Equip)。もう一度十字右・武装から離れる・動ける状態(Free・ガード)でなくなると取り消し。
        /// 選ぶ・付けるのは動ける状態のときだけ(振っている最中・回避の間に手の武装が替わって、当たりと見た目がずれないように)。
        /// 拾う案内(選んでいない間)では、十字右で付け先を選ぶ表示、十字下でそのまま所持品へ(重さの上限を超えるなら持てない)、
        /// 十字上で分解を選ぶ表示(十字上で弾・十字下で回復・十字右でやめる。離れる・動ける状態でなくなるでも取り消し)。
        /// 付け先を選ぶ表示で、付けると重さの上限を超える所のボタンを押したら、付けずに知らせる(表示はそのまま)。
        /// </summary>
        void UpdateChoice()
        {
            bool ready = player.Current is DuelPlayer.State.Free or DuelPlayer.State.Guard;
            if (Breaking != null)
            {
                if (Breaking != Nearby || ProtoInput.EquipNow || !ready)
                {
                    Breaking = null;
                    return;
                }
                bool toAmmo = ProtoInput.BreakNow, toHeal = !toAmmo && ProtoInput.StashNow;
                if (!toAmmo && !toHeal) return;
                if (SalvageFull(toHeal)) return;   // 弾が上限・HP が満タン:武装だけ消えないよう、分解しない(選ぶ表示はそのまま)
                var loot = Breaking;
                Breaking = null;
                var w = loot.Weapon;
                loot.Take();
                Salvage(w, toHeal);
                return;
            }
            if (Choosing != null)
            {
                if (Choosing != Nearby || ProtoInput.EquipNow || !ready)
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
            if (ProtoInput.EquipNow) Choosing = Nearby;
            else if (ProtoInput.StashNow) StashLoot(Nearby);
            else if (ProtoInput.BreakNow) Breaking = Nearby;
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

        /// <summary>武装を分解して、共通の弾(上限まで)か HP の回復にする。量は DuelArmsRules(レア度と重さ)。</summary>
        void Salvage(DuelWeapon w, bool heal)
        {
            var r = Rules;
            if (heal)
            {
                float before = player.HP;
                player.Heal(r.BreakHeal(w));
                game.Log($"{w.name}を分解して回復 +{player.HP - before:0}(HP {player.HP:0}/{player.MaxHP:0})");
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(0.45f, 1f, 0.55f), 14, 0.9f);
            }
            else
            {
                float before = Ammo;
                Ammo = Mathf.Min(r.ammoMax, Ammo + r.BreakAmmo(w));
                game.Log($"{w.name}を分解して弾 +{Ammo - before:0}(弾 {Ammo:0}/{r.ammoMax:0})" + (Ammo >= r.ammoMax ? " 上限" : ""));
                DuelSpark.Burst(player.Chest, Vector3.up, new Color(1f, 0.85f, 0.35f), 14, 0.9f);
            }
        }

        /// <summary>分解したときの量(弾・回復)。案内に出す。</summary>
        public int SalvageAmmo(DuelWeapon w) => Rules.BreakAmmo(w);
        public int SalvageHeal(DuelWeapon w) => Rules.BreakHeal(w);

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

        /// <summary>付け先を選ぶ表示の途中なら取り消す(装備の確認を開いたとき)。L1 の溜めもやめる(閉じた後に、離したことになって撃たないように)。</summary>
        public void CancelChoice()
        {
            StopCharge();
            Breaking = null;
            if (Choosing != null) EndChoice();
        }

        /// <summary>先行入力(押してからの受付時間内の射撃)を捨てる。装備の確認を開いたとき(閉じた直後に撃たないように)。</summary>
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
        bool CanDrop => CanRearrange && !(game.stage != null && game.stage.Cleared);

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
            Salvage(Unequip(slot, out _, out _), heal);
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
            Salvage(c.w, heal);
            return true;
        }

        /// <summary>撃つ状態を空に戻す(弾倉・リロード・間隔・押したことの記憶)。リロード中なら取り置いた弾を返す。</summary>
        void ResetState(int i)
        {
            Refund(resAmmo[i]);
            resAmmo[i] = 0f;
            resN[i] = 0;
            mag[i] = 0;
            bloom[i] = 0f;
            reload[i] = cd[i] = cdMax[i] = 0f;
            pressedAt[i] = -10f;
        }

        /// <summary>その状態の弾倉の残り(弾倉のある銃でなければ -1=満タン扱い)。</summary>
        int MagOf(int i) => slots[i] is { IsGun: true, magazine: > 0 } ? Mathf.Clamp(mag[i], 0, slots[i].magazine) : -1;

        /// <summary>その状態が次に撃てる時刻(待ちがなければ 0)。</summary>
        float ReadyOf(int i) => cd[i] > 0f ? Time.time + cd[i] : 0f;

        /// <summary>外すときに覚えさせる撃つ状態(弾倉の残り・主の撃ち方と、右手の両手の銃・砲なら L1 の撃ち方の装填待ち)。</summary>
        Memo MemoOf(int i) => new()
        {
            mag = MagOf(i),
            ready = ReadyOf(i),
            altReady = i == R && TwoHandGun != null ? ReadyOf(AltIndex) : 0f,
        };

        /// <summary>
        /// 付けたばかりの撃つ状態(弾倉は left、-1 なら満タンに)。撃てるまで少なくとも 0.3 秒待つ。
        /// 外したときの装填待ち(ready。時刻)が残っていれば、その残りを待つ(外して付け直して待ちが縮まないように)。
        /// </summary>
        void ArmState(int i, DuelWeapon w, int left = -1, float ready = 0f)
        {
            ResetState(i);
            if (w == null) return;
            mag[i] = left >= 0 ? Mathf.Min(left, w.magazine) : w.magazine;
            cd[i] = cdMax[i] = w.IsShooter ? 0.3f : 0f;
            lastFire[i] = -10f;   // 付けた直後の 0.3 秒の待ちは、装填のゲージに出さない(CooldownLeft)
            float rest = ready - Time.time;
            if (w.IsShooter && rest > cd[i])
            {
                // 外したときの装填待ちの続き(ゲージにも出す)
                cd[i] = rest;
                cdMax[i] = Mathf.Max(rest, w.interval);
                lastFire[i] = Time.time;
            }
        }

        /// <summary>
        /// 落ちている武装をその所に付ける。今の武装は、拾った武装のあった所にそのまま置く(その場で入れ替え。拾い直せる)。
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
            for (int k = 0; k < removed.Count; k++) DuelLoot.Place(removed[k].w, k == 0 ? at : Beside(at)).Memo = removed[k].memo;
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
                ArmState(R, slots[R], slots[R] == w ? memo.mag : -1, slots[R] == w ? memo.ready : 0f);
                ArmState(L, slots[L] != null && slots[L].IsTwoHand ? null : slots[L], slots[L] == w ? memo.mag : -1, slots[L] == w ? memo.ready : 0f);
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
                ArmState(i, w, memo.mag, memo.ready);
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
            if (Choosing != null) EndChoice();
            Breaking = null;
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
