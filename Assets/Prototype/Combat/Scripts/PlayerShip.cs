using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 화면 가로 위치에 고정되고 세로로만 이동하는 기체 (검증계획 3.3).
    /// </summary>
    public sealed class PlayerShip
    {
        const float EdgeMargin = 1f;

        readonly CombatTuning _tuning;
        readonly Transform _root;
        readonly Transform _model;
        float _tilt;

        public float Y { get; private set; }
        public Vector3 Position => _root.position;

        public PlayerShip(CombatTuning tuning, Color color)
        {
            _tuning = tuning;
            _root = new GameObject("PlayerShip").transform;
            _model = new GameObject("Model").transform;
            _model.SetParent(_root, false);

            // 기수 = +X
            var body = Shapes.Create(PrimitiveType.Capsule, color, _model, Vector3.zero, new Vector3(0.6f, 1.2f, 0.6f));
            body.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Shapes.Create(PrimitiveType.Cube, color * 0.8f, _model, new Vector3(-0.1f, 0f, 0f), new Vector3(0.6f, 0.08f, 2.2f));
            Shapes.Create(PrimitiveType.Cube, color * 0.8f, _model, new Vector3(-1f, 0.35f, 0f), new Vector3(0.35f, 0.6f, 0.08f));
            Shapes.Create(PrimitiveType.Sphere, new Color(0.2f, 0.25f, 0.35f), _model, new Vector3(0.45f, 0.22f, 0f), new Vector3(0.45f, 0.25f, 0.3f));
        }

        public void Reset(float y)
        {
            Y = y;
            _tilt = 0f;
        }

        public void Tick(float input, float dt, float mapHeight, float screenX)
        {
            Y = Mathf.Clamp(Y + input * _tuning.verticalSpeed * dt, EdgeMargin, mapHeight - EdgeMargin);
            _root.position = new Vector3(screenX, Y, 0f);

            _tilt = Mathf.Lerp(_tilt, input * _tuning.shipTiltAngle, 1f - Mathf.Exp(-10f * dt));
            _model.localRotation = Quaternion.Euler(0f, 0f, _tilt);
        }
    }
}
