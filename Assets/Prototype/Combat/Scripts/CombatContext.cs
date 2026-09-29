namespace Prototype.Combat
{
    /// <summary>전투 시스템들이 서로를 참조하는 통로. CombatBootstrap이 채운다.</summary>
    public sealed class CombatContext
    {
        public CombatTuning Tuning;
        public ProtoControls Controls;
        public ScrollController Scroll;
        public ScrollWorld World;
        public CameraRig Camera;
        public PlayerShip Player;
        public PlayerWeapons Weapons;
        public Squadron Squadron;
        public BulletPool PlayerBullets;
        public BulletPool EnemyBullets;
        public EnemySystem Enemies;
        public OrdnanceSystem Ordnance;
        public Effects Effects;
        public StageRunner Stage;
        public System.Random Rng = new(42);

        public float MapHeight;
        public int AirKills;
        public int GroundKills;
        public int TerrainHits;
        public int PlayerDowns;

        public float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
    }
}
