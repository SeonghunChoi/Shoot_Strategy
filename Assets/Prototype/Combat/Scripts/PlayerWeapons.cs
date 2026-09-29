using UnityEngine;

namespace Prototype.Combat
{
    public enum HardpointWeapon
    {
        [InspectorName("미사일")] Missile,
        [InspectorName("폭탄")] Bomb,
    }

    /// <summary>기관포(무제한 연사)와 하드포인트 무장(미사일·폭탄, 장탄 제한) 발사.</summary>
    public sealed class PlayerWeapons
    {
        readonly CombatContext _ctx;
        float _gunCooldown;
        float _hardpointCooldown;

        public HardpointWeapon Selected { get; private set; }
        public int MissileAmmo { get; private set; }
        public int BombAmmo { get; private set; }

        public PlayerWeapons(CombatContext ctx) => _ctx = ctx;

        public int Ammo(HardpointWeapon weapon) => weapon == HardpointWeapon.Missile ? MissileAmmo : BombAmmo;

        public void Reset()
        {
            MissileAmmo = _ctx.Tuning.missileAmmo;
            BombAmmo = _ctx.Tuning.bombAmmo;
            _gunCooldown = 0f;
            _hardpointCooldown = 0f;
        }

        public void Tick(float dt)
        {
            var c = _ctx.Controls;
            var t = _ctx.Tuning;

            // 무장이 2종이므로 이전·다음 모두 교대
            if (c.WeaponPrev.WasPressedThisFrame() || c.WeaponNext.WasPressedThisFrame())
                Selected = Selected == HardpointWeapon.Missile ? HardpointWeapon.Bomb : HardpointWeapon.Missile;

            _gunCooldown -= dt;
            _hardpointCooldown -= dt;
            if (_ctx.Player.Down || dt <= 0f) return;

            if (c.Gun.IsPressed() && _gunCooldown <= 0f)
            {
                _ctx.PlayerBullets.Spawn(_ctx.Player.Nose, new Vector2(t.gunBulletSpeed, 0f), 0.15f, t.gunDamage, 1.5f);
                _gunCooldown = 1f / t.gunFireRate;
            }

            if (c.Hardpoint.IsPressed() && _hardpointCooldown <= 0f && Ammo(Selected) > 0)
            {
                var pos = _ctx.Player.Position + new Vector2(0f, -0.5f);
                if (Selected == HardpointWeapon.Missile)
                {
                    _ctx.Ordnance.FireMissile(pos);
                    MissileAmmo--;
                    _hardpointCooldown = t.missileCooldown;
                }
                else
                {
                    _ctx.Ordnance.DropBomb(pos);
                    BombAmmo--;
                    _hardpointCooldown = t.bombCooldown;
                }
            }
        }
    }
}
