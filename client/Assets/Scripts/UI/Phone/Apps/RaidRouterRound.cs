using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    public sealed class RaidPacket
    {
        public int Index { get; }
        public int Lane { get; }
        public bool IsRaid { get; }
        public int Copy { get; }
        public double Arrival { get; }
        public bool Handled { get; internal set; }
        public bool Correct { get; internal set; }
        public bool CloseCall { get; internal set; }
        internal RaidPacket(int index, int lane, bool raid, int copy, double arrival)
        { Index=index; Lane=lane; IsRaid=raid; Copy=copy; Arrival=arrival; }
    }

    public sealed class RaidRouterResult
    {
        public int Score { get; }
        public int RaidsStopped { get; }
        public int ChatsBlocked { get; }
        public int BestCombo { get; }
        public int CloseCalls { get; }
        internal RaidRouterResult(int stopped,int blocked,int combo,int closeCalls)
        {
            RaidsStopped=stopped; ChatsBlocked=blocked; BestCombo=combo; CloseCalls=closeCalls;
            Score=(int)Math.Round(Math.Max(0,stopped-blocked)*100d/RaidRouterRound.RaidCount,MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Local fictional arcade rules. No services, payouts, clock sync or server assignment.</summary>
    public sealed class RaidRouterRound
    {
        public const string ProposedId="discord_mod_raid_router";
        public const int Duration=45, Maximum=100, PacketCount=24, RaidCount=12;
        public const double ApproachSeconds=4, CloseCallSeconds=.45;
        private readonly RaidPacket[] _packets=new RaidPacket[PacketCount];
        private readonly IReadOnlyList<RaidPacket> _readOnly;
        private double _lastNow, _shieldChanged;
        private int _handled;
        public IReadOnlyList<RaidPacket> Packets=>_readOnly;
        public double Elapsed { get; private set; }
        public double Remaining=>Math.Max(0,Duration-Elapsed);
        public bool Paused { get; private set; }
        public bool Finished=>Elapsed>=Duration;
        public int ShieldLane { get; private set; }=-1;
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int RaidsStopped { get; private set; }
        public int ChatsBlocked { get; private set; }
        public int CloseCalls { get; private set; }
        public int Handled=>_handled;
        public int Wave=>Elapsed<21?1:Elapsed<33?2:3;
        public RaidPacket LastPacket { get; private set; }
        public RaidRouterResult Result=>new RaidRouterResult(RaidsStopped,ChatsBlocked,BestCombo,CloseCalls);

        public RaidRouterRound(double now,int seed)
        {
            ValidateTime(now);_lastNow=now;_readOnly=Array.AsReadOnly(_packets);
            var random=new Random(seed);int lane=random.Next(3);
            for(int wave=0;wave<3;wave++)
            {
                var raids=new bool[8];for(int i=0;i<wave+3;i++)raids[i]=true;
                for(int i=7;i>0;i--){int j=random.Next(i+1);(raids[i],raids[j])=(raids[j],raids[i]);}
                for(int i=0;i<8;i++)
                {
                    int index=wave*8+i;lane=(lane+1+random.Next(2))%3;
                    double arrival=wave==0?6+i*2:wave==1?22+i*1.5:34+i*1.2;
                    _packets[index]=new RaidPacket(index,lane,raids[i],random.Next(8),arrival);
                }
            }
        }
        private static void ValidateTime(double now)
        {if(double.IsNaN(now)||double.IsInfinity(now))throw new ArgumentOutOfRangeException(nameof(now));}
        public void Step(double now)
        {
            ValidateTime(now);if(now<_lastNow)throw new ArgumentOutOfRangeException(nameof(now),"Clock moved backwards.");
            double delta=now-_lastNow;_lastNow=now;
            if(Paused||Finished)return;
            Elapsed=Math.Min(Duration,Elapsed+delta);
            while(_handled<PacketCount&&_packets[_handled].Arrival<=Elapsed)
            {
                var packet=_packets[_handled++];bool blocked=ShieldLane==packet.Lane;
                packet.Handled=true;packet.Correct=blocked==packet.IsRaid;
                if(packet.IsRaid&&blocked)RaidsStopped++;
                if(!packet.IsRaid&&blocked)ChatsBlocked++;
                packet.CloseCall=packet.IsRaid&&blocked&&packet.Arrival-_shieldChanged>=0&&packet.Arrival-_shieldChanged<=CloseCallSeconds;
                if(packet.CloseCall)CloseCalls++;
                Combo=packet.Correct?Combo+1:0;BestCombo=Math.Max(BestCombo,Combo);LastPacket=packet;
            }
        }
        public bool SetShield(int lane,double now)
        {
            if(lane< -1||lane>2)return false;
            Step(now); // Resolve arrivals before applying this input: no retroactive saves.
            if(Paused||Finished||lane==ShieldLane)return false;
            ShieldLane=lane;_shieldChanged=Elapsed;return true;
        }
        public bool Pause(double now)
        {Step(now);if(Finished||Paused)return false;Paused=true;return true;}
        public bool Resume(double now)
        {Step(now);if(Finished||!Paused)return false;Paused=false;return true;}
    }
}
