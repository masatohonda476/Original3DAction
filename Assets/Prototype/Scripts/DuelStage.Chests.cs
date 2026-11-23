using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelStage
    {
        const float ChestSpacing = 15f;
        const float ChestSaveClearance = 20f;
        const float ChestSpawnClearance = 8f;
        const float ChestGateClearance = 12f;
        const int ChestPlacementAttempts = 120;
        const float ChestMinFloorNormalY = 0.88f;
        const float ChestPlacementHalfExtent = 4f;
        const float ChestOpenHorizontalRange = 8f;
        const float ChestOpenVerticalRange = 8f;

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
                for (int attempt = 0; remaining > 0 && attempt < ChestPlacementAttempts; attempt++)
                {
                    if (!TryFindChestFloor(area.bounds, floors, out var floor, out var tilt)) continue;
                    var rotation = tilt * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    chests.Add(CreateChest(root, area.name, floor.point + floor.normal * 0.04f, rotation));
                    remaining--;
                }
            }
        }

        bool TryFindChestFloor(Bounds area, HashSet<Collider> floors, out RaycastHit floor, out Quaternion tilt)
        {
            floor = default;
            tilt = Quaternion.identity;
            Vector3 from = new(Random.Range(area.min.x, area.max.x), area.max.y + 1f,
                Random.Range(area.min.z, area.max.z));
            var hits = Physics.RaycastAll(from, Vector3.down, area.size.y + 1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return false;

            // 同じ真下に複数の階がある場合も、候補を見る順番を変える。
            int start = Random.Range(0, hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[(start + i) % hits.Length];
                if (!floors.Contains(hit.collider) || hit.normal.y < ChestMinFloorNormalY) continue;
                var floorTilt = Quaternion.FromToRotation(Vector3.up, hit.normal);
                if (!CanPlaceChest(hit, floorTilt)) continue;
                floor = hit;
                tilt = floorTilt;
                return true;
            }
            return false;
        }

        bool CanPlaceChest(RaycastHit floor, Quaternion tilt)
        {
            Vector3 p = floor.point;
            if (chests.Exists(c => (c.root.position - p).sqrMagnitude < ChestSpacing * ChestSpacing)) return false;
            if (saves.Exists(s => (s.pos - p).sqrMagnitude < ChestSaveClearance * ChestSaveClearance)) return false;
            if (spawns.Exists(s => (s.pos - p).sqrMagnitude < ChestSpawnClearance * ChestSpawnClearance)) return false;
            if ((bossGate.ClosestPoint(p) - p).sqrMagnitude < ChestGateClearance * ChestGateClearance) return false;

            // 箱全体を載せられる広さと、開いたふた・拾得物の上の空間を確かめる。
            Vector3 n = floor.normal;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var corner = p + tilt * new Vector3(x * ChestPlacementHalfExtent, 0.2f, z * ChestPlacementHalfExtent);
                if (!floor.collider.Raycast(new Ray(corner, -n), out _, 0.4f)) return false;
            }
            var halfExtents = new Vector3(ChestPlacementHalfExtent, 3.8f, ChestPlacementHalfExtent);
            return !Physics.CheckBox(p + n * 4f, halfExtents, tilt,
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
            float openRange = ChestOpenHorizontalRange + player.Radius;
            float openRangeSq = openRange * openRange;
            foreach (var chest in chests)
            {
                if (chest.opened)
                {
                    chest.lid.localRotation = Quaternion.RotateTowards(chest.lid.localRotation, OpenLid, 240f * Time.deltaTime);
                    continue;
                }
                Vector3 d = player.transform.position - chest.root.position;
                if (Cleared || Mathf.Abs(d.y) > ChestOpenVerticalRange || d.x * d.x + d.z * d.z > openRangeSq) continue;
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
    }
}
