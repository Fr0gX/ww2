using UnityEngine;

namespace WW2.Runtime
{
    /// <summary>Keeps a painted 2.5D component aligned with the active map camera.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraFacingSprite : MonoBehaviour
    {
        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera != null) transform.rotation = camera.transform.rotation;
        }
    }
}
