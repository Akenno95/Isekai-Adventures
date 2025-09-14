using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib; // AccessTools

namespace IsekaiAdventures
{
    public class Bullet_Scaling : Bullet
    {
        static int GetRawBaseDamage(ThingDef projDef, int fallback)
        {
            var props = projDef?.projectile;
            if (props != null)
            {
                var f = AccessTools.Field(props.GetType(), "damageAmountBase")
                        ?? AccessTools.Field(props.GetType(), "damageAmount");
                if (f != null)
                {
                    try { return (int)f.GetValue(props); } catch { }
                }
            }
            int dd = projDef?.projectile?.damageDef?.defaultDamage ?? 0;
            if (dd > 0) return dd;
            return Mathf.Max(1, fallback);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = base.Map;
            IntVec3 position = base.Position;

            if (blockedByShield)
            {
                Destroy();
                return;
            }

            if (hitThing != null && launcher is Pawn caster && def?.projectile != null)
            {
                var ext = def.GetModExtension<ModExt_ProjectileScaling>();

                // 1) Roh-Basis
                int baseDmg = GetRawBaseDamage(def, base.DamageAmount);

                // 2) Flat-Bonus
                float flatBonus = 0f;
                if (ext?.flatBonusStat != null)
                    flatBonus = caster.GetStatValue(ext.flatBonusStat) * ext.flatBonusFactor;

                // 3) Multiplier-Stat (auf Base+Flat)
                float mult = 1f;
                if (ext?.damageMultStat != null)
                    mult = caster.GetStatValue(ext.damageMultStat) * ext.damageMultScale;

                // 4) Finale Formel: (Base + Flat) * Mult
                int finalDmg = Mathf.Max(1, Mathf.RoundToInt((baseDmg + flatBonus) * mult));

                // 5) Armor Penetration
                float pen = ArmorPenetration;
                if (ext?.armorPenetrationStat != null)
                    pen += caster.GetStatValue(ext.armorPenetrationStat) * ext.armorPenetrationFactor;

                var dinfo = new DamageInfo(
                    def.projectile.damageDef,
                    finalDmg,
                    pen,
                    -1f,
                    launcher,
                    null,
                    equipmentDef,
                    DamageInfo.SourceCategory.ThingOrUnknown
                );
                hitThing.TakeDamage(dinfo);
            }

            if (def.projectile.landedEffecter != null)
                def.projectile.landedEffecter.Spawn(position, map)?.Cleanup();

            Destroy();
        }
    }
}
