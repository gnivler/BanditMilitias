using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Helpers.Helper.Globals;

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
                LordEquipment = CharacterObject.Templates.Where(x =>
                        x.StringId.Contains("lord") &&
                        x.FirstBattleEquipment != null)
                    .Select(x => x.FirstBattleEquipment)
                    .ToList();
                Militias.Clear();
                Hideouts = Settlement.FindAll(x =>
                    x.IsHideout() && x.MapFaction != CampaignData.NeutralFaction).ToList();
                var militias = MobileParty.All.Where(x =>
                    x != null && x.StringId.StartsWith("Bandit_Militia")).ToList();
                for (var i = 0; i < militias.Count; i++)
                {
                    var militia = militias[i];
                    if (militia.LeaderHero == null)
                    {
                        Mod.Log("Leaderless militia found and removed.");
                        Trash(militia);
                    }
                    else
                    {
                        Militias.Add(new Militia(militia));
                    }
                }

                Mod.Log($"Militias: {militias.Count} (registered {Militias.Count})");
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
                if (party?.MobileParty == null ||
                    !party.MobileParty.StringId.StartsWith("Bandit_Militia") ||
                    party.PrisonRoster != null &&
                    party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }

                if (party.MemberRoster?.TotalHealthyCount < Globals.Settings.MinPartySize &&
                    party.MemberRoster.TotalHealthyCount > 0 &&
                    party.PrisonRoster?.Count < Globals.Settings.MinPartySize &&
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
                if (hero.PartyBelongedTo != null &&
                    hero.PartyBelongedTo.StringId.StartsWith("Bandit_Militia"))
                {
                    return 1;
                }

                return 0;
            }
        }

        // 1.4.3b is throwing when militias are nuked and the game is reloaded with militia MapEvents
        //[HarmonyPatch(typeof(MapEvent), "RemoveInvolvedPartyInternal")]
        //public class MapEventRemoveInvolvedPartyInternalPatch
        //{
        //    private static bool Prefix(PartyBase party) => party.Visuals != null;
        //} 

        // 1.4.3b will crash on load at TaleWorlds.CampaignSystem.PlayerEncounter.DoWait()
        // because MapEvent.PlayerMapEvent is saved as null for whatever reason
        // best solution so far is to avoid the problem with a kludge
        // myriad other corrective attempts left the game unplayable (can't encounter anything)
        [HarmonyPatch(typeof(MBSaveLoad), "SaveGame")]
        public class MDSaveLoadSaveGamePatch
        {
            private static void Prefix()
            {
                var mapEvent = Traverse.Create(PlayerEncounter.Current).Field("_mapEvent").GetValue<MapEvent>();
                if (mapEvent != null &&
                    mapEvent.InvolvedParties.Any(x =>
                        x.MobileParty != null &&
                        x.MobileParty.StringId.StartsWith("Bandit_Militia")))
                {
                    mapEvent = null;
                    PlayerEncounter.Finish();
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                }
            }
        }

        [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "OnHeroDailyTick")]
        public class HeroSpawnCampaignBehaviorOnHeroDailyTickPatch
        {
            private static bool Prefix(Hero hero)
            {
                // latest 1.4.3b patch is trying to teleport bandit heroes apparently before they have parties
                // there's no party here so unable to filter by Bandit_Militia
                // for now this probably doesn't matter but vanilla isn't ready for bandit heroes
                // it could fuck up other mods relying on this method unfortunately
                // but that seems very unlikely to me right now
                return !Clan.BanditFactions.Contains(hero.Clan);
            }
        }
    }
}
