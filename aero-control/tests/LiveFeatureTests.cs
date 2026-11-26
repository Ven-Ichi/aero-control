using System;
using System.IO;
using System.Text;
using AeroControl;
class LiveFeatureTests {
    static int Main(string[] args) {
        var report=new StringBuilder();Guid? original=null;bool passed=true;
        try {
            var before=new Snapshot();WindowsPower.Read(before);original=before.PowerPlanId;
            if(!original.HasValue)throw new Exception(before.PowerStatus);
            report.AppendLine("Original plan: "+before.PowerPlan+" ["+original+"]");
            foreach(var plan in before.PowerPlans) {
                if(plan.Id==original.Value)continue;
                string error=WindowsPower.SetPlan(plan.Id);if(error!=null)throw new Exception(error);
                var after=new Snapshot();WindowsPower.Read(after);
                if(after.PowerPlanId!=plan.Id)throw new Exception("Active GUID did not match "+plan.Name);
                report.AppendLine("PASS: switched to "+after.PowerPlan+" ["+after.PowerPlanId+"] with Windows readback");
            }
            using(var hardware=new Hardware()) {
                Snapshot state=hardware.Read();report.AppendLine("Acer service connected: "+state.ServiceConnected+"; fan verified: "+state.FanControlSupported);
                if(!state.ServiceConnected || !state.FanControlSupported)throw new Exception("Live verified Acer adapter is required for this check.");
                report.AppendLine("CoolBoost: "+state.CoolBoostStatus);
                if(!state.CoolBoost.HasValue) {
                    string error=hardware.SetCoolBoost(true);
                    if(error==null)throw new Exception("Unknown CoolBoost state must block a write.");
                    Snapshot after=hardware.Read();
                    if(after.CoolBoostStatus!=state.CoolBoostStatus)throw new Exception("CoolBoost state changed unexpectedly.");
                    report.AppendLine("PASS: unknown CoolBoost blocked before setter: "+error);
                } else {
                    bool originalBoost=state.CoolBoost.Value;
                    try {
                        string error=hardware.SetCoolBoost(false);if(error!=null)throw new Exception(error);
                        if(hardware.Read().CoolBoost!=false)throw new Exception("Off was not confirmed by firmware readback.");
                        report.AppendLine("PASS: production adapter CoolBoost Off confirmed by firmware readback");
                        // A new adapter must infer enable value 3 even while firmware is Off.
                        using(var restarted=new Hardware()) {
                            Snapshot off=restarted.Read();if(off.CoolBoost!=false)throw new Exception("Restarted adapter did not read Off.");
                            error=restarted.SetCoolBoost(true);if(error!=null)throw new Exception(error);
                            if(restarted.Read().CoolBoost!=true)throw new Exception("On was not confirmed by firmware readback.");
                            report.AppendLine("PASS: fresh production adapter CoolBoost On confirmed after starting from Off");
                        }
                    } finally {
                        hardware.Read();string error=hardware.SetCoolBoost(originalBoost);
                        if(error!=null || hardware.Read().CoolBoost!=originalBoost)throw new Exception("CoolBoost restore failed: "+error);
                        report.AppendLine("PASS: original CoolBoost "+(originalBoost?"On":"Off")+" restored and confirmed");
                    }
                }
            }
        }catch(Exception e){passed=false;report.AppendLine("FAIL: "+e.Message);}
        finally {
            if(original.HasValue) {
                string error=WindowsPower.SetPlan(original.Value);
                var after=new Snapshot();WindowsPower.Read(after);
                if(error!=null || after.PowerPlanId!=original){passed=false;report.AppendLine("RESTORE FAILED: "+error);}
                else report.AppendLine("PASS: original plan restored: "+after.PowerPlan+" ["+after.PowerPlanId+"]");
            }
        }
        Console.Write(report);if(args.Length>0)File.WriteAllText(args[0],report.ToString());return passed?0:1;
    }
}
