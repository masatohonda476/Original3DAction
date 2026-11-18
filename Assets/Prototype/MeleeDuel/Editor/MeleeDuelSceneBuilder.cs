using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeleeDuel.EditorTools
{
    /// <summary>
    /// 近接の試作のシーンを組み立てる。機体は武装ハントのリグ付きプレハブを使い回す
    /// (先に「Prototype/HuntRelease/シーンを組み立てる」で作られている必要がある)。
    /// </summary>
    public static class MeleeDuelSceneBuilder
    {
        const string Root = "Assets/Prototype/MeleeDuel";
        public const string ScenePath = Root + "/MeleeDuel.unity";
        const string RigPrefab = "Assets/Prototype/HuntRelease/Generated/MechRig.prefab";
        const string GroundMat = "Assets/Prototype/HuntRelease/Generated/Ground.mat";
        const string PillarMat = "Assets/Prototype/HuntRelease/Generated/Pillar.mat";
        const float MechHeight = 9.06f;
        const float Half = 60f;

        [MenuItem("Prototype/MeleeDuel/シーンを組み立てる")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (rig == null)
            {
                Debug.LogError($"[MeleeDuel] 機体のプレハブがありません: {RigPrefab}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            cam.farClipPlane = 1000f;
            cam.nearClipPlane = 0.5f;
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0f, 15f, -50f);
            cam.gameObject.AddComponent<DuelCamera>();

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null) sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            // 床:部屋より少し広く
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(Half * 2.4f / 10f, 1f, Half * 2.4f / 10f);
            var gm = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            if (gm != null) ground.GetComponent<Renderer>().sharedMaterial = gm;

            // 部屋の縁:低い壁(見た目だけ。外へ出ないのはコードで止める)
            var pm = AssetDatabase.LoadAssetAtPath<Material>(PillarMat);
            var walls = new GameObject("Walls");
            for (int i = 0; i < 4; i++)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = $"Wall{i}";
                w.transform.SetParent(walls.transform, false);
                bool alongX = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                w.transform.localScale = alongX ? new Vector3(Half * 2f + 4f, 3f, 2f) : new Vector3(2f, 3f, Half * 2f + 4f);
                w.transform.position = alongX ? new Vector3(0f, 1.5f, sign * (Half + 1f)) : new Vector3(sign * (Half + 1f), 1.5f, 0f);
                if (pm != null) w.GetComponent<Renderer>().sharedMaterial = pm;
            }

            var game = new GameObject("DuelGame").AddComponent<DuelGame>();
            game.mechPrefab = rig;
            game.mechHeight = MechHeight;
            game.arenaHalf = Half;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeleeDuel] シーンを組み立てました: {ScenePath}");
        }
    }
}
