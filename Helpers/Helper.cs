using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming  

namespace Bandit_Militias.Helpers
{
    public static class Helper
    {
        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            var mountedTroops = troopRoster.GetTroopRoster().Where(x => x.Character.IsMounted);
            var mountedTroopTypeCount = mountedTroops.Count();
            var total = 0;
            for (var i = 0; i < mountedTroopTypeCount; i++)
            {
                total += troopRoster.GetElementNumber(i);
            }

            return total;
        }

        internal static float Variance => MBRandom.RandomFloatRanged(0.8f, 1.2f);
        private static readonly int MinSplitSize = Globals.Settings.MinPartySize * 2;

        internal static void TrySplitParty(MobileParty mobileParty)
        {
            if (GlobalMilitiaPower + mobileParty.Party.TotalStrength > CalculatedGlobalPowerLimit ||
                mobileParty.MemberRoster.TotalManCount < MinSplitSize ||
                !IsBM(mobileParty) ||
                mobileParty.IsTooBusyToMerge())
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (roll <= Globals.Settings.RandomSplitChance ||
                !IsBM(mobileParty) ||
                mobileParty.Party.TotalStrength <= CalculatedMaxPartyStrength * Globals.Settings.StrengthSplitFactor * Variance ||
                mobileParty.Party.MemberRoster.TotalManCount <= CalculatedMaxPartySize * Globals.Settings.SizeSplitFactor * Variance)
            {
                return;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().Where(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Mod.Log("Bad item: " + item);
                    continue;
                    //Traverse.Create(PartyBase.MainParty).Property<ItemRoster>("ItemRoster").Value.Remove(item);
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (Rng.Next(0, 1) == 0)
                {
                    troops1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    troops2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                troops1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
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
                var settlements = Settlement.FindSettlementsAroundPosition(original.Position2D, 30).ToList();
                militia1.MobileParty.SetMovePatrolAroundSettlement(settlements.GetRandomElement() ?? Settlement.All.GetRandomElement());
                militia2.MobileParty.SetMovePatrolAroundSettlement(settlements.GetRandomElement() ?? Settlement.All.GetRandomElement());
                Mod.Log($"{militia1.MobileParty.MapFaction.Name} <- Split -> {militia2.MobileParty.MapFaction.Name}");
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (__instance.IsBandit ||
                PartyMilitiaMap.ContainsKey(__instance) &&
                __instance.Party.IsMobile &&
                __instance.CurrentSettlement is null &&
                __instance.Party.MemberRoster.TotalManCount > 0 &&
                !__instance.IsUsedByAQuest() &&
                !__instance.StringId.StartsWith("ebdi_deserters_party_") &&
                !__instance.StringId.StartsWith("caravan_ambush_quest_") &&
                !__instance.StringId.StartsWith("arzagos_banner_piece_quest_raider_party_") &&
                !__instance.StringId.StartsWith("istiana_banner_piece_quest_raider_party_") &&
                !__instance.StringId.StartsWith("rescue_family_quest_raider_party_") &&
                !__instance.StringId.StartsWith("destroy_raiders_conspiracy_quest_") &&
                !__instance.StringId.StartsWith("radagos_raider_party_") &&
                !__instance.StringId.StartsWith("locate_and_rescue_traveller_quest_raider_party_") &&
                !__instance.StringId.StartsWith("company_of_trouble_") &&
                !__instance.StringId.StartsWith("locate_and_rescue_traveller_quest_raider_party_") &&
                !__instance.StringId.StartsWith("villagers_of_landlord_needs_access_to_village_common_quest") &&
                // Calradia Expanded Kingdoms
                !__instance.Name.Contains("manhunter") &&
                !__instance.IsTooBusyToMerge())
            {
                return true;
            }

            return false;
        }

        internal static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return Campaign.Current.VisualTrackerManager.CheckTracked(mobileParty);
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
        {
            var troopRoster = TroopRoster.CreateDummyTroopRoster();
            var prisonerRoster = TroopRoster.CreateDummyTroopRoster();
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
                foreach (var troopRosterElement in roster.GetTroopRoster().Where(x => x.Character?.HeroObject is null))
                {
                    troopRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number,
                        woundedCount: troopRosterElement.WoundedNumber, xpChange: troopRosterElement.Xp);
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var troopRosterElement in roster.GetTroopRoster().Where(x => x.Character?.HeroObject is null))
                {
                    prisonerRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number,
                        woundedCount: troopRosterElement.WoundedNumber, xpChange: troopRosterElement.Xp);
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
                    Mod.Log(">>> FLUSH " + mobileParty.Name);
                    Trash(mobileParty);
                }
            }
        }

        internal static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            if (mobileParty == mobileParty?.MoveTargetParty?.MoveTargetParty)
            {
                return false;
            }

            return mobileParty.TargetParty is not null ||
                   mobileParty.ShortTermTargetParty is not null ||
                   mobileParty.ShortTermBehavior == AiBehavior.EngageParty ||
                   mobileParty.ShortTermBehavior == AiBehavior.FleeToPoint;
        }

        internal static void Trash(MobileParty mobileParty)
        {
            Mod.Log("Trashing " + mobileParty.Name);
            PartyMilitiaMap.Remove(mobileParty);
            // added as workaround/fix for issue seen in 1.5.9 where TroopRoster.Count is wrong and TroopRoster.Clear() throws
            Traverse.Create(mobileParty.MemberRoster).Field<int>("_count").Value =
                mobileParty.MemberRoster.GetTroopRoster().Count(x => x.Character is not null);
            mobileParty.MemberRoster.UpdateVersion();
            mobileParty.RemoveParty();
        }


        internal static void KillHero(this Hero hero)
        {
            try
            {
                // howitzer approach to lobotomize the game of any bandit heroes
                hero.ChangeState(Hero.CharacterStates.Dead);
                MBObjectManager.Instance.UnregisterObject(hero);
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_deadAndDisabledHeroes").Value.Remove(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_allHeroes").Value.Remove(hero);
                var roList = Traverse.Create(Campaign.Current.CampaignObjectManager).Property<MBReadOnlyList<Hero>>("Heroes").Value;
                var fieldInfo = AccessTools.Field(typeof(MBReadOnlyList<Hero>), "_list");
                var heroes = fieldInfo.GetValue(roList) as List<Hero>;
                heroes?.Remove(hero);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.", LogLevel.Info);
            FlushDeadBanditRemnants();
            FlushBanditMilitias();
            FlushHeroes();
            FlushMapEvents();
            Flush();
        }

        private static void FlushBanditMilitias()
        {
            PartyMilitiaMap.Clear();
            var hasLogged = false;
            var partiesToRemove = MobileParty.All.Where(x => x.StringId.StartsWith("Bandit_Militia")).ToList();
            foreach (var mobileParty in partiesToRemove)
            {
                if (!hasLogged)
                {
                    Mod.Log($">>> FLUSH {partiesToRemove.Count} Bandit Militias", LogLevel.Info);
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        // clean-up for older version
        // DRY :(
        private static void FlushDeadBanditRemnants()
        {
            foreach (var settlement in Settlement.All.Where(x => x.Party.PrisonRoster.TotalManCount > 0))
            {
                var count = Traverse.Create(settlement.Party.PrisonRoster).Field<int>("_count").Value;
                if (count < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        var prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                        if (prisoner.IsHero &&
                            !prisoner.HeroObject.IsAlive)
                        {
                            Mod.Log($">>> FLUSH dead bandit hero prisoner {prisoner.HeroObject.Name} at {settlement.Name}.");
                            settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                            MBObjectManager.Instance.UnregisterObject(prisoner);
                        }
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }
            }

            foreach (var mobileParty in MobileParty.All.Where(x => x.Party.PrisonRoster.TotalManCount > 0))
            {
                // GetCharacterAtIndex() throws, if 0...
                var count = Traverse.Create(mobileParty.Party.PrisonRoster).Field<int>("_count").Value;
                if (count < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    var prisoner = mobileParty.Party.PrisonRoster.GetCharacterAtIndex(i);
                    if (prisoner.IsHero &&
                        !prisoner.HeroObject.IsAlive)
                    {
                        mobileParty.Party.PrisonRoster.AddToCounts(prisoner, -1);
                        Mod.Log($">>> FLUSH Dead bandit hero mobile party prisoner {prisoner.HeroObject.Name}.");
                    }
                }
            }
        }

        internal static void ReHome()
        {
            var tempList = PartyMilitiaMap.Values.Where(x => x?.Hero?.HomeSettlement is null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }

        internal static void Flush()
        {
            FlushSettlementHeroesWithoutParty();
            FlushHideoutsOfMilitias();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadCharacterObjects();
            FlushZeroParties();
            Mod.Log(">>> FLUSHED.");
        }

        // kills heroes that leaked before being fixed in 2.8.0
        private static void FlushSettlementHeroesWithoutParty()
        {
            // ~~fixed in 2.8.0, leaving in for now~~
            foreach (var settlement in Settlement.All.Where(x => x.HeroesWithoutParty.Count > 0))
            {
                var militiaHeroes = settlement.HeroesWithoutParty.Intersect(PartyMilitiaMap.Values.Select(x => x.Hero)).ToList();
                for (var i = 0; i < militiaHeroes.Count; i++)
                {
                    Traverse.Create(HeroesWithoutParty(settlement)).Field<List<Hero>>("_list").Value.Remove(militiaHeroes[i]);
                    Mod.Log($">>> FLUSH Removing bandit hero without party {militiaHeroes[i].Name} at {settlement}.");
                }
            }
        }

        // kills heroes that leak
        private static void FlushHeroes()
        {
            var heroes = Hero.All.Where(x =>
                    (x.PartyBelongedTo is null &&
                     x.PartyBelongedToAsPrisoner is null ||
                     x.HeroState == Hero.CharacterStates.Prisoner) &&
                    Clan.BanditFactions.Contains(x.MapFaction) &&
                    x.StringId.Contains("CharacterObject"))
                .ToList();

            for (var i = 0; i < heroes.Count; i++)
            {
                Mod.Log(">>> FLUSH prisoner " + heroes[i].Name);
                KillHero(heroes[i]);
            }
        }

        private static void FlushZeroParties()
        {
            var parties = MobileParty.All.Where(x =>
                x != MobileParty.MainParty &&
                x.CurrentSettlement is null && x.MemberRoster.TotalManCount == 0).ToList();
            for (var i = 0; i < parties.Count; i++)
            {
                Mod.Log($">>> FLUSH party without a current settlement or any troops.");
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
                    IsBM(x.MobileParty)))
                {
                    Mod.Log(">>> FLUSH MapEvent.");
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
                    if (IsBM(party))
                    {
                        Mod.Log(">>> FLUSH Hideout.");
                        LeaveSettlementAction.ApplyForParty(party);
                        party.SetMovePatrolAroundSettlement(hideout);
                    }
                }
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.All.Where(x =>
                x.Name.ToString() == "Bandit Militia" && x.PartyBelongedTo is null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($">>> FLUSH  {heroes.Count} null-party heroes.", LogLevel.Info);
                }

                Mod.Log(">>> FLUSH null party hero " + hero.Name);
                hero.KillHero();
            }
        }

        private static void FlushBadCharacterObjects()
        {
            var badChars = CharacterObject.All.Where(x => x.HeroObject is null)
                .Where(x =>
                    x.Name is null ||
                    x.Occupation == Occupation.NotAssigned ||
                    x.Occupation == Occupation.Outlaw &&
                    x.HeroObject?.CurrentSettlement is not null)
                .Where(x =>
                    !x.StringId.Contains("template") &&
                    !x.StringId.Contains("char_creation") &&
                    !x.StringId.Contains("equipment") &&
                    !x.StringId.Contains("for_perf") &&
                    !x.StringId.Contains("dummy") &&
                    !x.StringId.Contains("npc_") &&
                    // Calradia Expanded Kingdoms
                    !x.StringId.Contains("vlandian_balestrieri_veterani") &&
                    !x.StringId.Contains("unarmed_ai"))
                .ToList();

            var hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (badChar is null)
                {
                    continue;
                }

                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($">>> FLUSH  {badChars.Count} bad characters.", LogLevel.Info);
                }

                Mod.Log($">>> FLUSH mock Unregistering {badChar.StringId}");
                //Traverse.Create(badChar.HeroObject?.CurrentSettlement)
                //    .Field("_heroesWithoutParty").Method("Remove", badChar.HeroObject).GetValue();
                //MBObjectManager.Instance.UnregisterObject(badChar);
            }
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty.MobileParty + " size " + mobileParty.MobileParty.MemberRoster.TotalManCount, LogLevel.Info);
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushNeutralBanditParties Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MemberRoster.TotalManCount == 0))
            {
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushEmptyMilitiaParties Clearing {tempList.Count} empty parties", tempList);
        }

        // Bob's Bandit Militia vs Ross' Bandit Militia
        internal static string Possess(string input)
        {
            // game tries to redraw the PartyNamePlateVM after combat with multiple militias
            // and crashes because mobileParty.Party.LeaderHero?.FirstName.ToString() is null
            if (input is null)
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
                x.Name.ToString() != "Sparring Targe" &&
                x.Name.ToString() != "Trash Item" &&
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
            //T.Restart();
            var gear = new Equipment();
            var haveShield = false;
            var haveBow = false;
            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    EquipmentElement randomElement = default;
                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItems.Where(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                    {
                        break;
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow ||
                        randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (slot < 3)
                        {
                            // try again, try harder
                            if (haveBow)
                            {
                                slot--;
                                continue;
                            }

                            haveBow = true;
                            gear[slot] = randomElement;
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
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        // try again, try harder
                        if (haveShield)
                        {
                            slot--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[slot] = randomElement;
                }

                gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
                //Mod.Log("-----");
                //for (var i = 0; i < 10; i++)
                //{
                //    Mod.Log(gear[i].Item?.Name);
                //}
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }

            //Mod.Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }

        internal static void ClampSettingsValues(ref Settings settings)
        {
            settings.CooldownHours = settings.CooldownHours.Clamp(1, 168);
            settings.GrowthFactor = settings.GrowthFactor.Clamp(0, 100);
            settings.GrowthChance = settings.GrowthChance.Clamp(0, 1);
            settings.MaxItemValue = settings.MaxItemValue.Clamp(1000, int.MaxValue);
            settings.MinPartySize = settings.MinPartySize.Clamp(1, int.MaxValue);
            settings.RandomSplitChance = settings.RandomSplitChance.Clamp(0, 1);
            settings.StrengthSplitFactor = settings.StrengthSplitFactor.Clamp(0.25f, 1);
            settings.SizeSplitFactor = settings.SizeSplitFactor.Clamp(0.25f, 1);
            settings.PartyStrengthFactor = settings.PartyStrengthFactor.Clamp(0.25f, 2);
            settings.MaxPartySizeFactor = settings.MaxPartySizeFactor.Clamp(0.25f, 2);
            settings.GrowthChance = settings.GrowthChance.Clamp(0, 1);
            settings.GrowthFactor = settings.GrowthFactor.Clamp(0, 1); // todo fix dupe
            settings.MaxItemValue = settings.MaxItemValue.Clamp(1_000, int.MaxValue);
            settings.LooterUpgradeFactor = settings.LooterUpgradeFactor.Clamp(0, 1);
            settings.MaxStrengthDeltaPercent = settings.MaxStrengthDeltaPercent.Clamp(0, 100);
            settings.GlobalPowerFactor = settings.GlobalPowerFactor.Clamp(0, 1);
            settings.MaxTrainingTier = settings.MaxTrainingTier.Clamp(0, 6);
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
            try
            {
                var parties = MobileParty.All.Where(x => x.LeaderHero is not null && !IsBM(x)).ToList();
                CalculatedMaxPartySize = Convert.ToInt32(parties.Select(x => x.Party.PartySizeLimit).Average() * Globals.Settings.MaxPartySizeFactor * Variance);
                CalculatedMaxPartyStrength = Convert.ToInt32(parties.Select(x => x.Party.TotalStrength).Average() * Globals.Settings.PartyStrengthFactor * Variance);
                CalculatedGlobalPowerLimit = Convert.ToInt32(parties.Select(x => x.Party.TotalStrength).Sum() * Variance);
                GlobalMilitiaPower = Convert.ToInt32(PartyMilitiaMap.Keys.Select(x => x.Party.TotalStrength).Sum());
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        public static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 20;
            var settlements = Settlement.FindSettlementsAroundPosition(position, arbitraryDistance);
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
            var result = highest.ToList().GetRandomElement();
            return result;
        }

        public static void ConvertLootersToKingdomCultureRecruits(ref TroopRoster troopRoster, CultureObject culture, int numberToUpgrade)
        {
            var recruit = Recruits.Where(x =>
                    x.Culture == Clan.All.FirstOrDefault(k => k.Culture == culture)?.Culture)
                .ToList().GetRandomElement() ?? Recruits.ToList().GetRandomElement();

            troopRoster.AddToCounts(recruit, numberToUpgrade);
        }

        // 1.5.9 this condition leads to a null ref in DoWait() so we hack it
        internal static void FixMapEventFuckery()
        {
            if (PlayerEncounter.Battle is not null &&
                PartyBase.MainParty.MapEvent is null)
            {
                var sides = Traverse.Create(PlayerEncounter.Battle).Field<MapEventSide[]>("_sides").Value;
                var playerSide = sides.FirstOrDefault(x => x.LeaderParty.LeaderHero == Hero.MainHero);
                var attacker = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Attacker)?.LeaderParty;
                var defender = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Defender)?.LeaderParty;
                PartyBase.MainParty.MapEventSide = playerSide;
                var initialize = AccessTools.Method(typeof(MapEvent), "Initialize", new[] {typeof(PartyBase), typeof(PartyBase), typeof(MapEvent.BattleTypes)});
                initialize.Invoke(PartyBase.MainParty.MapEvent, new object[] {attacker, defender, MapEvent.BattleTypes.None});
            }
        }

        internal static bool IsBM(MobileParty mobileParty)
        {
            return mobileParty is not null &&
                   PartyMilitiaMap.ContainsKey(mobileParty);
        }
    }
}
