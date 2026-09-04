using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WW2.Runtime;

namespace WW2.Editor
{
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MaterialPath = "Assets/Resources/PrototypeSurface.mat";

        [InitializeOnLoadMethod]
        private static void EnsureProjectScene()
        {
            EditorApplication.delayCall += () =>
            {
                CreatePrototypeAssets();
                if (!File.Exists(ScenePath))
                {
                    CreateProjectScene();
                }
            };
        }

        [MenuItem("WW2/Setup Project Scene")]
        public static void CreateProjectScene()
        {
            CreatePrototypeAssets();
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Game");
            root.AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"WW2 project scene created at {ScenePath}");
        }

        public static void CreatePrototypeAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null)
            {
                return;
            }

            Directory.CreateDirectory("Assets/Resources");
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidDataException("Unity Standard shader is unavailable.");
            }

            var material = new Material(shader)
            {
                name = "Prototype Surface",
                color = Color.white
            };
            material.SetFloat("_Glossiness", 0.15f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"WW2 prototype material created at {MaterialPath}");
        }
    }
}
