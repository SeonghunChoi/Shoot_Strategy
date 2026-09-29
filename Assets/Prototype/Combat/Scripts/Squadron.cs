using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype.Combat
{
    /// <summary>
    /// 편대 3기와 기체 전환 (검증계획 3.6, Q4·Q5).
    /// 전환 버튼 탭: 살아 있는 다음 기체. 홀드: 링 메뉴(오른쪽 스틱, 키보드는 방향키)로 고르고 떼면 확정.
    /// 교대 직후 무방비 시간 동안 발사 불가·이동 감속. 격추되면 다음 기체가 자동 투입되고, 전원 격추 시 전멸.
    /// </summary>
    public sealed class Squadron
    {
        const float RingDeadZone = 0.5f;
        const float WipeRestoreDelay = 2f;

        readonly CombatContext _ctx;
        readonly List<SquadronMember> _members = new();
        float _pressTime;
        bool _pressing;
        bool _ringByKeyboard;
        float _vulnerableTimer;
        float _wipeTimer;

        public IReadOnlyList<SquadronMember> Members => _members;
        public int CurrentIndex { get; private set; }
        public SquadronMember Current => _members[CurrentIndex];
        public float VulnerableTimer => _vulnerableTimer;
        public bool RingOpen { get; private set; }
        public int RingSelection { get; private set; } = -1;
        public bool Wiped { get; private set; }

        /// <summary>키보드로 링 메뉴를 연 동안 방향키는 선택에만 쓴다.</summary>
        public bool SuppressMove => RingOpen && _ringByKeyboard;

        public Squadron(CombatContext ctx, AircraftDefinition[] defs)
        {
            _ctx = ctx;
            foreach (var def in defs) _members.Add(new SquadronMember { Def = def });
        }

        public void Reset()
        {
            foreach (var m in _members)
            {
                m.Refit(_ctx.Tuning.playerMaxHp);
                m.ForegroundTime = 0f;
            }
            Wiped = false;
            _wipeTimer = 0f;
            _ctx.Player.Down = false;
            CloseRing();
            _pressing = false;
            Activate(0, vulnerable: false);
        }

        /// <summary>unscaledDt: 링 슬로모션 중에도 버튼 홀드 시간은 실제 시간으로 잰다.</summary>
        public void Tick(float dt, float unscaledDt)
        {
            if (Wiped)
            {
                if (_ctx.Tuning.restoreSquadronOnWipe && (_wipeTimer += dt) >= WipeRestoreDelay) Restore();
                return;
            }

            if (Current.Down) HandleShotDown();
            if (Wiped) return;

            // 선회 중: 조작 불능, 전경 시간 정지 (검증계획 3.5)
            if (_ctx.Scroll.Turning)
            {
                if (RingOpen) CloseRing();
                _pressing = false;
                return;
            }

            Current.ForegroundTime += dt;
            _vulnerableTimer = Mathf.Max(0f, _vulnerableTimer - dt);
            _ctx.Player.Vulnerable = _vulnerableTimer > 0f;

            HandleSwitchInput(unscaledDt);
            Time.timeScale = RingOpen && _ctx.Tuning.ringSlowMotion ? _ctx.Tuning.ringSlowScale : 1f;
        }

        void HandleShotDown()
        {
            CloseRing();
            var next = NextAlive(CurrentIndex);
            if (next < 0)
            {
                Wiped = true;
                _wipeTimer = 0f;
                _ctx.Player.Down = true;
                _ctx.Player.Vulnerable = false;
                _ctx.Log.Event("전멸", _ctx.Tuning.restoreSquadronOnWipe ? "복구" : "");
                return;
            }
            _ctx.Log.Switch("자동", Current, _members[next]);
            Activate(next, vulnerable: true);
        }

        void Restore()
        {
            var y = _ctx.Player.Y;
            Reset();
            _ctx.Player.Place(y);
        }

        void HandleSwitchInput(float unscaledDt)
        {
            var c = _ctx.Controls;
            if (c.Switch.WasPressedThisFrame())
            {
                _pressing = true;
                _pressTime = 0f;
                _ringByKeyboard = c.Switch.activeControl?.device is Keyboard;
            }
            if (!_pressing) return;

            _pressTime += unscaledDt;
            if (!RingOpen && _pressTime >= _ctx.Tuning.switchHoldThreshold)
            {
                RingOpen = true;
                RingSelection = -1;
            }

            if (RingOpen)
            {
                var v = _ringByKeyboard ? c.Move.ReadValue<Vector2>() : c.RingSelect.ReadValue<Vector2>();
                if (v.magnitude >= RingDeadZone) RingSelection = SlotAt(v);
            }

            if (!c.Switch.WasReleasedThisFrame() && c.Switch.IsPressed()) return;

            _pressing = false;
            if (RingOpen)
            {
                var pick = RingSelection;
                CloseRing();
                if (pick >= 0 && pick != CurrentIndex && !_members[pick].Down) SwitchTo(pick, "링");
                else _ctx.Log.RingCanceled();
            }
            else
            {
                var next = NextAlive(CurrentIndex);
                if (next >= 0 && next != CurrentIndex) SwitchTo(next, "탭");
            }
        }

        /// <summary>링 칸 i의 방향: 위(90°)에서 시계 방향으로 120° 간격.</summary>
        public static float SlotAngle(int index, int count) => 90f - index * 360f / count;

        int SlotAt(Vector2 v)
        {
            var angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            var best = 0;
            var bestDelta = float.MaxValue;
            for (var i = 0; i < _members.Count; i++)
            {
                var d = Mathf.Abs(Mathf.DeltaAngle(angle, SlotAngle(i, _members.Count)));
                if (d < bestDelta) { bestDelta = d; best = i; }
            }
            return best;
        }

        void CloseRing()
        {
            RingOpen = false;
            RingSelection = -1;
            Time.timeScale = 1f;
        }

        int NextAlive(int from)
        {
            for (var step = 1; step <= _members.Count; step++)
            {
                var i = (from + step) % _members.Count;
                if (!_members[i].Down) return i;
            }
            return -1;
        }

        void SwitchTo(int index, string method)
        {
            _ctx.Log.Switch(method, Current, _members[index]);
            var player = _ctx.Player;
            _ctx.Effects.Retreat(Current.Def, player.CurrentModel, player.Facing); // 빠지는 기체는 뒤쪽 위로 사라짐 (배경 레이어 대용)
            Activate(index, vulnerable: true);
        }

        void Activate(int index, bool vulnerable)
        {
            CurrentIndex = index;
            var def = Current.Def;
            _ctx.Player.SetAircraft(index, Current);
            _vulnerableTimer = vulnerable ? _ctx.Tuning.switchVulnerableTime : 0f;
            _ctx.Player.Vulnerable = _vulnerableTimer > 0f;

            var scroll = _ctx.Scroll;
            scroll.OffsetSpeedScale = def.OffsetSpeedScale;
            scroll.DecelRangeScale = def.DecelRangeScale;
            scroll.AccelRangeScale = def.AccelRangeScale;
            _ctx.Weapons.OnAircraftChanged();
        }
    }
}
