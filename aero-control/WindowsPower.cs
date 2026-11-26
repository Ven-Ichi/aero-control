using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AeroControl {
    // Windows plans are independent of Acer's service and fan verification.
    public static class WindowsPower {
        public static void Read(Snapshot result) {
            try {
                PowerPlan[] plans=Plans(); Guid active=Active();
                result.PowerPlans=plans; result.PowerPlanId=active;
                PowerPlan plan=Array.Find(plans,p=>p.Id==active);
                result.PowerPlan=plan!=null?plan.Name:Name(active);
                result.PowerStatus=plans.Length>0?"Choose an installed Windows power plan.":"Windows reports no selectable power plans.";
            } catch(Exception e) {
                result.PowerPlans=new PowerPlan[0]; result.PowerPlanId=null;
                result.PowerPlan="Unavailable"; result.PowerStatus="Windows power plans unavailable: "+e.Message;
            }
        }
        public static string SetPlan(Guid id) {
            try {
                // Re-enumerate: a displayed plan may have been removed since the last poll.
                if(!Array.Exists(Plans(),p=>p.Id==id))return "This power plan is no longer installed. Refresh and choose an available plan.";
                uint status=PowerSetActiveScheme(IntPtr.Zero,ref id);
                if(status!=0)return "Windows rejected the power plan: "+new Win32Exception((int)status).Message+" ("+status+").";
                if(Active()!=id)return "Windows accepted the request, but the active plan did not match. Another app or policy may control it.";
                return null;
            } catch(Exception e) {return "Power plan could not be confirmed: "+e.Message;}
        }
        private static PowerPlan[] Plans() {
            var result=new List<PowerPlan>();
            for(uint index=0;;index++) {
                byte[] bytes=new byte[16]; uint size=16;
                uint status=PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,index,bytes,ref size);
                if(status==259)break;
                if(status!=0)throw new Win32Exception((int)status);
                if(size!=16)throw new InvalidOperationException("Unexpected Windows power-plan identifier.");
                Guid id=new Guid(bytes); result.Add(new PowerPlan(id,Name(id)));
            }
            return result.ToArray();
        }
        private static Guid Active() {
            IntPtr pointer; uint status=PowerGetActiveScheme(IntPtr.Zero,out pointer);
            if(status!=0)throw new Win32Exception((int)status);
            try {return (Guid)Marshal.PtrToStructure(pointer,typeof(Guid));}
            finally {LocalFree(pointer);}
        }
        private static string Name(Guid id) {
            uint size=0; uint status=PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,null,ref size);
            if((status!=0 && status!=234) || size==0 || size>8192)return id.ToString();
            byte[] bytes=new byte[size];
            return PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,bytes,ref size)==0?Encoding.Unicode.GetString(bytes).TrimEnd('\0'):id.ToString();
        }
        [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr subgroup,uint flags,uint index,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr guid);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root,ref Guid guid);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr subgroup,IntPtr setting,byte[] buffer,ref uint size);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
