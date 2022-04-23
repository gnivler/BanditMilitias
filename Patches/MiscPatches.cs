using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize");

                MinSplitSize = Globals.Settings.MinPartySize * 2;
                EquipmentItems.Clear();
                PopulateItems();

                // 1.7 changed CreateHeroAtOccupation to only fish from this: NotableAndWandererTemplates
                // this has no effect on 1.6.5 since the property doesn't exist
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

                Recruits = CharacterObject.All.Where(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && !filter.Contains(c.StringId)
                    && !c.StringId.EndsWith("_tier_1"));

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

                PartyMilitiaMap.Clear();
                Hideouts = Settlement.FindAll(x => x.IsHideout).ToList();

                // considers leaderless militias
                var militias = MobileParty.All.Where(m =>
                    m.LeaderHero is not null && m.StringId.StartsWith("Bandit_Militia")).ToList();

                for (var i = 0; i < militias.Count; i++)
                {
                    var militia = militias[i];
                    var recreatedMilitia = new Militia(militia);
                    SetMilitiaPatrol(recreatedMilitia.MobileParty);
                    PartyMilitiaMap.Add(recreatedMilitia.MobileParty, recreatedMilitia);
                }

                DoPowerCalculations(true);
                FlushMilitiaCharacterObjects();
                // 1.6 is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                Mod.Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
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
                    Mod.Log(__exception);
                    Mod.Log(party?.MobileParty.StringId);
                    Mod.Log(feat.StringId);
                    Mod.Log($"guessing: {party?.Owner?.Culture}?");
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
                    Mod.Log(__exception);
                    Mod.Log(owner.MobileParty.StringId);
                    Mod.Log(character.StringId);
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(Hero), "SetInitialValuesFromCharacter")]
        public class HeroSetInitialValuesFromCharacter
        {
            public static Exception Finalizer(Hero __instance, Exception __exception)
            {
                if (__exception is not null)
                {
                    Mod.Log(__instance);
                }

                return null;
            }
        }

        //[HarmonyPatch(typeof(ClanRoleMemberItemVM), "IsHeroAssignableForRole")]
        //public class ClanRoleMemberItemVMIsHeroAssignableForRole
        //{
        //    public static void Postfix(SkillEffect.PerkRole role, ref bool __result)
        //    {
        //        if ((int)role == 15)
        //        {
        //            __result = true;
        //        }
        //    }
        //}
        //
        //[HarmonyPatch(typeof(ClanRoleMemberItemVM), "GetRoleHint")]
        //public static class ClanRoleMemberItemVMGetRoleHint
        //{
        //    public static Exception Finalizer(ClanRoleMemberItemVM __instance, Exception __exception)
        //    {
        //        //Mod.Log(__exception);
        //        return null;
        //    }
        //}
        //
        //[HarmonyPatch(typeof(ClanPartyItemVM), "GetAssignablePartyRoles")]
        //public static class ClanPartyItemVMGetAssignablePartyRoles
        //{
        //    public static bool Prefix(ref IEnumerable __result)
        //    {
        //        __result = new[]
        //        {
        //            SkillEffect.PerkRole.Quartermaster,
        //            SkillEffect.PerkRole.Scout,
        //            SkillEffect.PerkRole.Surgeon,
        //            SkillEffect.PerkRole.Engineer,
        //            (SkillEffect.PerkRole)15
        //        };
        //
        //        return false;
        //    }
        //}
        //
        //[HarmonyPatch(typeof(Game), "Initialize")]
        //public static class GameInitialize
        //{
        //    public static void Postfix()
        //    {
        //        var gameText = Game.Current.GameTextManager.AddGameText("_merchant");
        //        gameText.AddVariation("{=aQducWrZ}Variation Bonus to trade skill: {a0}");
        //    }
        //}
        //
        //[HarmonyPatch(typeof(Campaign), "InitializeDefaultCampaignObjects")]
        //public static class CampaignCreateLists
        //{
        //    public static void Postfix(Campaign __instance)
        //    {
        //        var merchant = (SkillEffect)Traverse.Create(__instance.DefaultSkillEffects).Method("Create", "Merchant SkillEffect").GetValue();
        //        merchant.Initialize(new TextObject("{=aQducWrZ}SkillEffect Bonus to trade skill: {a0}"),
        //            new[] { DefaultSkills.Trade },
        //            (SkillEffect.PerkRole)15,
        //            10000f,
        //            SkillEffect.PerkRole.None,
        //            0f, SkillEffect.EffectIncrementType.Invalid);
        //        // Initialize() creates an empty TextObject name...
        //        Traverse.Create(merchant).Field<TextObject>("_name").Value = new TextObject("Merchant SkillEffect Name");
        //    }
        //}
        //
        //[HarmonyPatch(typeof(ClanRoleMemberItemVM), "GetRelevantSkillForRole")]
        //public static class ClanRoleMemberItemVMGetRelevantSkillForRole
        //{
        //    public static bool Prefix(SkillEffect.PerkRole role, ref SkillObject __result)
        //    {
        //        if ((int)role == 15)
        //        {
        //            __result = DefaultSkills.Trade;
        //            return false;
        //        }
        //
        //        return true;
        //    }
        //}
        //
        //private static Hero MerchantRoleOwner;
        //private static Hero MerchantEffectiveRoleOwner => MerchantRoleOwner;
        //
        //[HarmonyPatch(typeof(ClanRoleItemVM), "GetMemberAssignedToRole")]
        //public static class ClanRoleItemVMGetMemberAssignedToRole
        //{
        //    public static bool Prefix(ClanRoleItemVM __instance, MobileParty party, SkillEffect.PerkRole role, out Hero roleOwner, out Hero effectiveRoleOwner)
        //    {
        //        if ((int)role == 15)
        //        {
        //            __instance.Name = "Merchant";
        //            roleOwner = MerchantRoleOwner;
        //            effectiveRoleOwner = MerchantEffectiveRoleOwner;
        //            return false;
        //        }
        //
        //        roleOwner = default;
        //        effectiveRoleOwner = default;
        //        return true;
        //    }
        //}
        //
        //[HarmonyPatch(typeof(ClanRoleMemberItemVM), "ExecuteAssignHeroToRole")]
        //public static class ClanRoleMemberItemVMExecuteAssignHeroToRole
        //{
        //    public static bool Prefix(ClanRoleMemberItemVM __instance, Action ____onRoleAssigned)
        //    {
        //        if ((int)__instance.Role == 15)
        //        {
        //            Traverse.Create(__instance).Method("OnSetMemberAsRole", (SkillEffect.PerkRole)15).GetValue();
        //            ____onRoleAssigned?.Invoke();
        //            return false;
        //        }
        //
        //        return true;
        //    }
        //}
        //
        //
        //[HarmonyPatch(typeof(ClanRoleMemberItemVM), "OnSetMemberAsRole")]
        //public static class ClanRoleMemberItemVMOnSetMemberAsRole
        //{
        //    public static bool Prefix(ClanRoleMemberItemVM __instance, Action ____onRoleAssigned, SkillEffect.PerkRole role, MobileParty ____party)
        //    {
        //        if ((int)role == 15)
        //        {
        //            if (____party.GetHeroPerkRole(__instance.Member.HeroObject) != role)
        //            {
        //                ____party.RemoveHeroPerkRole(__instance.Member.HeroObject);
        //                MerchantRoleOwner = __instance.Member.HeroObject;
        //                // TODO need to patch RemoveHeroPerkRole?
        //            }
        //
        //            return false;
        //        }
        //
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(TroopRoster), "AddToCounts")]
        public class asdfsadf
        {
            public static void Prefix(TroopRoster __instance, int count)
            {
                try
                {
                    if (count < 0
                        && !__instance.IsPrisonRoster)
                    {
                        var party = Traverse.Create(__instance).Property<PartyBase>("OwnerParty").Value;
                        var stack = new StackTrace();
                        if (stack.GetFrames()![2].GetMethod().Name == "UpgradeReadyTroops")
                        {
                            return;
                        }
                        if (party?.MobileParty is not null && party.MobileParty.IsBM())
                        {
                            if (__instance.TotalManCount < Globals.Settings.MinPartySize)
                            {
                                Mod.Log("");
                                Mod.Log(party.MapEvent is null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }
    }
}
