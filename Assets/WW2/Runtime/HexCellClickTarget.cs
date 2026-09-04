using UnityEngine;
using WW2.Core.Model;

namespace WW2.Runtime
{
    public sealed class HexCellClickTarget : MonoBehaviour
    {
        private HexMapView _owner;
        private HexCoord _coord;

        public void Initialize(HexMapView owner, HexCoord coord)
        {
            _owner = owner;
            _coord = coord;
        }

        private void OnMouseOver()
        {
            if (Input.GetMouseButtonDown(0)) _owner?.NotifyCellClicked(_coord);
            if (Input.GetMouseButtonUp(1) && MapCameraController.IsRightClick) _owner?.NotifyCellRightClicked(_coord);
        }

        private void OnMouseEnter()
        {
            _owner?.NotifyCellHovered(_coord);
        }

        private void OnMouseExit()
        {
            _owner?.NotifyCellHoverEnded(_coord);
        }
    }

    public sealed class MapBackdropClickTarget : MonoBehaviour
    {
        private HexMapView _owner;

        public void Initialize(HexMapView owner)
        {
            _owner = owner;
        }

        private void OnMouseOver()
        {
            if (Input.GetMouseButtonDown(0)) _owner?.NotifyBackgroundClicked();
        }
    }

}
