namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 전략 월드 상태. 로직 없는 데이터로 두고 규칙은 별도 서비스가 다룬다 (기술 설계 3.3 원칙만 차용).
    /// 거점·편대·세력은 다음 단계에서 추가한다.
    /// </summary>
    public sealed class World
    {
        public HexMap Map;
        public Rng Rng;
        public int Turn = 1;

        public static World Create(ulong seed) => new()
        {
            Map = HexMap.Parse(MapLayouts.Zone1),
            Rng = new Rng(seed),
        };
    }
}
