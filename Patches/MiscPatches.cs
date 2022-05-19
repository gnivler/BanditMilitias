using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Craft;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
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
            private static void Postfix()
            {
                Log("MapScreen.OnInitialize");
                EquipmentItems.Clear();
                PopulateItems();
                //foreach (var clan in Clan.BanditFactions)
                //{
                //    foreach (var otherClan in Clan.BanditFactions.Except(new[] { clan }))
                //    {
                //        FactionManager.DeclareWar(clan.MapFaction, otherClan, true);
                //    }
                //}


                // 1.7 changed CreateHeroAtOccupation to only fish from this: NotableAndWandererTemplates
                // this has no effect on earlier versions since the property doesn't exist
                var characterObjects =
                    CharacterObject.All.Where(c =>
                        c.Occupation is Occupation.Bandit
                        && c.Name.Contains("Boss")).ToList().GetReadOnlyList();

                foreach (var clan in Clan.BanditFactions)
                {
                    Traverse.Create(clan.Culture).Property<IReadOnlyList<CharacterObject>>("NotableAndWandererTemplates").Value = characterObjects;
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
                Hideouts = Settlement.FindAll(x => x.IsHideout).ToList();
                DoPowerCalculations(true);
                MilitiaBehavior.FlushMilitiaCharacterObjects();
                var bmCount = MobileParty.All.CountQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent);
                Log($"Militias: {bmCount}");
                InformationManager.AddQuickInformation(new TextObject($"{bmCount} Bandit Militias!"));
                //Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                RunLateManualPatches();
            }
        }

        [HarmonyPatch(typeof(MobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MobilePartyTrackerVMCtorPatch
        {
            private static void Postfix(MobilePartyTrackerVM __instance)
            {
                Globals.MobilePartyTrackerVM = __instance;
            }
        }

        // TODO find root causes, remove finalizers
        // not sure where to start
        [HarmonyPatch(typeof(PartyBaseHelper), "HasFeat")]
        public static class PartyBaseHelperHasFeat
        {
            public static Exception Finalizer(Exception __exception, PartyBase party, FeatObject feat)
            {
                if (__exception is not null)
                {
                    Debugger.Break();
                }

                return null;
            }
        }

        // TODO find root causes, remove finalizers
        // maybe BM heroes being considered for troop upgrade - no upgrade targets though
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanTroopGainXp")]
        public static class DefaultPartyTroopUpgradeModelCanTroopGainXp
        {
            public static Exception Finalizer(Exception __exception, PartyBase owner, CharacterObject character)
            {
                if (__exception is not null)
                {
                    Debugger.Break();
                }

                return null;
            }
        }

        // TODO find root causes, remove finalizers
        [HarmonyPatch(typeof(Hero), "SetInitialValuesFromCharacter")]
        public class HeroSetInitialValuesFromCharacter
        {
            public static Exception Finalizer(Hero __instance, CharacterObject characterObject, Exception __exception)
            {
                if (__exception is not null)
                {
                    Debugger.Break();
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(Clan), "IsAtWarWith")]
        public class ClanIsAtWarWith
        {
            public static void Postfix(Clan __instance, ref bool __result)
            {
                if (__instance.MapFaction.IsBanditFaction)
                {
                    __result = true;
                }
            }
        }
    }
}
