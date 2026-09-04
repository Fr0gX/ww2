using UnityEngine;

namespace WW2.Runtime
{
    public sealed class MapCameraController : MonoBehaviour
    {
        private const float MinimumZoom = 4.2f;
        private const float MaximumZoom = 32f;
        private HexMapView _mapView;
        private bool _dragging;
        private bool _rightDragging;
        private Vector3 _dragAnchor;
        private Vector3 _rightMouseDown;

        public static bool IsRightClick { get; private set; } = true;

        public void Initialize(HexMapView mapView)
        {
            _mapView = mapView;
        }

        private void Update()
        {
            var camera = Camera.main;
            if (camera == null) return;

            var pointerOnMap = Input.mousePosition.x > 370f && Input.mousePosition.y > 70f;
            if (pointerOnMap && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
            {
                _mapView?.CancelCameraMotion();
                var before = GroundPoint(camera, Input.mousePosition);
                if (camera.orthographic)
                {
                    camera.orthographicSize = Mathf.Clamp(
                        camera.orthographicSize - Input.mouseScrollDelta.y * camera.orthographicSize * 0.10f,
                        MinimumZoom, MaximumZoom);
                }
                else
                {
                    var downward = Mathf.Max(0.001f, -camera.transform.forward.y);
                    var currentDistance = camera.transform.position.y / downward;
                    var currentSize = currentDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    var desiredSize = Mathf.Clamp(
                        currentSize - Input.mouseScrollDelta.y * currentSize * 0.10f,
                        MinimumZoom, MaximumZoom);
                    var desiredDistance = desiredSize /
                                          Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    camera.transform.position += camera.transform.forward * (currentDistance - desiredDistance);
                }
                var after = GroundPoint(camera, Input.mousePosition);
                if (before.HasValue && after.HasValue) camera.transform.position += before.Value - after.Value;
            }

            if (pointerOnMap && Input.GetMouseButtonDown(1))
            {
                _rightMouseDown = Input.mousePosition;
                _rightDragging = false;
                IsRightClick = true;
                BeginDrag(camera);
            }

            if (Input.GetMouseButton(1) && Vector3.Distance(Input.mousePosition, _rightMouseDown) > 7f)
            {
                _rightDragging = true;
                IsRightClick = false;
            }

            if (Input.GetMouseButtonUp(1))
            {
                IsRightClick = !_rightDragging;
                _dragging = false;
            }

            if (pointerOnMap && Input.GetMouseButtonDown(2))
            {
                BeginDrag(camera);
            }

            if (!Input.GetMouseButton(1) && !Input.GetMouseButton(2)) _dragging = false;
            if (!_dragging || (!_rightDragging && !Input.GetMouseButton(2))) return;
            var current = GroundPoint(camera, Input.mousePosition);
            if (current.HasValue) camera.transform.position += _dragAnchor - current.Value;
        }

        private void BeginDrag(Camera camera)
        {
            var point = GroundPoint(camera, Input.mousePosition);
            if (point.HasValue)
            {
                _mapView?.CancelCameraMotion();
                _dragging = true;
                _dragAnchor = point.Value;
            }
        }

        private static Vector3? GroundPoint(Camera camera, Vector3 screenPoint)
        {
            var ray = camera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, Vector3.up * WorldPresentation.GroundY);
            return plane.Raycast(ray, out var distance) ? ray.GetPoint(distance) : null;
        }
    }
}
