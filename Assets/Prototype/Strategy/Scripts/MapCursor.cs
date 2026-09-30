using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 마우스와 패드 커서를 하나로 다룬다. 마지막으로 움직인 장치가 커서를 가진다.
    /// 패드는 스틱으로 자유롭게 움직이다 손을 떼면 칸 중앙에 달라붙고, 화면 가장자리에 닿으면 맵이 따라 움직인다.
    /// </summary>
    public sealed class MapCursor
    {
        const float Deadzone = 0.2f;
        const float PadSpeed = 0.9f;    // 화면 높이 대비 초당 이동
        const float EdgeMargin = 0.08f; // 화면 비율

        readonly HexMap _map;
        readonly MapCamera _camera;
        readonly Rect _bounds;
        Vector2 _lastMouse;
        bool _padWasMoving;

        public bool PadMode { get; private set; }
        public Vector2 World { get; private set; }
        public Hex? Hover { get; private set; }

        public MapCursor(HexMap map, MapCamera camera, Rect bounds)
        {
            _map = map;
            _camera = camera;
            _bounds = bounds;
            World = bounds.center;
        }

        /// <param name="pointerOverUi">마우스가 패널 위에 있으면 칸을 가리키지 않는다.</param>
        public void Tick(Vector2 mouseScreen, Vector2 stick, float dt, bool pointerOverUi)
        {
            if ((mouseScreen - _lastMouse).sqrMagnitude > 1f)
            {
                PadMode = false;
                _lastMouse = mouseScreen;
            }

            var padMoving = stick.magnitude > Deadzone;
            if (padMoving) PadMode = true;

            if (PadMode)
            {
                if (padMoving)
                {
                    var p = World + stick * (PadSpeed * 2f * _camera.Size * dt);
                    p.x = Mathf.Clamp(p.x, _bounds.xMin, _bounds.xMax);
                    p.y = Mathf.Clamp(p.y, _bounds.yMin, _bounds.yMax);
                    World = p;
                    FollowEdge(stick, dt);
                }
                else if (_padWasMoving && Hover is { } h)
                {
                    World = HexLayout.ToWorld(h); // 손을 떼면 칸 중앙으로
                }
            }
            else
            {
                World = _camera.ScreenToWorld(mouseScreen);
            }
            _padWasMoving = padMoving;

            var hex = HexLayout.FromWorld(World);
            Hover = _map.Contains(hex) && !(pointerOverUi && !PadMode) ? hex : null;
        }

        void FollowEdge(Vector2 stick, float dt)
        {
            var s = _camera.WorldToScreen(World);
            var mx = Screen.width * EdgeMargin;
            var my = Screen.height * EdgeMargin;
            var push = Vector2.zero;
            if (s.x < mx && stick.x < 0f || s.x > Screen.width - mx && stick.x > 0f) push.x = stick.x;
            if (s.y < my && stick.y < 0f || s.y > Screen.height - my && stick.y > 0f) push.y = stick.y;
            _camera.Pan(push, dt);
        }
    }
}
