namespace Hansa
{
    /// <summary>One powder charge; separate shot is optional. Charge bits preserve what was actually consumed.</summary>
    public static class GunLoading
    {
        public const string Powder="game:blastingpowder", Shot="game:bullets-lead";
        public const byte PowderIn=1, Loaded=3;
        public static bool IsLoad(string code,bool needShot=false) => code==Powder || needShot && code==Shot;
        public static bool Ready(byte charge,bool needShot=false) => needShot ? charge==Loaded : (charge & PowderIn)!=0;
        public static byte After(byte charge,bool needShot=false) => needShot && charge==PowderIn ? Loaded : PowderIn;
        public static string Hint(byte charge,bool needShot=false) => Ready(charge,needShot)?"ships:gun-load-full":charge==0 || !needShot?"ships:gun-load-powder":"ships:gun-load-shot";
        public static string Problem(byte charge,bool runIn,bool moving,string code,int count,int need,bool needShot=false)
        {
            if(!runIn || moving) return "ships:gun-load-run-in";
            if(Ready(charge,needShot)) return "ships:gun-load-full";
            if(charge==0 && code!=Powder) return "ships:gun-load-powder";
            if(charge==PowderIn && code!=Shot) return "ships:gun-load-shot";
            return count<need?"ships:gun-load-more":null;
        }
    }
}
