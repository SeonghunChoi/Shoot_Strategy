using System;
using System.IO;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 빌드 확인용: 실행 인자 -protoAutoShot &lt;폴더&gt; 가 있으면 몇 초 간격으로 스크린샷을 찍고 종료한다.
    /// 예) ShootStrategyCombat.exe -protoAutoShot C:\temp\shots -screen-width 1280 -screen-height 720 -screen-fullscreen 0
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        static readonly float[] ShotTimes = { 3f, 8f, 14f };

        string _folder;
        int _next;
        float _time;

        public static void AttachIfRequested(GameObject host)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-protoAutoShot");
            if (i < 0 || i + 1 >= args.Length) return;
            host.AddComponent<AutoScreenshot>()._folder = args[i + 1];
        }

        void Start()
        {
            Directory.CreateDirectory(_folder);
            Debug.Log($"[CombatProto] 자동 스크린샷: {_folder}");
        }

        void Update()
        {
            _time += Time.unscaledDeltaTime;
            if (_next < ShotTimes.Length && _time >= ShotTimes[_next])
            {
                var path = Path.Combine(_folder, $"shot_{_next + 1}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[CombatProto] 스크린샷 {path}");
                _next++;
            }
            else if (_next >= ShotTimes.Length && _time >= ShotTimes[^1] + 1f)
            {
                Application.Quit();
            }
        }
    }
}
