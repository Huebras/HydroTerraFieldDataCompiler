using System.Security.Cryptography;
using System.Text;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class ProjectHealthEvaluator
{
    public static ProjectHealthSummary Evaluate(FieldDataProject project)
    {
        var health = new ProjectHealthSummary { EvaluatedUtc = DateTime.UtcNow };
        Add(health, "Project", "Project identification", !string.IsNullOrWhiteSpace(project.ProjectName), true,
            string.IsNullOrWhiteSpace(project.ProjectName) ? "Project name has not been entered." : project.ProjectName);
        Add(health, "Files", "HYPACK RAW data", project.RawFileSummaries.Count > 0, true,
            project.RawFileSummaries.Count == 0 ? "No RAW files have been scanned." : $"{project.RawFileSummaries.Count} RAW files scanned.");

        bool anySingle = SurveyRequirements.HasSingleBeam(project);
        bool anyMag = SurveyRequirements.HasMagnetometer(project);

        Add(health, "Configuration", "Header consistency", project.RawFileSummaries.Count > 0 && project.RawFileSummaries.Skip(1).All(f => f.IniDifferenceCount == 0), true,
            project.RawFileSummaries.Count == 0 ? "Not evaluated." : project.RawFileSummaries.Skip(1).Any(f => f.IniDifferenceCount > 0)
                ? $"{project.RawFileSummaries.Count(f => f.IniDifferenceCount > 0)} file(s) differ from the first loaded RAW header."
                : "All later RAW headers match the first loaded file.");

        bool geodesyComplete = !string.IsNullOrWhiteSpace(project.Geodesy.RecordedHorizontalDatum)
            && !string.IsNullOrWhiteSpace(project.Geodesy.RecordedProjection)
            && !string.IsNullOrWhiteSpace(project.Geodesy.RecordedZone)
            && !string.IsNullOrWhiteSpace(project.Geodesy.RecordedUnits);
        HealthStatus geodesyStatus = project.Geodesy.ValidationStatus.Equals("Failure", StringComparison.OrdinalIgnoreCase) ? HealthStatus.Failure
            : project.Geodesy.ValidationStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? HealthStatus.Warning
            : geodesyComplete ? HealthStatus.Pass : HealthStatus.Warning;
        Add(health, "Geodesy", "Coordinate system identified and validated", geodesyComplete && geodesyStatus == HealthStatus.Pass, true,
            $"{project.Geodesy.RecordedGrid}; {project.Geodesy.RecordedProjection}; {project.Geodesy.RecordedZone}; {project.Geodesy.RecordedUnits}. {project.Geodesy.CoordinateRangeSummary}".Trim(), geodesyStatus);
        Add(health, "Positioning", "Positioning method identified", project.DetectedPositioningMethod != PositioningMethod.Unknown || project.PositioningMethods.Count > 0, true,
            project.DetectedPositioningMethod == PositioningMethod.Unknown ? "Positioning method requires confirmation." : project.DetectedPositioningMethod.ToString());
        int nonFixed = project.RawFileSummaries.Sum(f => Count(f, GnssSolutionType.Float) + Count(f, GnssSolutionType.Autonomous) + Count(f, GnssSolutionType.Invalid) + Count(f, GnssSolutionType.NoSolution));
        int fixedCount = project.RawFileSummaries.Sum(f => Count(f, GnssSolutionType.Fixed));
        Add(health, "Positioning", "Production GNSS quality", fixedCount > 0 && nonFixed == 0, true,
            fixedCount == 0 ? "No fixed-quality observations were decoded." : nonFixed == 0 ? $"{fixedCount:N0} fixed observations; no degraded observations." : $"{nonFixed:N0} non-fixed or invalid observations require review.",
            fixedCount == 0 || nonFixed > 0 ? HealthStatus.Warning : HealthStatus.Pass);
        Add(health, "Devices", "Device inventory and offsets", project.Devices.Count > 0 && project.Devices.All(d => d.OffsetConfidence != DetectionConfidence.NotDetected), true,
            project.Devices.Count == 0 ? "No devices detected." : $"{project.Devices.Count} device(s) detected; {project.Devices.Count(d => d.OffsetConfidence == DetectionConfidence.NotDetected)} without decoded offsets.");

        if (project.LineCoverageResults.Count > 0)
        {
            int lineQualityWarnings = project.LineCoverageResults.Count(r => r.QualityObservationCount > 0 && r.FixedQualityPercent < project.MinimumFixedPercent);
            Add(health, "Survey Lines", "Line-level positioning quality", lineQualityWarnings == 0, true,
                lineQualityWarnings == 0 ? $"All {project.LineCoverageResults.Count} analyzed lines meet the {project.MinimumFixedPercent:0.0}% fixed-quality threshold." : $"{lineQualityWarnings} of {project.LineCoverageResults.Count} analyzed lines require positioning review.", HealthStatus.Warning);
            int navWarnings = project.LineCoverageResults.Count(r => r.NavigationIntegrityHasWarning);
            Add(health, "Data Integrity", "Navigation integrity", navWarnings == 0, true,
                navWarnings == 0 ? $"All {project.LineCoverageResults.Count} analyzed lines passed navigation-integrity checks." : $"{navWarnings} of {project.LineCoverageResults.Count} analyzed lines require navigation review.", navWarnings == 0 ? HealthStatus.Pass : HealthStatus.Warning);
        }

        if (anySingle)
        {
            Add(health, "Single Beam", "RAW/BIN pairs", project.RawFileSummaries.Where(f => f.EchosounderRecordCount > 0).All(f => f.HasMatchingBin), true,
                $"{project.RawFileSummaries.Count(f => f.EchosounderRecordCount > 0 && f.HasMatchingBin)} matched; {project.RawFileSummaries.Count(f => f.EchosounderRecordCount > 0 && !f.HasMatchingBin)} missing.");
            Add(health, "Single Beam", "Sounding records", project.RawFileSummaries.Sum(f => f.EchosounderRecordCount) > 0, true,
                $"{project.RawFileSummaries.Sum(f => f.EchosounderRecordCount):N0} EC2 records.");
            if (project.LineCoverageResults.Count > 0)
            {
                int depthWarnings = project.LineCoverageResults.Count(r => r.DepthQaHasWarning);
                Add(health, "Single Beam", "Line-level depth quality", depthWarnings == 0, true,
                    depthWarnings == 0 ? $"No depth-channel warnings on {project.LineCoverageResults.Count} analyzed lines." : $"{depthWarnings} line(s) contain invalid, spiking, or frozen depth observations.", HealthStatus.Warning);
            }
            bool hasDso = SurveyRequirements.HasBarCheckFile(project);
            bool hasBarException = !string.IsNullOrWhiteSpace(project.BarCheckExceptionReason);
            Add(health, "Single Beam", "ECHOTRAC bar-check DSO", hasDso, true,
                hasDso ? "Bar-check DSO documentation attached." : hasBarException ? $"Documented exception: {project.BarCheckExceptionReason.Trim()}" : "Attach the E20 bar-check .DSO file or document an exception.", HealthStatus.Warning);
            bool hasSvp = SurveyRequirements.HasSoundVelocityFile(project);
            bool hasSvpException = !string.IsNullOrWhiteSpace(project.SvpExceptionReason);
            Add(health, "Single Beam", "Sound velocity documentation", hasSvp, true,
                hasSvp ? "Sound-velocity documentation attached." : hasSvpException ? $"Documented exception: {project.SvpExceptionReason.Trim()}" : "No .VEL/.SVP file or documented exception is available.", HealthStatus.Warning);
            bool tide = project.RawFileSummaries.Sum(f => f.TideRecordCount) > 0 || project.SupportingFiles.Any(f => f.Category.Contains("Tide", StringComparison.OrdinalIgnoreCase));
            Add(health, "Single Beam", "Vertical correction / tide", tide, true,
                tide ? $"{project.RawFileSummaries.Sum(f => f.TideRecordCount):N0} TID records or external tide documentation available." : "No TID records or external tide documentation found.", HealthStatus.Warning);
        }
        if (anyMag)
        {
            Add(health, "Magnetometer", "Towfish/device configuration", project.Devices.Any(d => d.DeviceType.Contains("Tow", StringComparison.OrdinalIgnoreCase) || d.DeviceName.Contains("Tow", StringComparison.OrdinalIgnoreCase)), true,
                "Confirm towfish, magnetometer, and layback configuration.", HealthStatus.Warning);
        }

        int failures = project.Findings.Count(f => f.Severity.Equals("Failure", StringComparison.OrdinalIgnoreCase));
        int warnings = project.Findings.Count(f => f.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        Add(health, "QA", "Blocking findings", failures == 0, true, failures == 0 ? "No blocking failures." : $"{failures} blocking failure(s).", failures == 0 ? HealthStatus.Pass : HealthStatus.Failure);
        Add(health, "QA", "Warnings reviewed", warnings == 0, false, warnings == 0 ? "No warnings." : $"{warnings} warning(s) remain for review.", warnings == 0 ? HealthStatus.Pass : HealthStatus.Warning);

        BuildFingerprint(project, health);
        int required = health.Items.Count(i => i.IsRequired);
        int earned = health.Items.Where(i => i.IsRequired).Sum(i => i.Status == HealthStatus.Pass ? 100 : i.Status == HealthStatus.Warning ? 50 : 0);
        health.Score = required == 0 ? 0 : (int)Math.Round((double)earned / required);
        health.OverallStatus = health.Items.Any(i => i.IsRequired && i.Status == HealthStatus.Failure) ? HealthStatus.Failure
            : health.Items.Any(i => i.IsRequired && i.Status == HealthStatus.Warning) ? HealthStatus.Warning
            : required > 0 && health.Items.Where(i => i.IsRequired).All(i => i.Status == HealthStatus.Pass) ? HealthStatus.Pass
            : HealthStatus.NotEvaluated;
        project.ProjectHealth = health;
        return health;
    }

    private static int Count(RawFileSummary file, GnssSolutionType type) => file.GnssSolutionCounts.TryGetValue(type, out int count) ? count : 0;

    private static void Add(ProjectHealthSummary health, string category, string requirement, bool passed, bool required, string details, HealthStatus failedStatus = HealthStatus.Failure)
        => health.Items.Add(new ProjectHealthItem { Category = category, Requirement = requirement, IsRequired = required, Details = details, Status = passed ? HealthStatus.Pass : failedStatus });

    private static void BuildFingerprint(FieldDataProject project, ProjectHealthSummary health)
    {
        if (project.RawFileSummaries.Count == 0) return;
        var fingerprints = project.RawFileSummaries.Select(f => (File: f, Hash: Fingerprint(f))).ToList();
        health.BaselineFile = fingerprints[0].File.DisplayName;
        health.BaselineConfigurationFingerprint = fingerprints[0].Hash;
        health.MatchingConfigurationFiles = fingerprints.Count(x => x.Hash == fingerprints[0].Hash);
        health.DifferentConfigurationFiles = fingerprints.Count - health.MatchingConfigurationFiles;
        health.Items.Add(new ProjectHealthItem
        {
            Category = "Configuration",
            Requirement = "Configuration fingerprint",
            IsRequired = true,
            Status = health.DifferentConfigurationFiles == 0 ? HealthStatus.Pass : HealthStatus.Warning,
            Details = health.DifferentConfigurationFiles == 0 ? $"All {fingerprints.Count} files match baseline fingerprint {Short(health.BaselineConfigurationFingerprint)}."
                : $"{health.DifferentConfigurationFiles} file(s) differ from baseline {health.BaselineFile} ({Short(health.BaselineConfigurationFingerprint)})."
        });
    }

    private static string Fingerprint(RawFileSummary file)
    {
        var lines = new List<string>();
        lines.AddRange(file.IniSettings.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"INI|{x.Key.Trim()}={Normalize(x.Value)}"));
        lines.AddRange(file.DetectedDevices.OrderBy(d => d.DeviceId).Select(d => $"DEV|{d.DeviceId}|{Normalize(d.DeviceName)}|{d.InterfaceType}|{d.RecordedStarboard:0.######}|{d.RecordedForward:0.######}|{d.RecordedVertical:0.######}|{d.RecordedYaw:0.######}|{d.RecordedRoll:0.######}|{d.RecordedPitch:0.######}|{d.RecordedLatency:0.######}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    private static string Normalize(string value) => string.Join(" ", value.Trim().Trim('"').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Short(string hash) => hash.Length <= 16 ? hash : hash[..16];

    private static bool IsSingleBeamType(SurveyDataType type)
    {
        return type == SurveyDataType.SingleBeamFrequencyUnknown ||
               type == SurveyDataType.SingleBeamHighFrequency ||
               type == SurveyDataType.SingleBeamLowFrequency ||
               type == SurveyDataType.SingleBeamDualFrequency;
    }

}
