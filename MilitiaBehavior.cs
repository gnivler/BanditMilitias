using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using static Bandit_Militias.Helpers.Globals;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly Stopwatch T = new Stopwatch();
        private static readonly Dictionary<MobileParty, CampaignTime> MergeMap = new Dictionary<MobileParty, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTick);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, x => MergeMap.Remove(x.MobileParty));
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private static void HourlyTick(MobileParty __instance)
        {
            T.Restart();
            if (!IsValidParty(__instance))
            {
                return;
            }

            var lastMergedOrSplitDate = Militia.FindMilitiaByParty(__instance)?.LastMergedOrSplitDate;
            if (lastMergedOrSplitDate != null &&
                CampaignTime.Now < lastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                return;
            }

            var targetParty = MobileParty.FindPartiesAroundPosition(__instance.Position2D, FindRadius,
                x => x != __instance && x.IsBandit && IsValidParty(x)).GetRandomElement()?.Party;

            // "nobody" is a valid answer
            if (targetParty == null)
            {
                return;
            }

            var targetLastMergedOrSplitDate = Militia.FindMilitiaByParty(targetParty.MobileParty)?.LastMergedOrSplitDate;
            if (targetLastMergedOrSplitDate != null &&
                CampaignTime.Now < targetLastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                return;
            }

            if (Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, __instance) > MergeDistance)
            {
                if (!MergeMap.ContainsKey(__instance))
                {
                    MergeMap.Add(__instance, CampaignTime.Now);
                }

                if (!IsMovingToBandit(__instance, targetParty.MobileParty) &&
                    CampaignTime.Now > MergeMap[__instance] + CampaignTime.Hours(4))
                {
                    MergeMap.Remove(__instance);
                    Mod.Log($"{__instance} seeking >> target {targetParty.MobileParty}");
                    Traverse.Create(__instance).Method("SetNavigationModeParty", targetParty.MobileParty).GetValue();
                }

                if (targetParty.MobileParty.MoveTargetParty == __instance)
                {
                    MergeMap.Remove(targetParty.MobileParty);
                    Mod.Log($"{targetParty.MobileParty} target << seeking {__instance}");
                    Traverse.Create(targetParty.MobileParty).Method("SetNavigationModeParty", __instance).GetValue();
                }

                return;
            }

            var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
            if (militiaTotalCount > Globals.Settings.MaxPartySize ||
                militiaTotalCount > CalculatedMaxPartySize ||
                __instance.Party.TotalStrength > CalculatedMaxPartyStrength ||
                NumMountedTroops(__instance.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
            {
                return;
            }

            if (Settlement.FindSettlementsAroundPosition(__instance.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
            {
                return;
            }

            MergeMap.Remove(__instance);
            MergeMap.Remove(targetParty.MobileParty);

            // create a new party merged from the two
            var rosters = MergeRosters(__instance, targetParty);
            var militia = new Militia(__instance, rosters[0], rosters[1]);
            // teleport new militias near the player
            if (testingMode)
            {
                // in case a prisoner
                var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                militia.MobileParty.Position2D = party.Position2D;
            }

            militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
            Trash(__instance);
            Trash(targetParty.MobileParty);
            Mod.Log($"finished ==> {T.ElapsedTicks / 10000F:F3}ms");
            Mod.Log($"MergeMap.Count ==> {MergeMap.Count}");
        }
    }
}
