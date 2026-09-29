using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    public enum TerrainStyle
    {
        Flat,
        Hills,
        Mountains,
    }

    /// <summary>스테이지 구간 1개. 거리는 지면 좌표(m), 등장 간격 0은 등장 없음.</summary>
    public sealed class StageSection
    {
        public string Name;
        public float Length;
        public TerrainStyle Style;
        public Color Color;
        public float AirInterval;
        public float GroundInterval;
        public float[] AirWeights = { 1f, 1f, 1f }; // 직선, 곡선, 추적
        public float AntiAirRatio = 0.5f;          // 지상 목표 중 대공포 비율
        public bool IsTurnZone;

        public float Start { get; set; }
        public float End => Start + Length;
    }

    /// <summary>
    /// 테스트 스테이지 (검증계획 6장: 평지 → 산악 → 선회 구역). 수치를 바꿀 때는 이 파일만 고친다.
    /// </summary>
    public static class StageDefinition
    {
        public static List<StageSection> CreateTestStage()
        {
            var sections = new List<StageSection>
            {
                new()
                {
                    Name = "평지", Length = 700f, Style = TerrainStyle.Hills, Color = new Color(0.45f, 0.55f, 0.3f),
                    AirInterval = 3f, GroundInterval = 2.5f, AirWeights = new[] { 2f, 1f, 0.5f }, AntiAirRatio = 0.4f,
                },
                new()
                {
                    Name = "산악", Length = 900f, Style = TerrainStyle.Mountains, Color = new Color(0.5f, 0.44f, 0.38f),
                    AirInterval = 1.6f, GroundInterval = 5f, AirWeights = new[] { 1f, 1.5f, 1.5f }, AntiAirRatio = 0.8f,
                },
                new()
                {
                    Name = "선회 구역", Length = 300f, Style = TerrainStyle.Flat, Color = new Color(0.42f, 0.42f, 0.5f),
                    IsTurnZone = true,
                },
            };

            var start = 0f;
            foreach (var s in sections)
            {
                s.Start = start;
                start += s.Length;
            }
            return sections;
        }
    }
}
