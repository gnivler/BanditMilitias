using System;
using Bandit_Militias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace Bandit_Militias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        public override Hero PartyOwner => MobileParty.ActualClan?.Leader;
        public override Settlement HomeSettlement { get; }
        public override Hero Leader => leader;
        [SaveableField(1)] private Hero leader;

        private static readonly AccessTools.FieldRef<NameGenerator, TextObject[]> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, TextObject[]>("_gangLeaderNames");

        [CachedData] private TextObject cachedName;

        private ModBanditMilitiaPartyComponent(Hero hero)
        {
            leader = hero;
            HomeSettlement = hero.HomeSettlement;
        }

        public override TextObject Name
        {
            get
            {
                cachedName ??= MobileParty.MapFaction.Name;
                cachedName.SetTextVariable("IS_BANDIT", 1);
                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            if (leader != newLeader)
            {
                leader?.RemoveMilitiaHero();
            }

            leader = newLeader;
        }

        public static MobileParty CreateBanditParty(Clan clan)
        {
            var hero = Helper.CreateHero();
            try
            {
                var nameIndex = (int)Traverse.Create(NameGenerator.Current)
                    .Method("SelectNameIndex", hero, GangLeaderNames(NameGenerator.Current), 0, true, false)
                    .GetValue();
                var originalStringId = hero.CharacterObject.StringId;
                hero.CharacterObject.StringId = hero.CharacterObject.StringId.Replace("Bandit_Militia", "");
                NameGenerator.Current.AddName(
                    (uint)Traverse.Create(NameGenerator.Current)
                        .Method("CreateNameCode", hero.CharacterObject, GangLeaderNames(NameGenerator.Current), nameIndex)
                        .GetValue());
                hero.CharacterObject.StringId = originalStringId;
                var textObject = GangLeaderNames(NameGenerator.Current)[nameIndex].CopyTextObject();
                textObject.SetTextVariable("FIRST_NAME", hero.FirstName);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject);
                hero.SetName(textObject, hero.FirstName);
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }

            hero.Clan = clan;
            var mobileParty = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(hero), m =>
            {
                m.ActualClan = clan;
            });

            mobileParty.MemberRoster.AddToCounts(hero.CharacterObject, 1, false, 0, 0, true, 0);
            return mobileParty;
        }
    }
}
