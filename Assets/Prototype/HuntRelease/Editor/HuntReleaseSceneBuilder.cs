using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HuntProto.EditorTools
{
    /// <summary>
    /// プロトタイプのシーンを一から組み立てる。
    /// 機体FBXを1つのメッシュに焼き込み(前向き=+Z・足元=原点に自動補正)、部位定義・地面・カメラを用意する。
    /// Blenderで機体を更新して書き出し直したら、メニューから再実行するだけで反映される。
    /// </summary>
    public static class HuntReleaseSceneBuilder
    {
        const string Root = "Assets/Prototype/HuntRelease";
        const string Gen = Root + "/Generated";
        const string PartsDir = Root + "/Parts";
        const string MechFbx = "Assets/Prototype/Models/Mech1v3_TPose.fbx";
        const string SwordFbx = "Assets/Prototype/Models/Sword_Chokuto_R.fbx";
        const string RigFbx = "Assets/Prototype/Models/Mech1v3_Rig.fbx";
        public const string ScenePath = Root + "/HuntRelease.unity";

        [MenuItem("Prototype/HuntRelease/シーンを組み立てる")]
        public static void BuildMenu() => Build(false);

        [MenuItem("Prototype/HuntRelease/シーンを組み立てる(機体の前後を反転)")]
        public static void BuildFlippedMenu() => Build(true);

        public static void Build(bool flipForward)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Gen);
            EnsureFolder(PartsDir);

            var mechMesh = BakeMech(MechFbx, Gen + "/MechCombined.asset", flipForward, out var mechMats, out float height);
            var swordMesh = BakeSword(SwordFbx, Gen + "/SwordCombined.asset", out var swordMats);
            var parts = EnsureParts();
            var rigPrefab = BuildRigPrefab(swordMesh, swordMats, height / 9.06f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            cam.farClipPlane = 1500f;
            cam.nearClipPlane = 0.5f;
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0f, 15f, -30f);
            cam.gameObject.AddComponent<ProtoCamera>();

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null) sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 650f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.7f);

            const float half = 200f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(half * 2f / 10f, 1f, half * 2f / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = LitMaterial(Gen + "/Ground.mat", new Color(0.36f, 0.37f, 0.38f));

            BuildHorizon(half);

            var gameGo = new GameObject("ProtoGame");
            var game = gameGo.AddComponent<ProtoGame>();
            game.mechPrefab = rigPrefab;
            game.mechMesh = mechMesh;
            game.mechMaterials = mechMats;
            game.mechHeight = height;
            game.swordMesh = swordMesh;
            game.swordMaterials = swordMats;
            game.partPool = parts;
            game.ground = ground.GetComponent<Renderer>();
            game.arenaHalfSize = half;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HuntRelease] シーンを組み立てました: {ScenePath}  機体の高さ {height:F2}m  前後反転 {flipForward}");
        }

        // ───────── 機体の焼き込み ─────────

        static Mesh BakeMech(string fbxPath, string outPath, bool flip, out Material[] materials, out float height)
        {
            var combined = CombineFbx(fbxPath, out materials);
            var verts = combined.vertices;
            var normals = combined.normals;

            // Tポーズの腕が伸びている水平軸を左右、もう一方を前後とみなす
            var b = combined.bounds;
            bool armsAlongX = b.size.x >= b.size.z;
            Vector3 fwdAxis = armsAlongX ? Vector3.forward : Vector3.right;

            // つま先は前に出ているので、足元の頂点の重心が前寄りになる
            float footY = b.min.y + b.size.y * 0.06f;
            double footSum = 0, allSum = 0;
            int footN = 0;
            foreach (var v in verts)
            {
                float a = Vector3.Dot(v, fwdAxis);
                allSum += a;
                if (v.y <= footY) { footSum += a; footN++; }
            }
            float sign = footN > 0 && footSum / footN < allSum / verts.Length ? -1f : 1f;
            if (flip) sign = -sign;
            Vector3 forward = fwdAxis * sign;
            Quaternion rot = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.forward, Vector3.up), Vector3.up);

            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = rot * verts[i];
                if (i < normals.Length) normals[i] = rot * normals[i];
            }
            Bounds nb = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) nb.Encapsulate(v);
            Vector3 shift = new Vector3(-nb.center.x, -nb.min.y, -nb.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] += shift;

            combined.vertices = verts;
            if (normals.Length == verts.Length) combined.normals = normals;
            combined.RecalculateBounds();
            combined.RecalculateTangents();
            height = combined.bounds.size.y;
            return SaveMesh(combined, outPath);
        }

        static Mesh BakeSword(string fbxPath, string outPath, out Material[] materials)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath) == null)
            {
                materials = null;
                return null;
            }
            var combined = CombineFbx(fbxPath, out materials);
            var b = combined.bounds;
            Vector3 longAxis = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right
                             : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
            Quaternion rot = Quaternion.FromToRotation(longAxis, Vector3.up);
            var verts = combined.vertices;
            var normals = combined.normals;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = rot * verts[i];
                if (i < normals.Length) normals[i] = rot * normals[i];
            }
            Bounds nb = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) nb.Encapsulate(v);
            Vector3 shift = new Vector3(-nb.center.x, -nb.min.y, -nb.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] += shift;
            combined.vertices = verts;
            if (normals.Length == verts.Length) combined.normals = normals;
            combined.RecalculateBounds();
            return SaveMesh(combined, outPath);
        }

        /// <summary>FBX内の全メッシュを、マテリアルごとのサブメッシュを持つ1つのメッシュにまとめる。</summary>
        static Mesh CombineFbx(string fbxPath, out Material[] materials)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (src == null) throw new FileNotFoundException("FBXが見つかりません: " + fbxPath);
            var inst = Object.Instantiate(src);
            try
            {
                // ルートの回転は Blender(Z上) → Unity(Y上) の軸変換なので残す。位置だけ原点へ
                inst.transform.position = Vector3.zero;
                Matrix4x4 rootInv = Matrix4x4.identity;
                var fallback = LitMaterial(Gen + "/MechFallback.mat", new Color(0.7f, 0.72f, 0.75f));

                var groups = new Dictionary<Material, List<CombineInstance>>();
                var order = new List<Material>();
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mesh = mf.sharedMesh;
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        var m = i < mats.Length && mats[i] != null ? mats[i] : fallback;
                        if (!groups.TryGetValue(m, out var list)) { groups[m] = list = new List<CombineInstance>(); order.Add(m); }
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = rootInv * mr.localToWorldMatrix });
                    }
                }

                var subs = new List<Mesh>();
                foreach (var m in order)
                {
                    var sub = new Mesh { indexFormat = IndexFormat.UInt32 };
                    sub.CombineMeshes(groups[m].ToArray(), true, true);
                    subs.Add(sub);
                }
                var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(subs.Select(s => new CombineInstance { mesh = s, transform = Matrix4x4.identity }).ToArray(), false, false);
                foreach (var s in subs) Object.DestroyImmediate(s);
                materials = order.ToArray();
                return combined;
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            // 上書きコピーだと描画用のデータが古いまま残ることがあるので、作り直す
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ───────── リグ付き機体のプレハブ ─────────

        static void ConfigureRigImporter()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(RigFbx);
            if (imp == null) return;
            imp.animationType = ModelImporterAnimationType.Legacy;
            imp.importAnimation = true;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in imp.defaultClipAnimations)
            {
                string n = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                c.name = n;
                c.loopTime = n == "Idle" || n == "Walk" || n == "Run";
                c.wrapMode = c.loopTime ? WrapMode.Loop : WrapMode.ClampForever;
                clips.Add(c);
            }
            imp.clipAnimations = clips.ToArray();
            imp.SaveAndReimport();
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>握り点(ローカル -Y = 刃の向き、X = 刃筋)と、その子の剣のメッシュを作る。</summary>
        static Transform MakeSword(string name, Transform parent, Vector3 grip, Vector3 blade, Vector3 edge, float gripToTip, Mesh mesh, Material[] mats)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.SetPositionAndRotation(grip, Quaternion.LookRotation(Vector3.Cross(edge, -blade), -blade));
            var sword = new GameObject("Sword");
            sword.transform.SetParent(pivot, false);
            sword.transform.localPosition = new Vector3(0f, -gripToTip, 0f);   // メッシュの原点(切っ先)を握りから刃の向きへ
            sword.AddComponent<MeshFilter>().sharedMesh = mesh;
            sword.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return pivot;
        }

        static Transform Anchor(string name, Transform parent, Vector3 pos, Quaternion rot)
        {
            var a = new GameObject(name).transform;
            a.SetParent(parent, true);
            a.SetPositionAndRotation(pos, rot);
            return a;
        }

        /// <summary>
        /// Blenderで作ったリグ付き機体からプレハブを作る。待機ポーズの先頭で、
        /// 前向き(+Z)・接地を合わせ、部位の取付位置をボーンの子として置き、右手に剣を持たせる。
        /// </summary>
        static GameObject BuildRigPrefab(Mesh swordMesh, Material[] swordMats, float k)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RigFbx) == null) return null;
            ConfigureRigImporter();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RigFbx);

            var root = new GameObject("MechRigRoot");
            var inst = Object.Instantiate(model, root.transform, false);
            inst.name = "Model";

            var anim = inst.GetComponent<Animation>();
            if (anim == null) anim = inst.AddComponent<Animation>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(RigFbx))
                if (o is AnimationClip clip && !clip.name.StartsWith("__") && anim.GetClip(clip.name) == null)
                    anim.AddClip(clip, clip.name);
            anim.playAutomatically = false;
            var idle = anim.GetClip("Idle");
            if (idle != null) idle.SampleAnimation(inst, 0f);

            // 前向き:右の股関節 - 左の股関節 = 右方向 → 前 = 右 × 上
            Transform hipL = FindDeep(inst.transform, "L_股関節前後_Y");
            Transform hipR = FindDeep(inst.transform, "R_股関節前後_Y");
            Vector3 right = hipR.position - hipL.position;
            right.y = 0f;
            Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
            inst.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), Vector3.up) * inst.transform.rotation;

            // 接地と中心合わせ
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.updateWhenOffscreen = true;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            float minY = float.MaxValue;
            var m = smr.transform.localToWorldMatrix;
            foreach (var v in baked.vertices) minY = Mathf.Min(minY, m.MultiplyPoint3x4(v).y);
            Object.DestroyImmediate(baked);
            Transform pelvis = FindDeep(inst.transform, "骨盤");
            inst.transform.position += new Vector3(-pelvis.position.x, -minY, -pelvis.position.z);

            Transform elbowR = FindDeep(inst.transform, "R_肘曲げ_Y"), handR = FindDeep(inst.transform, "R手_全体");
            Transform elbowL = FindDeep(inst.transform, "L_肘曲げ_Y"), handL = FindDeep(inst.transform, "L手_全体");
            Transform chest = FindDeep(inst.transform, "胸部_腰ひねり_Z");
            Transform weapon = FindDeep(inst.transform, "R_武器");

            var rig = root.AddComponent<MechRig>();
            rig.anim = anim;
            rig.body = smr;
            rig.mountRightArm = Anchor("Mount_RightArm", elbowR, Vector3.Lerp(elbowR.position, handR.position, 0.5f),
                Quaternion.LookRotation(handR.position - elbowR.position, Vector3.up));
            rig.mountLeftArm = Anchor("Mount_LeftArm", elbowL, Vector3.Lerp(elbowL.position, handL.position, 0.5f),
                Quaternion.LookRotation(handL.position - elbowL.position, Vector3.up));
            rig.mountBack = Anchor("Mount_Back", chest, chest.position + new Vector3(0f, -0.3f, -1.7f) * k, Quaternion.identity);
            rig.mountShoulderR = Anchor("Mount_ShoulderR", chest, chest.position + new Vector3(1.6f, 2.0f, -0.2f) * k, Quaternion.identity);
            rig.mountShoulderL = Anchor("Mount_ShoulderL", chest, chest.position + new Vector3(-1.6f, 2.0f, -0.2f) * k, Quaternion.identity);

            // 剣:握り(R_武器)に「握り点」を置き、その子に剣のメッシュを付ける。
            // 握り点を刃の向きに伸ばすと、握りの位置はそのままで剣が長くなる(腕刃のリーチ延長の見た目)。
            // 剣のメッシュは切っ先が y=0、柄頭が y=全長 の向き。Blender で握り(R_武器)・切っ先(R_剣先)・刃筋(R_剣刃)をボーンにしてある
            Transform tip = FindDeep(inst.transform, "R_剣先"), edge = FindDeep(inst.transform, "R_剣刃");
            if (weapon != null && tip != null && edge != null && swordMesh != null)
            {
                const float GripFromTip = 5.15f;   // 切っ先から握りまで(鍔のすぐ下)
                float len = swordMesh.bounds.size.y;
                Vector3 blade = (tip.position - weapon.position).normalized;
                Vector3 e = Vector3.ProjectOnPlane(edge.position - weapon.position, blade).normalized;
                rig.sword = MakeSword("SwordGrip", weapon, weapon.position, blade, e, GripFromTip * (len / 6.65f), swordMesh, swordMats);

                // 左手用:右の剣を機体の左右で鏡写しにして左手に持たせる(左手に腕刃を付けたときだけ出す)
                Vector3 M(Vector3 v) => new Vector3(-v.x, v.y, v.z);
                Vector3 rootPos = root.transform.position;
                Vector3 gripL = rootPos + M(weapon.position - rootPos);
                rig.swordL = MakeSword("SwordGripL", handL, gripL, M(blade), M(e), GripFromTip * (len / 6.65f), swordMesh, swordMats);
                rig.swordL.gameObject.SetActive(false);
            }
            else
            {
                var sword = new GameObject("Sword");
                sword.transform.SetParent(weapon != null ? weapon : handR, false);
                if (swordMesh != null)
                {
                    sword.AddComponent<MeshFilter>().sharedMesh = swordMesh;
                    sword.AddComponent<MeshRenderer>().sharedMaterials = swordMats;
                }
                Vector3 fore = (handR.position - elbowR.position).normalized;
                Vector3 dir = (fore + Vector3.up * 0.8f).normalized;
                sword.transform.SetPositionAndRotation(handR.position, Quaternion.FromToRotation(Vector3.up, dir));
                rig.sword = sword.transform;
            }

            string path = Gen + "/MechRig.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ───────── 部位定義 ─────────

        static PartDefinition[] EnsureParts()
        {
            return new[]
            {
                Part("Part_Gatling", p =>
                {
                    p.type = PartType.Gatling; p.displayName = "ガトリング腕"; p.slot = MountSlot.RightArm;
                    p.placeholderShape = PrimitiveType.Cylinder; p.placeholderScale = new Vector3(0.9f, 1.8f, 0.9f);
                    p.placeholderEuler = new Vector3(90f, 0f, 0f); p.color = new Color(0.25f, 0.25f, 0.3f);
                    p.partHP = 70f; p.damage = 7f; p.interval = 0.09f; p.range = 65f; p.uses = 160;
                }),
                Part("Part_Thruster", p =>
                {
                    p.type = PartType.Thruster; p.displayName = "背部スラスター"; p.slot = MountSlot.Back;
                    p.placeholderShape = PrimitiveType.Cube; p.placeholderScale = new Vector3(2.4f, 2.8f, 1.3f);
                    p.color = new Color(0.95f, 0.5f, 0.15f);
                    p.partHP = 80f; p.multiplier = 2.6f; p.uses = 400;
                }),
                Part("Part_Cannon", p =>
                {
                    p.type = PartType.Cannon; p.displayName = "肩部キャノン"; p.slot = MountSlot.Shoulder;
                    p.placeholderShape = PrimitiveType.Cylinder; p.placeholderScale = new Vector3(0.8f, 1.6f, 0.8f);
                    p.placeholderEuler = new Vector3(90f, 0f, 0f); p.color = new Color(0.45f, 0.55f, 0.25f);
                    p.partHP = 70f; p.damage = 90f; p.interval = 2.2f; p.range = 60f; p.radius = 11f; p.uses = 6;
                }),
                Part("Part_Blade", p =>
                {
                    p.type = PartType.Blade; p.displayName = "刃腕"; p.slot = MountSlot.LeftArm;
                    p.placeholderShape = PrimitiveType.Cube; p.placeholderScale = new Vector3(0.35f, 0.9f, 5.5f);
                    p.color = new Color(0.6f, 0.9f, 1f);
                    p.partHP = 60f; p.multiplier = 1.8f; p.radius = 1.5f; p.uses = 15;
                }),
            };
        }

        /// <summary>既にあれば調整済みの値を残すため、上書きしない。</summary>
        static PartDefinition Part(string name, System.Action<PartDefinition> init)
        {
            string path = $"{PartsDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PartDefinition>(path);
            if (existing != null) return existing;
            var p = ScriptableObject.CreateInstance<PartDefinition>();
            init(p);
            AssetDatabase.CreateAsset(p, path);
            return p;
        }

        // ───────── 背景 ─────────

        static void BuildHorizon(float half)
        {
            var mat = LitMaterial(Gen + "/Pillar.mat", new Color(0.5f, 0.52f, 0.55f));
            var parent = new GameObject("Horizon").transform;
            var rng = new System.Random(7);
            for (int i = 0; i < 70; i++)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float dist = half * (0.95f + (float)rng.NextDouble() * 0.6f);
                float w = 4f + (float)rng.NextDouble() * 12f;
                float h = 8f + (float)rng.NextDouble() * 45f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Pillar";
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(Mathf.Cos(ang) * dist, h * 0.5f, Mathf.Sin(ang) * dist);
                go.transform.localScale = new Vector3(w, h, w * (0.6f + (float)rng.NextDouble()));
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
                go.isStatic = true;
            }
        }

        // ───────── ユーティリティ ─────────

        static Material LitMaterial(string path, Color c)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
