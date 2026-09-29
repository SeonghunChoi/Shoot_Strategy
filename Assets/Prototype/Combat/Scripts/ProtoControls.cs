using System;
using UnityEngine.InputSystem;

namespace Prototype.Combat
{
    /// <summary>
    /// 검증계획 4장의 입력 배치(배치 1·2)와 트리거 스로틀 배치 3을 코드로 정의한다. 패드 배치가 바뀌면 액션 맵을 새로 만든다.
    /// 기체 전환의 탭/홀드 구분은 사용하는 쪽에서 누른 시간으로 판단한다.
    /// </summary>
    public sealed class ProtoControls : IDisposable
    {
        InputActionMap _map;

        public PadLayout Layout { get; private set; }

        public InputAction Move { get; private set; }
        public InputAction ThrottleUp { get; private set; }
        public InputAction ThrottleDown { get; private set; }
        public InputAction Gun { get; private set; }
        public InputAction Hardpoint { get; private set; }
        public InputAction Switch { get; private set; }
        public InputAction RingSelect { get; private set; }
        public InputAction WeaponPrev { get; private set; }
        public InputAction WeaponNext { get; private set; }
        public InputAction Turn { get; private set; }
        public InputAction DebugToggle { get; private set; }
        public InputAction Restart { get; private set; }

        public ProtoControls(PadLayout layout) => Build(layout);

        /// <summary>가로 입력 (-1~1). 스틱·키보드 가로축 + 배치 3의 트리거 스로틀(RT 가속, LT 감속).</summary>
        public float Horizontal => Math.Clamp(
            Move.ReadValue<UnityEngine.Vector2>().x + ThrottleUp.ReadValue<float>() - ThrottleDown.ReadValue<float>(), -1f, 1f);

        public void SetLayout(PadLayout layout)
        {
            if (layout != Layout) Build(layout);
        }

        void Build(PadLayout layout)
        {
            _map?.Dispose();
            Layout = layout;
            _map = new InputActionMap("CombatProto");

            Move = _map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            var triggerThrottle = layout == PadLayout.Layout3;
            // 배치 3은 가로 입력을 트리거가 맡으므로 스틱 가로축을 끈다
            var stick = Move.AddBinding("<Gamepad>/leftStick");
            if (triggerThrottle) stick.WithProcessor("scaleVector2(x=0,y=1)");

            ThrottleUp = _map.AddAction("ThrottleUp", InputActionType.Value, expectedControlLayout: "Axis");
            ThrottleDown = _map.AddAction("ThrottleDown", InputActionType.Value, expectedControlLayout: "Axis");
            if (triggerThrottle)
            {
                ThrottleUp.AddBinding("<Gamepad>/rightTrigger");
                ThrottleDown.AddBinding("<Gamepad>/leftTrigger");
            }

            Gun = Button("Gun", "<Keyboard>/j", triggerThrottle ? "<Gamepad>/buttonSouth" : "<Gamepad>/rightTrigger");
            Hardpoint = Button("Hardpoint", "<Keyboard>/k", triggerThrottle ? "<Gamepad>/buttonEast" : "<Gamepad>/leftTrigger");
            Switch = Button("Switch", "<Keyboard>/l",
                layout == PadLayout.Layout2 ? "<Gamepad>/buttonNorth" : "<Gamepad>/leftShoulder");

            RingSelect = _map.AddAction("RingSelect", InputActionType.Value, expectedControlLayout: "Vector2");
            RingSelect.AddBinding("<Gamepad>/rightStick");

            WeaponPrev = Button("WeaponPrev", "<Keyboard>/q", triggerThrottle ? "<Gamepad>/buttonWest" : "<Gamepad>/dpad/left");
            WeaponNext = Button("WeaponNext", "<Keyboard>/e", triggerThrottle ? "<Gamepad>/buttonNorth" : "<Gamepad>/dpad/right");
            Turn = Button("Turn", "<Keyboard>/i", "<Gamepad>/rightShoulder");
            DebugToggle = Button("DebugToggle", "<Keyboard>/f1", "<Gamepad>/select");
            Restart = Button("Restart", "<Keyboard>/r", "<Gamepad>/start");

            _map.Enable();
        }

        InputAction Button(string name, params string[] bindings)
        {
            var action = _map.AddAction(name, InputActionType.Button);
            foreach (var path in bindings) action.AddBinding(path);
            return action;
        }

        public void Dispose()
        {
            _map?.Dispose();
            _map = null;
        }
    }
}
