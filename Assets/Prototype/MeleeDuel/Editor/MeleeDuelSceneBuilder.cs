using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeleeDuel.EditorTools
{
    /// <summary>
    /// 近接の試作のシーン(ステージ)を組み立てる。機体は武装ハントのリグ付きプレハブを使い回す
    /// (先に「Prototype/HuntRelease/シーンを組み立てる」で作られている必要がある)。
    /// 平らな部屋のシーン(MeleeDuel.unity)を組み立てるメニューは、部屋のモードと一緒にやめた(作者「MeleeDuel消して」2026-11-20)。
    /// </summary>
    public static class MeleeDuelSceneBuilder
    {
        const string Root = "Assets/Prototype/MeleeDuel";
        const string RigPrefab = "Assets/Prototype/HuntRelease/Generated/MechRig.prefab";
        const string GroundMat = "Assets/Prototype/HuntRelease/Generated/Ground.mat";
        const string PillarMat = "Assets/Prototype/HuntRelease/Generated/Pillar.mat";
        const float MechHeight = 9.06f;

        public const string StageScenePath = Root + "/MeleeDuelStage.unity";

        /// <summary>
        /// ステージ(墜落した船の中を下から上へ登る。途中で外殻の道と配管の道に分かれ、頭頂部の大ホールにボス)のシーン。
        /// 地形と敵は DuelStage が Play の開始時にコードから組み立てるので、シーンにはカメラ・光・DuelGame と DuelStage だけを置く
        /// (地形・敵の位置と向き・セーブポイントはシーンビューにギズモで出る)。
        /// </summary>
        [MenuItem("Prototype/MeleeDuel/ステージのシーンを組み立てる")]
        public static void BuildStage()
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
            cam.farClipPlane = 1500f;
            cam.nearClipPlane = 0.5f;
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0f, 15f, -15f);
            cam.gameObject.AddComponent<DuelCamera>().avoidWalls = true;

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null) sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var go = new GameObject("DuelGame");
            var game = go.AddComponent<DuelGame>();
            game.mechPrefab = rig;
            game.mechHeight = MechHeight;
            var stage = go.AddComponent<DuelStage>();
            stage.groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            stage.wallMat = AssetDatabase.LoadAssetAtPath<Material>(PillarMat);
            game.stage = stage;

            EditorSceneManager.SaveScene(scene, StageScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeleeDuel] ステージのシーンを組み立てました: {StageScenePath}");
        }
    }
}
