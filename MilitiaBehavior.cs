using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using static Bandit_Militias.Helpers.Globals;
using static Bandit_Militias.Helpers.Helper;

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly Stopwatch t = new Stopwatch();
        private static readonly Dictionary<MobileParty, CampaignTime> MergeMap = new Dictionary<MobileParty, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTick);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, x => MergeMap.Remove(x.MobileParty));
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void HourlyTick(MobileParty mobileParty)
        {
            t.Restart();
            if (!IsValidParty(mobileParty))
            {
                return;
            }

            var lastMergedOrSplitDate = Militia.FindMilitiaByParty(mobileParty)?.LastMergedOrSplitDate;
            if (lastMergedOrSplitDate != null &&
                CampaignTime.Now < lastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                return;
            }

            var targetParty = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius,
                x => x != mobileParty && x.IsBandit && IsValidParty(x)).GetRandomElement()?.Party;

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

            if (Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > MergeDistance)
            {
                if (!MergeMap.ContainsKey(mobileParty))
                {
                    MergeMap.Add(mobileParty, CampaignTime.Now);
                }

                if (!IsMovingToBandit(mobileParty, targetParty.MobileParty) &&
                    CampaignTime.Now > MergeMap[mobileParty] + CampaignTime.Hours(4))
                {
                    MergeMap.Remove(mobileParty);
                    Mod.Log($"{mobileParty} seeking >> target {targetParty.MobileParty}");
                    Traverse.Create(mobileParty).Method("SetNavigationModeParty", targetParty.MobileParty).GetValue();
                }

                if (targetParty.MobileParty.MoveTargetParty == mobileParty)
                {
                    MergeMap.Remove(targetParty.MobileParty);
                    Mod.Log($"{targetParty.MobileParty} target << seeking {mobileParty}");
                    Traverse.Create(targetParty.MobileParty).Method("SetNavigationModeParty", mobileParty).GetValue();
                }

                return;
            }

            var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
            if (militiaTotalCount > Globals.Settings.MaxPartySize ||
                militiaTotalCount > CalculatedMaxPartySize ||
                mobileParty.Party.TotalStrength > CalculatedMaxPartyStrength ||
                NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
            {
                return;
            }

            if (Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
            {
                return;
            }

            MergeMap.Remove(mobileParty);
            MergeMap.Remove(targetParty.MobileParty);

            // create a new party merged from the two
            var rosters = MergeRosters(mobileParty, targetParty);
            var militia = new Militia(mobileParty, rosters[0], rosters[1]);
            // teleport new militias near the player
            if (testingMode)
            {
                // in case a prisoner
                var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                militia.MobileParty.Position2D = party.Position2D;
            }

            militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
            Trash(mobileParty);
            Trash(targetParty.MobileParty);
            Mod.Log($"finished ==> {t.ElapsedTicks / 10000F:F3}ms");
            Mod.Log($"MergeMap.Count ==> {MergeMap.Count}");
        }
    }
}
