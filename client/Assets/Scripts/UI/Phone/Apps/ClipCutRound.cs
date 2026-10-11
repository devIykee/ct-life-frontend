using System;
namespace CTLife.UI
{
    public enum ClipBeatKind { Setup, Beat, Payoff, DeadAir, SideTrack }
    public sealed class ClipReel
    {
        private readonly ClipBeatKind[] _kinds;
        public int Story { get; }
        public int Setup { get; }
        public int Payoff { get; }
        public ClipBeatKind Kind(int frame)=>_kinds[frame];
        internal ClipReel(int story)
        {
            Story=story;
            switch(story)
            {
                case 0:Setup=2;Payoff=5;_kinds=new[]{ClipBeatKind.DeadAir,ClipBeatKind.SideTrack,ClipBeatKind.Setup,ClipBeatKind.Beat,ClipBeatKind.Beat,ClipBeatKind.Payoff,ClipBeatKind.DeadAir,ClipBeatKind.SideTrack};break;
                case 1:Setup=1;Payoff=4;_kinds=new[]{ClipBeatKind.DeadAir,ClipBeatKind.Setup,ClipBeatKind.Beat,ClipBeatKind.Beat,ClipBeatKind.Payoff,ClipBeatKind.SideTrack,ClipBeatKind.DeadAir,ClipBeatKind.DeadAir};break;
                case 2:Setup=2;Payoff=6;_kinds=new[]{ClipBeatKind.SideTrack,ClipBeatKind.DeadAir,ClipBeatKind.Setup,ClipBeatKind.Beat,ClipBeatKind.Beat,ClipBeatKind.Beat,ClipBeatKind.Payoff,ClipBeatKind.DeadAir};break;
                case 3:Setup=0;Payoff=2;_kinds=new[]{ClipBeatKind.Setup,ClipBeatKind.Beat,ClipBeatKind.Payoff,ClipBeatKind.DeadAir,ClipBeatKind.SideTrack,ClipBeatKind.DeadAir,ClipBeatKind.SideTrack,ClipBeatKind.DeadAir};break;
                case 4:Setup=4;Payoff=7;_kinds=new[]{ClipBeatKind.SideTrack,ClipBeatKind.DeadAir,ClipBeatKind.SideTrack,ClipBeatKind.DeadAir,ClipBeatKind.Setup,ClipBeatKind.Beat,ClipBeatKind.Beat,ClipBeatKind.Payoff};break;
                default:throw new ArgumentOutOfRangeException(nameof(story));
            }
        }
    }
    public sealed class ClipCutResult
    {
        public int Score{get;}public int Cuts{get;}public int Clean{get;}
        internal ClipCutResult(int cuts,int clean){Cuts=cuts;Clean=clean;Score=clean*20;}
    }
    /// <summary>Original text-storyboard trimming puzzle. It never acquires or publishes video.</summary>
    public sealed class ClipCutRound
    {
        public const string ProposedId="kol_engagement_clip_cut";
        public const int Duration=50,Maximum=100,ReelCount=5,FrameCount=8;
        private readonly ClipReel[] _reels=new ClipReel[ReelCount];private double _lastNow;
        public double Elapsed{get;private set;}public double Remaining=>Math.Max(0,Duration-Elapsed);
        public bool Paused{get;private set;}public bool Cancelled{get;private set;}
        public int Index{get;private set;}public ClipReel Current=>_reels[Index];
        public int In{get;private set;}public int Out{get;private set;}=7;
        public int Kept=>Out-In+1;public bool Cut{get;private set;}public bool CleanCut{get;private set;}
        public int Cuts{get;private set;}public int Clean{get;private set;}public int Score=>Clean*20;
        public bool AllCut=>Cuts==ReelCount;public bool Finished=>!Cancelled&&(AllCut||Elapsed>=Duration);
        public bool Exact=>In==Current.Setup&&Out==Current.Payoff;
        public ClipCutResult Result=>Finished?new ClipCutResult(Cuts,Clean):throw new InvalidOperationException("Unfinished or cancelled editing has no result.");
        public ClipCutRound(double now,int seed)
        {
            Validate(now);_lastNow=now;var random=new Random(seed);var order=new[]{0,1,2,3,4};
            for(int i=4;i>0;i--){int j=random.Next(i+1);(order[i],order[j])=(order[j],order[i]);}
            for(int i=0;i<ReelCount;i++)_reels[i]=new ClipReel(order[i]);
        }
        private static void Validate(double now){if(double.IsNaN(now)||double.IsInfinity(now))throw new ArgumentOutOfRangeException(nameof(now));}
        public void Step(double now)
        {
            Validate(now);if(now<_lastNow)throw new ArgumentOutOfRangeException(nameof(now),"Clock moved backwards.");
            double delta=now-_lastNow;_lastNow=now;if(Paused||Cancelled||Finished)return;Elapsed=Math.Min(Duration,Elapsed+delta);
        }
        private bool Editable(int expected,double now){Step(now);return !Paused&&!Cancelled&&!Finished&&!Cut&&expected==Index;}
        public bool SetIn(int expected,int frame,double now)
        {if(!Editable(expected,now)||frame<0||frame>=FrameCount||frame==In)return false;In=frame;if(Out<In)Out=In;return true;}
        public bool SetOut(int expected,int frame,double now)
        {if(!Editable(expected,now)||frame<0||frame>=FrameCount||frame==Out)return false;Out=frame;if(In>Out)In=Out;return true;}
        public bool Reset(int expected,double now)
        {if(!Editable(expected,now)||(In==0&&Out==7))return false;In=0;Out=7;return true;}
        public bool Submit(int expected,double now)
        {if(!Editable(expected,now))return false;CleanCut=Exact;Cut=true;Cuts++;if(CleanCut)Clean++;return true;}
        public bool Next(int expected,double now)
        {Step(now);if(Paused||Cancelled||Finished||!Cut||expected!=Index||Index>=ReelCount-1)return false;Index++;In=0;Out=7;Cut=false;CleanCut=false;return true;}
        public bool Pause(double now){Step(now);if(Paused||Cancelled||Finished)return false;Paused=true;return true;}
        public bool Resume(double now){Step(now);if(!Paused||Cancelled||Finished)return false;Paused=false;return true;}
        public void Cancel(){if(!Finished)Cancelled=true;}
    }
}
