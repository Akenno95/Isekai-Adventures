using System.Collections.Generic;
using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    // XML-Extension für Max-HP-Boni (flat oder %), Filter über Parts/Tags
    public class MaxHPBonusExtension : DefModExtension
    {
        // Zielauswahl
        public bool applyToAll = true;                 // alle Parts
        public List<BodyPartDef> includeParts;         // explizit
        public List<string> includePartTags;           // BodyPartTagDef.defName
        public List<BodyPartDef> excludeParts;         // ausschließen
        public List<string> excludePartTags;           // BodyPartTagDef.defName

        // Werte
        public float flatBonus = 0f;                   // +X HP (kann negativ sein)
        public float percentBonus = 0f;                // +X% (0.2 = +20%, -0.15 = -15%)

        // Stacking-Verhalten
        public bool stackMultiples = true;             // mehrere Instanzen addieren
        public int maxStacks = 0;                // 0 = unbegrenzt

        // Nur natürliche Parts (keine AddedPart/Bionics)?
        public bool onlyNatural = false;
    }
}
