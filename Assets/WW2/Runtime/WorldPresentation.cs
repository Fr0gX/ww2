using UnityEngine;

namespace WW2.Runtime
{
    /// <summary>
    /// Shared physical contract for every world-space visual. Gameplay hexes
    /// live on XZ, height is always world Y, and all ground contact starts at 0.
    /// </summary>
    public static class WorldPresentation
    {
        public const float GroundY = 0f;
        public const float InteractionY = 0.006f;
        public const float GridY = 0.010f;
        public const float RoadShadowY = 0.012f;
        public const float RoadPaintY = 0.021f;
        public const float PropAnchorY = 0.015f;
        public const float CameraPitch = 48f;
        public const float CameraYaw = 30f;
        public const float CameraFieldOfView = 28f;

        public static readonly Quaternion CameraRotation =
            Quaternion.Euler(CameraPitch, CameraYaw, 0f);
        public static readonly Vector3 CameraForward = CameraRotation * Vector3.forward;
        public static readonly Vector3 CameraRight = CameraRotation * Vector3.right;

        public static float CameraDistanceForViewSize(float viewSize)
        {
            return viewSize / Mathf.Tan(CameraFieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        public static Vector3 CameraPositionForTarget(Vector3 target, float viewSize,
            float screenRightCompositionOffset = 0f)
        {
            target.y = GroundY;
            var aim = target - CameraRight * screenRightCompositionOffset;
            var distance = CameraDistanceForViewSize(viewSize);
            return aim - CameraForward * distance;
        }
    }
}
