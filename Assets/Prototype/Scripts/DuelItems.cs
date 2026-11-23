using UnityEngine;

namespace Prototype
{
    public enum DuelItem { RepairArmor, Ammo }

    static class DuelItems
    {
        public const float RepairFraction = 0.4f;
        public const int AmmoAmount = 30;

        static readonly (string Name, string Effect, Color Tint)[] entries =
        {
            ("補修用装甲", $"最大HPの{RepairFraction * 100f:0}%を回復", new Color(0.35f, 1f, 0.65f)),
            ("弾薬", $"共通の弾 +{AmmoAmount}", new Color(1f, 0.72f, 0.28f)),
        };

        public static int Count => entries.Length;
        public static (string Name, string Effect, Color Tint) Get(DuelItem item) => entries[(int)item];
    }
}
