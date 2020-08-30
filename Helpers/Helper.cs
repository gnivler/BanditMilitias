using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming  

namespace Bandit_Militias.Helpers
{
    public static class Helper
    {
        internal static int NumMountedTroops(TroopRoster troopRoster) => troopRoster.Troops
            .Where(x => x.IsMounted).Sum(troopRoster.GetTroopCount);

        internal static float Variance => MBRandom.RandomFloatRanged(0.8f, 1.5f);

        internal static void TrySplitParty(MobileParty __instance)
        {
            if (!__instance.StringId.StartsWith("Bandit_Militia") ||
                __instance.Party.MemberRoster.TotalManCount < 50 ||
                __instance.IsTooBusyToMerge())
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (__instance.MemberRoster.TotalManCount == 0 ||
                roll <= Globals.Settings.RandomSplitChance ||
                !__instance.StringId.StartsWith("Bandit_Militia") ||
                __instance.Party.TotalStrength <= CalculatedMaxPartyStrength * Globals.Settings.StrengthSplitFactor * Variance ||
                __instance.Party.MemberRoster.TotalManCount <= CalculatedMaxPartySize * Globals.Settings.SizeSplitFactor * Variance)
            {
                return;
            }

            var party1 = new TroopRoster();
            var party2 = new TroopRoster();
            var prisoners1 = new TroopRoster();
            var prisoners2 = new TroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.Where(x => x.Character.HeroObject == null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster)
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                if (half > 2)
                {
                    inventory2.AddToCounts(item.EquipmentElement, half + remainder);
                }
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            var half = Math.Max(1, rosterElement.Number / 2);
            troops1.AddToCounts(rosterElement.Character, half);
            var remainder = rosterElement.Number % 2;
            // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
            if (half > 2)
            {
                troops2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                var militia1 = new Militia(original, party1, prisoners1);
                var militia2 = new Militia(original, party2, prisoners2);
                Mod.Log($"{militia1.MobileParty.MapFaction.Name} <<< Split >>> {militia2.MobileParty.MapFaction.Name}");
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
#if !OneFourTwo
                var warParties = Traverse.Create(original.ActualClan).Field("_warParties").GetValue<List<MobileParty>>();
                while (warParties.Contains(original))
                {
                    // it's been added twice... at least.  for Reasons?
                    warParties.Remove(original);
                }
#endif
                Trash(original);
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (!__instance.IsBandit ||
                !__instance.Party.IsMobile ||
                __instance.CurrentSettlement != null ||
                __instance.Party.MemberRoster.TotalManCount == 0 ||
                __instance.IsCurrentlyUsedByAQuest ||
                __instance.IsTooBusyToMerge())
            {
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
        {
            var troopRoster = new TroopRoster();
            var prisonerRoster = new TroopRoster();
            var rosters = new List<TroopRoster>
            {
                sourceParty.MemberRoster,
                targetParty.MemberRoster
            };

            var prisoners = new List<TroopRoster>
            {
                sourceParty.PrisonRoster,
                targetParty.PrisonRoster
            };

            // dumps all bandit heroes (shouldn't be more than 2 though...)
            foreach (var roster in rosters)
            {
                foreach (var troopRosterElement in roster.Where(x => x.Character?.HeroObject == null))
                {
                    troopRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number, woundedCount: troopRosterElement.WoundedNumber, xp: troopRosterElement.Xp);
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var troopRosterElement in roster.Where(x => x.Character?.HeroObject == null))
                {
                    prisonerRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number, woundedCount: troopRosterElement.WoundedNumber, xp: troopRosterElement.Xp);
                }
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        private static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage);
                foreach (var mobileParty in mobileParties)
                {
                    Trash(mobileParty);
                }
            }
        }

        private static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty != null ||
                   mobileParty.ShortTermTargetParty != null ||
                   mobileParty.ShortTermBehavior == AiBehavior.EngageParty ||
                   mobileParty.DefaultBehavior == AiBehavior.EngageParty ||
                   mobileParty.ShortTermBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.AssaultSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.AssaultSettlement;
        }

        internal static void Trash(MobileParty mobileParty)
        {
            Militias.Remove(Militia.FindMilitiaByParty(mobileParty));
            mobileParty.LeaderHero?.KillHero();
            mobileParty.RemoveParty();
        }

        internal static void KillHero(this Hero hero)
        {
            try
            {
                hero.ChangeState(Hero.CharacterStates.Dead);
                MBObjectManager.Instance.UnregisterObject(hero);
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});

                // no longer needed without registered heroes but leaving in for a few extra versions...
                //Traverse.Create(hero.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", hero).GetValue();
                //MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            }
            catch
            {
                // ignored
            }
        }

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.", LogLevel.Info);
            FlushBanditMilitias();
            Flush();
            InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
        }

        private static void FlushBanditMilitias()
        {
            Militias.Clear();
            MergeMap.Clear();
            var tempList = MobileParty.All.Where(x => x.StringId.StartsWith("Bandit_Militia")).ToList();
            var hasLogged = false;
            foreach (var mobileParty in tempList)
            {
                if (!hasLogged)
                {
                    Mod.Log($"Clearing {tempList.Count} Bandit Militias", LogLevel.Info);
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        internal static void ReHome()
        {
            var tempList = Militias.Where(x => x?.Hero?.HomeSettlement == null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }

        internal static void Flush()
        {
            FlushHideoutsOfMilitias();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadCharacterObjects();
            FlushBadBehaviors();
            FlushMapEvents();
            FlushZeroParties();
        }

        private static void FlushZeroParties()
        {
            var parties = MobileParty.All.Where(x => x.CurrentSettlement == null && x.MemberRoster.TotalManCount == 0).ToList();
            Mod.Log($"Removing {parties.Count()} parties without a current settlement or any troops");
            for (var i = 0; i < parties.Count; i++)
            {
                KillHero(parties[i].LeaderHero);
                parties[i].RemoveParty();
            }
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field("mapEvents").GetValue<List<MapEvent>>();
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.Any(x =>
                    x.MobileParty != null &&
                    x.MobileParty.StringId.StartsWith("Bandit_Militia")))
                {
                    mapEvent.FinalizeEvent();
                }
            }
        }

        private static void FlushHideoutsOfMilitias()
        {
            foreach (var hideout in Settlement.All.Where(x => x.IsHideout()).ToList())
            {
                for (var index = 0; index < hideout.Parties.Count; index++)
                {
                    var party = hideout.Parties[index];
                    if (party.StringId.StartsWith("Bandit_Militia"))
                    {
                        LeaveSettlementAction.ApplyForParty(party);
                        party.SetMovePatrolAroundSettlement(hideout);
                    }
                }
            }
        }

        private static void FlushBadBehaviors()
        {
            var behaviors = (IDictionary) Traverse.Create(
                    Campaign.Current.CampaignBehaviorManager.GetBehavior<DynamicBodyCampaignBehavior>())
                .Field("_heroBehaviorsDictionary").GetValue();
            var heroes = new List<Hero>();
            foreach (var hero in behaviors.Keys)
            {
                if (!Hero.All.Contains(hero))
                {
                    heroes.Add((Hero) hero);
                }
            }

            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} hero behaviors without heroes.", LogLevel.Info);
                }

                behaviors.Remove(hero);
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.All.Where(x =>
                x.Name.ToString() == "Bandit Militia" && x.PartyBelongedTo == null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Killing {heroes.Count} null-party heroes.", LogLevel.Info);
                }

                Mod.Log("Killing " + hero);
                hero.KillHero();
            }
        }

        private static void FlushBadCharacterObjects()
        {
            var badChars = CharacterObject.All.Where(x => x.HeroObject == null)
                .Where(x =>
                    x.Name == null ||
                    x.Occupation == Occupation.NotAssigned ||
                    x.Occupation == Occupation.Outlaw &&
                    x.HeroObject?.CurrentSettlement != null)
                .Where(x =>
                    !x.StringId.Contains("template") &&
                    !x.StringId.Contains("char_creation") &&
                    !x.StringId.Contains("equipment") &&
                    !x.StringId.Contains("for_perf") &&
                    !x.StringId.Contains("dummy") &&
                    !x.StringId.Contains("npc_") &&
                    !x.StringId.Contains("unarmed_ai"))
                .ToList();

            var hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (badChar == null)
                {
                    continue;
                }

                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Unregistering {badChars.Count} bad characters.", LogLevel.Info);
                }

                Mod.Log($"mock Unregistering {badChar.StringId}");
                //Traverse.Create(badChar.HeroObject?.CurrentSettlement)
                //    .Field("_heroesWithoutParty").Method("Remove", badChar.HeroObject).GetValue();
                //MBObjectManager.Instance.UnregisterObject(badChar);
            }
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All.Where(x =>
                x.StringId.StartsWith("Bandit_Militia") &&
                x.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount, LogLevel.Info);
                tempList.Add(mobileParty);
            }

            PurgeList($"FlushNeutralBanditParties Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.MemberRoster.TotalManCount == 0 && x.StringId.StartsWith("Bandit_Militia")))
            {
                tempList.Add(mobileParty);
            }

            PurgeList($"FlushEmptyMilitiaParties Clearing {tempList.Count} empty parties", tempList);
        }

        internal static bool IsMovingToBandit(MobileParty mobileParty, MobileParty other)
        {
            return mobileParty.MoveTargetParty != null &&
                   mobileParty.MoveTargetParty == other;
        }

        internal static string Possess(string input)
        {
            // game tries to redraw the PartyNamePlateVM after combat with multiple militias
            // and crashes because __instance.Party.LeaderHero?.FirstName.ToString() is null
            if (input == null)
            {
                return null;
            }

            var lastChar = input[input.Length - 1];
            return $"{input}{(lastChar == 's' ? "'" : "'s")}";
        }

        internal static void PopulateItems()
        {
            var all = ItemObject.All.Where(x =>
                !x.Name.Contains("Crafted") &&
                !x.Name.Contains("Wooden") &&
                !x.Name.Contains("Practice") &&
                x.Name.ToString() != "Torch" &&
                x.Name.ToString() != "Horse Whip" &&
                x.Name.ToString() != "Push Fork" &&
                x.Name.ToString() != "Bound Crossbow").ToList();
            Arrows = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Arrows)
                .Where(x => !x.Name.Contains("Ballista")).ToList();
            Bolts = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Bolts).ToList();
            all = all.Where(x => x.Value >= 1000 && x.Value <= Globals.Settings.MaxItemValue * Variance).ToList();
            var oneHanded = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Thrown &&
                                        x.Name.ToString() != "Boulder" && x.Name.ToString() != "Fire Pot");
            var shields = all.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.Where(x =>
                x.ItemType == ItemObject.ItemTypeEnum.Bow ||
                x.ItemType == ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).ToList());
            any.Do(x => EquipmentItems.Add(new EquipmentElement(x)));
        }

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        internal static Equipment BuildViableEquipmentSet()
        {
            T.Restart();
            var gear = new Equipment();
            var haveShield = false;
            var haveBow = false;
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    EquipmentElement randomElement = default;
                    switch (i)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItems.Where(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (i == 3 && !gear[3].IsEmpty)
                    {
                        break;
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow ||
                        randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (i < 3)
                        {
                            // try again, try harder
                            if (haveBow)
                            {
                                i--;
                                continue;
                            }

                            haveBow = true;
                            gear[i] = randomElement;
                            if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow)
                            {
                                gear[3] = new EquipmentElement(Arrows.ToList()[Rng.Next(0, Arrows.Count)]);
                            }
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                            {
                                gear[3] = new EquipmentElement(Bolts.ToList()[Rng.Next(0, Bolts.Count)]);
                            }

                            continue;
                        }

                        randomElement = EquipmentItems.Where(x =>
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).GetRandomElement();
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        // try again, try harder
                        if (haveShield)
                        {
                            i--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[i] = randomElement;
                }

                gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
                Mod.Log("-----");
                for (var i = 0; i < 10; i++)
                {
                    Mod.Log(gear[i].Item?.Name);
                }
            }
            catch (Exception ex)
            {
                var stackTrace = new StackTrace(ex, true);
                Mod.Log($"\n{stackTrace}");
                Mod.Log(stackTrace.GetFrame(0).GetFileLineNumber());
            }

            Mod.Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }


        internal static void ClampSettingsValues(ref Settings settings)
        {
            settings.CooldownHours = settings.CooldownHours.Clamp(1, 168);
            settings.GrowthInPercent = settings.GrowthInPercent.Clamp(0, 100);
            settings.GrowthChance = settings.GrowthChance.Clamp(0, 1);
            settings.MaxItemValue = settings.MaxItemValue.Clamp(1000, int.MaxValue);
            settings.MinPartySize = settings.MinPartySize.Clamp(15, int.MaxValue);
            settings.MaxPartySize = settings.MaxPartySize.Clamp(75, int.MaxValue);
            settings.RandomSplitChance = settings.RandomSplitChance.Clamp(0, 1);
            settings.StrengthSplitFactor = settings.StrengthSplitFactor.Clamp(0.25f, 1);
            settings.SizeSplitFactor = settings.SizeSplitFactor.Clamp(0.25f, 1);
            settings.PartyStrengthFactor = settings.PartyStrengthFactor.Clamp(0.25f, 2);
            settings.MaxPartySizeFactor = settings.MaxPartySizeFactor.Clamp(0.25f, 2);
            settings.GrowthChance = settings.GrowthChance.Clamp(0, 1);
            settings.GrowthInPercent = settings.GrowthInPercent.Clamp(0, 100);
            settings.MaxItemValue = settings.MaxItemValue.Clamp(1_000, int.MaxValue);
            settings.LooterUpgradeFactor = settings.LooterUpgradeFactor.Clamp(0, 1);
            settings.MilitiaLimitFactor = settings.MilitiaLimitFactor.Clamp(0, 1);
            settings.MaxStrengthDelta = settings.MaxStrengthDelta.Clamp(0, 100);
        }

        internal static void PrintValidatedSettings(Settings settings)
        {
            foreach (var value in settings.GetType().GetFields())
            {
                Mod.Log($"{value.Name}: {value.GetValue(settings)}");
            }
        }

        private static int Clamp(this int number, int min, int max) => Mathf.Clamp(number, min, max);
        private static float Clamp(this float number, float min, float max) => Mathf.Clamp(number, min, max);

        internal static void DailyCalculations()
        {
            // first campaign init hasn't populated this apparently
            var parties = MobileParty.All.Where(x =>
                x.LeaderHero != null &&
                !x.IsBandit).ToList();
            MilitiasLimit = Convert.ToInt32(parties.Count * Globals.Settings.MilitiaLimitFactor);
            CalculatedHeroPartyStrength = parties.Select(x => x.Party.TotalStrength).Average();
            // reduce strength
            CalculatedMaxPartyStrength = CalculatedHeroPartyStrength * Globals.Settings.PartyStrengthFactor * Variance;
            // maximum size grows over time as clans level up
            CalculatedMaxPartySize = Math.Round(parties.Select(x => x.Party.PartySizeLimit).Average());
            CalculatedMaxPartySize *= Globals.Settings.MaxPartySizeFactor * Variance;
            Mod.Log($"Daily calculations => size: {CalculatedMaxPartySize:0} strength: {CalculatedMaxPartyStrength:0} ({Militias.Count}/{MilitiasLimit} militias)");
        }

        public static CultureObject FindMostPrevalentFaction(Vec2 position)
        {
            var settlements = Settlement.FindSettlementsAroundPosition(position, 20);
            var map = new Dictionary<CultureObject, int>();
            foreach (var settlement in settlements)
            {
                if (map.ContainsKey(settlement.Culture))
                {
                    map[settlement.Culture]++;
                }
                else
                {
                    map.Add(settlement.Culture, 1);
                }
            }

            var highest = map.Where(x =>
                x.Value == map.Values.Max()).Select(x => x.Key);
            var result = highest.GetRandomElement();
            return result;
        }

        public static void ConvertLootersToKingdomCultureRecruits(ref TroopRoster troopRoster, CultureObject cultureObject, int numberToUpgrade)
        {
            if (cultureObject == null)
            {
                return;
            }

            if (Clan.BanditFactions.Any(x => x.Culture == cultureObject))
            {
                cultureObject = Kingdom.All.First(x => x.StringId == "empire").Culture;
            }

            var recruit = Recruits.Where(x =>
                    x.Culture == Kingdom.All.First(k =>
                        k.Culture == cultureObject).Culture)
                .GetRandomElement();
            troopRoster.AddToCounts(recruit, numberToUpgrade);
        }
    }
}
