namespace Hansa
{
    /// <summary>A simple charge: blasting powder, then lead shot. The vanilla items are also used by the small arms.</summary>
    public static class GunLoading
    {
        public const string Powder="game:blastingpowder", Shot="game:bullets-lead";
        public const byte PowderIn=1, Loaded=3;
        public static bool IsLoad(string code) => code==Powder || code==Shot;
        public static string Problem(byte charge,bool runIn,bool moving,string code,int count,int need)
        {
            if(!runIn || moving) return "ships:gun-load-run-in";
            if(charge==Loaded) return "ships:gun-load-full";
            if(charge==0 && code!=Powder) return "ships:gun-load-powder";
            if(charge==PowderIn && code!=Shot) return "ships:gun-load-shot";
            return count<need?"ships:gun-load-more":null;
        }
    }
}
