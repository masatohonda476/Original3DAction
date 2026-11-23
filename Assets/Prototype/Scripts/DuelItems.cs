using UnityEngine;

namespace Prototype
{
    public enum DuelItem { RepairArmor, Ammo, Grenade, MagneticAnchor, DecoyBeacon }

    static class DuelItems
    {
        public const float RepairFraction = 0.4f;
        public const int AmmoAmount = 30;

        static readonly (string Name, string Effect, Color Tint, int DropWeight)[] entries =
        {
            ("補修用装甲", $"最大HPの{RepairFraction * 100f:0}%を回復", new Color(0.35f, 1f, 0.65f), 30),
            ("弾薬", $"共通の弾 +{AmmoAmount}", new Color(1f, 0.72f, 0.28f), 30),
            ("手榴弾", "投擲して範囲攻撃", new Color(1f, 0.44f, 0.22f), 15),
            ("磁気アンカー", "地上の敵を減速", new Color(0.29f, 0.85f, 1f), 15),
            ("囮ビーコン", "交戦中の雑魚1体を誘導", new Color(0.91f, 0.56f, 1f), 10),
        };

        public static int Count => entries.Length;
        public static (string Name, string Effect, Color Tint, int DropWeight) Get(DuelItem item) => entries[(int)item];

        public static DuelItem RollDrop()
        {
            int total = 0;
            foreach (var entry in entries) total += entry.DropWeight;
            int roll = Random.Range(0, total);
            for (int i = 0; i < entries.Length - 1; i++)
            {
                roll -= entries[i].DropWeight;
                if (roll < 0) return (DuelItem)i;
            }
            return (DuelItem)(entries.Length - 1);
        }
    }
}
