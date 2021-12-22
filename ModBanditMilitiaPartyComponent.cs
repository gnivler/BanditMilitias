using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bandit_Militias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        public override Hero PartyOwner => MobileParty.ActualClan?.Leader;
        public override Settlement HomeSettlement { get; }
        private Hero leader;

        [CachedData]
        private TextObject cachedName;

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

        public static MobileParty CreateBanditParty(Clan clan)
        {
            var hero = Helper.CreateHero();
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
