using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype.Combat
{
    /// <summary>
    /// F1 / 패드 View(Select)로 여닫는 IMGUI 디버그 메뉴.
    /// CombatTuning의 public 필드를 리플렉션으로 그리므로, 조절 항목은 CombatTuning에 필드만 추가하면 된다.
    /// </summary>
    public sealed class DebugMenu : MonoBehaviour
    {
        CombatBootstrap _boot;
        FieldInfo[] _fields;
        bool _open;
        Rect _window = new(10, 10, 620, 720);
        Vector2 _scroll;
        readonly StringBuilder _sb = new();

        public void Init(CombatBootstrap boot)
        {
            _boot = boot;
            _fields = typeof(CombatTuning).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        void Update()
        {
            if (_boot != null && _boot.Controls.DebugToggle.WasPressedThisFrame()) _open = !_open;
        }

        void OnGUI()
        {
            if (!_open || _boot == null) return;

            ProtoGui.Begin();
            _window.height = Mathf.Min(720f, Screen.height - 20f);
            _window = GUILayout.Window(0x5EED, _window, DrawWindow, "Combat Prototype  (F1 / View)");
        }

        void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(_window.height - 30f)); // 가로 스크롤 없음
            if (GUILayout.Button("리셋 (R / Start)")) _boot.ResetRun();
            GUILayout.Space(8);
            DrawInputState();
            GUILayout.Space(8);
            DrawTuning();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        void DrawInputState()
        {
            var c = _boot.Controls;
            GUILayout.Label("입력 상태", ProtoGui.Bold);

            var device = c.Move.activeControl?.device ?? Gamepad.current as InputDevice ?? Keyboard.current;
            GUILayout.Label($"장치: {device?.displayName ?? "-"}   배치: {c.Layout}");
            GUILayout.Label($"Move: {c.Move.ReadValue<Vector2>():F2}   Ring: {c.RingSelect.ReadValue<Vector2>():F2}");
            GUILayout.Label($"가로 입력: {c.Horizontal:F2}   (RT {c.ThrottleUp.ReadValue<float>():F2} / LT {c.ThrottleDown.ReadValue<float>():F2})");

            _sb.Clear();
            Append(c.Gun); Append(c.Hardpoint); Append(c.Switch); Append(c.WeaponPrev);
            Append(c.WeaponNext); Append(c.Turn);
            GUILayout.Label("버튼: " + (_sb.Length > 0 ? _sb.ToString() : "-"));
        }

        void Append(InputAction action)
        {
            if (action.IsPressed()) _sb.Append(action.name).Append(' ');
        }

        void DrawTuning()
        {
            var tuning = _boot.Tuning;
            GUI.changed = false;

            foreach (var field in _fields)
            {
                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null)
                {
                    GUILayout.Space(6);
                    GUILayout.Label(header.header, ProtoGui.Bold);
                }

                var label = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? field.Name;
                var range = field.GetCustomAttribute<RangeAttribute>();
                var value = field.GetValue(tuning);

                switch (value)
                {
                    case float f:
                        field.SetValue(tuning, FloatRow(label, f, range));
                        break;
                    case int n:
                        field.SetValue(tuning, Mathf.RoundToInt(FloatRow(label, n, range, "0")));
                        break;
                    case bool b:
                        field.SetValue(tuning, GUILayout.Toggle(b, " " + label));
                        break;
                    case Enum e:
                        field.SetValue(tuning, EnumRow(label, e));
                        break;
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("기본값으로 초기화"))
            {
                var defaults = ScriptableObject.CreateInstance<CombatTuning>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), tuning);
                Destroy(defaults);
                GUI.changed = true;
            }

#if UNITY_EDITOR
            if (GUI.changed) UnityEditor.EditorUtility.SetDirty(tuning);
#endif
        }

        static float FloatRow(string label, float value, RangeAttribute range, string format = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(230));
            if (range != null)
                value = GUILayout.HorizontalSlider(value, range.min, range.max, GUILayout.ExpandWidth(true));
            GUILayout.Label(value.ToString(format), GUILayout.Width(64));
            GUILayout.EndHorizontal();
            return value;
        }

        static Enum EnumRow(string label, Enum value)
        {
            var type = value.GetType();
            var names = Enum.GetNames(type);
            var display = new string[names.Length];
            for (var i = 0; i < names.Length; i++)
                display[i] = type.GetField(names[i]).GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? names[i];

            GUILayout.Label(label);
            var index = Array.IndexOf(names, value.ToString());
            index = GUILayout.Toolbar(index, display);
            return (Enum)Enum.Parse(type, names[index]);
        }
    }
}
