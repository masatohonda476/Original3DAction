using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelStage
    {
        const float ChestItemChance = 0.3f;
        const float ChestSpacing = 15f;
        const int ChestPlacementAttempts = 120;

        static readonly Color ChestBody = new(0.33f, 0.23f, 0.12f);
        static readonly Color ChestTrim = new(1f, 0.72f, 0.23f);
        static readonly Quaternion OpenLid = Quaternion.Euler(-105f, 0f, 0f);

        sealed class Chest
        {
            public Transform root;
            public Transform lid;
            public Renderer latch;
            public bool opened;
        }

        readonly List<Chest> chests = new();

        void BuildChests(Transform parent, HashSet<Collider> floors)
        {
            var root = new GameObject("TreasureChests").transform;
            root.SetParent(parent, false);
            foreach (var area in places)
            {
                int remaining = Random.Range(0, 3);
                Bounds b = area.bounds;
                for (int i = 0; remaining > 0 && i < ChestPlacementAttempts; i++)
                {
                    Vector3 from = new(Random.Range(b.min.x, b.max.x), b.max.y + 1f, Random.Range(b.min.z, b.max.z));
                    var hits = Physics.RaycastAll(from, Vector3.down, b.size.y + 1f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (hits.Length == 0) continue;
                    int start = Random.Range(0, hits.Length);
                    for (int j = 0; j < hits.Length; j++)
                    {
                        var floor = hits[(start + j) % hits.Length];
                        if (!floors.Contains(floor.collider) || floor.normal.y < 0.88f) continue;
                        var tilt = Quaternion.FromToRotation(Vector3.up, floor.normal);
                        if (!CanPlaceChest(floor, tilt)) continue;
                        var rotation = tilt * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                        chests.Add(CreateChest(root, area.name, floor.point + floor.normal * 0.04f, rotation));
                        remaining--;
                        break;
                    }
                }
            }
        }

        bool CanPlaceChest(RaycastHit floor, Quaternion tilt)
        {
            Vector3 p = floor.point;
            if (chests.Exists(c => (c.root.position - p).sqrMagnitude < ChestSpacing * ChestSpacing)) return false;
            if (saves.Exists(s => (s.pos - p).sqrMagnitude < 20f * 20f)) return false;
            if (spawns.Exists(s => (s.pos - p).sqrMagnitude < 8f * 8f)) return false;
            if ((bossGate.ClosestPoint(p) - p).sqrMagnitude < 12f * 12f) return false;

            // 箱全体を載せられる広さと、開いたふた・拾得物の上の空間を確かめる。
            Vector3 n = floor.normal;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var corner = p + tilt * new Vector3(x * 4f, 0.2f, z * 4f);
                if (!floor.collider.Raycast(new Ray(corner, -n), out _, 0.4f)) return false;
            }
            return !Physics.CheckBox(p + n * 4f, new Vector3(4f, 3.8f, 4f), tilt,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static Chest CreateChest(Transform parent, string area, Vector3 pos, Quaternion rotation)
        {
            var root = new GameObject($"TreasureChest_{area}").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, rotation);
            ChestPart("Body", root, new Vector3(0f, 1.05f, 0f), new Vector3(4.5f, 2.1f, 3f), ProtoFx.Lit(ChestBody));
            ChestPart("Band", root, new Vector3(0f, 1.6f, 0f), new Vector3(4.6f, 0.2f, 3.1f), ProtoFx.Lit(ChestTrim));
            var lid = new GameObject("LidHinge").transform;
            lid.SetParent(root, false);
            lid.localPosition = new Vector3(0f, 2.1f, -1.5f);
            ChestPart("Lid", lid, new Vector3(0f, 0.35f, 1.5f), new Vector3(4.6f, 0.7f, 3.1f), ProtoFx.Lit(ChestTrim));
            var latch = ChestPart("Latch", root, new Vector3(0f, 1.85f, 1.56f), new Vector3(0.72f, 0.65f, 0.2f),
                ProtoFx.Unlit(ChestTrim));
            return new Chest { root = root, lid = lid, latch = latch };
        }

        static Renderer ChestPart(string name, Transform parent, Vector3 localPos, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = size;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        void TickChests()
        {
            float openRange = 8f + player.Radius;
            foreach (var chest in chests)
            {
                if (chest.opened)
                {
                    chest.lid.localRotation = Quaternion.RotateTowards(chest.lid.localRotation, OpenLid, 240f * Time.deltaTime);
                    continue;
                }
                Vector3 d = player.transform.position - chest.root.position;
                if (Cleared || Mathf.Abs(d.y) > 8f || d.x * d.x + d.z * d.z > openRange * openRange) continue;
                if (DuelSenses.ClearSight(player.Chest, chest.root.TransformPoint(new Vector3(0f, 2f, 0f))))
                    OpenChest(chest);
            }
        }

        void OpenChest(Chest chest)
        {
            chest.opened = true;
            chest.latch.enabled = false;
            Vector3 at = chest.root.TransformPoint(new Vector3(0f, 0f, 2.6f)) + Vector3.up * 3f;
            var reward = RollChestReward(chest.root.position.y);
            if (reward.weapon != null)
            {
                DuelLoot.Drop(reward.weapon, at, Vector3.zero);
                game.Log($"宝箱から{reward.weapon.RarityName}の武装：{reward.weapon.name}");
            }
            else if (reward.item is DuelItem item)
            {
                DuelLoot.Drop(item, at, Vector3.zero);
                game.Log($"宝箱からアイテム：{DuelItems.Get(item).Name}");
            }
            ProtoFx.Blast(at, 4f, ChestTrim, 0.25f);
        }

        (DuelWeapon weapon, DuelItem? item) RollChestReward(float y)
        {
            bool isItem = Random.value < ChestItemChance;
            float height = Mathf.InverseLerp(LowestFloorY, bossSpawn.pos.y, y);
            int rarity = DuelRarityWeights.RollRarity(Mathf.Lerp(80f, 25f, height),
                Mathf.Lerp(18f, 40f, height), Mathf.Lerp(2f, 35f, height));
            if (!isItem && PickWeapon(rarity) is DuelWeapon weapon) return (weapon, null);
            var item = rarity switch
            {
                0 => Random.Range(0, 2) == 0 ? DuelItem.RepairArmor : DuelItem.Ammo,
                1 => Random.Range(0, 2) == 0 ? DuelItem.Grenade : DuelItem.MagneticAnchor,
                _ => DuelItem.DecoyBeacon,
            };
            return (null, item);
        }
    }
}
