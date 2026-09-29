using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 기체를 세로로만 추적하는 직교 카메라. 맵 경계 밖은 비추지 않는다.
    /// </summary>
    public sealed class CameraRig
    {
        const float GroundView = 2f;

        readonly CombatTuning _tuning;
        readonly Camera _camera;
        float _velocity;

        public Camera Camera => _camera;
        public float HalfHeight => _camera.orthographicSize;
        public float HalfWidth => _camera.orthographicSize * _camera.aspect;
        public float Y => _camera.transform.position.y;

        public CameraRig(CombatTuning tuning, Camera camera)
        {
            _tuning = tuning;
            _camera = camera;
        }

        public void Snap(float targetY, float mapHeight)
        {
            _velocity = 0f;
            SetY(Clamp(targetY, mapHeight));
        }

        public void Tick(float targetY, float dt, float mapHeight)
        {
            var y = Y;
            switch (_tuning.cameraFollow)
            {
                case CameraFollowMode.Instant:
                    y = targetY;
                    break;
                case CameraFollowMode.Smooth:
                    y = Mathf.SmoothDamp(y, targetY, ref _velocity, _tuning.cameraSmoothTime, Mathf.Infinity, dt);
                    break;
                case CameraFollowMode.DeadZone:
                    var half = _tuning.cameraDeadZone * HalfHeight;
                    if (targetY > y + half) y = targetY - half;
                    else if (targetY < y - half) y = targetY + half;
                    break;
            }
            SetY(Clamp(y, mapHeight));
        }

        float Clamp(float y, float mapHeight)
        {
            // 바닥은 지면이 조금 보이도록 GroundView만큼 더 내려간다
            var half = HalfHeight;
            var min = half - GroundView;
            var max = Mathf.Max(min, mapHeight - half);
            return Mathf.Clamp(y, min, max);
        }

        void SetY(float y)
        {
            var p = _camera.transform.position;
            _camera.transform.position = new Vector3(p.x, y, p.z);
        }
    }
}
