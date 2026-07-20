using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ISFNodeIcons
{
    public sealed class ISFNodeIconsMod : Mod
    {
        public ISFNodeIconsMod(ModContentPack content) : base(content)
        {
            Harmony harmony = new Harmony("isf.nodeicons");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public sealed class LearningNodeIconExtension : DefModExtension
    {
        [NoTranslate]
        public string iconPath;

        public float iconSize = 32f;

        public float leftPadding = 6f;

        public float textGap = 6f;

        private bool attemptedLoad;
        private Texture2D cachedIcon;

        public Texture2D Icon
        {
            get
            {
                if (!attemptedLoad)
                {
                    attemptedLoad = true;

                    if (!iconPath.NullOrEmpty())
                    {
                        cachedIcon = ContentFinder<Texture2D>.Get(iconPath, false);
                    }
                }

                return cachedIcon;
            }
        }
    }

    [HarmonyPatch(typeof(ItsSorceryFramework.LearningTracker_Tree), "DrawRightGUI")]
    public static class LearningTrackerTree_DrawRightGUI_NodeIconPatch
    {
        private static readonly MethodInfo DrawTaggedLabelMethod =
            AccessTools.Method(
                typeof(LearningTrackerTree_DrawRightGUI_NodeIconPatch),
                nameof(DrawNodeLabel),
                new Type[]
                {
                    typeof(Rect).MakeByRefType(),
                    typeof(TaggedString),
                    typeof(bool),
                    typeof(bool),
                    typeof(ItsSorceryFramework.LearningTreeNodeDef)
                });

        private static readonly MethodInfo DrawStringLabelMethod =
            AccessTools.Method(
                typeof(LearningTrackerTree_DrawRightGUI_NodeIconPatch),
                nameof(DrawNodeLabelString),
                new Type[]
                {
                    typeof(Rect).MakeByRefType(),
                    typeof(string),
                    typeof(bool),
                    typeof(bool),
                    typeof(ItsSorceryFramework.LearningTreeNodeDef)
                });

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            bool patched = false;

            for (int i = 0; i < code.Count; i++)
            {
                MethodInfo calledMethod = code[i].operand as MethodInfo;

                if (!IsFourArgumentLabelCacheHeight(calledMethod))
                {
                    continue;
                }

                int labelCapGetterIndex = FindLabelCapGetterBefore(code, i);

                if (labelCapGetterIndex < 1)
                {
                    continue;
                }

                CodeInstruction nodeLoadInstruction = code[labelCapGetterIndex - 1];

                if (!IsLocalLoad(nodeLoadInstruction.opcode))
                {
                    continue;
                }

                ParameterInfo[] parameters = calledMethod.GetParameters();
                Type labelType = parameters[1].ParameterType;
                MethodInfo replacementMethod = null;

                if (labelType == typeof(TaggedString))
                {
                    replacementMethod = DrawTaggedLabelMethod;
                }
                else if (labelType == typeof(string))
                {
                    replacementMethod = DrawStringLabelMethod;
                }

                if (replacementMethod == null)
                {
                    continue;
                }

                code.Insert(
                    i,
                    new CodeInstruction(
                        nodeLoadInstruction.opcode,
                        nodeLoadInstruction.operand));

                code[i + 1].opcode = OpCodes.Call;
                code[i + 1].operand = replacementMethod;

                patched = true;
                break;
            }

            if (!patched)
            {
                Log.Error(
                    "[ISF Node Icons] Could not find the learning-node label call in " +
                    "LearningTracker_Tree.DrawRightGUI. The framework method may have changed.");
            }

            return code;
        }

        private static bool IsFourArgumentLabelCacheHeight(MethodInfo method)
        {
            if (method == null ||
                method.DeclaringType != typeof(Widgets) ||
                method.Name != nameof(Widgets.LabelCacheHeight))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();

            return parameters.Length == 4 &&
                   parameters[0].ParameterType == typeof(Rect).MakeByRefType();
        }

        private static int FindLabelCapGetterBefore(
            List<CodeInstruction> code,
            int callIndex)
        {
            int minimumIndex = Math.Max(0, callIndex - 12);

            for (int i = callIndex - 1; i >= minimumIndex; i--)
            {
                MethodInfo method = code[i].operand as MethodInfo;

                if (method != null && method.Name == "get_LabelCap")
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsLocalLoad(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc ||
                   opcode == OpCodes.Ldloc_S ||
                   opcode == OpCodes.Ldloc_0 ||
                   opcode == OpCodes.Ldloc_1 ||
                   opcode == OpCodes.Ldloc_2 ||
                   opcode == OpCodes.Ldloc_3;
        }

        public static void DrawNodeLabel(
            ref Rect nodeRect,
            TaggedString label,
            bool arg3,
            bool arg4,
            ItsSorceryFramework.LearningTreeNodeDef node)
        {
            LearningNodeIconExtension extension =
                node.GetModExtension<LearningNodeIconExtension>();

            if (extension == null || extension.Icon == null)
            {
                Widgets.LabelCacheHeight(ref nodeRect, label, arg3, arg4);
                return;
            }

            DrawIconAndText(nodeRect, label.ToString(), extension);
        }

        public static void DrawNodeLabelString(
            ref Rect nodeRect,
            string label,
            bool arg3,
            bool arg4,
            ItsSorceryFramework.LearningTreeNodeDef node)
        {
            LearningNodeIconExtension extension =
                node.GetModExtension<LearningNodeIconExtension>();

            if (extension == null || extension.Icon == null)
            {
                Widgets.LabelCacheHeight(ref nodeRect, label, arg3, arg4);
                return;
            }

            DrawIconAndText(nodeRect, label, extension);
        }

        private static void DrawIconAndText(
            Rect nodeRect,
            string label,
            LearningNodeIconExtension extension)
        {
            float iconSize = Mathf.Clamp(
                extension.iconSize,
                8f,
                Mathf.Max(8f, nodeRect.height - 4f));

            float leftPadding = Mathf.Max(0f, extension.leftPadding);
            float textGap = Mathf.Max(0f, extension.textGap);

            Rect iconRect = new Rect(
                nodeRect.x + leftPadding,
                nodeRect.y + ((nodeRect.height - iconSize) * 0.5f),
                iconSize,
                iconSize);

            Rect labelRect = new Rect(
                iconRect.xMax + textGap,
                nodeRect.y + 2f,
                Mathf.Max(0f, nodeRect.xMax - (iconRect.xMax + textGap) - 4f),
                nodeRect.height - 4f);

            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;

            GUI.color = Color.white;
            GUI.DrawTexture(
                iconRect,
                extension.Icon,
                ScaleMode.ScaleToFit,
                true);

            GUI.color = previousColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;

            Widgets.Label(labelRect, label);

            Text.Anchor = previousAnchor;
            Text.WordWrap = previousWordWrap;
        }
    }
}
