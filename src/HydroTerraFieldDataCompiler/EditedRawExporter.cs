using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public sealed class EditedRawExportResult
{
    public string OutputDirectory { get; set; } = string.Empty;
    public int ExportedRawCount { get; set; }
    public int ModifiedOffsetRecordCount { get; set; }
    public int RecalculatedTideRecordCount { get; set; }
    public int TideValidationMatchedCount { get; set; }
    public int TideValidationComparedCount { get; set; }
    public int FilesWithTideRecalculation { get; set; }
}

public static class EditedRawExporter
{
    private static readonly Regex OffRecord = new(@"^(?<prefix>\s*OFF\s+)(?<id>-?\d+)(?<rest>\s+.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PosRecord = new(@"^\s*POS\s+(?<id>-?\d+)\s+(?<time>-?\d+(?:\.\d+)?)\s+(?<x>-?\d+(?:\.\d+)?)\s+(?<y>-?\d+(?:\.\d+)?)\s+(?<z>-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TidRecord = new(@"^(?<prefix>\s*TID\s+)(?<id>-?\d+)\s+(?<time>-?\d+(?:\.\d+)?)\s+(?<value>-?\d+(?:\.\d+)?)(?<suffix>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const double OffsetChangeTolerance = 0.0000001;
    private const double TideFormulaTolerance = 0.015;
    private const double RequiredValidationRatio = 0.95;

    public static EditedRawExportResult Export(FieldDataProject project, string parentOutputDirectory)
    {
        if (project.ImportedRawFiles.Count == 0)
            throw new InvalidOperationException("No RAW or ZIP source files have been added to the project.");

        string output = Path.Combine(parentOutputDirectory, "Edited_RAW");
        Directory.CreateDirectory(output);
        var result = new EditedRawExportResult { OutputDirectory = output };
        var manifest = new List<string>
        {
            "Source,Output,Modified OFF records,Recalculated TID records,TID validation matches,TID validation comparisons,TID validation status"
        };

        foreach (string source in project.ImportedRawFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (source.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
            {
                string destination = UniquePath(output, Path.GetFileName(source));
                RewriteResult rewrite = RewriteRaw(File.ReadAllText(source), destination, project.Devices, source);
                Accumulate(result, rewrite);
                manifest.Add(Csv(source, destination, rewrite));
            }
            else if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(source);
                foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)))
                {
                    string safeName = Path.GetFileName(entry.FullName);
                    string destination = UniquePath(output, safeName);
                    using var reader = new StreamReader(entry.Open(), Encoding.Default, true);
                    string sourceName = source + "::" + entry.FullName;
                    RewriteResult rewrite = RewriteRaw(reader.ReadToEnd(), destination, project.Devices, sourceName);
                    Accumulate(result, rewrite);
                    manifest.Add(Csv(sourceName, destination, rewrite));
                }
            }
        }

        File.WriteAllLines(Path.Combine(output, "Edited_RAW_Manifest.csv"), manifest, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(output, "README_EDITED_RAW.txt"),
            "These files are edited copies created by HydroTerra Field Data Compiler.\r\n" +
            "The original source RAW and ZIP files were not modified.\r\n" +
            "Approved OFF header records were rewritten by device ID.\r\n" +
            "When a positioning-device vertical offset changed and the original RTK-tide relationship was verified, matching TID records were recalculated.\r\n" +
            "The verified relationship is: TID = -(POS vertical value + recorded positioning-device vertical offset).\r\n" +
            "The applied adjustment is: new TID = old TID - (approved vertical offset - recorded vertical offset).\r\n" +
            "Automatic TID recalculation is blocked when the source relationship cannot be confirmed at 95% or better within 0.015 source units.\r\n" +
            "Review the manifest and compare each edited file with its original before processing or delivery.\r\n");
        return result;
    }

    private static void Accumulate(EditedRawExportResult result, RewriteResult rewrite)
    {
        result.ExportedRawCount++;
        result.ModifiedOffsetRecordCount += rewrite.ModifiedOffRecords;
        result.RecalculatedTideRecordCount += rewrite.RecalculatedTidRecords;
        result.TideValidationMatchedCount += rewrite.TideValidationMatches;
        result.TideValidationComparedCount += rewrite.TideValidationComparisons;
        if (rewrite.RecalculatedTidRecords > 0) result.FilesWithTideRecalculation++;
    }

    private static RewriteResult RewriteRaw(string content, string destination, IReadOnlyCollection<DeviceConfiguration> devices, string sourceName)
    {
        var byId = devices.Where(d => d.DeviceId.HasValue).ToDictionary(d => d.DeviceId!.Value);
        var verticalChanges = byId.Values
            .Where(d => Math.Abs(d.ApprovedVertical - d.RecordedVertical) > OffsetChangeTolerance)
            .ToDictionary(d => d.DeviceId!.Value, d => d.ApprovedVertical - d.RecordedVertical);

        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool endsWithNewline = content.EndsWith("\n", StringComparison.Ordinal);
        string[] lines = content.Replace("\r\n", "\n").Split('\n');

        var positions = BuildPositionIndex(lines);
        var validation = ValidateTideRelationships(lines, positions, byId, verticalChanges);
        foreach (var item in validation.Values)
        {
            if (item.Comparisons > 0 && item.Ratio < RequiredValidationRatio)
            {
                string deviceName = byId.TryGetValue(item.DeviceId, out var device) ? device.DeviceName : $"Device {item.DeviceId}";
                throw new InvalidOperationException(
                    $"Edited RAW export was stopped for '{sourceName}'.\n\n" +
                    $"The vertical offset for {deviceName} changed, but the original TID relationship could not be verified. " +
                    $"Only {item.Matches} of {item.Comparisons} matched the expected RTK-tide formula within {TideFormulaTolerance:0.###} source units.\n\n" +
                    "No source file was modified. Confirm the TID source and vertical-reference workflow before exporting corrected copies.");
            }
            if (item.Comparisons == 0 && item.TidRecordCount > 0)
            {
                string deviceName = byId.TryGetValue(item.DeviceId, out var device) ? device.DeviceName : $"Device {item.DeviceId}";
                throw new InvalidOperationException(
                    $"Edited RAW export was stopped for '{sourceName}'.\n\n" +
                    $"The vertical offset for {deviceName} changed and TID records are present, but no matching POS vertical observations were found to verify the RTK-tide relationship.\n\n" +
                    "No source file was modified. Confirm the TID source before exporting corrected copies.");
            }
        }

        var rewrite = new RewriteResult();
        rewrite.TideValidationMatches = validation.Values.Sum(v => v.Matches);
        rewrite.TideValidationComparisons = validation.Values.Sum(v => v.Comparisons);
        rewrite.TideValidationStatus = validation.Count == 0 ? "Not applicable" : "Verified";

        for (int i = 0; i < lines.Length; i++)
        {
            Match offMatch = OffRecord.Match(lines[i]);
            if (offMatch.Success && int.TryParse(offMatch.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offId) && byId.TryGetValue(offId, out var device))
            {
                string[] tokens = Regex.Split(lines[i].Trim(), @"\s+");
                if (tokens.Length >= 5)
                {
                    var values = new[]
                    {
                        device.ApprovedStarboard, device.ApprovedForward, device.ApprovedVertical,
                        device.ApprovedYaw, device.ApprovedRoll, device.ApprovedPitch, device.ApprovedLatency
                    };
                    var rebuilt = new List<string> { "OFF", offId.ToString(CultureInfo.InvariantCulture) };
                    for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                        rebuilt.Add(values[valueIndex].ToString("0.000", CultureInfo.InvariantCulture));
                    if (tokens.Length > 9) rebuilt.AddRange(tokens.Skip(9));
                    string replacement = string.Join(" ", rebuilt);
                    if (!string.Equals(lines[i].Trim(), replacement, StringComparison.Ordinal))
                    {
                        lines[i] = replacement;
                        rewrite.ModifiedOffRecords++;
                    }
                }
                continue;
            }

            Match tidMatch = TidRecord.Match(lines[i]);
            if (!tidMatch.Success ||
                !int.TryParse(tidMatch.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tidId) ||
                !verticalChanges.TryGetValue(tidId, out double delta) ||
                !validation.TryGetValue(tidId, out var tideValidation) ||
                tideValidation.Comparisons == 0 || tideValidation.Ratio < RequiredValidationRatio ||
                !double.TryParse(tidMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double originalTid))
                continue;

            double revisedTid = originalTid - delta;
            int decimals = DecimalPlaces(tidMatch.Groups["value"].Value);
            string revisedText = revisedTid.ToString("F" + decimals, CultureInfo.InvariantCulture);
            lines[i] = tidMatch.Groups["prefix"].Value + tidId.ToString(CultureInfo.InvariantCulture) + " " + tidMatch.Groups["time"].Value + " " + revisedText + tidMatch.Groups["suffix"].Value;
            rewrite.RecalculatedTidRecords++;
        }

        string output = string.Join(newline, lines);
        if (!endsWithNewline) output = output.TrimEnd('\r', '\n');
        File.WriteAllText(destination, output, new UTF8Encoding(false));
        return rewrite;
    }

    private static Dictionary<PositionKey, double> BuildPositionIndex(IEnumerable<string> lines)
    {
        var positions = new Dictionary<PositionKey, double>();
        foreach (string line in lines)
        {
            Match match = PosRecord.Match(line);
            if (!match.Success ||
                !int.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                !double.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double time) ||
                !double.TryParse(match.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                continue;
            positions[new PositionKey(id, NormalizeTime(time))] = z;
        }
        return positions;
    }

    private static Dictionary<int, TideValidation> ValidateTideRelationships(
        IEnumerable<string> lines,
        IReadOnlyDictionary<PositionKey, double> positions,
        IReadOnlyDictionary<int, DeviceConfiguration> devices,
        IReadOnlyDictionary<int, double> verticalChanges)
    {
        var validations = verticalChanges.Keys.ToDictionary(id => id, id => new TideValidation { DeviceId = id });
        foreach (string line in lines)
        {
            Match match = TidRecord.Match(line);
            if (!match.Success ||
                !int.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                !validations.TryGetValue(id, out var validation) ||
                !double.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double time) ||
                !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tid))
                continue;

            validation.TidRecordCount++;
            if (!positions.TryGetValue(new PositionKey(id, NormalizeTime(time)), out double z) || !devices.TryGetValue(id, out var device))
                continue;

            validation.Comparisons++;
            double expected = -(z + device.RecordedVertical);
            if (Math.Abs(tid - expected) <= TideFormulaTolerance) validation.Matches++;
        }
        return validations;
    }

    private static long NormalizeTime(double seconds) => (long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);

    private static int DecimalPlaces(string value)
    {
        int dot = value.IndexOf('.');
        return dot < 0 ? 0 : Math.Max(0, value.Length - dot - 1);
    }

    private static string UniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int index = 2;
        while (File.Exists(path)) path = Path.Combine(directory, $"{stem}_{index++}{extension}");
        return path;
    }

    private static string Csv(string source, string output, RewriteResult rewrite) =>
        $"\"{source.Replace("\"", "\"\"")}\",\"{output.Replace("\"", "\"\"")}\",{rewrite.ModifiedOffRecords},{rewrite.RecalculatedTidRecords},{rewrite.TideValidationMatches},{rewrite.TideValidationComparisons},\"{rewrite.TideValidationStatus}\"";

    private readonly record struct PositionKey(int DeviceId, long Milliseconds);

    private sealed class TideValidation
    {
        public int DeviceId { get; set; }
        public int TidRecordCount { get; set; }
        public int Comparisons { get; set; }
        public int Matches { get; set; }
        public double Ratio => Comparisons == 0 ? 0.0 : (double)Matches / Comparisons;
    }

    private sealed class RewriteResult
    {
        public int ModifiedOffRecords { get; set; }
        public int RecalculatedTidRecords { get; set; }
        public int TideValidationMatches { get; set; }
        public int TideValidationComparisons { get; set; }
        public string TideValidationStatus { get; set; } = "Not applicable";
    }
}
