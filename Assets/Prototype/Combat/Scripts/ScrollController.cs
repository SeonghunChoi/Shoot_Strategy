using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 가로 입력을 진행 거리로 바꾼다 (검증계획 3.2). 기체는 화면 가로 위치에 고정되고 ScrollX가 -Distance로 이동한다.
    /// A: 기본 스크롤 + ±범위 오프셋 (떼면 유지) / B: 최저~최고 속도 가감속 / B+: 떼면 순항 속도로 복귀.
    /// </summary>
    public sealed class ScrollController
    {
        readonly CombatTuning _tuning;
        HorizontalMode _mode;
        float _baseDistance;
        float _throttleSpeed;

        public float Distance { get; private set; }
        public float Offset { get; private set; }
        public float Speed { get; private set; }

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
            _throttleSpeed = _tuning.cruiseSpeed;
            Speed = 0f;
        }

        public void Tick(float input, float dt)
        {
            if (dt <= 0f) return;
            if (_tuning.horizontalMode != _mode) SwitchMode(_tuning.horizontalMode);

            var previous = Distance;
            if (_mode == HorizontalMode.PositionOffset)
            {
                _baseDistance += _tuning.baseScrollSpeed * dt;
                Offset = Mathf.Clamp(Offset + input * _tuning.offsetSpeed * dt, -_tuning.offsetRange, _tuning.offsetRange);
                Distance = _baseDistance + Offset;
            }
            else
            {
                var hasInput = Mathf.Abs(input) > 0.1f;
                if (hasInput)
                    _throttleSpeed += input * _tuning.acceleration * dt;
                else if (_mode == HorizontalMode.SpeedCruiseReturn)
                    _throttleSpeed = Mathf.MoveTowards(_throttleSpeed, _tuning.cruiseSpeed, _tuning.cruiseReturnRate * dt);

                _throttleSpeed = Mathf.Clamp(_throttleSpeed, _tuning.minSpeed, _tuning.maxSpeed);
                Distance += _throttleSpeed * dt;
            }

            Speed = (Distance - previous) / dt;
        }

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
                _throttleSpeed = _tuning.baseScrollSpeed;
            }
            _mode = mode;
        }
    }
}
