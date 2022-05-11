using System.Net.Sockets;
using System.Reflection;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        public override Hero PartyOwner => MobileParty.ActualClan?.Leader;
        [SaveableField(1)] internal readonly Banner Banner;
        [SaveableField(2)] internal readonly string BannerKey;
        [SaveableField(3)] internal CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [SaveableField(4)] private static bool setBandit;
        [field: SaveableField(5)] public override Settlement HomeSettlement { get; }
        // Hero.HomeSettlement isn't saved for Reasons... this is just a shotgun approach to save-everything
        [field: SaveableField(6)] public override Hero Leader { get; }

        [CachedData] private TextObject cachedName;
        private static readonly MethodInfo GetLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");

        public override TextObject Name
        {
            get
            {
                cachedName ??= new TextObject((string)GetLocalizedText.Invoke(null, new object[] { $"{Possess(MobileParty.LeaderHero.FirstName.ToString())} Bandit Militia" }));
                if (!setBandit)
                {
                    setBandit = true;
                    cachedName.SetTextVariable("IS_BANDIT", 1);
                }

                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            if (Leader != newLeader)
            {
                Leader?.RemoveMilitiaHero();
            }
              
            Traverse.Create(this).Field<Hero>("<Leader>k__BackingField").Value = newLeader;
        }

        //private ModBanditMilitiaPartyComponent()
        //{
        //    Banner = Banners.GetRandomElement();
        //    BannerKey = Banner.Serialize();
        //    leader = CreateHero();
        //}
        //
        //private ModBanditMilitiaPartyComponent(Hero hero) : this()
        //{
        //    leader = hero;
        //    HomeSettlement = hero.HomeSettlement;
        //}

        public ModBanditMilitiaPartyComponent(Clan heroClan)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            Leader = CreateHero(heroClan);
            ConfigureLeader(Leader);
            HomeSettlement = Leader.BornSettlement;
            //LogMilitiaFormed(MobileParty);
        }
    }
}
