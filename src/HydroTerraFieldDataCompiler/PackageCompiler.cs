using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public sealed class PackageCompileResult
{
    public string ZipPath { get; set; } = string.Empty;
    public string WorkDirectory { get; set; } = string.Empty;
    public string WordReportPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public string ZipSha256 { get; set; } = string.Empty;
}

public static class PackageCompiler
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static PackageCompileResult Compile(FieldDataProject project, string outputRoot)
    {
        List<PackageReviewItem> review = PackageReviewBuilder.Build(project);
        foreach (PackageReviewItem item in review)
            item.Include = item.IsRequired || !project.ExcludedPackageItemKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase);

        var missingRequired = review.Where(i => i.IsRequired && (i.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated")).ToList();
        if (missingRequired.Count > 0)
            throw new InvalidOperationException(
                "The package is not ready. Resolve these required items first:\n\n" +
                string.Join("\n", missingRequired.Select(i => $"- {i.Category}: {i.Details}")));

        string safeName = SafeName(string.IsNullOrWhiteSpace(project.ProjectName) ? "HydroTerra_Project" : project.ProjectName);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string work = Path.Combine(outputRoot, safeName + "_Submittal_" + stamp);
        string originals = Path.Combine(work, "01_Original_Data");
        string edited = Path.Combine(work, "02_Edited_RAW");
        string supporting = Path.Combine(work, "03_Supporting_Files");
        string qa = Path.Combine(work, "04_QA_Exports");
        string reports = Path.Combine(work, "05_Reports");
        Directory.CreateDirectory(originals);
        Directory.CreateDirectory(supporting);
        Directory.CreateDirectory(qa);
        Directory.CreateDirectory(reports);

        var manifest = new List<(string Rel, string Source, long Size, string Hash, string Category)>();
        var usedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void CopyUnique(string source, string folder, string category)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Package file was not found.", source);
            string name = Path.GetFileName(source);
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string dest = Path.Combine(folder, name);
            int n = 2;
            while (!usedDestinations.Add(dest)) dest = Path.Combine(folder, $"{stem}_{n++}{ext}");
            File.Copy(source, dest, true);
            AddManifest(dest, source, category);
        }

        void AddManifest(string file, string source, string category)
        {
            manifest.Add((Path.GetRelativePath(work, file), source, new FileInfo(file).Length, ComputeSha256(file), category));
        }

        foreach (PackageReviewItem item in review.Where(i => i.Include && i.Key.StartsWith("original|", StringComparison.OrdinalIgnoreCase)))
        {
            string destination = item.Category.Equals("HYPACK LOG", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(originals, "HYPACK_LOG")
                : originals;
            Directory.CreateDirectory(destination);
            CopyUnique(item.SourcePath, destination, item.Category);
        }

        foreach (PackageReviewItem item in review.Where(i => i.Include && i.Key.StartsWith("support|", StringComparison.OrdinalIgnoreCase)))
            CopyUnique(item.SourcePath, supporting, item.Category);

        if (review.Any(i => i.Key == "generated|edited-raw" && i.Include))
        {
            var result = EditedRawExporter.Export(project, edited);
            foreach (string file in Directory.EnumerateFiles(result.OutputDirectory, "*", SearchOption.AllDirectories))
                AddManifest(file, "Generated from approved offsets", "Edited RAW export");
        }

        string lineQa = Path.Combine(qa, "Survey_Line_QA.csv");
        string healthCsv = Path.Combine(qa, "Project_Health.csv");
        WriteLineSummary(project, lineQa);
        WriteHealth(project, healthCsv);
        AddManifest(lineQa, "Generated", "QA export");
        AddManifest(healthCsv, "Generated", "QA export");

        string reportPath = Path.Combine(reports, "Field_Data_Report.docx");
        WordReportGenerator.Generate(project, reportPath);
        AddManifest(reportPath, "Generated", "Word field-data report");

        string manifestPath = Path.Combine(work, "Package_Manifest.csv");
        using (var writer = new StreamWriter(manifestPath, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("RelativePath,Category,SizeBytes,SHA256,OriginalSource");
            foreach (var m in manifest.OrderBy(m => m.Rel))
                writer.WriteLine($"{Csv(m.Rel)},{Csv(m.Category)},{m.Size},{m.Hash},{Csv(m.Source)}");
        }

        string readmePath = Path.Combine(work, "README.txt");
        File.WriteAllText(readmePath,
            $"HydroTerra Field Data Compiler submittal package\r\n" +
            $"Project: {project.ProjectName}\r\n" +
            $"Created: {DateTime.Now:O}\r\n" +
            $"Reviewed by: {project.ReviewedBy}\r\n" +
            $"Package approved: {(project.PackageApproved ? "Yes" : "No")}\r\n\r\n" +
            "Original files are preserved in 01_Original_Data. Edited RAW files, when present, are separate copies.\r\n" +
            "The Word field-data report is located in 05_Reports.\r\n" +
            (!string.IsNullOrWhiteSpace(project.BarCheckExceptionReason) ? $"Bar-check exception: {project.BarCheckExceptionReason}\r\n" : string.Empty) +
            (!string.IsNullOrWhiteSpace(project.SvpExceptionReason) ? $"SVP exception: {project.SvpExceptionReason}\r\n" : string.Empty),
            Encoding.UTF8);

        string zip = Path.Combine(outputRoot, safeName + "_Submittal_" + stamp + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, false);
        return new PackageCompileResult
        {
            ZipPath = zip,
            WorkDirectory = work,
            WordReportPath = reportPath,
            FileCount = manifest.Count + 2,
            ZipSha256 = ComputeSha256(zip)
        };
    }

    public static string GenerateWordReport(FieldDataProject project, string outputPath) => WordReportGenerator.Generate(project, outputPath);

    private static void WriteLineSummary(FieldDataProject p, string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("Line,QAPositionSource,Segments,SourceFiles,Positions,OfflinePositions,MaximumOfflineFeet,Gaps,FixedPercent,NonFixed,HFValid,LFValid,NavigationIntegrityScore,AverageIntervalSeconds,MaximumIntervalSeconds,NavigationGaps,EstimatedMissingEpochs,AverageSpeedKnots,MaximumSpeedKnots,Freezes,ImpossibleJumps,DepthQA,Status");
        foreach (var r in p.LineCoverageResults)
            w.WriteLine($"{Csv(r.LineName)},{Csv(r.QaPositionSource)},{r.SegmentCount},{Csv(string.Join("; ", r.SourceFiles))},{r.PositionCount},{r.OfflinePositionCount},{r.MaximumOfflineDistance:0.###},{r.Gaps.Count},{r.FixedQualityPercent:0.###},{r.NonFixedQualityCount},{r.HighFrequencyCount},{r.LowFrequencyCount},{r.NavigationIntegrityScore},{r.AveragePositionIntervalSeconds:0.###},{r.MaximumPositionIntervalSeconds:0.###},{r.NavigationGapCount},{r.EstimatedMissingEpochCount},{r.AverageSpeedKnots:0.###},{r.MaximumSpeedKnots:0.###},{r.PositionFreezeCount},{r.ImpossibleJumpCount},{Csv(r.DepthQaSummary)},{Csv(r.Status)}");
    }

    private static void WriteHealth(FieldDataProject p, string path)
    {
        ProjectHealthSummary health = p.ProjectHealth.Items.Count > 0 ? p.ProjectHealth : ProjectHealthEvaluator.Evaluate(p);
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("Category,Item,Required,Status,Detail");
        foreach (var i in health.Items)
            w.WriteLine($"{Csv(i.Category)},{Csv(i.Requirement)},{i.IsRequired},{i.Status},{Csv(i.Details)}");
    }

    private static string SafeName(string value)
    {
        string safe = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "HydroTerra_Project" : safe;
    }

    private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}
