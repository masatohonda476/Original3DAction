using UnityEngine;

namespace Prototype
{
    public partial class DuelStage
    {
        const float ChestSupplyChance = 0.3f;

        void TryDropSupply(DuelEnemy e)
        {
            if (supplyDropChance < 1f && Random.value >= supplyDropChance) return;
            var supply = RollSupply();
            Vector2 k = Random.insideUnitCircle * 5f;
            DuelLoot.Drop(supply, e.Chest, new Vector3(k.x, 0f, k.y));
        }

        /// <summary>
        /// 倒した敵が武装を落とすか決め(どの敵も dropChance。救済なし)、落とすなら敵の種類のレア度の重みで1つ選んで落とす。
        /// </summary>
        void TryDropWeapon(DuelEnemy e)
        {
            if (arsenal == null || arsenal.Count == 0) return;
            if (Random.value >= dropChance) return;
            var kind = Kind.Grunt;
            foreach (var s in spawns) if (s.enemy == e) { kind = s.kind; break; }
            var rarity = (kind == Kind.Duelist ? duelistRarity : gruntRarity) ?? new DuelRarityWeights();
            var w = PickWeapon(rarity.RollRarity());
            if (w == null) return;
            Vector2 k = Random.insideUnitCircle * 5f;
            DuelLoot.Drop(w, e.Chest, new Vector3(k.x, 0f, k.y));
            if (w.rarity >= 1) game.Log($"{w.RarityName}の武装が落ちた:{w.name}");
        }

        /// <summary>最下層と大ホールの重みの間で、宝箱の高さに応じて武装のレア度を抽選する。</summary>
        (DuelWeapon weapon, DuelSupply? supply) RollChestReward(float chestY)
        {
            if (Random.value < ChestSupplyChance) return (null, RollSupply());
            float heightFraction = Mathf.InverseLerp(LowestFloorY, bossSpawn.pos.y, chestY);
            int rarity = DuelRarityWeights.RollRarity(Mathf.Lerp(80f, 25f, heightFraction),
                Mathf.Lerp(18f, 40f, heightFraction), Mathf.Lerp(2f, 35f, heightFraction));
            var weapon = PickWeapon(rarity);
            return weapon != null ? (weapon, null) : (null, RollSupply());
        }

        static DuelSupply RollSupply() => Random.Range(0, 2) == 0 ? DuelSupply.RepairArmor : DuelSupply.Ammo;

        /// <summary>そのレア度から一つ選ぶ。今付けている武装は出にくくする。</summary>
        DuelWeapon PickWeapon(int rarity)
        {
            if (arsenal == null || arsenal.Count == 0) return null;
            var arms = player != null ? player.Arms : null;
            foreach (int r in new[] { rarity, rarity - 1, rarity + 1, rarity - 2, rarity + 2 })
            {
                float sum = 0f;
                foreach (var w in arsenal) if (w != null && w.rarity == r) sum += WeaponDropWeight(w, arms);
                if (sum <= 0f) continue;
                float pick = Random.value * sum;
                DuelWeapon last = null;
                foreach (var w in arsenal)
                {
                    if (w == null || w.rarity != r) continue;
                    last = w;
                    pick -= WeaponDropWeight(w, arms);
                    if (pick < 0f) return w;
                }
                return last;
            }
            return null;
        }

        float WeaponDropWeight(DuelWeapon w, DuelArms arms) =>
            Mathf.Max(0f, w.dropWeight) * (arms != null && arms.IsEquipped(w) ? sameWeaponWeight : 1f);
    }
}
