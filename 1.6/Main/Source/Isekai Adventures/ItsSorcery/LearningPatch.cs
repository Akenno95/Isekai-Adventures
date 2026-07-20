using HarmonyLib;
using ItsSorceryFramework;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace IsekaiAdventures
{
    // =========================
    // INIT
    // =========================
    [StaticConstructorOnStartup]
    public static class ISK_Init
    {
        static ISK_Init()
        {
            new Harmony("IsekaiAdventures.LearningIntegration").PatchAll();
        }
    }

    // =========================
    // GLOBAL NODE SYSTEM
    // =========================
    public static class GlobalLearningUtility
    {
        public static IEnumerable<SorcerySchema> GetAllSchemas(Pawn pawn)
        {
            return SorcerySchemaUtility.GetSorcerySchemaList(pawn) ?? Enumerable.Empty<SorcerySchema>();
        }

        public static bool IsNodeCompletedGlobal(LearningTreeNodeDef node, Pawn pawn)
        {
            foreach (var schema in GetAllSchemas(pawn))
            {
                foreach (var tracker in schema.learningTrackers)
                {
                    if (tracker is LearningTracker_Tree tree)
                    {
                        var record = tree.LearningRecord;

                        if (record.completion.TryGetValue(node, out bool completed) && completed)
                            return true;
                    }
                }
            }
            return false;
        }

        public static bool AreAllNodesCompletedGlobal(List<LearningTreeNodeDef> nodes, Pawn pawn)
        {
            foreach (var node in nodes)
            {
                if (!IsNodeCompletedGlobal(node, pawn))
                    return false;
            }
            return true;
        }

        public static bool IsAnyNodeCompletedGlobal(List<LearningTreeNodeDef> nodes, Pawn pawn)
        {
            foreach (var node in nodes)
            {
                if (IsNodeCompletedGlobal(node, pawn))
                    return true;
            }
            return false;
        }
    }

    // =========================
    // PATCH: GLOBAL PREREQS
    // =========================
    [HarmonyPatch(typeof(LearningNodeRecord), "PrereqFufilled")]
    public static class Patch_GlobalNodePrereqs
    {
        static void Postfix(LearningNodeRecord __instance, LearningTreeNodeDef node, ref bool __result)
        {
            if (!__result)
                return;

            if (node.prereqNodes == null || node.prereqNodes.Count == 0)
                return;

            Pawn pawn = __instance.pawn;

            bool success = false;

            switch (node.prereqNodeMode)
            {
                case LearningNodePrereqMode.All:
                    success = GlobalLearningUtility.AreAllNodesCompletedGlobal(node.prereqNodes, pawn);
                    break;

                case LearningNodePrereqMode.Or:
                    success = GlobalLearningUtility.IsAnyNodeCompletedGlobal(node.prereqNodes, pawn);
                    break;

                case LearningNodePrereqMode.Min:
                    int count = node.prereqNodes.Count(n => GlobalLearningUtility.IsNodeCompletedGlobal(n, pawn));
                    success = count >= node.prereqNodeModeMin;
                    break;

                default:
                    success = true;
                    break;
            }

            __result = success;
        }
    }

    // =========================
    // TAB VISIBILITY + LIVE REFRESH
    // =========================
    [HarmonyPatch]
    public static class Patch_TabVisibility
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Dialog_LearningTabs), "PreOpen")]
        public static void PreOpen_Postfix(Dialog_LearningTabs __instance)
        {
            RefreshVisibleTabs(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LearningNodeRecord), "CompletionLearningUnlock")]
        public static void CompletionLearningUnlock_Postfix(LearningNodeRecord __instance, LearningTreeNodeDef node)
        {
            if (Find.WindowStack.currentlyDrawnWindow is Dialog_LearningTabs dialog)
            {
                RefreshVisibleTabs(dialog);
            }
        }

        public static void RefreshVisibleTabs(Dialog_LearningTabs dialog)
        {
            if (dialog == null || dialog.learningTrackers == null)
                return;

            LearningTracker oldTracker = dialog.CurTracker;

            dialog.tabs.Clear();

            foreach (LearningTracker tracker in dialog.learningTrackers)
            {
                if (!ShouldBeVisible(tracker))
                    continue;

                LearningTracker capturedTracker = tracker;

                dialog.tabs.Add(new Dialog_LearningTabs.LearningTabRecord(
                    capturedTracker,
                    capturedTracker.def.LabelCap,
                    delegate
                    {
                        dialog.CurTracker = capturedTracker;
                    },
                    () => dialog.CurTracker == capturedTracker
                ));
            }

            if (oldTracker != null && dialog.tabs.Exists(t => t.tracker == oldTracker))
            {
                dialog.CurTracker = oldTracker;
            }
            else if (dialog.tabs.Count > 0)
            {
                dialog.CurTracker = dialog.tabs[0].tracker;
            }
            else
            {
                dialog.CurTracker = null;
            }
        }

        public static bool ShouldBeVisible(LearningTracker tracker)
        {
            if (tracker == null)
                return false;

            if (tracker.locked)
                return false;

            if (tracker is LearningTracker_Tree tree)
            {
                var record = tree.LearningRecord;

                foreach (var node in tree.AllRelativeNodes)
                {
                    if (!record.PrereqFufilled(node) && node.condVisiblePrereq)
                        continue;

                    return true;
                }

                return false;
            }

            return true;
        }
    }
}