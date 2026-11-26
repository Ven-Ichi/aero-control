using System;
using System.Reflection;
using AeroControl;
class PowerTests {
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static int Main() {
        try {
            Type power=typeof(Snapshot).Assembly.GetType("AeroControl.WindowsPower");
            Require(power!=null,"Windows power-plan switching must be implemented");
            var read=power.GetMethod("Read"); var set=power.GetMethod("SetPlan");
            var before=new Snapshot(); read.Invoke(null,new object[]{before});
            Require(before.PowerPlans.Length>0 && before.PowerPlanId.HasValue,"Installed plans and active GUID must be readable");
            Require(Array.Exists(before.PowerPlans,p=>p.Id==before.PowerPlanId.Value),"Active plan must be included in installed plans");
            string error=(string)set.Invoke(null,new object[]{Guid.Empty});
            Require(error!=null,"Uninstalled plan IDs must be rejected");
            var after=new Snapshot(); read.Invoke(null,new object[]{after});
            Require(after.PowerPlanId==before.PowerPlanId,"Rejecting an invalid plan must preserve the active plan");
            Console.WriteLine("PASS: installed Windows plans, active GUID, invalid-plan rejection and preserved active plan");return 0;
        } catch(Exception e) {Console.Error.WriteLine("FAIL: "+e.Message);return 1;}
    }
}
