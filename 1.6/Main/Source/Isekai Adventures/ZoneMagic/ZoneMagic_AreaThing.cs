// ZoneMagicArea.cs (FULL FILE) - layered visuals via DefModExtension, per-layer shaders supported
// Namespace: IsekaiAdventures
//
// Features:
// - One map Thing (ZoneMagicArea), no extra overlay Things.
// - Visual layers defined in XML via DefModExtension (modExtensions).
// - Unlimited layers: each layer has texPath + color + scaleMul + yOffset + OPTIONAL shaderType.
// - shaderType per layer supports:
//     - Custom Unity shader name via Shader.Find (e.g. "BS_EnergyShader")
//     - RimWorld ShaderTypeDef names via DefDatabase<ShaderTypeDef> (e.g. "Transparent", "Cutout", "CutoutComplex")
// - Color routing:
//     - If shader/material has _DrawColor, set it (for BS_EnergyShader-like shaders).
//     - Also sets _Color when present (normal shaders), otherwise uses mat.color.
// - Radius-based scaling: diameter = radius * 2, robust using mesh.bounds.
// - Keeps your enter/inside/exit logic + hediff/mental + immunity checks.
//
// XML EXAMPLE (put in ThingDef modExtensions):
// <modExtensions>
//   <li Class="IsekaiAdventures.ZoneMagicVisualLayersExtension">
//     <visualLayers>
//       <li>
//         <texPath>Things/Zone/Zone_In</texPath>
//         <shaderType>Transparent</shaderType>
//         <color>(0.10, 0.40, 1, 0.45)</color>
//         <scaleMul>1.00</scaleMul>
//         <yOffset>0.000</yOffset>
//       </li>
//       <li>
//         <texPath>Things/Zone/Zone_Out</texPath>
//         <shaderType>BS_EnergyShader</shaderType>
//         <color>(0.15, 0.55, 1, 0.60)</color>
//         <scaleMul>1.02</scaleMul>
//         <yOffset>0.002</yOffset>
//       </li>
//     </visualLayers>
//   </li>
// </modExtensions>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    // -----------------------------
    // DefModExtension for visuals
    // -----------------------------
    public class ZoneVisualLayer : IExposable
    {
        public string texPath;
        public Color color = Color.white;
        public float scaleMul = 1f;
        public float yOffset = 0f;

        // OPTIONAL: "Transparent", "Cutout", "CutoutComplex", "BS_EnergyShader", etc.
        public string shaderType;

        public void ExposeData()
        {
            Scribe_Values.Look(ref texPath, "texPath");
            Scribe_Values.Look(ref color, "color", Color.white);
            Scribe_Values.Look(ref scaleMul, "scaleMul", 1f);
            Scribe_Values.Look(ref yOffset, "yOffset", 0f);
            Scribe_Values.Look(ref shaderType, "shaderType");
        }
    }

    public class ZoneMagicVisualLayersExtension : DefModExtension
    {
        public List<ZoneVisualLayer> visualLayers = new List<ZoneVisualLayer>();
    }

    // -----------------------------
    // Zone Thing
    // -----------------------------
    public class ZoneMagicArea : ThingWithComps
    {
        private Pawn casterPawn;
        private Thing emitterThing;

        private float radius;
        private int tickInterval;
        private int durationTicks; // <= 0 = infinite
        private int ageTicks;

        private ZoneHostilityMode hostility;
        private ZoneTargetKind targetKind;

        private ZoneVisualMode visualMode;
        private ZoneVisualColor visualColor;

        private List<ZoneMagicEffectDef> effects;

        private IntVec3 forcedPosition = IntVec3.Invalid;

        private readonly HashSet<int> pawnsLast = new HashSet<int>();
        private readonly HashSet<int> pawnsNow = new HashSet<int>();

        // --- Shader translation ---
        private static readonly int DrawColorId = Shader.PropertyToID("_DrawColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Cached per-layer materials to avoid allocations every frame
        private List<Material> layerMats;
        private List<Color> layerMatColors;
        private List<string> layerMatTexPaths;
        private List<string> layerMatShaderTypes;

        private Shader cachedFallbackShader;
        private int cachedLayerCount = -1;

        private List<ZoneVisualLayer> VisualLayersFromDef =>
            def?.GetModExtension<ZoneMagicVisualLayersExtension>()?.visualLayers;

        // ----- Init -----

        public void InitFromEmitter(Thing emitter, CompProperties_ZoneMagicEmitter props, int durationOverrideTicks)
        {
            emitterThing = emitter;
            casterPawn = null;

            radius = props.radius;
            tickInterval = Mathf.Max(1, props.tickInterval);
            durationTicks = durationOverrideTicks;

            hostility = props.hostility;
            targetKind = props.targetKind;

            visualMode = props.visualMode;
            visualColor = props.visualColor;

            effects = props.effects != null ? new List<ZoneMagicEffectDef>(props.effects) : new List<ZoneMagicEffectDef>();

            InvalidateLayerCache();
        }

        public void InitFromAbility(Pawn caster, CompProperties_AbilityEffect_ZoneMagicPlace props)
        {
            casterPawn = caster;
            emitterThing = null;

            radius = props.radius;
            tickInterval = Mathf.Max(1, props.tickInterval);
            durationTicks = props.durationTicks;

            hostility = props.hostility;
            targetKind = props.targetKind;

            visualMode = props.visualMode;
            visualColor = props.visualColor;

            effects = props.effects != null ? new List<ZoneMagicEffectDef>(props.effects) : new List<ZoneMagicEffectDef>();

            InvalidateLayerCache();
        }

        public void SetForcedPosition(IntVec3 pos) => forcedPosition = pos;

        private void InvalidateLayerCache()
        {
            cachedFallbackShader = null;
            cachedLayerCount = -1;
        }

        // ----- Core Tick -----
        protected override void Tick()
        {
            base.Tick();

            ageTicks++;

            if (emitterThing != null)
            {
                if (!emitterThing.Spawned)
                {
                    Destroy();
                    return;
                }

                if (Spawned && Position != emitterThing.Position)
                    Position = emitterThing.Position;
            }
            else if (forcedPosition.IsValid && Spawned && Position != forcedPosition)
            {
                Position = forcedPosition;
            }

            if (durationTicks > 0 && ageTicks >= durationTicks)
            {
                Destroy();
                return;
            }

            if (Find.TickManager.TicksGame % tickInterval == 0)
                ApplyZoneStep();
        }

        // ----- Drawing: layers from mod extension -----
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!Spawned || Map == null) return;

            var layers = VisualLayersFromDef;

            // Fallback shader: use the shader from ThingDef.graphicData (Graphic.MatSingle)
            Shader fallbackShader = this.Graphic?.MatSingle?.shader;
            if (fallbackShader == null) return;

            // If no layers configured, fall back to normal drawing (optional)
            if (layers == null || layers.Count == 0)
            {
                base.DrawAt(drawLoc, flip);

                if (visualMode == ZoneVisualMode.Ring)
                    GenDraw.DrawRadiusRing(Position, radius, GetVisualColor(visualColor));

                return;
            }

            EnsureLayerMaterials(fallbackShader, layers);

            float yBase = AltitudeLayer.MoteOverhead.AltitudeFor();
            Vector3 centerBase = new Vector3(Position.x + 0.5f, yBase, Position.z + 0.5f);

            float diameter = radius * 2f;

            Mesh mesh = MeshPool.plane10;
            float meshSize = mesh.bounds.size.x;
            float baseScale = (meshSize > 0.001f) ? (diameter / meshSize) : diameter;

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null) continue;

                var mat = layerMats[i];
                if (mat == null) continue;

                float s = baseScale * Mathf.Max(0.01f, layer.scaleMul);
                Vector3 center = centerBase + new Vector3(0f, layer.yOffset, 0f);

                Graphics.DrawMesh(
                    mesh,
                    Matrix4x4.TRS(center, Quaternion.identity, new Vector3(s, 1f, s)),
                    mat,
                    0
                );
            }

            if (visualMode == ZoneVisualMode.Ring)
                GenDraw.DrawRadiusRing(Position, radius, GetVisualColor(visualColor));
        }

        private void EnsureLayerMaterials(Shader fallbackShader, List<ZoneVisualLayer> layers)
        {
            int count = layers.Count;

            // Recreate caches if count changed or first time
            if (layerMats == null || cachedLayerCount != count)
            {
                DestroyLayerMats();

                layerMats = new List<Material>(count);
                layerMatColors = new List<Color>(count);
                layerMatTexPaths = new List<string>(count);
                layerMatShaderTypes = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    layerMats.Add(null);
                    layerMatColors.Add(new Color(-1f, -1f, -1f, -1f));
                    layerMatTexPaths.Add(null);
                    layerMatShaderTypes.Add(null);
                }

                cachedLayerCount = count;
                cachedFallbackShader = null;
            }

            // If fallback shader changed, rebuild all (important if ThingDef shader changed)
            if (cachedFallbackShader != fallbackShader)
            {
                DestroyLayerMats();
                for (int i = 0; i < count; i++)
                {
                    layerMatColors[i] = new Color(-1f, -1f, -1f, -1f);
                    layerMatTexPaths[i] = null;
                    layerMatShaderTypes[i] = null;
                }
                cachedFallbackShader = fallbackShader;
            }

            for (int i = 0; i < count; i++)
            {
                var layer = layers[i];
                if (layer == null || string.IsNullOrEmpty(layer.texPath))
                    continue;

                string shaderTypeName = layer.shaderType ?? ""; // may be empty

                bool needsRebuild =
                    layerMats[i] == null ||
                    layerMatColors[i] != layer.color ||
                    layerMatTexPaths[i] != layer.texPath ||
                    layerMatShaderTypes[i] != shaderTypeName;

                if (!needsRebuild)
                    continue;

                // Destroy old
                if (layerMats[i] != null)
                {
                    UnityEngine.Object.Destroy(layerMats[i]);
                    layerMats[i] = null;
                }

                // Load texture (reportFailure true to surface mistakes early)
                Texture2D tex = ContentFinder<Texture2D>.Get(layer.texPath, reportFailure: true);
                if (tex == null)
                    continue;

                Shader layerShader = ResolveShaderForLayer(fallbackShader, shaderTypeName);

                Material mat = new Material(layerShader);
                mat.mainTexture = tex;

                // Set shader tint
                if (mat.HasProperty(DrawColorId))
                    mat.SetColor(DrawColorId, layer.color);

                if (mat.HasProperty(ColorId))
                    mat.SetColor(ColorId, layer.color);
                else
                    mat.color = layer.color;

                layerMats[i] = mat;
                layerMatColors[i] = layer.color;
                layerMatTexPaths[i] = layer.texPath;
                layerMatShaderTypes[i] = shaderTypeName;
            }
        }

        private static Shader ResolveShaderForLayer(Shader fallback, string shaderTypeName)
        {
            if (string.IsNullOrEmpty(shaderTypeName))
                return fallback;

            // 1) RimWorld built-in shader names (reliable)
            switch (shaderTypeName)
            {
                case "Transparent": return ShaderDatabase.Transparent;
                case "Cutout": return ShaderDatabase.Cutout;
                case "CutoutComplex": return ShaderDatabase.CutoutComplex;
                case "TransparentPostLight": return ShaderDatabase.TransparentPostLight;
                case "Mote": return ShaderDatabase.Mote;
                case "MoteGlow": return ShaderDatabase.MoteGlow;
                case "WorldOverlayTransparent": return ShaderDatabase.WorldOverlayTransparent;
            }

            // 2) Custom Unity shader (your BS shader)
            Shader sh = Shader.Find(shaderTypeName);
            return sh != null ? sh : fallback;
        }

        private void DestroyLayerMats()
        {
            if (layerMats == null) return;

            for (int i = 0; i < layerMats.Count; i++)
            {
                if (layerMats[i] != null)
                {
                    UnityEngine.Object.Destroy(layerMats[i]);
                    layerMats[i] = null;
                }
            }
        }

        private static Color GetVisualColor(ZoneVisualColor c)
        {
            return c == ZoneVisualColor.Yellow
                ? new Color(1f, 0.95f, 0.3f, 0.55f)
                : new Color(0.3f, 0.7f, 1f, 0.55f);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            DestroyLayerMats();
        }

        // ----- Zone logic -----

        private void ApplyZoneStep()
        {
            if (!Spawned || Map == null) return;
            if (effects == null || effects.Count == 0) return;

            pawnsNow.Clear();

            foreach (var cell in GenRadial.RadialCellsAround(Position, radius, true))
            {
                if (!cell.InBounds(Map)) continue;

                var things = cell.GetThingList(Map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn && IsValidTarget(pawn))
                        pawnsNow.Add(pawn.thingIDNumber);
                }
            }

            // OnEnter: now - last
            foreach (var id in pawnsNow)
            {
                if (pawnsLast.Contains(id)) continue;
                var pawn = FindPawnById(id);
                if (pawn != null && IsValidTarget(pawn))
                    ApplyTriggered(pawn, ZoneEffectTrigger.OnEnter);
            }

            // WhileInside: now
            foreach (var id in pawnsNow)
            {
                var pawn = FindPawnById(id);
                if (pawn != null && IsValidTarget(pawn))
                    ApplyTriggered(pawn, ZoneEffectTrigger.WhileInside);
            }

            // OnExit: last - now
            foreach (var id in pawnsLast)
            {
                if (pawnsNow.Contains(id)) continue;
                var pawn = FindPawnById(id);
                if (pawn != null && IsValidTarget(pawn))
                    ApplyTriggered(pawn, ZoneEffectTrigger.OnExit);
            }

            pawnsLast.Clear();
            foreach (var id in pawnsNow) pawnsLast.Add(id);
        }

        private Pawn FindPawnById(int id)
        {
            var pawns = Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i].thingIDNumber == id)
                    return pawns[i];
            return null;
        }

        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;

            switch (targetKind)
            {
                case ZoneTargetKind.HumanlikeOnly:
                    if (!pawn.RaceProps.Humanlike) return false;
                    break;
                case ZoneTargetKind.AnimalsOnly:
                    if (!pawn.RaceProps.Animal) return false;
                    break;
                case ZoneTargetKind.MechsOnly:
                    if (!pawn.RaceProps.IsMechanoid) return false;
                    break;
            }

            Faction sourceFaction = casterPawn?.Faction ?? emitterThing?.Faction;
            if (hostility != ZoneHostilityMode.Any && sourceFaction != null && pawn.Faction != null)
            {
                bool hostileToSource = pawn.Faction.HostileTo(sourceFaction);
                if (hostility == ZoneHostilityMode.HostileOnly && !hostileToSource) return false;
                if (hostility == ZoneHostilityMode.FriendlyOnly && hostileToSource) return false;
            }

            return true;
        }

        private void ApplyTriggered(Pawn pawn, ZoneEffectTrigger trigger)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var eff = effects[i];
                if (eff == null || !eff.IsValid) continue;
                if (eff.trigger != trigger) continue;
                if (eff.applyChance < 1f && !Rand.Chance(eff.applyChance)) continue;

                if (eff.requiredHediff != null &&
                    pawn.health?.hediffSet?.GetFirstHediffOfDef(eff.requiredHediff) == null)
                    continue;

                if (eff.IsHediff) ApplyHediffRespectingBlocks(pawn, eff);
                else if (eff.IsMental) ApplyMentalStateNoOverride(pawn, eff);
            }
        }

        private void ApplyHediffRespectingBlocks(Pawn pawn, ZoneMagicEffectDef eff)
        {
            if (pawn.health == null || eff.hediffDef == null) return;

            if (PawnIsImmuneToHediff_ByMakeImmuneTo(pawn, eff.hediffDef))
                return;

            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(eff.hediffDef);
            if (existing == null)
            {
                var newH = HediffMaker.MakeHediff(eff.hediffDef, pawn);
                newH.Severity = Mathf.Max(0f, eff.initialSeverity);
                pawn.health.AddHediff(newH);

                var after = pawn.health.hediffSet.GetFirstHediffOfDef(eff.hediffDef);
                if (after == null) return;

                return;
            }

            float newSeverity = existing.Severity + eff.addSeverity;

            float max = eff.hediffDef.maxSeverity;
            if (max > 0f)
                newSeverity = Mathf.Min(newSeverity, max);

            existing.Severity = newSeverity;
        }

        private static bool PawnIsImmuneToHediff_ByMakeImmuneTo(Pawn pawn, HediffDef target)
        {
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;

            for (int i = 0; i < hediffs.Count; i++)
            {
                var sourceDef = hediffs[i]?.def;
                if (sourceDef == null) continue;

                var list = GetHediffDefListByName(sourceDef, "makeImmuneTo")
                        ?? GetHediffDefListByName(sourceDef, "makesImmuneTo")
                        ?? GetHediffDefListByName(sourceDef, "makeImmuneToHediffs")
                        ?? GetHediffDefListByName(sourceDef, "makesImmuneToHediffs");

                if (list != null && list.Contains(target))
                    return true;
            }
            return false;
        }

        private static List<HediffDef> GetHediffDefListByName(HediffDef def, string name)
        {
            var t = def.GetType();

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(def);
                if (v is List<HediffDef> l1) return l1;
                if (v is IEnumerable e1)
                {
                    var outList = new List<HediffDef>();
                    foreach (var x in e1) if (x is HediffDef hd) outList.Add(hd);
                    return outList.Count > 0 ? outList : null;
                }
            }

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(def, null);
                if (v is List<HediffDef> l2) return l2;
                if (v is IEnumerable e2)
                {
                    var outList = new List<HediffDef>();
                    foreach (var x in e2) if (x is HediffDef hd) outList.Add(hd);
                    return outList.Count > 0 ? outList : null;
                }
            }

            return null;
        }

        private void ApplyMentalStateNoOverride(Pawn pawn, ZoneMagicEffectDef eff)
        {
            if (eff.mentalStateDef == null) return;
            if (pawn.InMentalState) return;

            var handler = pawn.mindState?.mentalStateHandler;
            if (handler == null) return;

            handler.TryStartMentalState(eff.mentalStateDef, reason: null, forceWake: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref casterPawn, "casterPawn");
            Scribe_References.Look(ref emitterThing, "emitterThing");

            Scribe_Values.Look(ref radius, "radius", 4.9f);
            Scribe_Values.Look(ref tickInterval, "tickInterval", 60);
            Scribe_Values.Look(ref durationTicks, "durationTicks", -1);
            Scribe_Values.Look(ref ageTicks, "ageTicks", 0);

            Scribe_Values.Look(ref hostility, "hostility", ZoneHostilityMode.Any);
            Scribe_Values.Look(ref targetKind, "targetKind", ZoneTargetKind.AnyPawn);

            Scribe_Values.Look(ref visualMode, "visualMode", ZoneVisualMode.None);
            Scribe_Values.Look(ref visualColor, "visualColor", ZoneVisualColor.Blue);

            Scribe_Collections.Look(ref effects, "effects", LookMode.Deep);

            Scribe_Values.Look(ref forcedPosition, "forcedPosition");
        }
    }
}