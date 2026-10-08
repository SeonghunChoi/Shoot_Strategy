namespace Prototype.Strategy.Rules
{
    /// <summary>시작 배치 (MapLayouts.Zone1 기준, 오프셋 좌표로 적음).</summary>
    public static class StartingSetup
    {
        public static void Apply(World world)
        {
            // 해안 기지: 등급 2, 반경 1 영역. 활주로 2칸 직선(북쪽 두 칸), 격납고, 정비소
            var coast = AddBase(world, "해안 기지", 3, 4, grade: 2);
            foreach (var h in Hex.Range(coast.Hex, 1)) world.Territory[h] = coast.Id;
            Put(world, 2, 3, FacilityType.Runway);
            Put(world, 3, 3, FacilityType.Runway);
            Put(world, 4, 4, FacilityType.Hangar);
            Put(world, 3, 5, FacilityType.Maintenance);

            // 남부 전초기지: 등급 1, 3칸 영역. 격납고, 활주로 1칸
            var south = AddBase(world, "남부 전초기지", 4, 9, grade: 1);
            world.Territory[Hex.FromOffset(3, 9)] = south.Id;
            world.Territory[Hex.FromOffset(5, 9)] = south.Id;
            Put(world, 3, 9, FacilityType.Hangar);
            Put(world, 5, 9, FacilityType.Runway);

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

            // 적 (4단계: 고정 표적). 맵 가운데 동쪽의 전진 기지(해안 기지에서 거리 9, 전초기지 하나로 닿음) + 평지 부대 2개
            var enemy = AddBase(world, "적 전진 기지", 11, 6, grade: 2);
            enemy.Owner = 1;
            enemy.AntiAir = 20;
            enemy.Structures = 30;
            foreach (var h in Hex.Range(enemy.Hex, 1)) world.Territory[h] = enemy.Id;
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "적 A부대", Hex = Hex.FromOffset(6, 3), Air = 15, Ground = 10 });
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "적 B부대", Hex = Hex.FromOffset(7, 8), Air = 8, Ground = 20 });
            // 기지 서쪽 인접 칸의 경비대: 기지와 작전 지역이 겹쳐 출격 순서(S2)를 시험할 수 있다
            world.EnemyUnits.Add(new EnemyUnit { Id = world.NextId++, Name = "적 경비대", Hex = Hex.FromOffset(10, 6), Air = 10, Ground = 12 });
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
            world.Territory[b.Hex] = b.Id;
            return b;
        }

        static void Put(World world, int col, int row, FacilityType type) =>
            world.Facilities[Hex.FromOffset(col, row)] = new Facility { Type = type };
    }
}
