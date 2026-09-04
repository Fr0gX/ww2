using UnityEngine;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;

namespace WW2.Runtime
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private HexMapView _mapView;
        private PrototypeGameController _controller;

        public GameState State { get; private set; }
        public GameSimulation Simulation { get; private set; }

        private void Awake()
        {
            Simulation = new GameSimulation(RulesCatalog.CreateDefault());
            State = PrototypeScenario.Create(Simulation.Rules);
            Simulation.Turns.BeginNationTurn(State, 1);
            ConfigureScene();

            _mapView = gameObject.AddComponent<HexMapView>();
            gameObject.AddComponent<MapCameraController>().Initialize(_mapView);
            _controller = gameObject.AddComponent<PrototypeGameController>();
            _controller.Initialize(this, _mapView);
            gameObject.AddComponent<PrototypeHud>().Initialize(this, _controller);
            gameObject.AddComponent<RuntimeVisualCapture>();
        }

        public void Restart()
        {
            Simulation = new GameSimulation(RulesCatalog.CreateDefault());
            State = PrototypeScenario.Create(Simulation.Rules);
            Simulation.Turns.BeginNationTurn(State, 1);
            _controller.Initialize(this, _mapView);
        }

        public void ReplaceState(GameState state)
        {
            State = state;
        }

        private static void ConfigureScene()
        {
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.rotation = WorldPresentation.CameraRotation;
                camera.transform.position = WorldPresentation.CameraPositionForTarget(Vector3.zero, 8.5f);
                camera.orthographic = false;
                camera.fieldOfView = WorldPresentation.CameraFieldOfView;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.06f, 0.07f);
                camera.allowHDR = false;
                camera.allowMSAA = true;
            }

            QualitySettings.antiAliasing = 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 42f;
            QualitySettings.lodBias = 2f;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.40f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.15f);

            if (FindFirstObjectByType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.91f, 0.76f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.72f;
                light.shadowBias = 0.045f;
                light.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            }

            if (GameObject.Find("Cool Fill Light") == null)
            {
                var fillObject = new GameObject("Cool Fill Light");
                var fill = fillObject.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.26f;
                fill.color = new Color(0.50f, 0.66f, 0.82f);
                fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(58f, 142f, 0f);
            }
        }
    }
}
