using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias
{
    public class Hacks
    {
        //[HarmonyPatch(typeof(TooltipVMExtensions), "AddPartyTroopProperties")]
        //public static class TooltipVMExtensionsAddPartyTroopProperties
        //{
        //    public static Exception Finalizer(Exception __exception, TooltipVM tooltipVM, TroopRoster troopRoster, TextObject title, bool isInspected)
        //    {
        //        if (__exception is not null)
        //        {
        //            Meow();
        //
        //
        //            tooltipVM.AddProperty(string.Empty, string.Empty, -1);
        //            tooltipVM.AddProperty(title.ToString(), (Func<string>)(() =>
        //            {
        //                TroopRoster troopRoster1 = troopRoster;
        //                int healtyNumber = 0;
        //                int woundedNumber = 0;
        //                for (int index = 0; index < troopRoster1.Count; ++index)
        //                {
        //                    TroopRosterElement elementCopyAtIndex = troopRoster1.GetElementCopyAtIndex(index);
        //                    healtyNumber += elementCopyAtIndex.Number - elementCopyAtIndex.WoundedNumber;
        //                    woundedNumber += elementCopyAtIndex.WoundedNumber;
        //                }
        //
        //                TextObject textObject = new TextObject("{=iXXTONWb} ({PARTY_SIZE})");
        //                textObject.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(healtyNumber, woundedNumber, isInspected));
        //                return textObject.ToString();
        //            }));
        //            if (isInspected)
        //                tooltipVM.AddProperty("", "", propertyFlags: TooltipProperty.TooltipPropertyFlags.RundownSeperator);
        //            if (isInspected)
        //            {
        //                Dictionary<FormationClass, Tuple<int, int>> dictionary = new Dictionary<FormationClass, Tuple<int, int>>();
        //                for (int index = 0; index < troopRoster.Count; ++index)
        //                {
        //                    TroopRosterElement elementCopyAtIndex = troopRoster.GetElementCopyAtIndex(index);
        //                    if (dictionary.ContainsKey(elementCopyAtIndex.Character.DefaultFormationClass))
        //                    {
        //                        Tuple<int, int> tuple = dictionary[elementCopyAtIndex.Character.DefaultFormationClass];
        //                        dictionary[elementCopyAtIndex.Character.DefaultFormationClass] = new Tuple<int, int>(tuple.Item1 + elementCopyAtIndex.Number - elementCopyAtIndex.WoundedNumber, tuple.Item2 + elementCopyAtIndex.WoundedNumber);
        //                    }
        //                    else
        //                        dictionary.Add(elementCopyAtIndex.Character.DefaultFormationClass, new Tuple<int, int>(elementCopyAtIndex.Number - elementCopyAtIndex.WoundedNumber, elementCopyAtIndex.WoundedNumber));
        //                }
        //
        //                foreach (KeyValuePair<FormationClass, Tuple<int, int>> keyValuePair in dictionary)
        //                {
        //                    TextObject textObject = new TextObject("{=Dqydb21E} {PARTY_SIZE}");
        //                    textObject.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(keyValuePair.Value.Item1, keyValuePair.Value.Item2, true));
        //                    TextObject text = GameTexts.FindText("str_troop_type_name", keyValuePair.Key.GetName());
        //                    tooltipVM.AddProperty(text.ToString(), textObject.ToString());
        //                }
        //            }
        //
        //            if (!(tooltipVM.IsExtended & isInspected))
        //                return null;
        //            tooltipVM.AddProperty(string.Empty, string.Empty, -1);
        //            tooltipVM.AddProperty(GameTexts.FindText("str_troop_types").ToString(), " ");
        //            tooltipVM.AddProperty("", "", propertyFlags: TooltipProperty.TooltipPropertyFlags.DefaultSeperator);
        //            for (int index = 0; index < troopRoster.Count; ++index)
        //            {
        //                TroopRosterElement elementCopyAtIndex1 = troopRoster.GetElementCopyAtIndex(index);
        //                if (elementCopyAtIndex1.Character.IsHero)
        //                {
        //                    CharacterObject hero = elementCopyAtIndex1.Character;
        //                    tooltipVM.AddProperty(elementCopyAtIndex1.Character.Name.ToString(), (Func<string>)(() =>
        //                    {
        //                        TroopRoster troopRoster2 = troopRoster;
        //                        int indexOfTroop = troopRoster2.FindIndexOfTroop(hero);
        //                        if (indexOfTroop == -1)
        //                            return "";
        //                        TroopRosterElement elementCopyAtIndex2 = troopRoster2.GetElementCopyAtIndex(indexOfTroop);
        //                        TextObject textObject = new TextObject("{=aE4ZRbB6} {HEALTH}%");
        //                        textObject.SetTextVariable("HEALTH", elementCopyAtIndex2.Character.HeroObject.HitPoints * 100 / elementCopyAtIndex2.Character.MaxHitPoints());
        //                        return textObject.ToString();
        //                    }));
        //                }
        //            }
        //
        //            try
        //            {
        //                for (int j = 0; j < troopRoster.Count; j++)
        //                {
        //                    int buf = j;
        //                    CharacterObject character = troopRoster.GetElementCopyAtIndex(buf).Character;
        //                    if (!character.IsHero)
        //                        tooltipVM.AddProperty(character.Name.ToString(), (Func<string>)(() =>
        //                        {
        //                            TroopRoster troopRoster3 = troopRoster;
        //                            if (troopRoster3.FindIndexOfTroop(character) == -1)
        //                                return "";
        //                            TroopRosterElement elementCopyAtIndex3 = troopRoster3.GetElementCopyAtIndex(troopRoster3.FindIndexOfTroop(character));
        //                            CharacterObject character1 = elementCopyAtIndex3.Character;
        //                            if ((character1 != null ? (!character1.IsHero ? 1 : 0) : 0) != 0 && troopRoster3.GetElementCopyAtIndex(buf).Character != null)
        //                            {
        //                                TroopRosterElement elementCopyAtIndex4 = troopRoster3.GetElementCopyAtIndex(buf);
        //                                TextObject textObject = new TextObject("{=QyVbwGLp}{PARTY_SIZE}");
        //                                textObject.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(elementCopyAtIndex4.Number - elementCopyAtIndex4.WoundedNumber, elementCopyAtIndex4.WoundedNumber, true));
        //                                return textObject.ToString();
        //                            }
        //
        //                            if (j <= troopRoster3.Count)
        //                            {
        //                                CharacterObject character2 = elementCopyAtIndex3.Character;
        //                            }
        //
        //                            return "";
        //                        }));
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                Debugger.Break();
        //            }
        //        }
        //
        //        return null;
        //    }
        //}
        //
        // throws during nuke
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        //
        private static Exception ExperienceFinalizer(DefaultPartyTrainingModel __instance, Exception __exception, MobileParty mobileParty, TroopRosterElement troop)
        {
            if (__exception is not null)
            {
                Log(__exception);
                Meow();
            }

            return null;
        }
        //    foreach (var m in MobileParty.All.WhereQ(m => m.MemberRoster.GetTroopRoster().AnyQ(e => e.Character.StringId.Contains("looter"))))
        //        {
        //            m.MemberRoster.RemoveIf(e => e.Character.Culture is null);
        //        }
        //
        //        ExplainedNumber stat = new ExplainedNumber();
        //        if (mobileParty.IsLordParty && !troop.Character.IsHero && (mobileParty.Army == null || mobileParty.Army.LeaderParty != MobileParty.MainParty) && mobileParty.MapEvent == null && (mobileParty.Party.Owner == null || mobileParty.Party.Owner.Clan != Clan.PlayerClan))
        //        {
        //            if (mobileParty.LeaderHero != null && mobileParty.LeaderHero == mobileParty.ActualClan.Leader)
        //                stat.Add((float)(15.0 + (double)troop.Character.Tier * 3.0));
        //            else
        //                stat.Add((float)(10.0 + (double)troop.Character.Tier * 2.0));
        //        }
        //
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Leadership.CombatTips))
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Leadership.CombatTips));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Leadership.RaiseTheMeek) && troop.Character.Tier < 3)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Leadership.RaiseTheMeek));
        //        //if (mobileParty.IsGarrison && mobileParty.CurrentSettlement?.Town.Governor != null && mobileParty.CurrentSettlement.Town.Governor.GetPerkValue(DefaultPerks.Bow.BullsEye))
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Bow.BullsEye));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Polearm.Drills, true))
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Polearm.Drills));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.OneHanded.MilitaryTradition) && troop.Character.IsInfantry)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.OneHanded.MilitaryTradition));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Athletics.WalkItOff, true) && !troop.Character.IsMounted && mobileParty.IsMoving)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Athletics.WalkItOff));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Throwing.Saddlebags, true) && troop.Character.IsInfantry)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Throwing.Saddlebags));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Athletics.AGoodDaysRest, true) && !troop.Character.IsMounted && !mobileParty.IsMoving && mobileParty.CurrentSettlement != null)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Athletics.AGoodDaysRest));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Bow.Trainer, true) && troop.Character.IsRanged)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Bow.Trainer));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Crossbow.RenownMarksmen) && troop.Character.IsRanged)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Crossbow.RenownMarksmen));
        //        if (mobileParty.IsActive && mobileParty.IsMoving)
        //        {
        //            if ((double)mobileParty.Morale > 75.0)
        //                PerkHelper.AddPerkBonusForParty(DefaultPerks.Scouting.ForcedMarch, mobileParty, false, ref stat);
        //            if ((double)mobileParty.ItemRoster.TotalWeight > (double)mobileParty.InventoryCapacity)
        //                PerkHelper.AddPerkBonusForParty(DefaultPerks.Scouting.Unburdened, mobileParty, false, ref stat);
        //        }
        //
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Steward.SevenVeterans) && troop.Character.Tier >= 4)
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Steward.SevenVeterans));
        //        //if (mobileParty.IsActive && mobileParty.HasPerk(DefaultPerks.Steward.DrillSergant))
        //        //    stat.Add((float)__instance.GetPerkExperiencesForTroops(DefaultPerks.Steward.DrillSergant));
        //        if (troop.Character.Culture.IsBandit)
        //            PerkHelper.AddPerkBonusForParty(DefaultPerks.Roguery.NoRestForTheWicked, mobileParty, true, ref stat);
        //    }
        //
        //    return null;
        //}
    }
}
