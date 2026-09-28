using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 전투 프로토타입 진입점. 씬에는 이 오브젝트와 카메라·조명만 두고 나머지는 코드로 생성한다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CombatBootstrap : MonoBehaviour
    {
        [SerializeField] CombatTuning tuning;

        public CombatTuning Tuning => tuning;
        public ProtoControls Controls { get; private set; }

        void Awake()
        {
            if (tuning == null)
            {
                Debug.LogWarning("[CombatProto] CombatTuning이 지정되지 않아 기본값 인스턴스를 사용합니다.");
                tuning = ScriptableObject.CreateInstance<CombatTuning>();
            }

            Controls = new ProtoControls(tuning.padLayout);
            gameObject.AddComponent<DebugMenu>().Init(this);
        }

        void Update()
        {
            Controls.SetLayout(tuning.padLayout);
        }

        void OnDestroy()
        {
            Controls?.Dispose();
        }
    }
}
