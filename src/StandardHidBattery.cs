using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

// Only GET_REPORT for battery usages explicitly declared by the HID descriptor.
// No vendor commands, SetFeature, WriteFile or interrupt-stream consumption.
static class StandardHidBattery {
    const int Success=0x110000;
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage,Page,Input,Output,Feature;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort Links,InButtons,InValues,InIndices,OutButtons,OutValues,OutIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [StructLayout(LayoutKind.Explicit,Size=72)] public struct ValueCaps {
        [FieldOffset(0)] public ushort Page;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(4)] public ushort BitField;
        [FieldOffset(6)] public ushort Link;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(16)] public byte HasNull;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int Min;
        [FieldOffset(44)] public int Max;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
    }
    public sealed class Field {public ValueCaps Caps;public ushort Usage;public int Type,Length;}
    public sealed class Descriptor {public string Path,Name;public readonly List<Field> Fields=new List<Field>();}
    public sealed class Sample {
        public int? Percent;public bool Conflict;public DateTime At;
        public string Note="标准 HID 电量暂不可读取";
    }
    sealed class Layout {public Descriptor Descriptor;public DateTime Next;}
    sealed class Slot {public Task<Sample> Pending;public Sample Value;public DateTime Next;}
    static readonly Dictionary<string,Layout> layouts=new Dictionary<string,Layout>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,Slot> slots=new Dictionary<string,Slot>(StringComparer.OrdinalIgnoreCase);
    static readonly object gate=new object();
    static int activeReads;
    public static readonly Dictionary<string,string> Diagnostics=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint create,uint flags,IntPtr template);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle,byte[] text,int length);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,[Out] ValueCaps[] caps,ref ushort count,IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type,ushort page,ushort link,ushort usage,out uint value,IntPtr data,byte[] report,uint length);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] report,int length);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetInputReport(SafeFileHandle handle,[In,Out] byte[] report,int length);

    public static bool IsBattery(ValueCaps caps,ushort usage) {
        bool declared=caps.IsRange==0?caps.UsageMin==usage:caps.UsageMin<=usage&&usage<=caps.UsageMax;
        return declared&&(caps.BitField&1)==0&&caps.IsAbsolute!=0&&caps.ReportCount==1&&caps.BitSize>0&&caps.BitSize<=32&&caps.Min>=0&&caps.Max>caps.Min&&caps.Max<=(1L<<caps.BitSize)-1&&
            ((caps.Page==6&&usage==0x20)||(caps.Page==0x85&&usage==0x64&&caps.Min==0&&caps.Max==100));
    }
    public static int? Normalize(uint raw,ValueCaps caps,ushort usage) {
        if(!IsBattery(caps,usage)||(long)raw<caps.Min||(long)raw>caps.Max)return null;
        return (int)Math.Round(((long)raw-caps.Min)*100.0/((long)caps.Max-caps.Min),MidpointRounding.AwayFromZero);
    }
    public static Descriptor Describe(string path) {
        lock(gate){Layout old;if(layouts.TryGetValue(path,out old)&&DateTime.UtcNow<old.Next)return old.Descriptor;}
        Descriptor descriptor=null;string diagnostic="接口属性不可读取";
        using(var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            IntPtr data;
            if(!handle.IsInvalid&&HidD_GetPreparsedData(handle,out data))try{
                Caps caps;if(HidP_GetCaps(data,out caps)==Success){
                    var found=new Descriptor {Path=path};
                    var name=new byte[512];if(HidD_GetProductString(handle,name,name.Length))found.Name=System.Text.Encoding.Unicode.GetString(name).TrimEnd('\0').Trim();
                    foreach(int type in new[]{2,0}){
                        ushort count=type==2?caps.FeatureValues:caps.InValues;int length=type==2?caps.Feature:caps.Input;
                        if(count==0||count>512||length<2||length>4096)continue;
                        var values=new ValueCaps[count];if(HidP_GetValueCaps(type,values,ref count,data)!=Success)continue;
                        foreach(var value in values)foreach(ushort usage in new ushort[]{0x20,0x64})if(IsBattery(value,usage))found.Fields.Add(new Field {Caps=value,Usage=usage,Type=type,Length=length});
                    }
                    diagnostic=String.Format("usage={0:X4}:{1:X4}, input={2}, feature={3}, batteryFields={4}",caps.Page,caps.Usage,caps.Input,caps.Feature,found.Fields.Count);
                    if(found.Fields.Count>0)descriptor=found;
                }
            }finally{HidD_FreePreparsedData(data);}
        }
        lock(gate){layouts[path]=new Layout {Descriptor=descriptor,Next=DateTime.UtcNow.AddSeconds(60)};Diagnostics[path]=diagnostic;}
        return descriptor;
    }
    static Sample ReadReports(Descriptor descriptor) {
        var result=new Sample {At=DateTime.Now};
        using(var handle=CreateFile(descriptor.Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            IntPtr data;if(handle.IsInvalid||!HidD_GetPreparsedData(handle,out data))return result;
            try{
                var reports=new Dictionary<string,byte[]>();
                foreach(var field in descriptor.Fields){
                    string key=field.Type+":"+field.Caps.ReportId;byte[] report;
                    if(!reports.TryGetValue(key,out report)){
                        if(reports.Count>=4)continue;
                        report=new byte[field.Length];report[0]=field.Caps.ReportId;
                        bool ok=field.Type==2?HidD_GetFeature(handle,report,report.Length):HidD_GetInputReport(handle,report,report.Length);
                        if(!ok||report[0]!=field.Caps.ReportId)report=null;
                        reports[key]=report;
                    }
                    if(report==null)continue;uint raw;
                    if(HidP_GetUsageValue(field.Type,field.Caps.Page,field.Caps.Link,field.Usage,out raw,data,report,(uint)report.Length)!=Success)continue;
                    var percent=Normalize(raw,field.Caps,field.Usage);if(!percent.HasValue)continue;
                    if(result.Percent.HasValue&&result.Percent!=percent){result.Conflict=true;result.Percent=null;result.Note="多个标准电量字段冲突，未选择猜测值";return result;}
                    result.Percent=percent;result.Note="设备描述符声明的标准 HID 电量";
                }
            }finally{HidD_FreePreparsedData(data);}
        }
        return result;
    }
    // A slow driver cannot freeze the inventory: at most one task per interface,
    // four in total. A pending query is reused rather than started again.
    public static Sample Read(string path,bool force) {
        var descriptor=Describe(path);if(descriptor==null)return new Sample {Note="设备未声明标准 HID 电量字段"};
        Slot slot;lock(gate){if(!slots.TryGetValue(path,out slot))slots[path]=slot=new Slot();}
        return ReadSlot(slot,()=>ReadReports(descriptor),force,200);
    }
    static Sample ReadSlot(Slot slot,Func<Sample> reader,bool force,int waitMs) {
        lock(slot){
            if(slot.Pending!=null){
                if(!slot.Pending.IsCompleted)return new Sample {Note="标准 HID 查询尚未完成"};
                slot.Value=slot.Pending.Result;slot.Pending=null;slot.Next=DateTime.UtcNow.AddSeconds(30);return slot.Value;
            }
            if(!force&&slot.Value!=null&&DateTime.UtcNow<slot.Next)return slot.Value;
            if(Interlocked.Increment(ref activeReads)>4){Interlocked.Decrement(ref activeReads);return new Sample {Note="标准 HID 查询繁忙"};}
            slot.Pending=Task.Factory.StartNew(()=>{try{return reader();}catch(Exception){return new Sample {Note="标准 HID 查询未成功"};}finally{Interlocked.Decrement(ref activeReads);}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            slot.Value=null;
            if(!slot.Pending.Wait(waitMs))return new Sample {Note="标准 HID 查询尚未完成"};
            slot.Value=slot.Pending.Result;slot.Pending=null;slot.Next=DateTime.UtcNow.AddSeconds(30);return slot.Value;
        }
    }
    public static Sample Merge(List<Sample> samples) {
        var result=new Sample();foreach(var sample in samples){
            if(sample.Conflict){result.Conflict=true;break;}
            if(!sample.Percent.HasValue)continue;
            if(result.Percent.HasValue&&result.Percent!=sample.Percent){result.Conflict=true;break;}
            result.Percent=sample.Percent;result.At=sample.At;result.Note=sample.Note;
        }
        if(result.Conflict){result.Percent=null;result.Note="多个标准电量报告冲突，未选择猜测值";}return result;
    }
    public static void Verify() {
        if(Marshal.SizeOf(typeof(ValueCaps))!=72||Marshal.SizeOf(typeof(Caps))!=64)throw new Exception("HID native structure layout invalid");
        var cap=new ValueCaps {Page=6,UsageMin=0x20,IsAbsolute=1,ReportCount=1,BitSize=8,Min=0,Max=255};
        if(Normalize(128,cap,0x20)!=50||Normalize(0,cap,0x20)!=0||Normalize(255,cap,0x20)!=100||Normalize(256,cap,0x20)!=null)throw new Exception("Standard battery scale invalid");
        cap.Page=1;if(Normalize(100,cap,0x20)!=null)throw new Exception("Non-battery field accepted");cap.Page=6;cap.ReportCount=2;if(Normalize(50,cap,0x20)!=null)throw new Exception("Usage array accepted");cap.ReportCount=1;cap.Min=5;cap.Max=5;if(Normalize(5,cap,0x20)!=null)throw new Exception("Zero range accepted");
        cap.Page=0x85;cap.UsageMin=0x64;cap.Min=0;cap.Max=100;if(Normalize(73,cap,0x64)!=73||Normalize(101,cap,0x64)!=null)throw new Exception("Relative SoC invalid");
        cap.UsageMin=0x66;if(Normalize(73,cap,0x66)!=null)throw new Exception("Absolute capacity interpreted as percentage");cap.Page=6;cap.UsageMin=0x20;cap.BitField=1;if(Normalize(73,cap,0x20)!=null)throw new Exception("Constant report accepted as battery");
        var merged=Merge(new List<Sample>{new Sample {Percent=51},new Sample {Percent=52}});if(!merged.Conflict||merged.Percent.HasValue)throw new Exception("Conflicting battery fields mixed");
        if(Merge(new List<Sample>{new Sample {Percent=0},new Sample {Percent=0}}).Percent!=0)throw new Exception("Real zero percent lost");
        var slot=new Slot();int calls=0;Func<Sample> slow=()=>{Interlocked.Increment(ref calls);Thread.Sleep(120);return new Sample {Percent=47};};
        var pending=ReadSlot(slot,slow,true,5);
        if(pending.Percent.HasValue)throw new Exception("Slow HID report blocked inventory");
        ReadSlot(slot,slow,true,5);if(!slot.Pending.Wait(1000))throw new Exception("Simulated HID read did not complete");
        if(ReadSlot(slot,slow,true,5).Percent!=47||calls!=1)throw new Exception("Pending HID queries overlapped");
        if(ReadSlot(slot,()=>{throw new IOException("test");},true,1000).Percent.HasValue)throw new Exception("Failed HID read retained stale battery");
        if(ReadSlot(slot,()=>new Sample {Percent=46},true,1000).Percent!=46)throw new Exception("HID read did not recover");
        using(var finish=new ManualResetEventSlim(false)){
            var blocked=new List<Slot>();
            try{
                for(int i=0;i<4;i++){var item=new Slot();blocked.Add(item);ReadSlot(item,()=>{finish.Wait();return new Sample {Percent=20};},true,1);}
                var fifth=new Slot();ReadSlot(fifth,()=>new Sample {Percent=20},true,1);
                if(fifth.Pending!=null||activeReads!=4)throw new Exception("Global HID query limit failed");
            }finally{finish.Set();foreach(var item in blocked)if(item.Pending!=null&&!item.Pending.Wait(2000))throw new Exception("Simulated HID query cleanup failed");}
        }
    }
}
