using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 편대 기체 정의 (검증계획 6장: 제공기·공격기·폭격기, 차이는 수치와 색). 수치를 바꿀 때는 이 파일만 고친다.
    /// 배율(Scale)은 CombatTuning의 기준값(= 공격기)에 곱한다. 장탄은 절대값.
    /// </summary>
    public sealed class AircraftDefinition
    {
        public string Name;
        public Color Color;
        public float ModelScale = 1f;
        public float HitScale = 1f;

        public float HpScale = 1f;
        public float VerticalSpeedScale = 1f;

        // 가로 입력: A 오프셋 이동 속도, B 감속·가속 폭
        public float OffsetSpeedScale = 1f;
        public float DecelRangeScale = 1f;
        public float AccelRangeScale = 1f;

        public float GunRateScale = 1f;
        public float GunDamageScale = 1f;
        public int Missiles;
        public int Bombs;
        public float BombDamageScale = 1f;
        public float BombRadiusScale = 1f;

        public static AircraftDefinition[] CreateSquadron() => new[]
        {
            new AircraftDefinition
            {
                Name = "제공기", Color = new Color(0.75f, 0.82f, 0.9f),
                HpScale = 0.8f, VerticalSpeedScale = 1.2f,
                OffsetSpeedScale = 1.3f, DecelRangeScale = 1f, AccelRangeScale = 1.3f,
                GunRateScale = 1.33f, GunDamageScale = 1.2f, Missiles = 8, Bombs = 0,
            },
            new AircraftDefinition
            {
                Name = "공격기", Color = new Color(0.55f, 0.6f, 0.4f),
                Missiles = 4, Bombs = 8,
            },
            new AircraftDefinition
            {
                Name = "폭격기", Color = new Color(0.5f, 0.46f, 0.4f), ModelScale = 1.3f, HitScale = 1.2f,
                HpScale = 1.4f, VerticalSpeedScale = 0.75f,
                OffsetSpeedScale = 0.7f, DecelRangeScale = 0.8f, AccelRangeScale = 0.6f,
                GunRateScale = 0.67f, GunDamageScale = 0.8f, Missiles = 0, Bombs = 6,
                BombDamageScale = 2f, BombRadiusScale = 1.6f,
            },
        };
    }

    /// <summary>편대 내 기체 1기의 출격 중 상태.</summary>
    public sealed class SquadronMember
    {
        public AircraftDefinition Def;
        public float Hp;
        public float MaxHp;
        public int Missiles;
        public int Bombs;
        public bool Down;
        public float ForegroundTime;

        public void Refit(float baseMaxHp)
        {
            MaxHp = baseMaxHp * Def.HpScale;
            Hp = MaxHp;
            Missiles = Def.Missiles;
            Bombs = Def.Bombs;
            Down = false;
        }
    }
}
