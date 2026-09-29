using UnityEngine;

namespace Prototype.Combat
{
    public enum HardpointWeapon
    {
        [InspectorName("미사일")] Missile,
        [InspectorName("폭탄")] Bomb,
    }

    /// <summary>
    /// 기관포(무제한 연사)와 하드포인트 무장(미사일·폭탄) 발사. 장탄은 현재 기체(편대 상태)에 있고,
    /// 수치는 튜닝 기준값 × 기체 배율. 교대 무방비 중이거나 전멸 시 발사하지 않는다.
    /// </summary>
    public sealed class PlayerWeapons
    {
        readonly CombatContext _ctx;
        float _gunCooldown;
        float _hardpointCooldown;

        public HardpointWeapon Selected { get; private set; }

        public PlayerWeapons(CombatContext ctx) => _ctx = ctx;

        SquadronMember Member => _ctx.Player.Member;

        public int Ammo(HardpointWeapon weapon) => weapon == HardpointWeapon.Missile ? Member.Missiles : Member.Bombs;

        public bool Has(HardpointWeapon weapon) =>
            (weapon == HardpointWeapon.Missile ? Member.Def.Missiles : Member.Def.Bombs) > 0;

        /// <summary>교대 시: 쿨다운 초기화, 없는 무장이 선택돼 있으면 다른 무장으로.</summary>
        public void OnAircraftChanged()
        {
            _gunCooldown = 0f;
            _hardpointCooldown = 0f;
            if (!Has(Selected) && Has(Other(Selected))) Selected = Other(Selected);
        }

        public void Tick(float dt)
        {
            var c = _ctx.Controls;
            var t = _ctx.Tuning;
            var def = Member.Def;

            // 무장이 2종이므로 이전·다음 모두 교대 (없는 무장은 건너뜀)
            if ((c.WeaponPrev.WasPressedThisFrame() || c.WeaponNext.WasPressedThisFrame()) && Has(Other(Selected)))
                Selected = Other(Selected);

            _gunCooldown -= dt;
            _hardpointCooldown -= dt;
            var player = _ctx.Player;
            if (player.Down || player.Vulnerable || player.Turning || dt <= 0f) return;

            if (c.Gun.IsPressed() && _gunCooldown <= 0f)
            {
                _ctx.PlayerBullets.Spawn(player.Nose, new Vector2(player.Facing * t.gunBulletSpeed, 0f), 0.15f, t.gunDamage * def.GunDamageScale, 1.5f);
                _gunCooldown = 1f / (t.gunFireRate * def.GunRateScale);
            }

            if (c.Hardpoint.IsPressed() && _hardpointCooldown <= 0f && Ammo(Selected) > 0)
            {
                var pos = player.Position + new Vector2(0f, -0.5f * player.ModelScale);
                if (Selected == HardpointWeapon.Missile)
                {
                    _ctx.Ordnance.FireMissile(pos, player.Facing);
                    Member.Missiles--;
                    _hardpointCooldown = t.missileCooldown;
                }
                else
                {
                    _ctx.Ordnance.DropBomb(pos, t.bombDamage * def.BombDamageScale, t.bombRadius * def.BombRadiusScale, def.BombRadiusScale);
                    Member.Bombs--;
                    _hardpointCooldown = t.bombCooldown;
                }
            }
        }

        public static HardpointWeapon Other(HardpointWeapon w) =>
            w == HardpointWeapon.Missile ? HardpointWeapon.Bomb : HardpointWeapon.Missile;
    }
}
