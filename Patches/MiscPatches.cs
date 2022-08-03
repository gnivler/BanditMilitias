using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
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
        // idea from True Battle Loot
        [HarmonyPatch(typeof(MapEventSide), "OnTroopKilled")]
        public static class MapEventSideOnTroopKilled
        {
            public static void Postfix(MapEventSide __instance, CharacterObject ____selectedSimulationTroop)
            {
                if (!Globals.Settings.UpgradeTroops && MapEvent.PlayerMapEvent is not null && ____selectedSimulationTroop is null)
                    return;
                EquipmentMap.Remove(____selectedSimulationTroop.StringId);
                // makes all loot drop in any BM-involved fight which isn't with the main party
                var BMs = __instance.Parties.WhereQ(p =>
                    p.Party.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).SelectQ(p => p.Party);
                if (BMs.Any() && !__instance.IsMainPartyAmongParties())
                {
                    for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    {
                        var item = ____selectedSimulationTroop.Equipment[index];
                        if (item.IsEmpty) continue;

                        if (Rng.Next(0, 101) < 66) continue;
                        if (LootRecord.TryGetValue(__instance, out _))
                        {
                            LootRecord[__instance].Add(new EquipmentElement(item));
                        }
                        else
                        {
                            LootRecord.Add(__instance, new List<EquipmentElement> { item });
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BattleCampaignBehavior), "CollectLoots")]
        public static class MapEventSideDistributeLootAmongWinners
        {
            public static void Prefix(MapEvent mapEvent, PartyBase party, ref ItemRoster loot)
            {
                if (!Globals.Settings.UpgradeTroops || !mapEvent.HasWinner || !party.IsMobile || !party.MobileParty.IsBM())
                    return;
                if (LootRecord.TryGetValue(party.MapEventSide, out var equipment))
                {
                    foreach (var e in equipment)
                    {
                        loot.AddToCounts(e, 1);
                    }
                }

                if (loot.AnyQ(i => !i.IsEmpty))
                {
                    UpgradeEquipment(party, loot);
                }

                Globals.LootRecord.Remove(party.MobileParty.MapEventSide);
            }
        }

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                {
                    Nuke();
                }
            }

            public static void Postfix()
            {
                Log("MapScreen.OnInitialize");
                EquipmentItems.Clear();
                PopulateItems();
                RaidCap = Convert.ToInt32(Settlement.FindAll(s => s.IsVillage).CountQ() / 10f);

                // 1.7 changed CreateHeroAtOccupation to only fish from this: NotableAndWandererTemplates
                // this has no effect on earlier versions since the property doesn't exist
                var banditBosses =
                    CharacterObject.All.Where(c => c.Occupation is Occupation.Bandit
                                                   && c.StringId.EndsWith("boss")).ToList().GetReadOnlyList();

                foreach (var clan in Clan.BanditFactions)
                {
                    Traverse.Create(clan.Culture).Property<IReadOnlyList<CharacterObject>>("NotableAndWandererTemplates").Value = banditBosses;
                }

                var filter = new List<string>
                {
                    "regular_fighter",
                    "veteran_borrowed_troop",
                    "_basic_root",
                    "_elite_root"
                };

                var allRecruits = CharacterObject.All.Where(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && filter.All(s => !c.StringId.Contains(s))
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
                foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    Globals.ItemTypes[itemType] = Items.All.Where(i => i.Type == itemType
                                                                       && i.Value >= 1000
                                                                       && i.Value <= Globals.Settings.MaxItemValue).ToList();
                }

                // front-load
                BanditEquipment.Clear();
                for (var i = 0; i < 1000; i++)
                {
                    BanditEquipment.Add(BuildViableEquipmentSet());
                }

                Hideouts = Settlement.FindAll(s => s.IsHideout).ToListQ();
                PartyImageMap.Clear();
                DoPowerCalculations(true);
                var bmCount = MobileParty.All.CountQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent);
                Log($"Militias: {bmCount}."); //  Custom troops: {MobileParty.All.SelectMany(m => m.MemberRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.  Troop prisoners: {MobileParty.All.SelectMany(m => m.PrisonRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.");
                //Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                RunLateManualPatches();
            }
        }

        [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MapMobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MapMobilePartyTrackerVM __instance)
            {
                Globals.MapMobilePartyTrackerVM = __instance;
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
                    Meow();
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
                if (__exception is not null) Log(__exception);
                //if (__exception is not null
                //    && owner.MobileParty is not null
                //    && owner.MobileParty.IsBM())
                //{
                //    return null;
                //}
                //
                //return __exception;
                return null;
            }
        }

        [HarmonyPatch(typeof(SaveableCampaignTypeDefiner), "DefineContainerDefinitions")]
        public class SaveableCampaignTypeDefinerDefineContainerDefinitions
        {
            public static void Postfix(SaveableCampaignTypeDefiner __instance)
            {
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<Hero, float>) });
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<string, Equipment>) });
            }
        }
    }
}
