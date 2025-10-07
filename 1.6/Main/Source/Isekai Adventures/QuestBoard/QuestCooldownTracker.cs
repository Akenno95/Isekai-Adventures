using System.Collections.Generic;
using Verse;

namespace IsekaiAdventures
{
    public class QuestCooldownTracker : GameComponent
    {
        private Dictionary<string, int> cooldownTicks = new();

        public static QuestCooldownTracker Instance => Current.Game.GetComponent<QuestCooldownTracker>();

        public QuestCooldownTracker(Game game) { }

        public override void GameComponentTick()
        {
            var keys = new List<string>(cooldownTicks.Keys);
            foreach (var key in keys)
            {
                cooldownTicks[key] -= 1;
                if (cooldownTicks[key] <= 0)
                    cooldownTicks.Remove(key);
            }
        }

        public bool IsOnCooldown(string questDefName)
        {
            return cooldownTicks.ContainsKey(questDefName);
        }

        public int TicksRemaining(string questDefName)
        {
            return cooldownTicks.TryGetValue(questDefName, out int ticks) ? ticks : 0;
        }

        public void StartCooldown(string questDefName, int ticks)
        {
            cooldownTicks[questDefName] = ticks;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref cooldownTicks, "cooldownTicks", LookMode.Value, LookMode.Value);
        }
    }
}
