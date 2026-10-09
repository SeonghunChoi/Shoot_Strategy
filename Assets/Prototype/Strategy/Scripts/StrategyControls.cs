using System;
using UnityEngine.InputSystem;

namespace Prototype.Strategy
{
    /// <summary>
    /// 검증계획 5장 조작을 코드로 정의한다. 패널(IMGUI)은 마우스 전용이다.
    /// </summary>
    public sealed class StrategyControls : IDisposable
    {
        readonly InputActionMap _map = new("StrategyProto");

        public InputAction Point { get; }
        public InputAction MouseSelect { get; }
        public InputAction PadSelect { get; }
        public InputAction Cancel { get; }
        public InputAction Pan { get; }
        public InputAction DragButton { get; }
        public InputAction DragDelta { get; }
        public InputAction Scroll { get; }
        public InputAction PadZoom { get; }
        public InputAction PadCursor { get; }
        public InputAction DebugToggle { get; }
        public InputAction EndTurn { get; }
        public InputAction Restart { get; }
        public InputAction PanelUp { get; }
        public InputAction PanelDown { get; }
        public InputAction TabPrev { get; }
        public InputAction TuningToggle { get; }
        public InputAction TabNext { get; }

        public StrategyControls()
        {
            Point = _map.AddAction("Point", InputActionType.Value, "<Mouse>/position", expectedControlLayout: "Vector2");
            MouseSelect = Button("MouseSelect", "<Mouse>/leftButton");
            PadSelect = Button("PadSelect", "<Gamepad>/buttonSouth");
            Cancel = Button("Cancel", "<Mouse>/rightButton", "<Keyboard>/escape", "<Gamepad>/buttonEast");

            Pan = _map.AddAction("Pan", InputActionType.Value, expectedControlLayout: "Vector2");
            Pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Pan.AddBinding("<Gamepad>/rightStick");

            DragButton = Button("DragButton", "<Mouse>/middleButton");
            DragDelta = _map.AddAction("DragDelta", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            Scroll = _map.AddAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");

            // RT 확대, LT 축소
            PadZoom = _map.AddAction("PadZoom", InputActionType.Value, expectedControlLayout: "Axis");
            PadZoom.AddCompositeBinding("1DAxis")
                .With("Positive", "<Gamepad>/rightTrigger")
                .With("Negative", "<Gamepad>/leftTrigger");

            PadCursor = _map.AddAction("PadCursor", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
            DebugToggle = Button("DebugToggle", "<Keyboard>/f1", "<Gamepad>/select");
            EndTurn = Button("EndTurn", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Gamepad>/start");
            Restart = Button("Restart", "<Keyboard>/r");
            PanelUp = Button("PanelUp", "<Gamepad>/dpad/up");
            PanelDown = Button("PanelDown", "<Gamepad>/dpad/down");
            TabPrev = Button("TabPrev", "<Keyboard>/q", "<Gamepad>/leftShoulder");
            TuningToggle = Button("TuningToggle", "<Keyboard>/f2");
            TabNext = Button("TabNext", "<Keyboard>/e", "<Gamepad>/rightShoulder");

            _map.Enable();
        }

        InputAction Button(string name, params string[] bindings)
        {
            var action = _map.AddAction(name, InputActionType.Button);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        public void Dispose() => _map.Dispose();
    }
}
