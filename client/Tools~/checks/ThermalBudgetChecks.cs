using System; using CTLife.World;
class Check {
 static int n;
 static void Is(bool value,string message){ n++; if(!value) throw new Exception(message); }
 static void Main(){
 var expected=new[]{60,60,60,30,15,5,5};
 for(int s=0;s<7;s++){var b=new ThermalBudget();b.Observe(s,0,0);Is(b.FramesPerSecond==expected[s],"Android status "+s);Is(b.AllowScreenSleep==(s>=4),"Sleep status "+s);}
 var budget=new ThermalBudget();budget.Observe(5,0,0);budget.Observe(0,0,1);budget.Observe(0,0,30.9);Is(budget.FramesPerSecond==5,"No premature recovery");budget.Observe(0,0,31);Is(budget.FramesPerSecond==60,"Thirty continuous cool seconds recover");
 budget.Observe(5,0,32);budget.Observe(0,0,33);budget.Observe(-1,0,64);Is(budget.FramesPerSecond==5,"Unknown poll cannot establish cooling");budget.Observe(0,0,65);Is(budget.FramesPerSecond==5,"Fresh recovery starts after unknown poll");budget.Observe(0,0,95);Is(budget.FramesPerSecond==60,"Recovery after restored sensor");
 budget.Observe(3,0,100);budget.Observe(0,0,101);budget.Observe(3,0,110);budget.Observe(0,0,111);budget.Observe(0,0,131);Is(budget.FramesPerSecond==30,"Hot recurrence resets recovery clock");budget.Observe(0,0,141);Is(budget.FramesPerSecond==60,"New cool window recovers");
 budget.Observe(0,2,145);Is(budget.FramesPerSecond==30,"AP throttling lowers frame limit");budget.Observe(4,0,146);Is(budget.FramesPerSecond==15,"Full Android signal wins over coarse AP");budget.Observe(5,0,147);Is(budget.FramesPerSecond==5,"Escalation immediate inside same legacy signal level");budget.Observe(-1,0,999);Is(budget.FramesPerSecond==5,"Unknown keeps last emergency status");
 budget.Observe(0,0,1000);budget.InterruptRecovery();budget.Observe(0,0,1040);Is(budget.FramesPerSecond==5,"Suspension does not count as observed cooling");budget.Observe(0,0,1070);Is(budget.FramesPerSecond==60,"Recovery after resumed observation");
 Console.WriteLine("PASS: "+n+" thermal transition assertions; actual production policy source");
 }}