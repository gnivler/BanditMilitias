using TaleWorlds.SaveSystem;

namespace Bandit_Militias
{
    public class MilitiaSaveableTypeDefiner  : SaveableTypeDefiner
    {
        public MilitiaSaveableTypeDefiner() : base(42069)
        {
        }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(BanditMilitiaPartyComponent), 42069);
        }
    }
}
