using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Library;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global  
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize");
                Globals.Militias.Clear();
                Globals.Hideouts = Settlement.FindAll(x =>
                    x.IsHideout() && x.MapFaction != CampaignData.NeutralFaction).ToList();
                var militias = MobileParty.All.Where(x =>
                    x != null && x.Name.Equals("Bandit Militia")).ToList();
                foreach (var militia in militias)
                {
                    Globals.Militias.Add(new Militia(militia));
                }

                Mod.Log($"Militias: {militias.Count} (registered {Globals.Militias.Count})");
                Flush();
                // 1.4.3b is dropping the militia settlements at some point, I haven't figured out where
                // this will cause a crash at map load if the mod isn't installed but has militias
                ReHome();
                CalcMergeCriteria();
            }
        }

        [HarmonyPatch(typeof(Campaign), "DailyTick")]
        public static class CampaignDailyTickPatch
        {
            private static void Postfix() => CalcMergeCriteria();
        }

        // 0 member parties will form if this is happening
        // was only happening with debugger attached because that makes sense
        [HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
        public class MobilePartyFillPartyStacksPatch
        {
            private static bool Prefix(PartyTemplateObject pt)
            {
                if (pt == null)
                {
                    Mod.Log("BROKEN");
                    Debug.PrintError("Bandit Militias is broken please notify @gnivler via Nexus");
                    return false;
                }

                return true;
            }
        }

        // just disperse small militias
        // todo prevent this unless the militia has lost or retreated from combat
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance, PartyBase party)
            {
                if (party.Name.ToString() != "Bandit Militia")
                {
                    return;
                }

                if (party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }

                if (party.MemberRoster.TotalHealthyCount < Globals.Settings.MinPartySize &&
                    party.MemberRoster.TotalHealthyCount > 0 &&
                    party.PrisonRoster.Count < Globals.Settings.MinPartySize &&
                    __instance.Casualties > party.MemberRoster.Count / 2)
                {
                    Mod.Log($"Dispersing militia of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster.Count}p");
                    Trash(party.MobileParty);
                }
            }
        }

        // prevents militias from being added to DynamicBodyCampaignBehavior._heroBehaviorsDictionary
        // checked 1.4.3b
        [HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "OnAfterDailyTick")]
        public class DynamicBodyCampaignBehaviorOnAfterDailyTickPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
            {
                var label = ilg.DefineLabel();
                var codes = instructions.ToList();
                var insertAt = codes.FindIndex(x => x.opcode.Equals(OpCodes.Stloc_2));
                insertAt++;
                var moveNext = AccessTools.Method(typeof(IEnumerator), nameof(IEnumerator.MoveNext));
                var jumpIndex = codes.FindIndex(x =>
                    x.opcode == OpCodes.Callvirt && (MethodInfo) x.operand == moveNext);
                jumpIndex--;
                codes[jumpIndex].labels.Add(label);
                var helperMi = AccessTools.Method(
                    typeof(DynamicBodyCampaignBehaviorOnAfterDailyTickPatch), nameof(helper));
                var stack = new List<CodeInstruction>
                {
                    // copy the Hero on top of the stack then feed it to the helper for a bool then branch
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Call, helperMi),
                    new CodeInstruction(OpCodes.Brfalse, label)
                };
                codes.InsertRange(insertAt, stack);
                return codes.AsEnumerable();
            }

            private static int helper(Hero hero)
            {
                // ReSharper disable once PossibleNullReferenceException
                return hero.Name.Equals("Bandit Militia") ? 1 : 0;
            }
        }

        // prevents militias from being added to DynamicBodyCampaignBehavior._heroBehaviorsDictionary 
        [HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "CanBeEffectedByProperties")]
        public class DynamicBodyCampaignBehaviorCanBeEffectedByPropertiesPatch
        {
            private static void Postfix(Hero hero, ref bool __result)
            {
                if (hero.Name.Equals("Bandit Militia"))
                {
                    Mod.Log("DynamicBodyCampaignBehaviorCanBeEffectedByPropertiesPatch");
                    __result = false;
                }
            }
        }

        // 1.4.3b is throwing when militias are nuked and the game is reloaded with militia MapEvents
        //[HarmonyPatch(typeof(MapEvent), "RemoveInvolvedPartyInternal")]
        //public class MapEventRemoveInvolvedPartyInternalPatch
        //{
        //    private static bool Prefix(PartyBase party) => party.Visuals != null;
        //} 
    }
}
