using System.Collections.Generic;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 규칙이 남긴 이동 기록(World.Traces)을 맵 위에서 차례로 재생한다. 결과는 이미 정해져 있고 보여 주기만 한다.
    /// 재생이 끝나거나 건너뛰면 done을 부른다 (디브리핑·적 턴 요약 열기).
    /// </summary>
    public sealed class MovePlayback
    {
        public const float SecondsPerHex = 0.15f;
        const float EventSeconds = 0.45f;

        readonly List<MoveTrace> _queue = new();
        int _index;
        float _time;
        System.Action _done;

        public bool Playing => _index < _queue.Count;

        public void Start(IEnumerable<MoveTrace> traces, System.Action done)
        {
            _queue.Clear();
            _queue.AddRange(traces.Where(Worth));
            _index = 0;
            _time = 0f;
            _done = done;
            if (!Playing) Finish();
        }

        /// <summary>보여 줄 것이 있는 기록만: 사건은 시야 안일 때, 이동은 한 칸 이상이고 일부라도 시야 안일 때.</summary>
        static bool Worth(MoveTrace t) =>
            t.Kind == TraceKind.Event ? t.Seen[0] : t.Path.Count > 1 && t.Seen.Any(x => x);

        static float Duration(MoveTrace t) => t.Kind == TraceKind.Event ? EventSeconds : (t.Path.Count - 1) * SecondsPerHex;

        public void Tick(float dt)
        {
            if (!Playing) return;
            _time += dt;
            while (Playing && _time >= Duration(_queue[_index]))
            {
                _time -= Duration(_queue[_index]);
                _index++;
            }
            if (!Playing) Finish();
        }

        public void Skip()
        {
            if (!Playing) return;
            _index = _queue.Count;
            Finish();
        }

        void Finish()
        {
            var done = _done;
            _done = null;
            done?.Invoke();
        }

        /// <summary>재생이 끝나지 않은 기록이 있는 부대 (정적 표시를 숨기고 재생 표식으로 그린다).</summary>
        public bool Hides(TraceKind kind, int id)
        {
            for (var i = _index; i < _queue.Count; i++)
                if (_queue[i].Kind == kind && _queue[i].UnitId == id) return true;
            return false;
        }

        public readonly struct Token
        {
            public readonly MoveTrace Trace;
            public readonly Vector2 Position;
            public readonly bool Current;
            /// <summary>사건 표시의 진행도 (0~1).</summary>
            public readonly float EventProgress;

            public Token(MoveTrace trace, Vector2 position, bool current, float eventProgress)
            {
                Trace = trace;
                Position = position;
                Current = current;
                EventProgress = eventProgress;
            }
        }

        /// <summary>
        /// 지금 그릴 표식: 재생 중인 기록은 경로 위 보간 위치, 아직 차례가 오지 않은 기록은 출발 칸 (부대마다 첫 기록만).
        /// 적 이동은 시야 안 구간만 돌려준다.
        /// </summary>
        public IEnumerable<Token> Tokens()
        {
            var seen = new HashSet<(TraceKind, int)>();
            for (var i = _index; i < _queue.Count; i++)
            {
                var t = _queue[i];
                var current = i == _index;
                if (t.Kind == TraceKind.Event)
                {
                    if (current) yield return new Token(t, HexLayout.ToWorld(t.Path[0]), true, _time / EventSeconds);
                    continue;
                }
                if (!seen.Add((t.Kind, t.UnitId))) continue;
                if (current)
                {
                    var f = _time / SecondsPerHex;
                    var step = Mathf.Clamp(Mathf.FloorToInt(f), 0, t.Path.Count - 2);
                    var frac = Mathf.Clamp01(f - step);
                    if (!t.Seen[step] && !t.Seen[step + 1]) continue;
                    var pos = Vector2.Lerp(HexLayout.ToWorld(t.Path[step]), HexLayout.ToWorld(t.Path[step + 1]), frac);
                    yield return new Token(t, pos, true, 0f);
                }
                else if (t.Seen[0])
                {
                    yield return new Token(t, HexLayout.ToWorld(t.Path[0]), false, 0f);
                }
            }
        }
    }
}
