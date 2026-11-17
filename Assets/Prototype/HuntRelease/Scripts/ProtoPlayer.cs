using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// 自機。操作の作法(移動・向き・ロックオン・回避・連撃・先行入力)は電脳斬禅に合わせ、
    /// 重さは「最高速度が遅い・足音で揺れる・踏み込みでわずかに減速する」で出す(操作の遅れでは出さない)。
    /// 剥ぎ取った部位を装備すると、移動・攻撃のルールそのものが変わる。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ProtoPlayer : MonoBehaviour
    {
        public static ProtoPlayer I;

        [Header("移動(素体)")]
        [Tooltip("スティックを倒し切ったときの速さ(走り)。軽く倒すと遅くなり、walkToRun 未満は歩きの動きになる")]
        public float runSpeed = 16.7f;
        [Tooltip("ブースト中(○/Shiftで入切)の速さの倍率(走りに対して)。16.7×1.5 ≒ 25m/s")]
        public float boostMul = 1.5f;
        [Tooltip("この速さ(m/s)を超えると歩きから走りの動きに切り替わる")]
        public float walkToRun = 9f;
        [Tooltip("走りの1歩で進む距離(Blender側の R_STEP と同じ)")]
        public float runStepLength = 5.57f;
        [Tooltip("走りで足が着く瞬間の速さの落ち込み(歩きより軽い)")]
        [Range(0f, 1f)] public float runSpeedDip = 0.85f;
        [Tooltip("向き直りの速さ(電脳斬禅は12)")]
        public float turnRate = 12f;
        [Tooltip("1歩で進む距離(歩きアニメーションの歩幅。Blender側の STEP_LEN と同じ)。足が滑らないよう、進んだ距離で足を運ぶ")]
        public float stepLength = 3.85f;
        [Tooltip("足が着く瞬間の速さの落ち込み(1で落ちない)")]
        [Range(0f, 1f)] public float stepSpeedDip = 0.7f;
        public float footShake = 0.3f;

        [Header("エネルギー(ブースト・飛行・回避で減り、使っていない間に回復する)")]
        public float maxEnergy = 100f;
        [Tooltip("回復量(毎秒)。使い終わってから recoverDelay 秒後に回復が始まる")]
        public float energyRegen = 40f;
        public float recoverDelay = 0.6f;
        [Tooltip("空になったら、この割合まで回復するまでブースト・飛行できない")]
        [Range(0f, 1f)] public float overheatResume = 0.3f;
        public float boostDrain = 14f;
        public float flyDrain = 26f;
        public float dodgeCost = 18f;

        [Header("飛行(×/Space長押しで上昇、離すと落ちる)")]
        public float flyRiseSpeed = 14f;
        public float flyAccel = 60f;
        public float gravity = 30f;

        [Header("回避(一瞬で加速して、すっと止まる)")]
        public float dodgeDistance = 9f;
        public float dodgeDuration = 0.45f;
        public float dodgeCooldown = 0.1f;
        [Tooltip("回避の先行入力(秒)")]
        public float dodgeBuffer = 0.3f;

        [Header("剣(3段の連撃)")]
        public float swordDamage = 32.5f;   // 26 × 1.25(動きを0.8倍速にした分、重く)
        public float swordReach = 9f;
        public float swordArc = 70f;
        [Tooltip("腕刃のリーチと剣の長さに、腕刃の性能の倍率がどれだけ効くか(1 でそのまま、0.5 で半分)")]
        public float bladeReachScale = 0.5f;
        [Tooltip("斬撃全体(振りかぶり・振り下ろし・戻り)の速さの倍率")]
        public float attackSpeed = 0.96f;   // 1.2 × 0.8
        public float windup = 0.4f;
        [Tooltip("2段目以降の振りかぶりの倍率")]
        public float comboWindupMul = 0.7f;
        public float strikeTime = 0.12f;
        public float recovery = 0.5f;
        [Tooltip("攻撃の先行入力(秒)")]
        public float attackBuffer = 0.6f;
        [Tooltip("次の段へつなげられる猶予(秒)")]
        public float comboWindow = 1.3f;
        public float hitStop = 0.08f;

        [Header("銃(左手の素の武装。L1/右クリック長押し)")]
        [Tooltip("最初から左手に銃を持つか(切ると左手は部位を付けるまで空)")]
        public bool startWithGun = false;
        public float gunDamage = 9f;
        [Tooltip("発射間隔(秒)")]
        public float gunInterval = 0.22f;
        public float gunRange = 80f;

        [Header("反動(パラメータ制のガトリングだけ)")]
        [Tooltip("撃っていない間に、ばらつきが戻る速さ(度/秒)")]
        public float recoilRecover = 4f;
        [Tooltip("ばらつきの上限(度)")]
        public float maxSpread = 10f;

        [Header("射撃・砲撃で部位を削る")]
        [Tooltip("射撃が(自分の側を向いた)部位に当たる確率")]
        [Range(0f, 1f)] public float partHitChance = 0.45f;
        [Tooltip("砲撃の爆風が部位に与えるダメージの倍率")]
        public float partBlastMul = 0.6f;

        [Header("回復アイテム(十字キー上 / H で使う)")]
        public int maxHealItems = 10;
        [Tooltip("最初から持っている数")]
        public int startHealItems = 3;
        [Tooltip("1個で回復する割合(最大HPに対して)")]
        [Range(0f, 1f)] public float healAmount = 0.4f;

        [Header("照準(△ / C 長押し)")]
        [Tooltip("この秒数だけ押し続けると照準が出る(短く押すのは拾う操作)")]
        public float aimHoldDelay = 0.5f;

        [Header("在庫の削除(メニューで△ / C 長押し)")]
        [Tooltip("この秒数だけ押し続けると、選んでいる在庫を完全に削除する")]
        public float deleteHoldTime = 0.6f;

        [Header("ロックオン")]
        [Tooltip("ロックオンできる最大距離(m)")]
        public float lockRange = 150f;
        public float softAimRange = 25f;
        public float softAimAngle = 75f;

        [Header("HP")]
        public float maxHP = 300f;

        [Header("切り替え方式")]
        [Tooltip("ラックに溜めるほど重くなる")]
        public bool rackAddsWeight = true;
        public float rackWeightPerPart = 0.06f;
        public float releaseRadius = 30f;
        public float releaseDamage = 60f;

        public float HP { get; private set; }
        public float Energy { get; private set; }
        public bool Boosting { get; private set; }
        public bool Flying { get; private set; }
        /// <summary>エネルギーを使い切った後の回復待ち(この間はブースト・飛行・回避ができない)。</summary>
        public bool Overheated { get; private set; }
        public int HealItems { get; private set; }
        public bool IsDead { get; private set; }
        /// <summary>実際に動いている速さ(回避・飛行・落下も含む)。速いほど敵の射撃が当たりにくい。</summary>
        public float Speed { get; private set; }
        public float Radius => cc != null ? cc.radius : 2f;
        /// <summary>△で拾える範囲にある武装(HUDの案内用)。</summary>
        public ProtoLoot NearbyLoot { get; private set; }
        /// <summary>付けている武装と在庫の重さの合計(パラメータ制のときだけ意味がある)。在庫は stockWeightRate 倍(半分)で数える。</summary>
        public float TotalWeight => EquippedWeight + StockWeight * (game != null ? game.stockWeightRate : 0.5f);
        public float EquippedWeight
        {
            get { float w = 0f; foreach (var m in parts) w += m.def.weight; return w; }
        }
        public float StockWeight
        {
            get { float w = 0f; foreach (var it in stock) w += it.def.weight; return w; }
        }
        /// <summary>重さによる機動力の倍率(現行の方式では常に1)。走り・ブースト・回避・上昇に掛かる。下限はなく、重いほど遅くなり続ける(0未満にはならない)。</summary>
        public float Mobility => ProtoGame.Arms == ArmsRule.Classic || game == null ? 1f
            : Mathf.Clamp01(1f - TotalWeight * game.weightSlowPerUnit);
        /// <summary>重量オーバー(機動力が下限以下)。装備メニューを閉じられない。</summary>
        public bool Overweight => ProtoGame.Arms != ArmsRule.Classic && game != null && Mobility <= game.overweightMobility + 0.0001f;
        /// <summary>
        /// それを拾うと重量オーバーになるか(在庫に入るので stockWeightRate 倍で数える)。重量オーバーになるものは拾えない。
        /// 解放型のラックは重さに数えないので対象外。
        /// </summary>
        public bool WouldOverweight(PartDefinition def)
        {
            if (def == null || game == null || ProtoGame.Arms == ArmsRule.Classic || ProtoGame.Mode != ProtoMode.Gradual) return false;
            float w = TotalWeight + def.weight * game.stockWeightRate;
            return 1f - w * game.weightSlowPerUnit <= game.overweightMobility + 0.0001f;
        }
        /// <summary>重量オーバーで閉じられなかった時刻(警告を光らせるため。メニュー中は時間が止まることがあるので unscaled)。</summary>
        public float OverweightBlockedAt { get; private set; } = -10f;
        /// <summary>手のガトリングの今のばらつき(度)。</summary>
        public float SpreadAt(Hardpoint at) => at == Hardpoint.LeftHand ? spreadL : spreadR;
        float spreadR, spreadL;
        public float MechHeight => game != null ? game.mechHeight : 9f;
        public Vector3 ChestPosition => transform.position + Vector3.up * MechHeight * 0.6f;
        public ProtoEnemy Target => target != null && !target.Dead ? target : null;
        public bool AutoLock { get; private set; }
        /// <summary>手動照準中(△長押し):カメラを自由に動かせ、機体はカメラの向きを向き、射撃は照準の先へ飛ぶ。</summary>
        public bool Aiming => !IsDead && !MenuOpen && ProtoInput.AimHeld && aimHoldT >= aimHoldDelay;
        float aimHoldT;
        public IReadOnlyList<PartDefinition> Equipped => equipped;
        public IReadOnlyList<PartDefinition> Rack => rack;
        /// <summary>入手して、まだ付けていない部位。</summary>
        public IReadOnlyList<Item> Stock => stock;
        bool menuOpen;
        int menuClosedFrame = -1;
        public bool MenuOpen
        {
            get => menuOpen;
            private set { if (menuOpen && !value) menuClosedFrame = Time.frameCount; menuOpen = value; }
        }
        /// <summary>戦闘の操作を効かなくするか。閉じたフレームも含める(閉じる○がブースト切替として効かないように)。</summary>
        public bool MenuBlocksInput => menuOpen || menuClosedFrame == Time.frameCount;
        public int MenuRow { get; private set; }

        public class Item { public PartDefinition def; public int uses; }
        readonly List<Item> stock = new();

        readonly List<PartDefinition> equipped = new();
        readonly List<PartDefinition> rack = new();
        readonly List<GameObject> rackCrates = new();
        readonly List<Mounted> parts = new();

        /// <summary>装備中の部位1つ分。uses が尽きると外れる(-1 は外れない)。</summary>
        class Mounted { public PartDefinition def; public Transform visual; public Hardpoint at; public int uses; }

        enum Atk { None, Windup, Strike, Recover }

        ProtoGame game;
        CharacterController cc;
        Renderer body;
        MechRig rig;
        MaterialPropertyBlock mpb;
        Transform rackRoot;
        GameObject rifle;
        ProtoEnemy target, aimEnemy;
        Vector3 planarVel, aimDir, dodgeDir, spawnPos;
        float vy, stepPhase, idleT, gunTR, gunTL, cannonCdR, cannonCdL, respawnT, hurtFlash, energyUsedAt = -10f;
        float dodgeT = -1f, dodgeEndAt = -10f, dodgeDur;
        float attackPressedAt = -10f, dodgePressedAt = -10f, lastStrikeAt = -10f;
        int lastStep, gunCycle, comboIndex;
        bool flashing, flickArmed = true;
        Atk atk;
        /// <summary>今振っている手と、次に振る手(先行入力)。</summary>
        Hardpoint atkHand = Hardpoint.RightHand, queuedHand = Hardpoint.RightHand;
        float atkT, curWindup;

        public void Init(ProtoGame g)
        {
            I = this;
            game = g;
            cc = GetComponent<CharacterController>();
            HP = maxHP;
            Energy = maxEnergy;
            spawnPos = transform.position;
            mpb = new MaterialPropertyBlock();
            rig = MechFactory.CreateMech(g, transform, out body);
            float k = g.mechHeight / 9.06f;
            rackRoot = new GameObject("Rack").transform;
            rackRoot.SetParent(transform, false);
            rackRoot.localPosition = new Vector3(0f, 3.2f, -2.4f) * k;
            AutoLock = true;
            if (startWithGun) CreateRifle(k);
            HealItems = Mathf.Min(startHealItems, maxHealItems);
            RefreshHands();
        }

        /// <summary>左手の素の銃(仮の形)。ガトリングを持っている間は隠す。</summary>
        void CreateRifle(float k)
        {
            var parent = game.MountParent(rig, transform, MountSlot.LeftArm, 0, out var lp);
            rifle = new GameObject("銃");
            rifle.transform.SetParent(parent, false);
            rifle.transform.localPosition = lp;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(rifle.transform, false);
            box.transform.localScale = new Vector3(0.45f, 0.8f, 4.2f) * k;
            box.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(new Color(0.2f, 0.22f, 0.25f));
        }

        int Count(PartType t)
        {
            int n = 0;
            foreach (var m in parts) if (m.def.type == t) n++;
            return n;
        }

        int CountAt(Hardpoint at)
        {
            int n = 0;
            foreach (var m in parts) if (m.at == at) n++;
            return n;
        }

        PartDefinition First(PartType t)
        {
            foreach (var m in parts) if (m.def.type == t) return m.def;
            return null;
        }

        /// <summary>その部位を付けられる場所か。</summary>
        public static bool Fits(PartType t, Hardpoint at) => t switch
        {
            PartType.Blade or PartType.Gatling => at == Hardpoint.RightHand || at == Hardpoint.LeftHand,
            PartType.Cannon => at == Hardpoint.RightShoulder || at == Hardpoint.LeftShoulder,
            _ => at == Hardpoint.Back,
        };

        PartDefinition DefAt(Hardpoint at) => parts.Find(x => x.at == at)?.def;

        /// <summary>その手で斬れるか(刃腕を付けている、または素の右手=剣)。</summary>
        bool HandIsBlade(Hardpoint at)
        {
            var d = DefAt(at);
            return d != null ? d.type == PartType.Blade : at == Hardpoint.RightHand;
        }

        /// <summary>その手で撃てるか(ガトリングを付けている、または素の左手=銃の設定)。</summary>
        bool HandIsGun(Hardpoint at)
        {
            var d = DefAt(at);
            return d != null ? d.type == PartType.Gatling : at == Hardpoint.LeftHand && startWithGun;
        }

        int BladeCount(Hardpoint at) => DefAt(at)?.type == PartType.Blade ? 1 : 0;

        /// <summary>手の見た目:右手に銃を持ったら素の剣をしまう。左手に何か持ったら素の銃をしまう。</summary>
        void RefreshHands()
        {
            if (rifle != null) rifle.SetActive(DefAt(Hardpoint.LeftHand) == null);
            if (rig == null) return;
            // 剣:右手は銃を持っていなければ出す。左手は腕刃のときだけ出す。腕刃の手は剣がリーチの倍率だけ長くなる
            if (rig.sword != null)
            {
                rig.sword.gameObject.SetActive(DefAt(Hardpoint.RightHand)?.type != PartType.Gatling);
                rig.sword.localScale = new Vector3(1f, ReachMul(Hardpoint.RightHand), 1f);
            }
            if (rig.swordL != null)
            {
                rig.swordL.gameObject.SetActive(DefAt(Hardpoint.LeftHand)?.type == PartType.Blade);
                rig.swordL.localScale = new Vector3(1f, ReachMul(Hardpoint.LeftHand), 1f);
            }
        }

        /// <summary>その手の剣のリーチの倍率(腕刃なら radius、素の剣なら 1)。見た目の長さも同じ倍率。</summary>
        float ReachMul(Hardpoint at)
        {
            var d = DefAt(at);
            return d != null && d.type == PartType.Blade ? BladeReach(d) : 1f;
        }

        /// <summary>腕刃のリーチ(=剣の長さ)の倍率。性能の倍率に bladeReachScale だけ効く(1 で倍率そのまま、0.5 で半分)。</summary>
        float BladeReach(PartDefinition d) => d == null ? 1f : 1f + (d.multiplier - 1f) * bladeReachScale;

        /// <summary>HUD用:その場所にあるもの(残り回数つき)。</summary>
        public string SlotText(Hardpoint at)
        {
            var m = parts.Find(x => x.at == at);
            if (m == null)
                return at == Hardpoint.RightHand ? "剣(素)" : at == Hardpoint.LeftHand && startWithGun ? "銃(素)" : "―";
            return m.uses < 0 ? m.def.displayName : $"{m.def.displayName}(残{m.uses})";
        }

        /// <summary>その場所に付いているもの(素の武装なら null)。</summary>
        public Item SlotItem(Hardpoint at)
        {
            var m = parts.Find(x => x.at == at);
            return m == null ? null : new Item { def = m.def, uses = m.uses };
        }

        public static string ItemText(Item it) => it.uses < 0 ? it.def.displayName : $"{it.def.displayName}(残{it.uses})";

        /// <summary>その場所に付けられる在庫の数。</summary>
        public int CandidateCount(Hardpoint at) => stock.FindAll(x => Fits(x.def.type, at)).Count;

        // ───────── 入手・装備 ─────────

        public void Acquire(PartDefinition def) => Acquire(def, def.uses > 0 ? def.uses : -1);

        /// <summary>uses:残り回数(-1 は外れない)。捨てたものを拾い直したときは、捨てた時点の回数を引き継ぐ。</summary>
        public void Acquire(PartDefinition def, int uses)
        {
            game.OnHarvest(def);
            if (ProtoGame.Mode == ProtoMode.Gradual)
            {
                AddStock(def, uses);
                game.Log($"入手: {def.displayName}{AcquireNote(def)}  [{ProtoInput.L(ProtoInput.Btn.Menu)}]で装備");
                return;
            }
            rack.Add(def);
            float k = game.mechHeight / 9.06f;
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(crate.GetComponent<Collider>());
            crate.transform.SetParent(rackRoot, false);
            crate.transform.localPosition = new Vector3(((rackCrates.Count % 2) - 0.5f) * 1.4f, (rackCrates.Count / 2) * 1.3f, 0f) * k;
            crate.transform.localScale = Vector3.one * 1.2f * k;
            crate.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(def.GradeColor);
            rackCrates.Add(crate);
        }

        /// <summary>入手の知らせに添える一言。今付けている同じ種類との差(付けていなければ A案は振れたパラメータ)。</summary>
        public string AcquireNote(PartDefinition def)
        {
            if (!def.HasStats) return "";
            // 付けられる場所に空きがあればそれを知らせる。なければ同じ種類を付けている場所と比べる。A案で比べる相手がなければ振れたところ
            var at = CompareSlot(def.type, out var cur);
            if (cur == null)
            {
                string head = at.HasValue ? $"{HardpointName(at.Value)}が空き" : "未装備の種類";
                if (ProtoGame.Arms != ArmsRule.StatsA) return $"({head})";
                string t = PartDefinition.TagsText(def.SwingTags());
                return t.Length > 0 ? $"({head} / {t})" : $"({head})";
            }
            string d = PartDefinition.TagsText(def.DiffTags(cur.def));
            return d.Length > 0 ? $"(今の{HardpointName(at.Value)}と比べて {d})" : "(今のものとほぼ同じ)";
        }

        /// <summary>
        /// 拾う前に比べる相手の場所。付けられる場所に空きがあればそこを優先(cur は null)、
        /// なければ同じ種類を付けている場所(cur はその武装と残り回数)。どちらもなければ null。
        /// </summary>
        public Hardpoint? CompareSlot(PartType t, out Item cur)
        {
            cur = null;
            foreach (var at in MenuSlots)
                if (Fits(t, at) && !parts.Exists(x => x.at == at)) return at;
            var m = parts.Find(x => x.def.type == t && Fits(t, x.at));
            if (m == null) return null;
            cur = new Item { def = m.def, uses = m.uses };
            return m.at;
        }

        /// <summary>足元の武装をその場で付ける場所:空き → 同じ種類を付けている場所 → 付けられる最初の場所。</summary>
        public Hardpoint DirectSlot(PartType t)
        {
            var at = CompareSlot(t, out _);
            if (at.HasValue) return at.Value;
            foreach (var h in MenuSlots) if (Fits(t, h)) return h;
            return Hardpoint.Back;
        }

        /// <summary>その場で装備できる方式か(パラメータ制で、拾ったら在庫へ入る方式のときだけ)。</summary>
        public bool CanEquipNow => ProtoGame.Arms != ArmsRule.Classic && ProtoGame.Mode == ProtoMode.Gradual;

        /// <summary>その場で装備すると重量オーバーになるか(付いていたものは在庫へ戻る)。</summary>
        public bool WouldOverweightEquip(PartDefinition def)
        {
            if (def == null || game == null || !CanEquipNow) return false;
            var old = DefAt(DirectSlot(def.type));
            float w = TotalWeight + def.weight;
            if (old != null) w -= old.weight * (1f - game.stockWeightRate);   // 付いていたものは在庫へ(重さの数え方が変わる)
            return 1f - w * game.weightSlowPerUnit <= game.overweightMobility + 0.0001f;
        }

        /// <summary>足元の武装をその場で装備する。付いていたものは在庫へ戻す。</summary>
        void EquipFromGround(ProtoLoot loot)
        {
            var def = loot.Def;
            var at = DirectSlot(def.type);
            var old = DefAt(at);
            game.OnHarvest(def);
            Equip(new Item { def = def, uses = loot.Uses }, at);
            loot.Take();
            game.Log(old != null ? $"装備: {def.displayName} → {HardpointName(at)}({old.displayName}は在庫へ)" : $"装備: {def.displayName} → {HardpointName(at)}");
        }

        /// <summary>メニューで在庫を削除する長押しの進み具合(0〜1、押していなければ 0)。</summary>
        public float DeleteProgress => deleteTarget != null ? Mathf.Clamp01(deleteHoldT / Mathf.Max(0.01f, deleteHoldTime)) : 0f;
        Item deleteTarget;
        float deleteHoldT;
        bool deleteNeedsRelease;

        /// <summary>そのキャノンの装填の残り(0=撃てる、1=撃った直後)。キャノンでなければ 0。</summary>
        public float CannonReload01(Hardpoint at)
        {
            float cd = at == Hardpoint.RightShoulder ? cannonCdR : at == Hardpoint.LeftShoulder ? cannonCdL : 0f;
            float max = at == Hardpoint.RightShoulder ? cannonMaxR : cannonMaxL;
            return cd > 0f && max > 0f && DefAt(at)?.type == PartType.Cannon ? Mathf.Clamp01(cd / max) : 0f;
        }
        /// <summary>そのキャノンの装填の残り秒数。</summary>
        public float CannonReloadLeft(Hardpoint at) => Mathf.Max(0f, at == Hardpoint.RightShoulder ? cannonCdR : at == Hardpoint.LeftShoulder ? cannonCdL : 0f);
        float cannonMaxR, cannonMaxL;

        /// <summary>メニュー用:その場所に今付いている武装(素の武装なら null)。</summary>
        public PartDefinition MountedDef(Hardpoint at) => DefAt(at);

        void AddStock(PartDefinition def, int uses) => stock.Add(new Item { def = def, uses = uses });

        /// <summary>在庫の部位をその場所へ付ける。付いていたものは在庫へ戻す(残り回数はそのまま)。</summary>
        void Equip(Item it, Hardpoint at)
        {
            Unequip(at);
            stock.Remove(it);
            var def = it.def;
            MountSlot slot; int mountIdx = 0;
            switch (at)
            {
                case Hardpoint.RightHand: slot = MountSlot.RightArm; break;
                case Hardpoint.LeftHand: slot = MountSlot.LeftArm; break;
                case Hardpoint.RightShoulder: slot = MountSlot.Shoulder; break;
                case Hardpoint.LeftShoulder: slot = MountSlot.Shoulder; mountIdx = 1; break;
                default: slot = MountSlot.Back; break;
            }
            var parent = game.MountParent(rig, transform, slot, mountIdx, out var lp);
            var v = MechFactory.CreatePartVisual(def, parent, lp, false);
            if (def.type == PartType.Blade) v.SetActive(false);   // 腕刃は前腕に出さず、手の剣が伸びる見た目で表す
            parts.Add(new Mounted { def = def, visual = v.transform, at = at, uses = it.uses });
            equipped.Add(def);
            RefreshHands();
        }

        /// <summary>その場所の部位を外して在庫へ戻す(素の武装に戻る)。</summary>
        void Unequip(Hardpoint at, bool toStock = true, int insertAt = -1)
        {
            var m = parts.Find(x => x.at == at);
            if (m == null) return;
            parts.Remove(m);
            equipped.Remove(m.def);
            if (m.visual != null) Destroy(m.visual.gameObject);
            if (toStock)
            {
                var it = new Item { def = m.def, uses = m.uses };
                if (insertAt >= 0) stock.Insert(insertAt, it); else stock.Add(it);
            }
            RefreshHands();
        }

        /// <summary>捨てた武装を足元(少し後ろ)へ落とす。拾い直せる。</summary>
        void DropAtFeet(PartDefinition def, int uses)
        {
            Vector3 side = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * -transform.forward;
            ProtoLoot.DropDiscarded(def, uses, ChestPosition, side * 4f);
        }

        /// <summary>その場所の武装を1回使う。尽きたら外れて落ちる(素の武装に戻る)。</summary>
        void Consume(Hardpoint at, int amount = 1)
        {
            Mounted m = parts.Find(x => x.at == at);
            if (m == null || m.uses < 0) return;
            m.uses -= amount;
            if (m.uses > 0) return;
            parts.Remove(m);
            equipped.Remove(m.def);
            BreakFx(m);
            RefreshHands();
            game.Alert($"{HardpointName(at)}の{m.def.displayName}が壊れた");
            if (CandidateCount(at) > 0) game.Log($"[{ProtoInput.L(ProtoInput.Btn.Menu)}]で付け替え");
        }

        public static string HardpointName(Hardpoint at) => at switch
        {
            Hardpoint.RightHand => "右手",
            Hardpoint.LeftHand => "左手",
            Hardpoint.RightShoulder => "右肩",
            Hardpoint.LeftShoulder => "左肩",
            _ => "背中",
        };

        /// <summary>使い切った武装が壊れる演出:その場で爆発し、焦げた色になって落ちる。</summary>
        void BreakFx(Mounted m)
        {
            if (m.visual != null)
            {
                Vector3 pos = m.visual.gameObject.activeInHierarchy ? m.visual.position : ChestPosition;
                ProtoFx.Blast(pos, 7f, new Color(1f, 0.25f, 0.1f), 0.45f);
                ProtoFx.Blast(pos, 4f, new Color(1f, 0.85f, 0.3f), 0.25f);
                for (int i = 0; i < 4; i++)
                    ProtoFx.Blast(pos + Random.insideUnitSphere * 2.5f, 2f, new Color(1f, 0.5f, 0.15f), 0.35f + i * 0.08f);
                foreach (var r in m.visual.GetComponentsInChildren<Renderer>(true))
                {
                    r.SetPropertyBlock(null);   // 品質の色(MPB)を外して焦げた色にする
                    r.sharedMaterial = ProtoFx.Lit(new Color(0.18f, 0.15f, 0.14f));
                }
            }
            Drop(m.visual);
        }

        /// <summary>外れた部位を落とす(しばらくして消える)。</summary>
        void Drop(Transform v)
        {
            if (v == null) return;
            v.gameObject.SetActive(true);   // 隠していた腕刃も、外れるときは落ちる姿を見せる
            v.SetParent(null, true);
            var rb = v.gameObject.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.linearVelocity = (-transform.forward + Vector3.up) * 6f + planarVel;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
            foreach (var mf in v.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<BoxCollider>();
            ProtoFx.Blast(v.position, 2.5f, new Color(1f, 0.6f, 0.2f), 0.25f);
            Destroy(v.gameObject, 4f);
        }

        /// <summary>切り替え方式:ラックに溜めた部位を一気に在庫へ出し、周りを吹き飛ばす。</summary>
        void Release()
        {
            int n = rack.Count;
            foreach (var d in rack) AddStock(d, d.uses > 0 ? d.uses : -1);
            rack.Clear();
            foreach (var c in rackCrates) Destroy(c);
            rackCrates.Clear();

            ProtoCamera.Shake(1.6f);
            ProtoFx.Blast(ChestPosition, releaseRadius, new Color(0.4f, 0.9f, 1f), 0.6f);
            foreach (var e in new List<ProtoEnemy>(game.Alive))
            {
                if (e.Dead) continue;
                Vector3 d = e.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude - e.Radius > releaseRadius) continue;
                e.TakeDamage(releaseDamage);
                ProtoDamageText.Show(BodyPoint(e), releaseDamage);
                e.Knockback(d.normalized * 30f);
            }
            game.OnRelease(n);
        }

        // ───────── 装備メニュー(開いている間は時間が止まる) ─────────

        public static readonly Hardpoint[] MenuSlots =
            { Hardpoint.RightHand, Hardpoint.LeftHand, Hardpoint.RightShoulder, Hardpoint.LeftShoulder, Hardpoint.Back };

        /// <summary>メニューの段:false=場所を選ぶ / true=その場所に付けるものを選ぶ。</summary>
        public bool MenuPicking { get; private set; }
        public int MenuItem { get; private set; }

        /// <summary>選んでいる場所の候補:付けられる在庫(+付いていれば最後に「外す」= null)。</summary>
        public List<Item> MenuCandidates()
        {
            var at = MenuSlots[MenuRow];
            var list = stock.FindAll(x => Fits(x.def.type, at));
            // 性能順:同じ種類でまとめ、品質の高い順、同じ品質なら残り回数の多い順
            list.Sort((a, b) =>
            {
                int c = a.def.type.CompareTo(b.def.type);
                if (c != 0) return c;
                c = b.def.grade.CompareTo(a.def.grade);
                if (c != 0) return c;
                return UsesOrder(b.uses).CompareTo(UsesOrder(a.uses));
            });
            if (parts.Exists(x => x.at == at)) list.Add(null);
            return list;
        }

        static int UsesOrder(int uses) => uses < 0 ? int.MaxValue : uses;   // 回数無制限は最上位

        void OpenMenu()
        {
            MenuOpen = true;
            MenuPicking = false;
        }

        void UpdateMenu()
        {
            if (ProtoInput.Menu) { TryCloseMenu(); return; }
            if (!MenuPicking)
            {
                if (ProtoInput.MenuUp) MenuRow = (MenuRow + MenuSlots.Length - 1) % MenuSlots.Length;
                if (ProtoInput.MenuDown) MenuRow = (MenuRow + 1) % MenuSlots.Length;
                if (ProtoInput.MenuConfirm && MenuCandidates().Count > 0) { MenuPicking = true; MenuItem = 0; }
                else if (ProtoInput.MenuDiscard) Unequip(MenuSlots[MenuRow]);   // 付いているものを在庫へ戻す(捨てない)
                else if (ProtoInput.MenuBack) TryCloseMenu();
                return;
            }
            var cands = MenuCandidates();
            if (cands.Count == 0) { MenuPicking = false; return; }
            MenuItem = Mathf.Clamp(MenuItem, 0, cands.Count - 1);
            if (ProtoInput.MenuUp) MenuItem = (MenuItem + cands.Count - 1) % cands.Count;
            if (ProtoInput.MenuDown) MenuItem = (MenuItem + 1) % cands.Count;

            // △ / C 長押し:選んでいる在庫を完全に削除(足元にも落とさない)。消した後は一度離すまで次を消さない
            var sel = cands[MenuItem];
            bool holding = ProtoInput.MenuDeleteHeld;
            if (!holding) deleteNeedsRelease = false;
            if (holding && !deleteNeedsRelease && sel != null)
            {
                if (deleteTarget != sel) { deleteTarget = sel; deleteHoldT = 0f; }
                deleteHoldT += Time.unscaledDeltaTime;
                if (deleteHoldT >= deleteHoldTime)
                {
                    stock.Remove(sel);
                    game.Log($"削除した: {sel.def.displayName}");
                    deleteTarget = null;
                    deleteHoldT = 0f;
                    deleteNeedsRelease = true;
                    if (MenuCandidates().Count == 0) MenuPicking = false;
                }
                return;   // 長押しの間は他の操作をしない
            }
            deleteTarget = null;
            deleteHoldT = 0f;

            if (ProtoInput.MenuConfirm)
            {
                var at = MenuSlots[MenuRow];
                var pick = cands[MenuItem];
                if (pick == null) Unequip(at); else Equip(pick, at);
                MenuPicking = false;
            }
            else if (ProtoInput.MenuDiscard && cands[MenuItem] != null)
            {
                // 在庫の武装を捨てる(その場で消える)。候補がなくなったら場所の選択へ戻る
                var it = cands[MenuItem];
                stock.Remove(it);
                DropAtFeet(it.def, it.uses);
                game.Log($"捨てた: {it.def.displayName}(足元に落ちている)");
                if (MenuCandidates().Count == 0) MenuPicking = false;
            }
            else if (ProtoInput.MenuBack) MenuPicking = false;
        }

        /// <summary>重量オーバーなら閉じずに警告を光らせる。</summary>
        void TryCloseMenu()
        {
            if (Overweight) { OverweightBlockedAt = Time.unscaledTime; return; }
            MenuOpen = false;
        }

        // ───────── 被弾・死亡 ─────────

        public void TakeDamage(float dmg)
        {
            if (IsDead) return;
            HP -= dmg;
            hurtFlash = 0.15f;
            ProtoCamera.Shake(0.5f);
            if (HP <= 0f) Die();
        }

        void Die()
        {
            IsDead = true;
            HP = 0f;
            respawnT = 2f;
            atk = Atk.None;
            dodgeT = -1f;
            ProtoFx.Blast(ChestPosition, 8f, new Color(1f, 0.3f, 0.2f), 0.5f);
            game.OnPlayerDeath();
        }

        void Respawn()
        {
            // 死は軽く:装備もラックもそのまま、出発地点で復帰
            cc.enabled = false;
            transform.position = spawnPos;
            cc.enabled = true;
            planarVel = Vector3.zero;
            vy = 0f;
            HP = maxHP;
            Energy = maxEnergy;
            Overheated = Boosting = Flying = false;
            IsDead = false;
        }

        // ───────── ロックオン(電脳斬禅の PlayerTargeting と同じ考え方) ─────────

        bool ValidTarget(ProtoEnemy e) => e != null && !e.Dead &&
            Vector3.Distance(e.transform.position, transform.position) <= lockRange + e.Radius;

        /// <summary>画面の中央に近く、近い敵ほど優先。</summary>
        ProtoEnemy PickBest()
        {
            var cam = Camera.main;
            ProtoEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Alive)
            {
                if (!ValidTarget(e)) continue;
                Vector3 v = cam.WorldToViewportPoint(e.transform.position + Vector3.up * MechHeight * 0.6f * e.Scale);
                if (v.z <= 0f) continue;
                float centerOff = Vector2.Distance(new Vector2(v.x, v.y), new Vector2(0.5f, 0.5f));
                float d = Vector3.Distance(e.transform.position, transform.position);
                float score = centerOff * 10f + d * 0.1f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>次の敵へ(+1 右回り / -1 左回り)。</summary>
        void Cycle(int dir)
        {
            if (!AutoLock) { AutoLock = true; target = PickBest(); return; }
            Vector3 baseDir = target != null ? Flat(target.transform.position - transform.position) : transform.forward;
            ProtoEnemy next = null;
            float bestAngle = float.MaxValue;
            foreach (var e in game.Alive)
            {
                if (e == target || !ValidTarget(e)) continue;
                float a = Vector3.SignedAngle(baseDir, Flat(e.transform.position - transform.position), Vector3.up) * (dir >= 0 ? 1f : -1f);
                if (a <= 0f) a += 360f;
                if (a < bestAngle) { bestAngle = a; next = e; }
            }
            if (next != null) target = next;
        }

        void UpdateTargeting()
        {
            if (ProtoInput.LockToggle)
            {
                AutoLock = !AutoLock;
                target = AutoLock ? PickBest() : null;
            }
            if (ProtoInput.SwitchTarget) Cycle(1);
            // ロックオン中は右スティックを左右に弾いて切り替え(OFFの間はカメラ操作)
            if (Target != null && !Aiming)   // 照準中は右スティックをカメラに使う
            {
                float x = ProtoInput.RightStick.x;
                if (flickArmed && Mathf.Abs(x) > 0.75f) { Cycle(x > 0f ? 1 : -1); flickArmed = false; }
                else if (Mathf.Abs(x) < 0.3f) flickArmed = true;
            }
            if (!AutoLock) { target = null; return; }
            if (!ValidTarget(target)) target = PickBest();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }

        // ───────── 更新 ─────────

        void Update()
        {
            if (game == null) return;
            float dt = Time.deltaTime;
            float now = Time.time;

            UpdateFlash(dt);
            aimHoldT = ProtoInput.AimHeld ? aimHoldT + Time.unscaledDeltaTime : 0f;
            if (IsDead)
            {
                MenuOpen = false;
                respawnT -= dt;
                if (respawnT <= 0f) Respawn();
                return;
            }
            bool inMenu = MenuOpen;
            if (inMenu)
            {
                UpdateMenu();
                // 時間を止めるメニューならここまで。止めないなら、操作だけ効かない状態で動き(落下・減速など)を続ける
                if (game.pauseInMenu) return;
            }
            if (ProtoInput.Heal) UseHeal();
            // 足元の武装:パラメータ制では△を押した瞬間に拾う(長押しはそのまま照準になる)
            NearbyLoot = ProtoGame.Arms != ArmsRule.Classic ? ProtoLoot.NearestWeapon(this) : null;
            if (NearbyLoot != null && ProtoInput.PickUp)
            {
                if (WouldOverweight(NearbyLoot.Def))
                {
                    if (Time.unscaledTime - OverweightBlockedAt > 1f)
                        game.Log($"重量オーバーになるので拾えない  [{ProtoInput.L(ProtoInput.Btn.Menu)}]で在庫を捨てて軽くする");
                    OverweightBlockedAt = Time.unscaledTime;
                }
                else { NearbyLoot.PickUp(this); NearbyLoot = null; }
            }
            else if (NearbyLoot != null && CanEquipNow && ProtoInput.EquipNow)
            {
                if (WouldOverweightEquip(NearbyLoot.Def))
                {
                    if (Time.unscaledTime - OverweightBlockedAt > 1f)
                        game.Log($"重量オーバーになるので装備できない  [{ProtoInput.L(ProtoInput.Btn.Menu)}]で在庫を捨てて軽くする");
                    OverweightBlockedAt = Time.unscaledTime;
                }
                else { EquipFromGround(NearbyLoot); NearbyLoot = null; }
            }
            if (!inMenu && ProtoInput.Menu)
            {
                if (ProtoGame.Mode == ProtoMode.Burst && rack.Count > 0) Release();
                OpenMenu();
                return;
            }

            int thr = Count(PartType.Thruster);
            UpdateTargeting();

            // 移動の基準:ロックオン中は相手の方向、なければカメラの向き
            Vector2 mv = ProtoInput.Move;
            Vector3 fwd = Target != null && !Aiming ? Flat(Target.transform.position - transform.position) : Flat(Camera.main.transform.forward);
            if (fwd == Vector3.zero) fwd = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 worldMove = Vector3.ClampMagnitude(fwd * mv.y + right * mv.x, 1f);

            // R1/L1:その手が剣なら斬る(押した瞬間)。銃なら下の射撃処理で押している間撃つ
            if (ProtoInput.Attack && HandIsBlade(Hardpoint.RightHand)) { attackPressedAt = now; queuedHand = Hardpoint.RightHand; }
            if (ProtoInput.LeftHandPressed && HandIsBlade(Hardpoint.LeftHand)) { attackPressedAt = now; queuedHand = Hardpoint.LeftHand; }
            if (ProtoInput.Dodge) dodgePressedAt = now;

            // 回避:振りかぶりは85%まで、振り抜いた後はいつでも取り消せる。先行入力あり
            bool dodging = dodgeT >= 0f;
            if (ProtoInput.BoostToggle) Boosting = !Boosting && !Overheated;
            bool canDodge = !dodging && now - dodgeEndAt >= dodgeCooldown && !Overheated &&
                            (atk == Atk.None || atk == Atk.Recover || (atk == Atk.Windup && atkT < curWindup * 0.85f));
            if (now - dodgePressedAt <= dodgeBuffer && canDodge)
            {
                dodgePressedAt = -10f;
                dodgeDistMul = (Boosting ? ThrusterMul() : 1f) * Mobility;   // 回避の始めの性能で最後まで動く(途中で外れても距離は変わらない)
                dodgeDir = worldMove.sqrMagnitude > 0.01f ? worldMove.normalized : -transform.forward;
                dodgeDur = dodgeDuration;
                dodgeT = 0f;
                atk = Atk.None;
                dodging = true;
                UseEnergy(dodgeCost);
                ProtoCamera.Shake(thr > 0 ? 0.35f : 0.25f);
            }

            // 攻撃:先行入力。振り抜いた直後から次の段を受け付ける。回避の後半なら回避斬り
            float spd = AttackSpeedMul(BladeCount(atk == Atk.None || atk == Atk.Recover ? queuedHand : atkHand), thr);
            bool canAttack = (atk == Atk.None && (!dodging || dodgeT > dodgeDur * 0.4f)) ||
                             (atk == Atk.Recover && atkT >= 0.12f);
            if (now - attackPressedAt <= attackBuffer && canAttack)
            {
                attackPressedAt = -10f;
                atkHand = queuedHand;
                comboIndex = now - lastStrikeAt <= comboWindow && comboIndex < 3 ? comboIndex + 1 : 1;
                atk = Atk.Windup;
                atkT = 0f;
                curWindup = windup * spd * (comboIndex > 1 || dodging ? comboWindupMul : 1f);
                if (dodging) { dodgeT = -1f; dodgeEndAt = now; dodging = false; }
                aimEnemy = Target != null ? Target : SoftAim(worldMove);
                aimDir = aimEnemy != null ? Flat(aimEnemy.transform.position - transform.position)
                       : worldMove.sqrMagnitude > 0.01f ? worldMove.normalized : transform.forward;
            }

            // 速さ
            // スラスターはこのフレームの途中(回避など)で使い切って外れることがあるので、ここで取り直す
            // スラスターは歩きには効かない(ブースト・上昇・回避にだけ、性能の倍率がそのまま乗る)
            float speedMul = Boosting ? ThrusterMul() : 1f;
            // 攻撃中・回避中は動かない(回避は別に消費する)ので、スティックを倒していても減らさない
            bool boostMoving = Boosting && worldMove.sqrMagnitude > 0.04f && atk == Atk.None && !dodging;
            float maxSpd = runSpeed * speedMul * (Boosting ? boostMul : 1f) * Mobility;
            if (boostMoving) UseEnergy(boostDrain * dt);
            if (ProtoGame.Mode == ProtoMode.Burst && rackAddsWeight)
                maxSpd *= Mathf.Max(0.35f, 1f - rackWeightPerPart * rack.Count);

            // 足が着くたびに踏み込みでわずかに減速する。足は進んだ距離に合わせて運ぶ(滑らない)
            // 歩き/走りの切り替え(行ったり来たりしないよう少し幅を持たせる)
            float sp = planarVel.magnitude;
            if (running ? sp < walkToRun - 0.7f : sp > walkToRun + 0.7f) running = !running;
            if (sp > 0.5f) stepPhase += sp * dt / (running ? runStepLength : stepLength);
            if (cc.isGrounded && !Boosting)
            {
                float frac = stepPhase - Mathf.Floor(stepPhase);
                maxSpd *= Mathf.Lerp(running ? runSpeedDip : stepSpeedDip, 1f, Mathf.Sin(frac * Mathf.PI));
            }
            int stepIdx = Mathf.FloorToInt(stepPhase);
            if (stepIdx != lastStep)
            {
                lastStep = stepIdx;
                if (!Boosting) ProtoCamera.Shake(footShake * Mathf.Clamp01(planarVel.magnitude / runSpeed));
            }

            // 移動:スティックの入力がそのまま速度になる(加減速はごく短く)
            Vector3 vel;
            if (dodging)
            {
                dodgeT += dt;
                float t = Mathf.Clamp01(dodgeT / dodgeDur);
                float dist = dodgeDistance * dodgeDistMul;
                vel = dodgeDir * (3f * dist / dodgeDur) * (1f - t) * (1f - t);
                if (dodgeT >= dodgeDur) { dodgeT = -1f; dodgeEndAt = now; }
                planarVel = Vector3.zero;
            }
            else if (atk != Atk.None)
            {
                // 攻撃中は前へ進まない(踏み込みはモーションの中だけ)
                planarVel = Vector3.zero;
                vel = Vector3.zero;
            }
            else
            {
                planarVel = Vector3.MoveTowards(planarVel, worldMove * maxSpd, 80f * dt);
                vel = planarVel;
            }

            // 飛行:長押しの間は上昇し続ける(上限の高さで止まる)。離すか、エネルギーが尽きると落ちる
            // 上昇の上限は「真下の足場から」の高さ(段差の上ならそこから測る)
            float groundY = ProtoStage.GroundHeight(transform.position);
            aboveGround = transform.position.y - groundY;
            Flying = ProtoInput.FlyHeld && !Overheated && atk == Atk.None;
            if (Flying)
            {
                UseEnergy(flyDrain * dt);
                // スラスターがあれば速く上がる(品質が良いほど速い)
                // 上昇にはスラスターの性能の半分だけ効く(倍率2.6なら 1 + 1.6/2 = 1.8倍)
                float riseMul = Boosting ? 1f + (ThrusterMul() - 1f) * 0.5f : 1f;
                riseMul *= Mobility;
                float rise = flyRiseSpeed * riseMul;   // 高さの上限はなし(エネルギーが尽きるまで上がれる)
                vy = Mathf.MoveTowards(vy, rise, flyAccel * riseMul * dt);
            }
            else
            {
                if (cc.isGrounded && vy < 0f) vy = -2f;
                vy -= gravity * dt;
            }
            cc.Move((vel + Vector3.up * vy) * dt);
            Speed = cc.velocity.magnitude;
            UpdateEnergy(dt, now);
            ClampToArena();

            // 向き
            Vector3 face = Vector3.zero;
            float turn = turnRate;
            if (atk == Atk.Windup)
            {
                if (aimEnemy != null && !aimEnemy.Dead) aimDir = Flat(aimEnemy.transform.position - transform.position);
                else if (worldMove.sqrMagnitude > 0.04f) aimDir = worldMove.normalized;   // 狙いがなければスティックで向きを直せる
                face = aimDir;
                turn = 14f;
            }
            else if (atk == Atk.None)
            {
                if (Aiming) { face = Flat(Camera.main.transform.forward); turn = 18f; }   // 照準中はカメラの向きへ
                else if (Target != null) face = Flat(Target.transform.position - transform.position);
                else if (dodging) face = dodgeDir;
                else if (worldMove.sqrMagnitude > 0.01f) face = worldMove;
            }
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-turn * dt));

            UpdateAttack(dt, spd);

            // 射撃:手に銃があれば押している間撃つ / R2 右肩 / L2 左肩
            gunTR -= dt;
            gunTL -= dt;
            spreadR = Mathf.MoveTowards(spreadR, 0f, recoilRecover * dt);
            spreadL = Mathf.MoveTowards(spreadL, 0f, recoilRecover * dt);
            if (ProtoInput.RightHandHeld && HandIsGun(Hardpoint.RightHand)) FireGun(Hardpoint.RightHand, ref gunTR);
            if (ProtoInput.LeftHandHeld && HandIsGun(Hardpoint.LeftHand)) FireGun(Hardpoint.LeftHand, ref gunTL);
            cannonCdR -= dt;
            cannonCdL -= dt;
            if (ProtoInput.RightShoulderHeld && cannonCdR <= 0f) FireCannon(Hardpoint.RightShoulder, ref cannonCdR);
            if (ProtoInput.LeftShoulderHeld && cannonCdL <= 0f) FireCannon(Hardpoint.LeftShoulder, ref cannonCdL);

            Animate(dt, thr, dodging);
        }

        float backWear;

        /// <summary>背中のスラスターを消耗させる(端数は貯めておき、1たまるごとに1回分減る)。</summary>
        void WearThruster(float amount)
        {
            if (Count(PartType.Thruster) == 0) { backWear = 0f; return; }
            backWear += amount;
            int n = Mathf.FloorToInt(backWear);
            if (n <= 0) return;
            backWear -= n;
            Consume(Hardpoint.Back, n);
        }

        void UseEnergy(float amount)
        {
            if (Boosting) WearThruster(amount);   // スラスターはブースト中だけ働き、そのときだけ消耗する
            if (Boosting)
            {
                var thruster = First(PartType.Thruster);
                if (thruster != null) amount *= thruster.drain;   // 燃費(パラメータ制。現行は1)
            }
            Energy -= amount;
            energyUsedAt = Time.time;
            if (Energy > 0f) return;
            Energy = 0f;
            Overheated = true;
            Boosting = false;
            game.Log("エネルギー切れ");
        }

        void UpdateEnergy(float dt, float now)
        {
            if (now - energyUsedAt >= recoverDelay)
                Energy = Mathf.Min(maxEnergy, Energy + energyRegen * dt);
            if (Overheated && Energy >= maxEnergy * overheatResume) Overheated = false;
        }

        float AttackSpeedMul(int blades, int thr)
        {
            // 返すのは「かかる時間の倍率」。スラスターは斬撃の速さに効かない
            // 腕刃は速さではなくリーチに効く(SwordHit / ReachMul)
            return 1f / Mathf.Max(0.1f, attackSpeed);
        }

        float aboveGround, dodgeDistMul = 1f;
        bool running;

        /// <summary>背中のスラスターの性能の倍率(付いていなければ 1)。ブースト・上昇・回避距離に掛かる。</summary>
        float ThrusterMul()
        {
            var d = First(PartType.Thruster);
            return d != null && Boosting ? d.multiplier : 1f;   // ブースト中だけ効く
        }

        /// <summary>回復アイテムを拾う。持てる数を超えるなら拾わない(false)。</summary>
        public bool TryAddHeal()
        {
            if (HealItems >= maxHealItems) return false;
            HealItems++;
            game.Log($"回復アイテムを拾った({HealItems}/{maxHealItems})  [{ProtoInput.L(ProtoInput.Btn.Heal)}]で使う");
            return true;
        }

        void UseHeal()
        {
            if (HealItems <= 0 || HP >= maxHP) return;
            HealItems--;
            HP = Mathf.Min(maxHP, HP + maxHP * healAmount);
            ProtoFx.Blast(ChestPosition, 6f, new Color(0.3f, 1f, 0.5f), 0.4f);
            game.Log($"回復(残り{HealItems})");
        }

        /// <summary>撃墜・落下からの復帰地点(ステージでは区画に入るたびに更新される)。</summary>
        public void SetCheckpoint(Vector3 p) => spawnPos = p;

        void ClampToArena()
        {
            if (ProtoStage.I != null)
            {
                // ステージは壁で囲ってあるので、万一落ちたときだけ復帰地点へ戻す
                if (transform.position.y < spawnPos.y - 40f)   // 復帰地点より40m下へ落ちたら(断崖の橋など)
                {
                    cc.enabled = false;
                    transform.position = spawnPos;
                    cc.enabled = true;
                    vy = 0f;
                    TakeDamage(maxHP * 0.1f);
                    game.Log("落下 ― 復帰地点へ戻った");
                }
                return;
            }
            Vector3 pos = transform.position;
            float lim = game.arenaHalfSize - 10f;
            if (Mathf.Abs(pos.x) <= lim && Mathf.Abs(pos.z) <= lim) return;
            cc.enabled = false;
            transform.position = new Vector3(Mathf.Clamp(pos.x, -lim, lim), pos.y, Mathf.Clamp(pos.z, -lim, lim));
            cc.enabled = true;
        }

        void UpdateFlash(float dt)
        {
            if (hurtFlash > 0f)
            {
                hurtFlash -= dt;
                mpb.SetColor("_BaseColor", new Color(1f, 0.35f, 0.3f));
                body.SetPropertyBlock(mpb);
                flashing = true;
            }
            else if (flashing)
            {
                body.SetPropertyBlock(null);
                flashing = false;
            }
            if (body) body.enabled = !IsDead;
        }

        // ───────── アニメーション ─────────

        void Animate(float dt, int thr, bool dodging)
        {
            if (rig == null) return;
            string atkClip = atkHand == Hardpoint.LeftHand && rig.Has(MechRig.AttackL) ? MechRig.AttackL : MechRig.Attack;
            switch (atk)
            {
                case Atk.Windup:
                    rig.Pose(atkClip, MechRig.AttackRaised * Mathf.Clamp01(atkT / curWindup), 25f);
                    return;
                case Atk.Strike:
                    rig.Pose(atkClip, Mathf.Lerp(MechRig.AttackRaised, MechRig.AttackStruck, Mathf.Clamp01(atkT / (strikeTime / attackSpeed))), 25f);
                    return;
                case Atk.Recover:
                    float rc = recovery * AttackSpeedMul(BladeCount(atkHand), thr);
                    rig.Pose(atkClip, Mathf.Lerp(MechRig.AttackStruck, MechRig.AttackEnd, Mathf.Clamp01(atkT / rc)), 25f);
                    return;
            }
            bool gliding = (Boosting && planarVel.magnitude > runSpeed * 1.1f) || (!cc.isGrounded && aboveGround > 1.5f);
            if (dodging || gliding) rig.Pose(MechRig.Dash, 0f, 12f);
            else if (planarVel.magnitude > 0.5f)
            {
                // 1周期(2歩)= stepPhase 2。歩きと走りは同じ位相で切り替えるので足がずれない
                string clip = running && rig.Has(MechRig.Run) ? MechRig.Run : MechRig.Walk;
                rig.Pose(clip, stepPhase * 0.5f * rig.Length(clip), 10f);
            }
            else
            {
                idleT += dt;
                rig.Pose(MechRig.Idle, idleT, 6f);
            }
        }

        // ───────── 剣 ─────────

        void UpdateAttack(float dt, float spd)
        {
            switch (atk)
            {
                case Atk.Windup:
                    atkT += dt;
                    if (atkT >= curWindup)
                    {
                        atk = Atk.Strike; atkT = 0f;
                        var blade = DefAt(atkHand);
                        if (blade != null && blade.type != PartType.Blade) blade = null;
                        SwordHit(blade);
                        lastStrikeAt = Time.time;
                        if (blade != null) Consume(atkHand);
                    }
                    break;
                case Atk.Strike:
                    atkT += dt;
                    if (atkT >= strikeTime / attackSpeed) { atk = Atk.Recover; atkT = 0f; }
                    break;
                case Atk.Recover:
                    atkT += dt;
                    if (atkT >= recovery * spd) atk = Atk.None;
                    break;
            }
        }

        /// <summary>ロックオンしていないときの自動の狙い:スティックの方向を優先し、なければ正面の近い敵。</summary>
        ProtoEnemy SoftAim(Vector3 stick)
        {
            Vector3 baseDir = stick.sqrMagnitude > 0.04f ? stick.normalized : transform.forward;
            ProtoEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Alive)
            {
                if (e.Dead) continue;
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude - e.Radius;
                float a = Vector3.Angle(baseDir, to);
                if (d > softAimRange || a > softAimAngle) continue;
                float score = d + a * 0.1f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>
        /// 素体は一番近い一体だけを斬る。刃腕があると間合いと威力が増え、扇の中の全員を薙ぎ払う。
        /// 部位は「自分から見て胴体より手前にある部位」だけ狙える(背中の部位は背後に回らないと斬れない)。
        /// </summary>
        void SwordHit(PartDefinition bladeDef)
        {
            int blades = bladeDef != null ? 1 : 0;
            float reach = swordReach * BladeReach(bladeDef);
            float dmg = swordDamage * (blades > 0 ? 1f + (bladeDef.multiplier - 1f) * blades : 1f);
            if (comboIndex == 3) dmg *= 1.3f;   // 3段目は重い
            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward;

            var targets = new List<(ProtoEnemy e, float d)>();
            foreach (var e in game.Alive)
            {
                if (e.Dead) continue;
                Vector3 to = e.transform.position - pos;
                if (Mathf.Abs(to.y) > MechHeight * Mathf.Max(1f, e.Scale)) continue;   // 段の上下は斬れない
                to.y = 0f;
                float d = to.magnitude - e.Radius;
                if (d > reach) continue;
                if (d > 0.5f && Vector3.Angle(fwd, to) > swordArc) continue;
                targets.Add((e, d));
            }
            if (targets.Count == 0)
            {
                ProtoCamera.Shake(0.2f);
                return;
            }
            targets.Sort((a, b) => a.d.CompareTo(b.d));
            int n = blades > 0 ? targets.Count : 1;

            for (int i = 0; i < n; i++)
            {
                var e = targets[i].e;
                Vector3 to = e.transform.position - pos;
                to.y = 0f;
                float centerDist = to.magnitude;

                EnemyPart bestPart = null;
                float bestD = float.MaxValue;
                foreach (var part in e.Parts)
                {
                    if (part.Severed || part.Broken) continue;   // 壊した部位はもう狙わず、本体を斬る
                    Vector3 pp = part.transform.position - pos;
                    pp.y = 0f;
                    float pd = pp.magnitude;
                    if (pd > reach + e.Radius) continue;
                    if (Vector3.Angle(fwd, pp) > swordArc * 0.7f) continue;
                    if (pd > centerDist + 0.8f * e.Scale) continue;
                    if (pd < bestD) { bestD = pd; bestPart = part; }
                }

                Vector3 hitPoint;
                if (bestPart != null)
                {
                    bestPart.TakeHit(dmg);
                    e.TakeDamage(dmg * 0.4f);
                    hitPoint = bestPart.transform.position;
                    ProtoDamageText.Show(hitPoint, dmg, true);
                }
                else
                {
                    e.TakeDamage(dmg);
                    hitPoint = BodyPoint(e);
                    ProtoDamageText.Show(hitPoint, dmg);
                }
                e.Knockback(to.normalized * (blades > 0 ? 14f : 6f));
                ProtoFx.Blast(hitPoint, 2.5f, Color.white, 0.15f);
            }

            game.TimeWarp(0.05f, blades > 0 ? hitStop * 0.6f : hitStop);
            ProtoCamera.Shake(blades > 0 ? 0.5f : 0.9f);
        }

        // ───────── 射撃武装 ─────────

        /// <summary>射撃の向き:ロックオン中は相手、なければカメラの向き。</summary>
        Vector3 AimForward()
        {
            if (Target != null) return Flat(Target.transform.position - transform.position);
            Vector3 f = Flat(Camera.main.transform.forward);
            return f != Vector3.zero ? f : transform.forward;
        }

        ProtoEnemy FindTarget(float range, float cone)
        {
            if (Target != null && Vector3.Distance(Target.transform.position, transform.position) <= range) return Target;
            Vector3 f = AimForward();
            ProtoEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in game.Alive)
            {
                if (e.Dead) continue;
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > range || Vector3.Angle(f, to) > cone) continue;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// 射撃が敵に当たったときの処理。自分の側を向いている部位に partHitChance の確率で当たり、
        /// 部位を削る(本体には一部だけ通る)。外れたら本体へ。当たった点を返す(弾道の表示用)。
        /// </summary>
        Vector3 RangedHit(ProtoEnemy t, float dmg, Vector3 from)
        {
            Vector3 center = t.transform.position + Vector3.up * MechHeight * 0.55f * t.Scale;
            float centerDist = Vector3.Distance(Flat3(from), Flat3(t.transform.position));
            if (Random.value < partHitChance)
            {
                var front = new List<EnemyPart>();
                foreach (var part in t.Parts)
                {
                    if (part.Severed || part.Broken) continue;
                    if (Vector3.Distance(Flat3(from), Flat3(part.transform.position)) > centerDist + 0.8f * t.Scale) continue;
                    front.Add(part);
                }
                if (front.Count > 0)
                {
                    var hit = front[Random.Range(0, front.Count)];
                    hit.TakeHit(dmg);
                    t.TakeDamage(dmg * 0.4f);
                    ProtoDamageText.Show(hit.transform.position, dmg, true);
                    return hit.transform.position;
                }
            }
            t.TakeDamage(dmg);
            Vector3 at = center + Random.insideUnitSphere * 1.5f;
            ProtoDamageText.Show(at, dmg);
            return at;
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        /// <summary>敵の本体の真ん中あたり(ダメージの数字などを出す所)。</summary>
        Vector3 BodyPoint(ProtoEnemy e) => e.transform.position + Vector3.up * MechHeight * 0.55f * e.Scale;

        /// <summary>手の銃を撃つ(ガトリング、または素の銃)。狙える敵がいなければ正面へ撃つ(当たらない)。</summary>
        void FireGun(Hardpoint at, ref float cd)
        {
            if (cd > 0f) return;
            var def = DefAt(at);
            if (def != null && def.type != PartType.Gatling) return;
            float range = def != null ? def.range : gunRange;
            cd = def != null ? def.interval : gunInterval;

            var m = parts.Find(x => x.at == at);
            Transform muzzle = m != null ? m.visual : rifle != null ? rifle.transform : transform;
            Color c = def != null ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.9f, 1f);
            float dmg = def != null ? def.damage : gunDamage;
            // 反動(パラメータ制のガトリングだけ):撃つたびにばらつきが広がり、外れやすくなる
            float spread = 0f;
            if (def != null && def.HasStats)
            {
                if (at == Hardpoint.LeftHand) spread = spreadL = Mathf.Min(maxSpread, spreadL + def.recoil);
                else spread = spreadR = Mathf.Min(maxSpread, spreadR + def.recoil);
            }
            var t = !Aiming && Target != null && Vector3.Distance(Target.transform.position, transform.position) <= range ? Target : null;
            if (t != null)
            {
                // ロックオン中:相手へ(自分の側を向いた部位に当たることがある)。間を地形がさえぎっていれば壁で止まる
                // ばらつきが大きいほど外れやすい(外れた弾はぶれた向きへ飛び、ほかの物に当たることはある)
                Vector3 from = muzzle.position + AimForward() * 2f;
                Vector3 center = t.transform.position + Vector3.up * MechHeight * 0.55f * t.Scale;
                bool miss = spread > 0f && Random.value < spread / (spread + 6f);
                Vector3 aim = miss
                    ? ShotRay(from, Jitter((center - from).normalized, spread), range, dmg)
                    : ProtoStage.Blocked(from, center)
                    ? ShotRay(from, (center - from).normalized, Vector3.Distance(from, center), dmg)
                    : RangedHit(t, dmg, from);
                ProtoFx.Tracer(from, aim, c, 0.3f);
            }
            else if (!Aiming && Target != null)
            {
                // ロックオン中だが射程の外:相手の方へ撃つ(射程の端で消える。手前に何かあればそこに当たる)
                Vector3 from = muzzle.position + AimForward() * 2f;
                Vector3 center = Target.transform.position + Vector3.up * MechHeight * 0.55f * Target.Scale;
                Vector3 end = ShotRay(from, Jitter((center - from).normalized, spread), range, dmg);
                ProtoFx.Tracer(from, end, c, 0.3f);
            }
            else
            {
                // ロックオンなし:機体の向いている方へ、高さはカメラの狙いに合わせて真っすぐ撃つ
                // 狙いは機体の中心から引いた線で決め、銃口からその点へ撃つ(銃が腕の横にあっても正面に集まる)
                Vector3 from = muzzle.position + Flat(transform.forward) * 2f;
                Vector3 goal = FreeAimPoint(range);
                Vector3 dir = Jitter((goal - from).normalized, spread);
                Vector3 end = ShotRay(from, dir, Vector3.Distance(from, goal) + 2f, dmg);
                ProtoFx.Tracer(from, end, c, 0.3f);
            }
            if (def != null) Consume(at);
        }

        /// <summary>向きを最大 deg 度の範囲でランダムにぶらす(0ならそのまま)。</summary>
        static Vector3 Jitter(Vector3 dir, float deg)
        {
            if (deg <= 0f) return dir;
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            Vector3 tilted = Quaternion.AngleAxis(Random.Range(0f, deg), perp.normalized) * dir;
            return Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * tilted;
        }

        /// <summary>肩のキャノンを一斉に撃つ(その肩に付いている門数ぶん)。</summary>
        void FireCannon(Hardpoint at, ref float cd)
        {
            var list = parts.FindAll(x => x.at == at);
            if (list.Count == 0) return;
            var def = list[0].def;
            cd = def.interval;
            if (at == Hardpoint.RightShoulder) cannonMaxR = cd; else cannonMaxL = cd;
            if (Target == null || Aiming)
            {
                // ロックオンなし・照準中:機体の向き+カメラの高さで狙った先(地形・敵に当たる所、なければ射程の端)へ撃ち込む
                foreach (var m in list)
                {
                    Vector3 from = m.visual.position;
                    Vector3 point = FreeAimPoint(def.range);
                    StartCoroutine(Shell(from, point, def));
                }
                WearCannons(list);
                return;
            }
            var targets = new List<ProtoEnemy>();
            targets.Add(Target);
            Vector3 f = AimForward();
            var others = new List<ProtoEnemy>();
            foreach (var e in game.Alive)
            {
                if (e.Dead || e == Target) continue;
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude <= def.range && Vector3.Angle(f, to) < 60f) others.Add(e);
            }
            others.Sort((a, b) =>
                (a.transform.position - transform.position).sqrMagnitude.CompareTo((b.transform.position - transform.position).sqrMagnitude));
            targets.AddRange(others);

            for (int i = 0; i < list.Count; i++)
            {
                Vector3 point = i < targets.Count ? targets[i].transform.position : transform.position + f * def.range * 0.6f;
                Vector3 from = list[i].visual.position;
                // 間を地形がさえぎっていれば、壁に当たった所で爆発する
                Vector3 aimAt = point + Vector3.up * 2f;
                if (ProtoStage.Blocked(from, aimAt))
                    point = AimRayPoint(from, (aimAt - from).normalized, Vector3.Distance(from, aimAt));
                StartCoroutine(Shell(from, point, def));
            }
            WearCannons(list);
        }

        /// <summary>撃ったキャノンの使用回数を減らし、尽きたものは外す。</summary>
        void WearCannons(List<Mounted> list)
        {
            foreach (var m in list)
            {
                if (m.uses < 0) continue;
                m.uses--;
                if (m.uses > 0) continue;
                // 一斉に撃って同時に尽きたものはまとめて外す
                parts.Remove(m);
                equipped.Remove(m.def);
                BreakFx(m);
                game.Alert($"{HardpointName(m.at)}の{m.def.displayName}が壊れた");
            }
        }

        // ───────── ロックオンなしの狙い ─────────

        /// <summary>
        /// 撃つ向き:水平は機体の正面、上下はカメラの上下の角度に合わせる。
        /// カメラはふだん少し見下ろしている(ProtoCamera.DefaultPitch)ので、その角度を「水平」とし、そこからカメラを上げた分だけ上を、下げた分だけ下を撃つ。
        /// </summary>
        public Vector3 FreeAimDir(Vector3 from, float range)
        {
            Vector3 fwd = Flat(transform.forward);
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            float camDown;
            if (ProtoCamera.I != null) camDown = ProtoCamera.I.AimPitch;   // 壁よけでカメラが上がった分は入れない
            else
            {
                Vector3 camF = Camera.main.transform.forward;
                camDown = Mathf.Asin(Mathf.Clamp(-camF.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            float shotDown = Mathf.Clamp(camDown - ProtoCamera.DefaultPitch, -60f, 60f);
            float r = shotDown * Mathf.Deg2Rad;
            return (fwd * Mathf.Cos(r) + Vector3.down * Mathf.Sin(r)).normalized;
        }

        /// <summary>from から dir へ飛ばして最初に当たる点(地形・敵の本体・部位)。当たらなければ射程の端。</summary>
        public Vector3 AimRayPoint(Vector3 from, Vector3 dir, float range)
        {
            Vector3 end = from + dir * range;
            float best = range;
            foreach (var h in Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Collide))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                bool solid = ProtoStage.IsStage(h.collider) || h.collider.GetComponentInParent<ProtoEnemy>() != null;
                if (!solid || h.distance >= best) continue;
                best = h.distance; end = h.point;
            }
            return end;
        }

        /// <summary>弾をまっすぐ飛ばし、最初に当たったもの(部位・敵の本体・地形)に当てる。止まった点を返す。</summary>
        Vector3 ShotRay(Vector3 from, Vector3 dir, float range, float dmg)
        {
            var hits = Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (ProtoStage.IsStage(h.collider)) return h.point;
                var part = h.collider.GetComponentInParent<EnemyPart>();
                if (part != null && part.Owner != null && !part.Owner.Dead && !part.Severed && !part.Broken)
                {
                    part.TakeHit(dmg);
                    part.Owner.TakeDamage(dmg * 0.4f);
                    ProtoDamageText.Show(h.point, dmg, true);
                    return h.point;
                }
                var e = h.collider.GetComponentInParent<ProtoEnemy>();
                if (e != null && !e.Dead)
                {
                    e.TakeDamage(dmg);
                    ProtoDamageText.Show(h.point, dmg);
                    return h.point;
                }
            }
            return from + dir * range;
        }

        /// <summary>ロックオンなしの狙いの点:機体の中心から、機体の向き+カメラの上下の角度で引いた線が最初に当たる所。</summary>
        public Vector3 FreeAimPoint(float range)
        {
            Vector3 from = ChestPosition;
            return AimRayPoint(from, FreeAimDir(from, range), range);
        }

        /// <summary>照準の目安(HUD用)。</summary>
        public Vector3 AimPreview() => FreeAimPoint(gunRange);

        IEnumerator Shell(Vector3 from, Vector3 point, PartDefinition def)
        {
            ProtoFx.Tracer(from, point + Vector3.up * 3f, new Color(0.7f, 1f, 0.4f), 0.8f, 0.12f);
            yield return new WaitForSeconds(0.25f);
            ProtoFx.Blast(point + Vector3.up * 2f, def.radius, new Color(0.8f, 1f, 0.3f), 0.35f);
            ProtoCamera.Shake(0.7f);
            foreach (var e in new List<ProtoEnemy>(game.Alive))
            {
                if (e.Dead) continue;
                Vector3 d = e.transform.position - point;
                float dy = point.y - e.transform.position.y;
                d.y = 0f;
                if (d.magnitude - e.Radius > def.radius) continue;
                if (dy < -def.radius || dy > MechHeight * e.Scale + def.radius) continue;   // 上下に離れていれば当たらない
                e.TakeDamage(def.damage);
                ProtoDamageText.Show(BodyPoint(e), def.damage);
                e.Knockback((d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward) * 20f);
                // 爆風に入った部位も削れる(自分の側を向いていない部位にも当たる)
                foreach (var part in e.Parts)
                {
                    if (part.Severed || part.Broken) continue;
                    if (Vector3.Distance(part.transform.position, point) > def.radius + 2f) continue;
                    part.TakeHit(def.damage * partBlastMul);
                    ProtoDamageText.Show(part.transform.position, def.damage * partBlastMul, true);
                }
            }
        }
    }
}
