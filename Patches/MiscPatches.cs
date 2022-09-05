using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BanditMilitias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
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
                if (!SubModule.MEOWMEOW || !Globals.Settings.UpgradeTroops || MapEvent.PlayerMapEvent is not null && ____selectedSimulationTroop is null)
                    return;
                // makes loot drop in any BM-involved fight which isn't with the main party
                var BMs = __instance.Parties.WhereQ(p =>
                    p.Party.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).SelectQ(p => p.Party);
                if (BMs.Any() && !__instance.IsMainPartyAmongParties())
                {
                    for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    {
                        var item = ____selectedSimulationTroop.Equipment[index];
                        if (item.IsEmpty)
                            continue;
                        if (Rng.Next(0, 101) < 66)
                            continue;
                        if (LootRecord.TryGetValue(__instance, out _))
                            LootRecord[__instance].Add(new EquipmentElement(item));
                        else
                            LootRecord.Add(__instance, new List<EquipmentElement> { item });
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BattleCampaignBehavior), "CollectLoots")]
        public static class MapEventSideDistributeLootAmongWinners
        {
            public static void Prefix(MapEvent mapEvent, PartyBase winnerParty, ref Dictionary<PartyBase, ItemRoster> lootedItems)
            {
                if (!SubModule.MEOWMEOW || !Globals.Settings.UpgradeTroops || !mapEvent.HasWinner || !winnerParty.IsMobile || !winnerParty.MobileParty.IsBM())
                    return;
                if (LootRecord.TryGetValue(winnerParty.MapEventSide, out var equipment))
                {
                    var itemRoster = new ItemRoster();
                    foreach (var e in equipment)
                        itemRoster.AddToCounts(e, 1);

                    lootedItems.Add(winnerParty, itemRoster);
                    if (lootedItems[winnerParty].AnyQ(i => !i.IsEmpty))
                        EquipmentUpgrading.UpgradeEquipment(winnerParty, lootedItems[winnerParty]);
                }

                LootRecord.Remove(winnerParty.MobileParty.MapEventSide);
            }
        }

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                DeferringLogger.Instance.Debug?.Log("MapScreen.OnInitialize");
                ClearGlobals();
                PopulateItems();
                Looters = Clan.BanditFactions.First(c => c.StringId == "looters");
                // ROT
                Wights = Clan.BanditFactions.FirstOrDefaultQ(c => c.StringId == "wights");
                Globals.CampaignPeriodicEventManager = Traverse.Create(Campaign.Current).Field<CampaignPeriodicEventManager>("_campaignPeriodicEventManager").Value;
                Ticker = AccessTools.Field(typeof(CampaignPeriodicEventManager), "_partiesWithoutPartyComponentsPartialHourlyAiEventTicker").GetValue(Globals.CampaignPeriodicEventManager);
                Hideouts = Settlement.All.WhereQ(s => s.IsHideout).ToListQ();
                RaidCap = Convert.ToInt32(Settlement.FindAll(s => s.IsVillage).CountQ() / 10f);
                HeroTemplates = CharacterObject.All.WhereQ(c =>
                    c.Occupation is Occupation.Bandit && c.StringId.StartsWith("lord_")).ToListQ();

                var filter = new List<string>
                {
                    "regular_fighter",
                    "veteran_borrowed_troop",
                    "_basic_root", // MyLittleWarband StringIds
                    "_elite_root"
                };

                var allRecruits = CharacterObject.All.WhereQ(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && filter.All(s => !c.StringId.Contains(s))
                    && !c.StringId.EndsWith("_tier_1"));

                foreach (var recruit in allRecruits)
                {
                    if (Recruits.ContainsKey(recruit.Culture))
                        Recruits[recruit.Culture].Add(recruit);
                    else
                        Recruits.Add(recruit.Culture, new List<CharacterObject> { recruit });
                }

                // used for armour
                foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    ItemTypes[itemType] = Items.All.WhereQ(i =>
                        i.Type == itemType
                        && i.Value >= 1000
                        && i.Value <= Globals.Settings.MaxItemValue).ToList();
                }

                // front-load
                for (var i = 0; i < 3000; i++)
                    BanditEquipment.Add(BuildViableEquipmentSet());

                DoPowerCalculations(true);
                ReHome();
                var bmCount = MobileParty.All.CountQ(m => m.IsBM());
                DeferringLogger.Instance.Debug?.Log($"Militias: {bmCount}.  Upgraded BM troops: {MobileParty.All.SelectMany(m => m.MemberRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.  Troop prisoners: {MobileParty.All.SelectMany(m => m.PrisonRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.");
            }
        }

        [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MapMobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MapMobilePartyTrackerVM __instance) => Globals.MapMobilePartyTrackerVM = __instance;
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

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        [HarmonyPatch(typeof(MBObjectManager), "UnregisterNonReadyObjects")]
        public class MBObjectManagerUnregisterNonReadyObjects
        {
            private static readonly MethodInfo from = AccessTools.Method(typeof(MBObjectManager), "UnregisterObject");
            private static readonly MethodInfo to = AccessTools.Method(typeof(MBObjectManagerUnregisterNonReadyObjects), "UnregisterObject");

            public static bool Prepare()
            {
                return SubModule.MEOWMEOW;
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return instructions.MethodReplacer(from, to);
            }

            // ReSharper disable once UnusedParameter.Local
            private static void UnregisterObject(MBObjectManager manager, MBObjectBase obj)
            {
                // a "dehydrated" custom CharacterObject that we don't want to unregister
                if (obj is CharacterObject troop && troop.GetName() == null)
                    if (EquipmentMap.TryGetValue(troop.StringId, out _))
                    {
                        MilitiaBehavior.RehydrateCharacterObject(troop);
                        EquipmentMap.Add(troop.StringId, troop.Equipment);
                    }
                    else
                        MBObjectManager.Instance.UnregisterObject(obj);
            }
        }

        [HarmonyPatch(typeof(MapEventSide), "CaptureRegularTroops")]
        public static class MapEventSideCaptureRegularTroops
        {
            public static bool Prepare()
            {
                return SubModule.MEOWMEOW;
            }

            public static void Postfix(MapEventSide __instance, PartyBase defeatedParty, bool isSurrender)
            {
                if (!Globals.Settings.UpgradeTroops)
                    return;
                // copied from 1.8 assembly to mimic what it's transferring to the prison roster
                for (var index = 0; index < defeatedParty.MemberRoster.Count; ++index)
                {
                    var troop = defeatedParty.MemberRoster.GetElementCopyAtIndex(index);
                    if (!troop.Character.IsHero && (troop.WoundedNumber > 0 || isSurrender && troop.Number > 0))
                        TakenPrisoner.Add(troop.Character);
                }
            }
        }

        [HarmonyPatch(typeof(RecruitPrisonersCampaignBehavior), "RecruitPrisonersAi")]
        public static class RecruitPrisonersCampaignBehaviorRecruitPrisonersAi
        {
            public static void Prefix(CharacterObject troop)
            {
                if (!SubModule.MEOWMEOW || !Globals.Settings.UpgradeTroops)
                    return;
                if (Troops.ContainsQ(troop))
                    TakenPrisoner.Remove(troop);
            }
        }
    }
}
