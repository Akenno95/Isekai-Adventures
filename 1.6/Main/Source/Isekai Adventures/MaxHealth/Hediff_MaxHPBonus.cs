using Verse;

namespace IsekaiAdventures
{
    public class Hediff_MaxHPBonus : HediffWithComps
    {
        public MaxHPBonusExtension Ext => def.GetModExtension<MaxHPBonusExtension>();

        public override string TipStringExtra
        {
            get
            {
                var e = Ext;
                if (e == null) return null;
                string s = "";
                if (e.flatBonus != 0f) s += $"Max HP {(e.flatBonus >= 0 ? "+" : "")}{e.flatBonus:0}\n";
                if (e.percentBonus != 0f) s += $"Max HP {(e.percentBonus >= 0 ? "+" : "")}{e.percentBonus:P0}\n";
                return s.TrimEnd();
            }
        }
    }
}
