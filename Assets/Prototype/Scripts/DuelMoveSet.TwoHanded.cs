namespace Prototype
{
    // 5 枠版の振りを、本線の R1 連撃・R2 強攻撃に合わせた表。
    public partial class DuelMoveSet
    {
        static DuelSwing Thrust(float windup, float active, float recover, float damage, float stagger,
            float flinch, float reach, float arc, float lunge, float knock, float cost,
            float twist, float lean, float drop, bool heavy = false) => new()
        {
            windup = windup, active = active, recover = recover, damage = damage, stagger = stagger,
            flinch = flinch, reach = reach, arc = arc, lunge = lunge, knock = knock, cost = cost,
            heavy = heavy,
            pose0 = R * 0.5f, pose1 = R * 0.75f, pose2 = R * 0.8f, pose3 = R * 0.6f,
            twist0 = -twist * 0.8f, twist1 = -twist, twist2 = twist * 0.4f,
            lean0 = -lean * 0.3f, lean1 = -lean * 0.4f, lean2 = lean, drop = drop,
        };

        public static (DuelMoveSet moves, DuelSwing heavy) TwoHandSword() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    new() { windup = 0.36f, active = 0.18f, recover = 0.65f, damage = 14f, stagger = 10f, flinch = 0.6f, reach = 11.1f, arc = 95f, lunge = 4f, knock = 7f, cost = 13f,
                            pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.6f), twist0 = -10f, twist1 = -25f, twist2 = 30f, lean0 = -4f, lean1 = -6f, lean2 = 12f, drop = 0.5f },
                    new() { windup = 0.3f, active = 0.18f, recover = 0.65f, damage = 14f, stagger = 10f, flinch = 0.6f, reach = 11.1f, arc = 110f, lunge = 3.5f, knock = 8f, cost = 13f,
                            pose0 = Mid(0.6f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 35f, twist1 = 55f, twist2 = -60f, lean1 = -4f, lean2 = 8f, drop = 0.3f },
                    new() { windup = 0.42f, active = 0.18f, recover = 0.75f, damage = 20f, stagger = 14f, flinch = 0.75f, reach = 11.6f, arc = 80f, lunge = 5f, knock = 14f, cost = 15f,
                            pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -10f, lean1 = -14f, lean2 = 22f, drop = 0.9f },
                },
                dodgeSlash = new() { windup = 0.2f, active = 0.18f, recover = 0.6f, damage = 20f, stagger = 13f, flinch = 0.7f, reach = 11.6f, arc = 120f, lunge = 8f, knock = 12f, cost = 13f,
                    pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -20f, twist1 = -40f, twist2 = 45f, lean2 = 14f, drop = 0.6f },
            },
            new DuelSwing() { windup = 0.3f, active = 0.16f, recover = 0.6f, damage = 22f, stagger = 23f, flinch = 0.7f, reach = 11.6f, arc = 70f, lunge = 14f, knock = 16f, cost = 25f, heavy = true,
                pose0 = R * 0.5f, pose1 = R, pose2 = F, pose3 = Mid(0.6f), twist0 = -20f, twist1 = -35f, twist2 = 40f, lean0 = 6f, lean1 = 10f, lean2 = 20f, drop = 0.5f });

        public static (DuelMoveSet moves, DuelSwing heavy) Spear() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    Thrust(0.24f, 0.1f, 0.45f, 10f, 7f, 0.5f, 12.6f, 35f, 3.5f, 6f, 9f, 25f, 20f, 0.2f),
                    Thrust(0.2f, 0.1f, 0.45f, 10f, 7f, 0.5f, 12.6f, 35f, 3f, 6f, 9f, 20f, 18f, 0.2f),
                    Thrust(0.3f, 0.12f, 0.5f, 14f, 10f, 0.6f, 13.1f, 35f, 6f, 10f, 11f, 30f, 26f, 0.3f),
                },
                dodgeSlash = Thrust(0.12f, 0.1f, 0.45f, 15f, 10f, 0.55f, 13.1f, 35f, 10f, 10f, 9f, 25f, 24f, 0.2f),
            },
            new DuelSwing() { windup = 0.4f, active = 0.24f, recover = 0.7f, damage = 16f, stagger = 19f, flinch = 0.7f, reach = 12.1f, arc = 170f, lunge = 2f, knock = 14f, cost = 21f, heavy = true,
                pose0 = 0.1f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 50f, twist1 = 90f, twist2 = -110f, lean1 = -4f, lean2 = 10f, drop = 0.5f });

        public static (DuelMoveSet moves, DuelSwing heavy) Glaive() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    new() { windup = 0.3f, active = 0.16f, recover = 0.55f, damage = 12f, stagger = 9f, flinch = 0.55f, reach = 11.6f, arc = 110f, lunge = 3f, knock = 6f, cost = 11f,
                            pose0 = R * 0.5f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -15f, twist1 = -35f, twist2 = 45f, lean2 = 8f, drop = 0.3f },
                    new() { windup = 0.26f, active = 0.16f, recover = 0.55f, damage = 12f, stagger = 9f, flinch = 0.55f, reach = 11.6f, arc = 120f, lunge = 3f, knock = 6f, cost = 11f,
                            pose0 = Mid(0.5f), pose1 = E, pose2 = R * 0.9f, pose3 = R * 0.6f, twist0 = 40f, twist1 = 60f, twist2 = -65f, lean2 = 6f, drop = 0.2f },
                    new() { windup = 0.28f, active = 0.16f, recover = 0.55f, damage = 13f, stagger = 10f, flinch = 0.55f, reach = 11.6f, arc = 110f, lunge = 4f, knock = 8f, cost = 11f,
                            pose0 = R * 0.6f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -40f, twist1 = -70f, twist2 = 70f, lean0 = 2f, lean2 = 10f, drop = 0.4f },
                    new() { windup = 0.34f, active = 0.18f, recover = 0.65f, damage = 16f, stagger = 12f, flinch = 0.65f, reach = 12.1f, arc = 150f, lunge = 3f, knock = 12f, cost = 13f,
                            pose0 = Mid(0.5f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 60f, twist1 = 95f, twist2 = -110f, lean1 = -4f, lean2 = 12f, drop = 0.5f },
                },
                dodgeSlash = new() { windup = 0.14f, active = 0.16f, recover = 0.5f, damage = 16f, stagger = 11f, flinch = 0.6f, reach = 12.1f, arc = 140f, lunge = 8f, knock = 10f, cost = 11f,
                    pose0 = R * 0.6f, pose1 = R, pose2 = S, pose3 = Mid(0.5f), twist0 = -30f, twist1 = -60f, twist2 = 70f, lean2 = 10f, drop = 0.4f },
            },
            Thrust(0.26f, 0.1f, 0.5f, 16f, 23f, 0.6f, 12.6f, 30f, 8f, 12f, 26f, 30f, 26f, 0.3f, true));

        public static (DuelMoveSet moves, DuelSwing heavy) Hammer() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    new() { windup = 0.48f, active = 0.2f, recover = 0.75f, damage = 20f, stagger = 13f, flinch = 0.75f, reach = 10.7f, arc = 90f, lunge = 4f, knock = 12f, cost = 16f,
                            pose0 = 0.05f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -10f, twist1 = -30f, twist2 = 35f, lean0 = -6f, lean1 = -10f, lean2 = 18f, drop = 0.8f },
                    new() { windup = 0.42f, active = 0.2f, recover = 0.75f, damage = 20f, stagger = 13f, flinch = 0.75f, reach = 10.7f, arc = 110f, lunge = 3.5f, knock = 12f, cost = 16f,
                            pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = 35f, twist1 = 60f, twist2 = -65f, lean0 = 4f, lean1 = -4f, lean2 = 12f, drop = 0.5f },
                },
                dodgeSlash = new() { windup = 0.26f, active = 0.2f, recover = 0.7f, damage = 22f, stagger = 15f, flinch = 0.8f, reach = 10.7f, arc = 120f, lunge = 8f, knock = 16f, cost = 16f,
                    pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = -20f, twist1 = -40f, twist2 = 45f, lean2 = 16f, drop = 0.7f },
            },
            new DuelSwing() { windup = 0.6f, active = 0.2f, recover = 0.85f, damage = 26f, stagger = 28f, flinch = 0.9f, reach = 10.2f, arc = 180f, lunge = 2f, knock = 26f, cost = 30f, heavy = true,
                pose0 = 0f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist1 = -5f, twist2 = 5f, lean0 = -18f, lean1 = -22f, lean2 = 32f, drop = 1.6f });

        public static (DuelMoveSet moves, DuelSwing heavy) GreatTachi() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    new() { windup = 0.42f, active = 0.22f, recover = 0.7f, damage = 16f, stagger = 11f, flinch = 0.75f, reach = 12.7f, arc = 150f, lunge = 3f, knock = 12f, cost = 16f,
                            pose0 = 0.1f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 40f, twist1 = 85f, twist2 = -95f, lean0 = -4f, lean1 = -8f, lean2 = 12f, drop = 0.7f },
                    new() { windup = 0.38f, active = 0.22f, recover = 0.7f, damage = 16f, stagger = 11f, flinch = 0.75f, reach = 12.7f, arc = 150f, lunge = 3f, knock = 12f, cost = 16f,
                            pose0 = Mid(0.7f), pose1 = E, pose2 = R * 0.85f, pose3 = R * 0.5f, twist0 = -90f, twist1 = -100f, twist2 = 95f, lean2 = 10f, drop = 0.6f },
                    new() { windup = 0.5f, active = 0.24f, recover = 0.8f, damage = 20f, stagger = 14f, flinch = 0.8f, reach = 13.2f, arc = 170f, lunge = 4f, knock = 16f, cost = 18f,
                            pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 60f, twist1 = 110f, twist2 = -130f, lean1 = -6f, lean2 = 14f, drop = 0.9f },
                },
                dodgeSlash = new() { windup = 0.24f, active = 0.26f, recover = 0.7f, damage = 24f, stagger = 15f, flinch = 0.8f, reach = 13.2f, arc = 170f, lunge = 9f, knock = 16f, cost = 16f,
                    pose0 = R * 0.4f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 50f, twist1 = 90f, twist2 = -100f, lean2 = 12f, drop = 0.7f },
            },
            new DuelSwing() { windup = 0.5f, active = 0.3f, recover = 0.8f, damage = 22f, stagger = 27f, flinch = 0.85f, reach = 12.7f, arc = 180f, lunge = 4f, knock = 18f, cost = 34f, heavy = true,
                pose0 = R * 0.3f, pose1 = R, pose2 = F, pose3 = Mid(0.7f), twist0 = 70f, twist1 = 130f, twist2 = -170f, lean2 = 10f, drop = 0.8f });

        public static (DuelMoveSet moves, DuelSwing heavy) Lance() => (
            new DuelMoveSet
            {
                light = new DuelSwing[]
                {
                    Thrust(0.22f, 0.1f, 0.45f, 12f, 8f, 0.55f, 13.6f, 40f, 3.5f, 8f, 10f, 25f, 20f, 0.2f),
                    Thrust(0.2f, 0.1f, 0.45f, 12f, 8f, 0.55f, 13.6f, 40f, 3f, 8f, 10f, 20f, 18f, 0.2f),
                    Thrust(0.28f, 0.12f, 0.5f, 16f, 11f, 0.65f, 14.1f, 40f, 6f, 12f, 12f, 30f, 26f, 0.3f),
                },
                dodgeSlash = Thrust(0.12f, 0.1f, 0.45f, 18f, 12f, 0.6f, 14.1f, 40f, 12f, 12f, 10f, 25f, 26f, 0.2f),
            },
            Thrust(0.34f, 0.14f, 0.7f, 30f, 29f, 0.9f, 14.6f, 45f, 18f, 30f, 34f, 30f, 34f, 0.3f, true));
    }
}
