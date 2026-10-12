namespace Prototype.Strategy.Rules
{
    /// <summary>시작 배치 (MapLayouts.Zone1 기준, 오프셋 좌표로 적음).</summary>
    public static class StartingSetup
    {
        public static void Apply(World world)
        {
            // 해안 기지: 등급 2. 활주로 2등급(중형), 격납고, 정비소 — 영역은 등급에 따라 자동
            var coast = AddBase(world, "해안 기지", 3, 4, grade: 2);
            Put(coast, FacilityType.Runway, 2);
            Put(coast, FacilityType.Hangar);
            Put(coast, FacilityType.Maintenance);

            // 남부 전초기지: 등급 1. 격납고, 활주로 1등급
            var south = AddBase(world, "남부 전초기지", 4, 9, grade: 1);
            Put(south, FacilityType.Hangar);
            Put(south, FacilityType.Runway);

            // 기체 F-5 ×2, A-4 ×1 / 파일럿 네임드 1 + 일반 3 / 편대 2개 (해안 기지에 1·2편대)
            var f5a = SquadronRules.AddAircraft(world, AircraftType.F5);
            var f5b = SquadronRules.AddAircraft(world, AircraftType.F5);
            var a4 = SquadronRules.AddAircraft(world, AircraftType.A4);
            var ace = SquadronRules.AddPilot(world, "강민호", named: true, skill: 4, AircraftType.F5);
            var p1 = SquadronRules.AddPilot(world, "파일럿 1", named: false, skill: 2, AircraftType.F5);
            var p2 = SquadronRules.AddPilot(world, "파일럿 2", named: false, skill: 2, AircraftType.A4);
            SquadronRules.AddPilot(world, "파일럿 3", named: false, skill: 1, AircraftType.A4);

            var s1 = SquadronRules.AddSquadron(world, "1편대", coast);
            var s2 = SquadronRules.AddSquadron(world, "2편대", coast);
            SquadronRules.AttachAircraft(world, s1, f5a);
            SquadronRules.AttachAircraft(world, s1, a4);
            SquadronRules.AttachAircraft(world, s2, f5b);
            SquadronRules.AssignPilot(world, f5a, ace);
            SquadronRules.AssignPilot(world, a4, p2);
            SquadronRules.AssignPilot(world, f5b, p1);

            // 적 세력 1 동부군: 맵 가운데 동쪽의 전진 기지(해안 기지에서 거리 9) + 부대 3개
            world.Factions.Add(new Faction { Id = 1, Name = "동부군" });
            var east = AddBase(world, "동부군 전진 기지", 11, 6, grade: 2);
            east.Owner = 1;
            east.AntiAir = 20;
            east.Structures = 30;
            world.GoalBaseId = east.Id; // 상부 장기 목표: 동부군 전진 기지 점령 (승리 조건)
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "동부군 A부대", Hex = Hex.FromOffset(6, 3), Air = 15, Ground = 10, Owner = 1 });
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "동부군 B부대", Hex = Hex.FromOffset(8, 10), Air = 8, Ground = 20, Owner = 1 });
            // 기지 서쪽 인접 칸의 경비대: 기지와 작전 지역이 겹쳐 출격 순서(S2)를 시험할 수 있다
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "동부군 경비대", Hex = Hex.FromOffset(10, 6), Air = 10, Ground = 12, Owner = 1 });

            // 적 세력 2 북부군: 북동 산악 너머 기지 + 부대 2개. 동부군과도 적대
            world.Factions.Add(new Faction { Id = 2, Name = "북부군" });
            var north = AddBase(world, "북부군 기지", 12, 1, grade: 2);
            north.Owner = 2;
            north.AntiAir = 15;
            north.Structures = 25;
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "북부군 1부대", Hex = Hex.FromOffset(11, 3), Air = 12, Ground = 12, Owner = 2 });
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "북부군 2부대", Hex = Hex.FromOffset(13, 3), Air = 6, Ground = 16, Owner = 2 });

            // 자원 타일: 맵의 유전·산악·평지. 대부분 중립, 적 기지 근처 두 곳만 적 소유 (지상군이 들어가면 점령)
            AddSite(world, SiteType.Refinery, "서부 유전", 5, 5, grade: 1, ResourceSite.Neutral);
            AddSite(world, SiteType.Refinery, "동부 유전", 15, 5, grade: 2, owner: 1);
            AddSite(world, SiteType.Mine, "중부 광산", 7, 7, grade: 1, ResourceSite.Neutral);
            AddSite(world, SiteType.Mine, "북부 광산", 10, 1, grade: 1, owner: 2);
            AddSite(world, SiteType.City, "남부 마을", 6, 9, grade: 1, ResourceSite.Neutral);
            AddSite(world, SiteType.City, "중앙 도시", 9, 4, grade: 2, ResourceSite.Neutral);
        }

        static Base AddBase(World world, string name, int col, int row, int grade)
        {
            var b = new Base
            {
                Id = world.Bases.Count + 1,
                Name = name,
                Hex = Hex.FromOffset(col, row),
                Grade = grade,
                Owner = World.Player,
            };
            world.Bases.Add(b);
            return b;
        }

        static void Put(Base b, FacilityType type, int grade = 1) => b.Facilities.Add(new Facility { Type = type, Grade = grade });

        static void AddSite(World world, SiteType type, string name, int col, int row, int grade, int owner) =>
            world.Sites.Add(new ResourceSite { Id = world.NextId++, Type = type, Name = name, Hex = Hex.FromOffset(col, row), Grade = grade, Owner = owner });
    }
}
