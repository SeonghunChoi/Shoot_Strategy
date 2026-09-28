using System;
using UnityEngine.InputSystem;

namespace Prototype.Combat
{
    /// <summary>
    /// 검증계획 4장의 입력 배치를 코드로 정의한다. 패드 배치가 바뀌면 액션 맵을 새로 만든다.
    /// 기체 전환의 탭/홀드 구분은 사용하는 쪽에서 누른 시간으로 판단한다.
    /// </summary>
    public sealed class ProtoControls : IDisposable
    {
        InputActionMap _map;

        public PadLayout Layout { get; private set; }

        public InputAction Move { get; private set; }
        public InputAction Gun { get; private set; }
        public InputAction Hardpoint { get; private set; }
        public InputAction Switch { get; private set; }
        public InputAction RingSelect { get; private set; }
        public InputAction WeaponPrev { get; private set; }
        public InputAction WeaponNext { get; private set; }
        public InputAction Turn { get; private set; }
        public InputAction DebugToggle { get; private set; }

        public ProtoControls(PadLayout layout) => Build(layout);

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
            Move.AddBinding("<Gamepad>/leftStick");

            Gun = Button("Gun", "<Keyboard>/j", "<Gamepad>/rightTrigger");
            Hardpoint = Button("Hardpoint", "<Keyboard>/k", "<Gamepad>/leftTrigger");
            Switch = Button("Switch", "<Keyboard>/l",
                layout == PadLayout.Layout1 ? "<Gamepad>/leftShoulder" : "<Gamepad>/buttonNorth");

            RingSelect = _map.AddAction("RingSelect", InputActionType.Value, expectedControlLayout: "Vector2");
            RingSelect.AddBinding("<Gamepad>/rightStick");

            WeaponPrev = Button("WeaponPrev", "<Keyboard>/q", "<Gamepad>/dpad/left");
            WeaponNext = Button("WeaponNext", "<Keyboard>/e", "<Gamepad>/dpad/right");
            Turn = Button("Turn", "<Keyboard>/i", "<Gamepad>/rightShoulder");
            DebugToggle = Button("DebugToggle", "<Keyboard>/f1", "<Gamepad>/select");

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
