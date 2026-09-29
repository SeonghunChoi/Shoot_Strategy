using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 화면 가로 위치에 고정되고 세로로만 이동하는 기체 (검증계획 3.3).
    /// 체력이 0이 되면 DownTime 동안 사라졌다가 체력을 채우고 복귀한다 (기체 전환은 5단계).
    /// </summary>
    public sealed class PlayerShip
    {
        const float EdgeMargin = 1f;
        const float DownTime = 2f;
        const float ReviveInvulnTime = 1.5f;
        public const float HitRadius = 0.5f;

        readonly CombatTuning _tuning;
        readonly Transform _root;
        readonly Transform _model;
        float _tilt;
        float _downTimer;
        float _invuln;

        public float Y { get; private set; }
        public float Hp { get; private set; }
        public float VelocityY { get; private set; }
        public Vector2 Position => _root.position;
        public Vector2 Nose => Position + new Vector2(1.3f, 0f);
        public bool Down => _downTimer > 0f;
        public bool CanBeHit => !Down && _invuln <= 0f && !_tuning.playerInvincible;

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
            Hp = _tuning.playerMaxHp;
            _downTimer = 0f;
            _invuln = 0f;
        }

        /// <summary>피해를 적용한다. 이번 피해로 격추되면 true.</summary>
        public bool Damage(float amount)
        {
            if (!CanBeHit) return false;
            Hp -= amount;
            _invuln = _tuning.hitInvulnTime;
            if (Hp > 0f) return false;
            Hp = 0f;
            _downTimer = DownTime;
            return true;
        }

        public void Tick(float input, float dt, float mapHeight, float screenX)
        {
            if (Down)
            {
                _downTimer -= dt;
                if (!Down)
                {
                    Hp = _tuning.playerMaxHp;
                    _invuln = ReviveInvulnTime;
                }
                input = 0f;
            }
            _invuln = Mathf.Max(0f, _invuln - dt);

            var previousY = Y;
            Y = Mathf.Clamp(Y + input * _tuning.verticalSpeed * dt, EdgeMargin, mapHeight - EdgeMargin);
            VelocityY = dt > 0f ? (Y - previousY) / dt : 0f;
            _root.position = new Vector3(screenX, Y, 0f);

            _tilt = Mathf.Lerp(_tilt, input * _tuning.shipTiltAngle, 1f - Mathf.Exp(-10f * dt));
            _model.localRotation = Quaternion.Euler(0f, 0f, _tilt);

            // 격추 중 숨김, 무적 중 깜빡임
            _model.gameObject.SetActive(!Down && (_invuln <= 0f || Mathf.Repeat(_invuln * 15f, 2f) < 1f));
        }
    }
}
