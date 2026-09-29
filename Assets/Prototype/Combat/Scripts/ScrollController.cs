using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 가로 입력을 진행 거리로 바꾼다 (검증계획 3.2). 기체는 화면 가로 위치에 고정되고 ScrollX가 -Distance로 이동한다.
    /// A: 기본 스크롤 + ±범위 오프셋 (떼면 유지) /
    /// B: 기본 스크롤 + 스로틀 (−감속 폭 ~ +가속 폭, 떼면 유지) / B+: 떼면 스로틀이 0으로 복귀.
    /// 스로틀은 기본 스크롤에 대한 차이로 저장하므로 기본 스크롤 속도를 바꾸면 B에도 바로 반영된다.
    /// </summary>
    public sealed class ScrollController
    {
        readonly CombatTuning _tuning;
        HorizontalMode _mode;
        float _baseDistance;
        float _throttle; // B: 기본 스크롤 대비 속도 차
        float _knockback;

        const float KnockbackDecay = 20f;

        public float Distance { get; private set; }
        public float Offset { get; private set; }
        public float Speed { get; private set; }
        public float Throttle => _throttle;

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
        }

        public void Tick(float input, float dt)
        {
            if (dt <= 0f) return;
            if (_tuning.horizontalMode != _mode) SwitchMode(_tuning.horizontalMode);

            var previous = Distance;

            if (_mode == HorizontalMode.PositionOffset)
            {
                _baseDistance += (_tuning.baseScrollSpeed - _knockback) * dt;
                Offset = Mathf.Clamp(Offset + input * _tuning.offsetSpeed * dt, -_tuning.offsetRange, _tuning.offsetRange);
                Distance = _baseDistance + Offset;
            }
            else
            {
                var hasInput = Mathf.Abs(input) > 0.1f;
                if (hasInput)
                    _throttle += input * _tuning.acceleration * dt;
                else if (_mode == HorizontalMode.SpeedCruiseReturn)
                    _throttle = Mathf.MoveTowards(_throttle, 0f, _tuning.cruiseReturnRate * dt);

                _throttle = Mathf.Clamp(_throttle, -_tuning.decelRange, _tuning.accelRange);
                var speed = Mathf.Max(0f, _tuning.baseScrollSpeed + _throttle);
                Distance += (speed - _knockback) * dt;
            }
            _knockback = Mathf.MoveTowards(_knockback, 0f, KnockbackDecay * dt);

            Speed = (Distance - previous) / dt;
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
