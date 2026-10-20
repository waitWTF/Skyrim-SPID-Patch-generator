using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WearableDistributor;

public static class InstalledAudit
{
    public static void Run(string data,string report)
    {
        var timer=Stopwatch.StartNew();
        var result=new WearableScanService().Scan(data,new AppPreferences{DataPath=data},data,new Stages(),CancellationToken.None);
        var scanMs=timer.Elapsed.TotalMilliseconds;Console.WriteLine("Installed read-only scan complete: "+result.LoadOrder.Count+" plugins, "+result.Npcs.Count+" NPC profiles.");
        timer.Restart();var audiences=new AudiencePreparation(result.Targets,result.Npcs);var prepareMs=timer.Elapsed.TotalMilliseconds;
        Console.WriteLine("Installed audience preparation complete: "+prepareMs.ToString("F0")+" ms.");
        var before=audiences.Presets.GetPresets("Female");timer.Restart();for(int i=0;i<20;i++)if(!ReferenceEquals(before,audiences.Presets.GetPresets("Female")))throw new InvalidOperationException("Preset cache returned rebuilt lists.");var cachedMs=timer.Elapsed.TotalMilliseconds;
        var patches=DetectedPatchReader.Read(result.PatchFiles);
        var summary=new{Mode="Read-only installed-mod audit",Plugins=result.LoadOrder.Count,SourceCandidates=result.Plugins.Count,EquipmentOnly=result.Plugins.Count(p=>!p.IsFiltered),MixedOrHidden=result.Plugins.Count(p=>p.IsFiltered),EligibleNpcs=result.Npcs.Count(n=>n.CanReceiveWearables),DetectedPatchFiles=patches.Select(p=>p.PatchPlugin).Distinct(StringComparer.OrdinalIgnoreCase).Count(),PatchedSources=patches.Where(p=>p.PresentInPlugin).Select(p=>p.Entry.SourcePlugin).Distinct(StringComparer.OrdinalIgnoreCase).Count(),ScanMilliseconds=scanMs,AudiencePreparationMilliseconds=prepareMs,CachedTwentyPresetLookupsMilliseconds=cachedMs,AnyPresets=audiences.Presets.GetPresets("Any").Count,FemalePresets=before.Count,MalePresets=audiences.Presets.GetPresets("Male").Count,GameFilesWritten=false};
        File.WriteAllText(report,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(summary));
    }
    private sealed class Stages:IProgress<ScanProgress>{private string previous="";public void Report(ScanProgress value){if(previous==value.Stage)return;previous=value.Stage;Console.WriteLine("Audit stage: "+value.Stage);}}
}
