using System.Collections.Generic;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 튜닝 메뉴 (F2). Balance·기체 정의의 [Tune] 필드를 그룹별 슬라이더·토글로 그린다. 마우스 전용.
    /// 테스터 이름, 튜닝 값 내보내기, 기본값 초기화, 로그 폴더 열기도 여기 둔다.
    /// </summary>
    public sealed class StrategyTuningMenu : MonoBehaviour
    {
        const float Width = 520f;

        StrategyBootstrap _boot;
        Rect _rect;
        Vector2 _scroll;
        readonly HashSet<string> _open = new() { "경제" };
        string _status;
        bool _showImport;
        System.Action _pending;

        public bool Visible { get; set; }

        public void Init(StrategyBootstrap boot)
        {
            _boot = boot;
            _rect = new Rect(Screen.width * 0.5f - Width * 0.5f, 60f, Width, Mathf.Min(640f, Screen.height - 100f));
        }

        public void Toggle() => Visible = !Visible;

        /// <summary>화면 좌표(아래가 0)가 메뉴 위인지.</summary>
        public bool IsOver(Vector2 screen) => Visible && _rect.Contains(new Vector2(screen.x, Screen.height - screen.y));

        void OnGUI()
        {
            if (!Visible || _boot == null) return;
            ProtoGui.Begin();
            if (Event.current.type == EventType.Layout)
            {
                var p = _pending;
                _pending = null;
                p?.Invoke();
            }
            GUI.depth = -10;
            HandleDrag();
            ProtoGui.Panel(_rect, opaque: true);
            DrawContent();
        }

        bool _dragging;
        Vector2 _dragOffset;

        /// <summary>제목 줄(위 26px)을 끌어 옮긴다. GUI.Window는 내용이 흐리게 그려져 쓰지 않는다.</summary>
        void HandleDrag()
        {
            var e = Event.current;
            var title = new Rect(_rect.x, _rect.y, _rect.width, 26f);
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && title.Contains(e.mousePosition):
                    _dragging = true;
                    _dragOffset = e.mousePosition - _rect.position;
                    e.Use();
                    break;
                case EventType.MouseDrag when _dragging:
                    _rect.position = e.mousePosition - _dragOffset;
                    _rect.x = Mathf.Clamp(_rect.x, 0f, Screen.width - 60f);
                    _rect.y = Mathf.Clamp(_rect.y, 0f, Screen.height - 40f);
                    e.Use();
                    break;
                case EventType.MouseUp when _dragging:
                    _dragging = false;
                    e.Use();
                    break;
            }
        }

        void DrawContent()
        {
            var bal = _boot.Balance;
            var log = _boot.Log;
            GUILayout.BeginArea(new Rect(_rect.x + 12f, _rect.y + 8f, _rect.width - 24f, _rect.height - 16f));
            GUILayout.Label($"튜닝 (F2로 닫기)  ·  기본값과 다른 항목 {Tuning.ChangedCount(bal)}개", ProtoGui.Bold);
            GUILayout.Label("제목 줄을 끌어 옮길 수 있음. 바로 적용되며, '재시작' 표시 항목은 R로 다시 시작해야 반영", ProtoGui.Small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("테스터 이름", GUILayout.Width(80));
            var tester = GUILayout.TextField(log.Tester ?? "", 32);
            if (tester != log.Tester) _boot.SetTester(tester);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("튜닝 값 내보내기")) _pending = () => _status = $"저장: {log.ExportTuning(bal)}";
            if (GUILayout.Button("기본값으로 초기화")) _pending = () => { Tuning.ResetToDefaults(bal); _status = "기본값으로 되돌림"; };
            if (GUILayout.Button("로그 폴더 열기")) _pending = () => _boot.OpenLogFolder();
            GUILayout.EndHorizontal();

            // 가져오기: 로그 폴더의 tuning_*.md 중 하나를 골라 적용 (기본값으로 되돌린 뒤 파일 값 적용)
            if (GUILayout.Button(_showImport ? "▼ 튜닝 값 가져오기" : "▶ 튜닝 값 가져오기", GUI.skin.label)) _pending = () => _showImport = !_showImport;
            if (_showImport)
            {
                var files = log.TuningFiles();
                if (files.Length == 0) GUILayout.Label("가져올 파일 없음 — 로그 폴더에 tuning_*.md를 넣어 주세요", ProtoGui.Small);
                foreach (var path in files.Take(8))
                {
                    var file = path;
                    if (GUILayout.Button(System.IO.Path.GetFileName(file)))
                        _pending = () =>
                        {
                            var (applied, unknown) = log.ImportTuning(bal, file);
                            _status = $"가져옴: {System.IO.Path.GetFileName(file)} — {applied}개 적용" +
                                      (unknown.Count > 0 ? $", 모르는 항목 {unknown.Count}개 ({string.Join(", ", unknown.Take(3))}{(unknown.Count > 3 ? " …" : "")})" : "") +
                                      ". 시작 값(재시작 표시)은 R로 다시 시작해야 반영";
                        };
                }
            }
            GUILayout.Label($"판 {log.RunId}  ·  기록한 턴 {log.TurnsLogged}  ·  이번 턴 {_boot.TurnSeconds:0}초", ProtoGui.Small);
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, ProtoGui.Small);

            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            var defaults = Tuning.Items(new Balance());
            var items = Tuning.Items(bal);
            foreach (var group in items.Select((it, i) => (it, i)).GroupBy(x => x.it.Group))
            {
                var open = _open.Contains(group.Key);
                var changed = group.Count(x => !Equals(x.it.Value, defaults[x.i].Value));
                if (GUILayout.Button($"{(open ? "▼" : "▶")} {group.Key}{(changed > 0 ? $"  ({changed} 변경)" : "")}", GUI.skin.label))
                {
                    var key = group.Key;
                    _pending = () => { if (!_open.Remove(key)) _open.Add(key); };
                }
                if (!open) continue;
                foreach (var (it, i) in group) DrawItem(it, defaults[i].Value);
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 숫자 입력칸: 편집 중인 항목의 글자 (Enter·초점 이동 시 반영)
        string _editKey;
        string _editText;

        /// <summary>한 줄: 라벨 · [−] 슬라이더 [+] · 입력칸 · 기본값.</summary>
        void DrawItem(Tuning.Item it, object def)
        {
            var label = it.Label + (it.Tune.Restart ? " (재시작)" : "");
            var changed = !Equals(it.Value, def);
            var style = changed ? ProtoGui.Bold : ProtoGui.Small;
            GUILayout.BeginHorizontal();
            if (it.Value is bool b)
            {
                var nb = GUILayout.Toggle(b, " " + label);
                if (nb != b) it.Field.SetValue(it.Target, nb);
                GUILayout.EndHorizontal();
                return;
            }

            var isInt = it.Value is int;
            var value = isInt ? (int)it.Value : (float)it.Value;
            var step = Step(it.Tune, isInt);
            GUILayout.Label(label, style, GUILayout.Width(170));
            if (GUILayout.Button("−", GUILayout.Width(22))) value -= step;
            var slid = GUILayout.HorizontalSlider(value, it.Tune.Min, it.Tune.Max, GUILayout.Width(110));
            if (!Mathf.Approximately(slid, value) && Mathf.Abs(slid - value) > step * 0.5f) value = slid;
            if (GUILayout.Button("+", GUILayout.Width(22))) value += step;

            // 입력칸: 슬라이더 범위를 넘는 값도 직접 넣을 수 있다
            var key = it.Group + "/" + it.Label;
            var shown = _editKey == key ? _editText : Tuning.Format(it.Value);
            GUI.SetNextControlName(key);
            var typed = GUILayout.TextField(shown, GUILayout.Width(60));
            var focused = GUI.GetNameOfFocusedControl() == key;
            if (focused)
            {
                _editKey = key;
                _editText = typed;
                var e = Event.current;
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (TryParse(typed, out var parsed)) value = parsed;
                    _editKey = null;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                }
            }
            else if (_editKey == key)
            {
                // 초점이 떠나면 반영
                if (TryParse(_editText, out var parsed)) value = parsed;
                _editKey = null;
            }
            GUILayout.Label(Tuning.Format(def), ProtoGui.Small, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            if (isInt)
            {
                var ni = Mathf.RoundToInt(value);
                if (ni != (int)it.Value) it.Field.SetValue(it.Target, ni);
            }
            else
            {
                var nf = Mathf.Round(value * 1000f) / 1000f;
                if (!Mathf.Approximately(nf, (float)it.Value)) it.Field.SetValue(it.Target, nf);
            }
        }

        /// <summary>± 버튼 간격: 정수는 범위 100 이하면 1, 그보다 크면 범위의 1/100을 5·10 단위로. 실수는 범위에 따라 0.01·0.05·0.1.</summary>
        static float Step(TuneAttribute t, bool isInt)
        {
            var range = t.Max - t.Min;
            if (isInt) return range <= 100f ? 1f : range <= 500f ? 5f : range <= 2000f ? 10f : 50f;
            return range <= 1.5f ? 0.01f : range <= 5f ? 0.05f : 0.1f;
        }

        static bool TryParse(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
    }
}
