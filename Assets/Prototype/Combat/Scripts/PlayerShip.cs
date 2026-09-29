using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 화면 가로 위치에 고정되고 세로로만 이동하는 전경 기체 (검증계획 3.3).
    /// 편대 기체별 모델을 미리 만들어 두고 교대 시 켜고 끈다. 체력·장탄은 Member(편대 상태)에 있다.
    /// </summary>
    public sealed class PlayerShip
    {
        const float EdgeMargin = 1f;
        const float BounceDecay = 6f;
        const float BaseHitRadius = 0.5f;

        readonly CombatTuning _tuning;
        readonly Transform _root;
        readonly Transform[] _models;
        Transform _model;
        float _tilt;
        float _invuln;
        float _bounceVelocity;

        public SquadronMember Member { get; private set; }
        public float Y { get; private set; }
        public float VelocityY { get; private set; }
        public float Hp => Member.Hp;
        public float HitRadius => BaseHitRadius * Member.Def.HitScale;
        public float ModelScale => Member.Def.ModelScale;
        public Vector2 Position => _root.position;
        public Vector2 Nose => Position + new Vector2(Facing * 1.3f * ModelScale, 0f);
        public Transform CurrentModel => _model;

        /// <summary>기수 방향 (+1 오른쪽, -1 왼쪽). 부트스트랩이 진행 방향으로 설정.</summary>
        public int Facing { get; set; } = 1;

        /// <summary>선회 중: 조작 불능·무적 (검증계획 3.5).</summary>
        public bool Turning { get; set; }

        /// <summary>조종 가능한 기체가 없음 (편대 전멸).</summary>
        public bool Down { get; set; }

        /// <summary>교대 직후 무방비 (발사 불가, 이동 감속). Squadron이 설정.</summary>
        public bool Vulnerable { get; set; }

        public bool CanBeHit => !Down && !Turning && _invuln <= 0f && !_tuning.playerInvincible;

        public PlayerShip(CombatTuning tuning, AircraftDefinition[] defs)
        {
            _tuning = tuning;
            _root = new GameObject("PlayerShip").transform;
            _models = new Transform[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                _models[i] = BuildModel(defs[i], _root);
                _models[i].gameObject.SetActive(false);
            }
        }

        /// <summary>기체 모델 생성 (교대 퇴장 연출에서도 사용).</summary>
        public static Transform BuildModel(AircraftDefinition def, Transform parent)
        {
            var model = new GameObject(def.Name).transform;
            model.SetParent(parent, false);
            model.localScale = Vector3.one * def.ModelScale;

            // 기수 = +X
            var color = def.Color;
            var body = Shapes.Create(PrimitiveType.Capsule, color, model, Vector3.zero, new Vector3(0.6f, 1.2f, 0.6f));
            body.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Shapes.Create(PrimitiveType.Cube, color * 0.8f, model, new Vector3(-0.1f, 0f, 0f), new Vector3(0.6f, 0.08f, 2.2f));
            Shapes.Create(PrimitiveType.Cube, color * 0.8f, model, new Vector3(-1f, 0.35f, 0f), new Vector3(0.35f, 0.6f, 0.08f));
            Shapes.Create(PrimitiveType.Sphere, new Color(0.2f, 0.25f, 0.35f), model, new Vector3(0.45f, 0.22f, 0f), new Vector3(0.45f, 0.25f, 0.3f));
            return model;
        }

        public void SetAircraft(int index, SquadronMember member)
        {
            Member = member;
            if (_model != null) _model.gameObject.SetActive(false);
            _model = _models[index];
            _model.localRotation = Quaternion.Euler(0f, 0f, _tilt);
        }

        public void Reset(float y)
        {
            Y = y;
            _tilt = 0f;
            _invuln = 0f;
            _bounceVelocity = 0f;
            Down = false;
            Vulnerable = false;
        }

        /// <summary>현재 기체에 피해를 적용한다. 이번 피해로 격추되면 true.</summary>
        public bool Damage(float amount)
        {
            if (!CanBeHit || Member.Down) return false;
            Member.Hp -= amount;
            _invuln = _tuning.hitInvulnTime;
            if (Member.Hp > 0f) return false;
            Member.Hp = 0f;
            Member.Down = true;
            _invuln = 0f;
            return true;
        }

        /// <summary>지형 밖으로 dy만큼 밀어내고 세로 튕김 속도를 준다.</summary>
        public void Push(float dy, float bounceVelocity)
        {
            Y += dy;
            _bounceVelocity = bounceVelocity;
            _root.position = new Vector3(_root.position.x, Y, 0f);
        }

        /// <summary>위치를 강제로 옮긴다.</summary>
        public void Place(float y)
        {
            Y = y;
            _root.position = new Vector3(_root.position.x, Y, 0f);
        }

        /// <param name="visualDirection">+1 오른쪽 ~ -1 왼쪽. 선회 중에는 그 사이 값 (기체가 뒤집힘)</param>
        /// <param name="turnProgress">선회 진행 0~1 (위로 호를 그리는 연출용)</param>
        public void Tick(float input, float dt, float mapHeight, float screenX, float visualDirection, float turnProgress)
        {
            if (Down || Turning) input = 0f;
            _invuln = Mathf.Max(0f, _invuln - dt);

            var speed = _tuning.verticalSpeed * Member.Def.VerticalSpeedScale * (Vulnerable ? _tuning.vulnerableMoveScale : 1f);
            var previousY = Y;
            Y = Mathf.Clamp(Y + (input * speed + _bounceVelocity) * dt, EdgeMargin, mapHeight - EdgeMargin);
            VelocityY = dt > 0f ? (Y - previousY) / dt : 0f;
            _bounceVelocity *= Mathf.Exp(-BounceDecay * dt);
            _root.position = new Vector3(screenX, Y, 0f);

            // 방향: 기체를 Y축으로 돌려 뒤집는다. 선회 중에는 위로 호를 그리며 기수를 든다
            _root.rotation = Quaternion.Euler(0f, Mathf.Lerp(180f, 0f, (visualDirection + 1f) * 0.5f), 0f);
            var arc = Mathf.Sin(Mathf.PI * turnProgress);
            _model.localPosition = new Vector3(0f, arc * 3f, 0f);

            _tilt = Mathf.Lerp(_tilt, input * _tuning.shipTiltAngle, 1f - Mathf.Exp(-10f * dt));
            _model.localRotation = Quaternion.Euler(0f, 0f, _tilt + arc * 60f);

            // 전멸 시 숨김, 피격 무적·교대 무방비 중 깜빡임
            var blink = (_invuln > 0f || Vulnerable) && Mathf.Repeat(Time.unscaledTime * 15f, 2f) >= 1f;
            _model.gameObject.SetActive(!Down && !blink);
        }
    }
}
