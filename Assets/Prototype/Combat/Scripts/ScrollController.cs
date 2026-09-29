using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 가로 입력을 진행 거리로 바꾼다 (검증계획 3.2). 기체는 화면 가로 위치에 고정되고 ScrollX가 -Distance로 이동한다.
    /// A: 기본 스크롤 + ±범위 오프셋 (떼면 유지) /
    /// B: 기본 스크롤 + 스로틀 (−감속 폭 ~ +가속 폭, 떼면 유지) / B+: 떼면 스로틀이 0으로 복귀.
    /// 스로틀은 기본 스크롤에 대한 차이로 저장하므로 기본 스크롤 속도를 바꾸면 B에도 바로 반영된다.
    /// 진행 방향(Direction)이 -1이면 거리가 줄어드는 쪽으로 진행하고 입력도 진행 방향 기준으로 뒤집는다.
    /// 선회(검증계획 3.5): 속도가 0을 지나 반대로 바뀌는 동안 입력을 받지 않는다.
    /// </summary>
    public sealed class ScrollController
    {
        const float KnockbackDecay = 20f;

        readonly CombatTuning _tuning;
        HorizontalMode _mode;
        float _baseDistance;
        float _throttle; // B: 기본 스크롤 대비 속도 차
        float _knockback;
        float _turnTime;
        float _turnSpeed;

        public float Distance { get; private set; }
        public float Offset { get; private set; }
        public float Speed { get; private set; }  // 부호 있는 대지 속도 (거리 증가 방향이 +)
        public float Throttle => _throttle;
        public int Direction { get; private set; } = 1;
        public bool Turning { get; private set; }
        public float TurnProgress => Turning ? Mathf.Clamp01(_turnTime / _tuning.turnDuration) : 0f;

        /// <summary>화면 배치용 연속 방향: 평소 ±1, 선회 중 이전 방향에서 새 방향으로 부드럽게 넘어간다.</summary>
        public float VisualDirection => Turning ? Mathf.Lerp(Direction, -Direction, Mathf.SmoothStep(0f, 1f, TurnProgress)) : Direction;

        // 현재 기체의 가로 입력 배율 (Squadron이 교대 시 설정)
        public float OffsetSpeedScale { get; set; } = 1f;
        public float DecelRangeScale { get; set; } = 1f;
        public float AccelRangeScale { get; set; } = 1f;
        public float DecelRange => _tuning.decelRange * DecelRangeScale;
        public float AccelRange => _tuning.accelRange * AccelRangeScale;

        public ScrollController(CombatTuning tuning)
        {
            _tuning = tuning;
            Reset();
        }

        public void Reset()
        {
            _mode = _tuning.horizontalMode;
            _baseDistance = 0f;
            Offset = 0f;
            Distance = 0f;
            _throttle = 0f;
            _knockback = 0f;
            Speed = 0f;
            Direction = 1;
            Turning = false;
        }

        public void StartTurn()
        {
            if (Turning) return;
            Turning = true;
            _turnTime = 0f;
            _turnSpeed = Mathf.Max(Mathf.Abs(Speed), 1f);
            _knockback = 0f;
        }

        /// <param name="input">가로 입력 (화면 오른쪽 +)</param>
        public void Tick(float input, float dt)
        {
            if (dt <= 0f) return;
            if (_tuning.horizontalMode != _mode) SwitchMode(_tuning.horizontalMode);

            var previous = Distance;
            if (Turning) TickTurn(dt);
            else if (_mode == HorizontalMode.PositionOffset) TickOffset(input * Direction, dt);
            else TickThrottle(input * Direction, dt);

            _knockback = Mathf.MoveTowards(_knockback, 0f, KnockbackDecay * dt);
            Speed = (Distance - previous) / dt;
        }

        void TickTurn(float dt)
        {
            _turnTime += dt;
            var k = Mathf.Clamp01(_turnTime / _tuning.turnDuration);
            Distance += Direction * _turnSpeed * Mathf.Cos(Mathf.PI * k) * dt;
            if (k < 1f) return;

            Turning = false;
            Direction = -Direction;
            _baseDistance = Distance; // 모드 A: 오프셋을 접어 새 기준으로
            Offset = 0f;
        }

        void TickOffset(float forwardInput, float dt)
        {
            _baseDistance += Direction * (_tuning.baseScrollSpeed - _knockback) * dt;
            Offset = Mathf.Clamp(Offset + forwardInput * _tuning.offsetSpeed * OffsetSpeedScale * dt, -_tuning.offsetRange, _tuning.offsetRange);
            Distance = _baseDistance + Direction * Offset;
        }

        void TickThrottle(float forwardInput, float dt)
        {
            if (Mathf.Abs(forwardInput) > 0.1f)
                _throttle += forwardInput * _tuning.acceleration * dt;
            else if (_mode == HorizontalMode.SpeedCruiseReturn)
                _throttle = Mathf.MoveTowards(_throttle, 0f, _tuning.cruiseReturnRate * dt);

            _throttle = Mathf.Clamp(_throttle, -DecelRange, AccelRange);
            var speed = Mathf.Max(0f, _tuning.baseScrollSpeed + _throttle);
            Distance += Direction * (speed - _knockback) * dt;
        }

        /// <summary>지형 정면 충돌 시 뒤로 밀려나는 속도. 빠르게 감쇠한다.</summary>
        public void Knockback(float speed) => _knockback = Mathf.Max(_knockback, speed);

        void SwitchMode(HorizontalMode mode)
        {
            // 진행 거리는 이어가고, 새 모드의 기준값만 현재 상태에서 다시 잡는다
            if (mode == HorizontalMode.PositionOffset)
            {
                _baseDistance = Distance;
                Offset = 0f;
            }
            else if (_mode == HorizontalMode.PositionOffset)
            {
                _throttle = 0f;
            }
            _mode = mode;
        }
    }
}
