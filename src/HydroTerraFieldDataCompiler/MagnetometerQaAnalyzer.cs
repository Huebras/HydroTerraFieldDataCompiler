using System.Globalization;
using System.IO.Compression;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class MagnetometerQaAnalyzer
{
    private sealed record Sample(string Line, string File, int DeviceId, double Time, double? Value);

    public static List<MagnetometerLineQaResult> Analyze(FieldDataProject project)
    {
        var magIds = project.Devices.Where(IsMagnetometer).Select(d => d.DeviceId).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var names = project.Devices.Where(IsMagnetometer).Where(d => d.DeviceId.HasValue).ToDictionary(d => d.DeviceId!.Value, d => d.DeviceName);
        var samples = new List<Sample>();
        foreach (string source in project.ImportedRawFiles) ReadSource(source, magIds, samples);
        var results = new List<MagnetometerLineQaResult>();
        foreach (var group in samples.GroupBy(x => string.IsNullOrWhiteSpace(x.Line) ? Path.GetFileNameWithoutExtension(x.File) : x.Line, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(x => x.Time).ToList();
            var valid = ordered.Where(x => x.Value.HasValue && double.IsFinite(x.Value.Value)).ToList();
            var intervals = ordered.Zip(ordered.Skip(1), (a,b) => b.Time-a.Time).Where(x => x > 0).ToList();
            double avg = intervals.Count == 0 ? 0 : intervals.Average();
            int gaps = intervals.Count(x => avg > 0 && x > Math.Max(1.0, avg*3));
            int missing = intervals.Sum(x => avg > 0 && x > avg*3 ? Math.Max(0,(int)Math.Round(x/avg)-1) : 0);
            int frozen=0, run=1;
            for(int i=1;i<valid.Count;i++) { if(Math.Abs(valid[i].Value!.Value-valid[i-1].Value!.Value)<1e-9) run++; else { if(run>=10) frozen++; run=1; } }
            if(run>=10) frozen++;
            int dev=ordered.Select(x=>x.DeviceId).FirstOrDefault();
            var r=new MagnetometerLineQaResult { LineName=group.Key, SourceFiles=ordered.Select(x=>x.File).Distinct().ToList(), DeviceId=dev, DeviceName=names.GetValueOrDefault(dev,"Magnetometer"), RecordCount=ordered.Count, InvalidValueCount=ordered.Count-valid.Count, FrozenRunCount=frozen, DataGapCount=gaps, EstimatedMissingRecordCount=missing, AverageIntervalSeconds=avg, MaximumIntervalSeconds=intervals.DefaultIfEmpty().Max(), MinimumValue=valid.Count>0?valid.Min(x=>x.Value):null, MaximumValue=valid.Count>0?valid.Max(x=>x.Value):null };
            r.Summary = $"{r.RecordCount:N0} records; {r.DataGapCount} gap(s); {r.FrozenRunCount} frozen run(s); {r.InvalidValueCount} invalid value(s).";
            results.Add(r);
        }
        return results.OrderBy(x=>x.LineName,StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsMagnetometer(DeviceConfiguration d) => d.DeviceType.Contains("Magnet",StringComparison.OrdinalIgnoreCase) || d.DeviceName.Contains("Magnet",StringComparison.OrdinalIgnoreCase) || d.DriverPath.Contains("Magnet",StringComparison.OrdinalIgnoreCase);
    private static void ReadSource(string source, HashSet<int> magIds, List<Sample> samples)
    {
        if(source.Contains("::")) { var p=source.Split(new[]{"::"},2,StringSplitOptions.None); using var z=ZipFile.OpenRead(p[0]); var e=z.GetEntry(p[1]); if(e!=null) using(var sr=new StreamReader(e.Open())) Read(sr,p[1],magIds,samples); return; }
        if(source.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) { using var z=ZipFile.OpenRead(source); foreach(var e in z.Entries.Where(e=>e.FullName.EndsWith(".raw",StringComparison.OrdinalIgnoreCase))) using(var sr=new StreamReader(e.Open())) Read(sr,e.FullName,magIds,samples); return; }
        if(File.Exists(source) && source.EndsWith(".raw",StringComparison.OrdinalIgnoreCase)) using(var sr=File.OpenText(source)) Read(sr,source,magIds,samples);
    }
    private static void Read(TextReader reader,string file,HashSet<int> magIds,List<Sample> samples)
    {
        string lineName=""; string? line;
        while((line=reader.ReadLine())!=null) {
            var f=line.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries); if(f.Length==0) continue;
            if(f[0].Equals("LNN",StringComparison.OrdinalIgnoreCase) && f.Length>1) { lineName=string.Join(" ",f.Skip(1)); continue; }
            if(f.Length<4 || !int.TryParse(f[1],out int id) || !magIds.Contains(id) || !double.TryParse(f[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double t)) continue;
            double? value=null; for(int i=3;i<f.Length;i++) if(double.TryParse(f[i],NumberStyles.Float,CultureInfo.InvariantCulture,out double v)){ value=v; break; }
            samples.Add(new Sample(lineName,file,id,t,value));
        }
    }
}
