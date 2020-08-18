// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

namespace Bandit_Militias
{
    public class Settings
    {
        public bool CanTrain = true;
        public bool RandomBanners = true;
        public float CooldownHours = 24;
        public string XpGift = "NORMAL";
        public string GoldReward = "NORMAL";
        public int MaxPartySize = 300;
        public int MinPartySize = 20;
        public float RandomSplitChance = 0.25f;
        public float StrengthSplitFactor = 0.8f;
        public float SizeSplitFactor = 0.8f;
        public float PartyStrengthFactor = 0.8f;
        public float MaxPartySizeFactor = 0.8f;
        public bool Growth = true;
        public float GrowthChance = 0.75f;
        public float GrowthInPercent = 2;
    }
}
