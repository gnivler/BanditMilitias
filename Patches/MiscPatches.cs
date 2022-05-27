using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Postfix()
            {
                Log("MapScreen.OnInitialize");
                EquipmentItems.Clear();
                PopulateItems();
                RaidCap = Convert.ToInt32(Settlement.FindAll(s => s.IsVillage).CountQ() / 10f);

                // 1.7 changed CreateHeroAtOccupation to only fish from this: NotableAndWandererTemplates
                // this has no effect on earlier versions since the property doesn't exist
                var banditBosses =
                    CharacterObject.All.Where(c =>
                        c.Occupation is Occupation.Bandit
                        && c.StringId.EndsWith("boss")).ToList().GetReadOnlyList();

                foreach (var clan in Clan.BanditFactions)
                {
                    Traverse.Create(clan.Culture).Property<IReadOnlyList<CharacterObject>>("NotableAndWandererTemplates").Value = banditBosses;
                }

                var filter = new List<string>
                {
                    "regular_fighter",
                    "veteran_borrowed_troop",
                };

                var allRecruits = CharacterObject.All.Where(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && !filter.Contains(c.StringId)
                    && !c.StringId.EndsWith("_tier_1"));

                foreach (var recruit in allRecruits)
                {
                    if (Recruits.ContainsKey(recruit.Culture))
                    {
                        Recruits[recruit.Culture].Add(recruit);
                    }
                    else
                    {
                        Recruits.Add(recruit.Culture, new List<CharacterObject> { recruit });
                    }
                }

                // used for armour
                foreach (ItemObject.ItemTypeEnum value in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    ItemTypes[value] = Items.All.Where(x =>
                        x.Type == value && x.Value >= 1000 && x.Value <= Globals.Settings.MaxItemValue).ToList();
                }

                // front-load
                BanditEquipment.Clear();
                for (var i = 0; i < 1000; i++)
                {
                    BanditEquipment.Add(BuildViableEquipmentSet());
                }

                PartyImageMap.Clear();
                Hideouts = Settlement.FindAll(s => s.IsHideout && s.Culture.Name is not null).ToListQ();
                DoPowerCalculations(true);
                MilitiaBehavior.FlushMilitiaCharacterObjects();
                var bmCount = MobileParty.All.CountQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent);
                Log($"Militias: {bmCount}");
                //Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                RunLateManualPatches();
            }
        }

        [HarmonyPatch(typeof(MobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MobilePartyTrackerVM __instance)
            {
                Globals.MobilePartyTrackerVM = __instance;
            }
        }

        // TODO find root causes, remove finalizers
        // not sure where to start
        // hasn't thrown since 3.7.x
        [HarmonyPatch(typeof(PartyBaseHelper), "HasFeat")]
        public static class PartyBaseHelperHasFeat
        {
            public static Exception Finalizer(Exception __exception, PartyBase party, FeatObject feat)
            {
                if (__exception is not null
                    && party.LeaderHero.Culture.Name is null)
                {
                    party.LeaderHero.Culture = Clan.BanditFactions.GetRandomElementInefficiently().Culture;
                    Log($"{party.LeaderHero} has a fucked up Culture - fixed");
                    Debugger.Break();
                    return null;
                }

                return __exception;
            }
        }

        // TODO find root causes, remove finalizers
        // BM heroes seem to have null UpgradeTargets[] at load time, randomly
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanTroopGainXp")]
        public static class DefaultPartyTroopUpgradeModelCanTroopGainXp
        {
            public static Exception Finalizer(Exception __exception, PartyBase owner)
            {
                if (__exception is not null
                    && owner.MobileParty is not null
                    && owner.MobileParty.IsBM())
                {
                    return null;
                }

                return __exception;
            }
        }

        [HarmonyPatch(typeof(SaveableCampaignTypeDefiner), "DefineContainerDefinitions")]
        public class SaveableCampaignTypeDefinerDefineContainerDefinitions
        {
            public static void Postfix(SaveableCampaignTypeDefiner __instance)
            {
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<Hero, float>) });
            }
        }
    }
}
