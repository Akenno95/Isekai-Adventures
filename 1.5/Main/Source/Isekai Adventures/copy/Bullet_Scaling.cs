using Verse;
using RimWorld;
using UnityEngine;

namespace IsekaiAdventures
{
    public class Bullet_Scaling : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = base.Map;
            IntVec3 position = base.Position;

            // Optional: Effekt bei Schild-Block
            if (blockedByShield)
            {
                Destroy();
                return;
            }

            // Nur Schaden berechnen, wenn ein Ziel getroffen wurde
            if (hitThing != null && launcher is Pawn caster && def?.projectile != null)
            {
                var modExt = def.GetModExtension<ModExt_ProjectileScaling>();
                int dmg = DamageAmount;
                float pen = ArmorPenetration;

                if (modExt != null)
                {
                    if (modExt.damageStat != null)
                        dmg += Mathf.RoundToInt(caster.GetStatValue(modExt.damageStat) * modExt.damageMultiplier);

                    if (modExt.armorPenetrationStat != null)
                        pen += caster.GetStatValue(modExt.armorPenetrationStat) * modExt.armorPenetrationMultiplier;
                }

                var dinfo = new DamageInfo(
                    def.projectile.damageDef,
                    dmg,
                    pen,
                    ExactRotation.eulerAngles.y,
                    launcher,
                    null,
                    equipmentDef,
                    DamageInfo.SourceCategory.ThingOrUnknown,
                    intendedTarget.Thing
                );

                hitThing.TakeDamage(dinfo);
            }

            // visuelle Effekte und Zerstörung wie gewohnt
            if (def.projectile.landedEffecter != null)
            {
                def.projectile.landedEffecter.Spawn(position, map)?.Cleanup();
            }

            Destroy();
        }
    }
}
