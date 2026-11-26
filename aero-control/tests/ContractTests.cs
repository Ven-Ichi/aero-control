using System;
using AeroControl;
class ContractTests {
    static int count;
    static void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
    static int Main() {
        try {
            Check(ValueRules.Temperature(64) == 64, "A valid temperature must survive");
            Check(ValueRules.Temperature(0) == null && ValueRules.Temperature(255) == null, "Sensor sentinel values must be unknown");
            Check(ValueRules.Temperature(double.NaN) == null && ValueRules.Temperature(double.PositiveInfinity) == null, "Nonfinite readings must be unknown");
            Check(ValueRules.Percent(0) == 0 && ValueRules.Percent(100) == 100, "Idle and full utilization are valid");
            Check(ValueRules.Percent(-1) == null && ValueRules.Percent(101) == null, "Out of range usage must be unknown");
            Check(ValueRules.Rpm(0) == 0 && ValueRules.Rpm(5769) == 5769, "Stopped and active fans are valid");
            Check(ValueRules.Rpm(65535) == null && ValueRules.Rpm(-1) == null, "Fan sentinel readings must be unknown");
            Check(ValueRules.FanTelemetrySufficient(3000, 3200), "Two live fan channels are sufficient");
            Check(ValueRules.FanTelemetrySufficient(3000, null), "A single live CPU fan channel is sufficient");
            Check(ValueRules.FanTelemetrySufficient(null, 3200), "A single live GPU fan channel is sufficient");
            Check(!ValueRules.FanTelemetrySufficient(null, null), "No live fan channel must not permit fan control");
            Check(ValueRules.FanBehavior(true, true) == 0x410009UL, "Auto behavior must equal the vendor wire value");
            Check(ValueRules.FanBehavior(false, false) == 0xC30009UL, "Custom behavior must equal the vendor wire value");
            Check(ValueRules.FanBehavior(true, false) == 0xC10009UL && ValueRules.FanBehavior(false, true) == 0x430009UL, "Mixed per-fan states must equal the vendor wire values");
            Check(ValueRules.FanSpeed(1, 50) == 0x3201UL && ValueRules.FanSpeed(4, 100) == 0x6404UL, "Fan speed wire value must match the vendor layout (selector 1 CPU / 4 GPU, percent in bits 8-15)");
            Check(ValueRules.FanPercent(5) == 20 && ValueRules.FanPercent(140) == 100 && ValueRules.FanPercent(60) == 60, "Fan percent must stay in the safe range");
            DateTime now = DateTime.UtcNow;
            Check(ValueRules.CanControl(true, true, now, now), "Established live capabilities permit controls");
            Check(!ValueRules.CanControl(false, true, now, now), "Disconnected service must block writes");
            Check(!ValueRules.CanControl(true, false, now, now), "Unverified hardware must block writes");
            Check(!ValueRules.CanControl(true, true, now.AddSeconds(-16), now), "Stale data must block writes");
            Check(!ValueRules.CanControl(true, true, now.AddMinutes(1), now), "Future timestamps must block writes");
            // Wrong command packing or interpreting firmware errors as Off would enable unsafe writes.
            var rules=typeof(ValueRules);
            var encode=rules.GetMethod("CoolBoostRequest",new[]{typeof(bool),typeof(ulong)});
            var decode=rules.GetMethod("CoolBoostState");
            Check(encode!=null && decode!=null,"CoolBoost requests must select the enable value from firmware readback");
            Check((ulong)encode.Invoke(null,new object[]{true,0UL})==0x10007UL,"Legacy firmware must keep its boolean enable value");
            Check((ulong)encode.Invoke(null,new object[]{false,0x100UL})==7UL,"Legacy Off must use the recovered vendor request");
            Check((ulong)encode.Invoke(null,new object[]{true,0x60000UL})==0x30007UL,"Aspire Off readback must select verified enable value 3 after restarting the app");
            Check((ulong)encode.Invoke(null,new object[]{true,0x60300UL})==0x30007UL,"Aspire On readback must preserve enable value 3");
            Check((ulong)encode.Invoke(null,new object[]{false,0x60300UL})==7UL,"Aspire Off must clear the observed enable value");
            Check((bool?)decode.Invoke(null,new object[]{0x100UL})==true,"Firmware On must decode as On");
            Check((bool?)decode.Invoke(null,new object[]{0UL})==false,"Firmware Off must decode as Off");
            Check(decode.Invoke(null,new object[]{1UL})==null,"Firmware error status must remain unknown");
            Check((bool?)decode.Invoke(null,new object[]{0x60300UL})==true,"Verified Aspire state 3 with signature 6 must be On");
            Check((bool?)decode.Invoke(null,new object[]{0x60000UL})==false,"Verified Aspire cleared state with signature 6 must be Off");
            Check(decode.Invoke(null,new object[]{0x300UL})==null,"State 3 without the verified firmware signature must remain unknown");
            Check(decode.Invoke(null,new object[]{0x60100UL})==null,"Partial Aspire state 1 was rejected in the live test and must not be treated as On");
            Check(decode.Invoke(null,new object[]{0x60200UL})==null,"Unverified partial Aspire state 2 must remain unknown");
            Check(decode.Invoke(null,new object[]{0x603E4UL})==null,"An error status on a recognized Aspire payload must remain unknown");
            Check(decode.Invoke(null,new object[]{UInt64.MaxValue})==null,"Unsupported response must remain unknown");
            bool rejected=false;
            try {encode.Invoke(null,new object[]{true,0x60100UL});}catch(System.Reflection.TargetInvocationException e){rejected=e.InnerException is ArgumentException;}
            Check(rejected,"Unknown partial Aspire state must not produce a write request");
            rejected=false;
            try {encode.Invoke(null,new object[]{false,1UL});}catch(System.Reflection.TargetInvocationException e){rejected=e.InnerException is ArgumentException;}
            Check(rejected,"Firmware error status must block even an Off request");
            var gate=rules.GetMethod("CanCoolBoost");
            Check(gate!=null,"CoolBoost must have its own capability gate");
            Check((bool)gate.Invoke(null,new object[]{true,true,(bool?)false,now,now}),"A verified live Off reading permits enabling CoolBoost");
            Check(!(bool)gate.Invoke(null,new object[]{true,true,null,now,now}),"Unknown CoolBoost state must block writes even on a fan-verified device");
            Check(!(bool)gate.Invoke(null,new object[]{true,false,(bool?)true,now,now}),"CoolBoost requires device enrollment");
            Check(!(bool)gate.Invoke(null,new object[]{true,true,(bool?)true,now.AddSeconds(-9),now}),"Stale CoolBoost telemetry must block writes");
            Check(!(bool)gate.Invoke(null,new object[]{false,true,(bool?)true,now,now}),"Disconnected CoolBoost must block writes");
            Check(!(bool)gate.Invoke(null,new object[]{true,true,(bool?)true,now.AddSeconds(1),now}),"Future CoolBoost telemetry must block writes");
            Console.WriteLine("PASS: " + count + " contract assertions"); return 0;
        } catch (Exception e) { Console.Error.WriteLine("FAIL: " + e.Message); return 1; }
    }
}
