using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
///     Add AutoEquipOnHitComponent to projectiles fired by weapons
///     if the weapon has the AddAutoEquipToProjectileComponent
/// </summary>
public sealed class AddAutoEquipToProjectileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddAutoEquipToProjectileComponent, AmmoShotEvent>(OnAmmoShot);
    }

    private void OnAmmoShot(Entity<AddAutoEquipToProjectileComponent> ent, ref AmmoShotEvent args)
    {
        foreach (var projectile in args.FiredProjectiles)
        {
            EnsureComp<AutoEquipOnHitComponent>(projectile);
        }
    }
}
