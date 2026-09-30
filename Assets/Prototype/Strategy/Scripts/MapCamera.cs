using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>탑다운 직교 카메라. 이동·확대 후 맵 범위 안으로 가둔다.</summary>
    public sealed class MapCamera
    {
        const float MinSize = 3f;
        const float MaxSize = 11f;
        const float PanSpeed = 1.2f;   // 화면 높이 대비 초당 이동
        const float WheelZoom = 0.12f; // 휠 한 칸당 배율
        const float PadZoomSpeed = 1.5f;

        readonly Camera _camera;
        readonly Rect _bounds;

        public Camera Camera => _camera;
        public float Size => _camera.orthographicSize;

        public MapCamera(Camera camera, Rect mapBounds)
        {
            _camera = camera;
            _bounds = mapBounds;
            _camera.orthographic = true;
            // 처음에는 맵 세로 전체가 보이게 (상단 바 여유 포함)
            _camera.orthographicSize = Mathf.Clamp(mapBounds.height * 0.5f + 1.2f, MinSize, MaxSize);
            var c = mapBounds.center;
            _camera.transform.SetPositionAndRotation(new Vector3(c.x, c.y, -50f), Quaternion.identity);
        }

        public Vector2 ScreenToWorld(Vector2 screen) => _camera.ScreenToWorldPoint(screen);

        public Vector2 WorldToScreen(Vector2 world) => _camera.WorldToScreenPoint(world);

        /// <summary>입력 방향(−1~1)으로 이동. 확대할수록 느려진다.</summary>
        public void Pan(Vector2 input, float dt)
        {
            if (input.sqrMagnitude < 0.0001f) return;
            Move(input * (PanSpeed * 2f * Size * dt));
        }

        /// <summary>화면 픽셀 이동량만큼 맵을 끌어당김 (가운데 버튼 드래그).</summary>
        public void Drag(Vector2 screenDelta)
        {
            var worldPerPixel = 2f * Size / Screen.height;
            Move(-screenDelta * worldPerPixel);
        }

        /// <summary>배율 변경. anchor(화면 좌표) 아래의 월드 지점이 제자리에 남는다.</summary>
        public void Zoom(float factor, Vector2 anchorScreen)
        {
            var before = ScreenToWorld(anchorScreen);
            _camera.orthographicSize = Mathf.Clamp(Size * factor, MinSize, MaxSize);
            var after = ScreenToWorld(anchorScreen);
            Move(before - after);
        }

        public void ZoomWheel(float scrollY, Vector2 anchorScreen)
        {
            if (Mathf.Abs(scrollY) < 0.01f) return;
            Zoom(Mathf.Pow(1f - WheelZoom, Mathf.Sign(scrollY)), anchorScreen);
        }

        public void ZoomPad(float axis, float dt, Vector2 anchorScreen)
        {
            if (Mathf.Abs(axis) < 0.05f) return;
            Zoom(Mathf.Exp(-axis * PadZoomSpeed * dt), anchorScreen);
        }

        void Move(Vector2 delta)
        {
            var p = (Vector2)_camera.transform.position + delta;
            // 카메라 중심은 맵 범위 안 (가장자리를 화면 가운데까지 끌어올 수 있음)
            p.x = Mathf.Clamp(p.x, _bounds.xMin, _bounds.xMax);
            p.y = Mathf.Clamp(p.y, _bounds.yMin, _bounds.yMax);
            _camera.transform.position = new Vector3(p.x, p.y, -50f);
        }
    }
}
