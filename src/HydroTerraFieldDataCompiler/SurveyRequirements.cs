using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public enum RequirementDisposition
{
    Required,
    ReviewOrDocumentException,
    Recommended,
    NotApplicable
}

public sealed record SurveyRequirementRule(
    string Id,
    string Label,
    string Category,
    RequirementDisposition Disposition,
    string Details);

public sealed class ActiveSurveyRequirements
{
    public List<SurveyDataType> SurveyTypes { get; } = new();
    public List<SurveyRequirementRule> Rules { get; } = new();

    public bool HasSingleBeam => SurveyTypes.Any(SurveyRequirements.IsSingleBeamType);
    public bool HasMagnetometer => SurveyTypes.Contains(SurveyDataType.Magnetometer);
    public bool HasSideScan => SurveyTypes.Contains(SurveyDataType.SideScan);
    public bool HasMultibeam => SurveyTypes.Contains(SurveyDataType.Multibeam);
    public bool HasTowfishWorkflow => HasMagnetometer || HasSideScan || SurveyTypes.Contains(SurveyDataType.TowfishPositioning);

    public bool Applies(string id) => Rules.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && r.Disposition != RequirementDisposition.NotApplicable);
    public RequirementDisposition DispositionOf(string id) => Rules.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Disposition ?? RequirementDisposition.NotApplicable;
}

/// <summary>
/// Central requirements engine. Confirmed Step 4 selections are the source of truth.
/// Project Health, line QA, finalization, packaging, and reporting should query this class
/// instead of independently inferring applicability.
/// </summary>
public static class SurveyRequirements
{
    public const string RawData = "files.raw";
    public const string BinPairing = "files.bin";
    public const string BarCheck = "support.barcheck";
    public const string SoundVelocity = "support.soundvelocity";
    public const string Tide = "support.tide";
    public const string LineCoverage = "qa.linecoverage";
    public const string Offline = "qa.offline";
    public const string NavigationIntegrity = "qa.navigation";
    public const string PositionQuality = "qa.positionquality";
    public const string DepthQa = "qa.depth";
    public const string MagnetometerQa = "qa.magnetometer";
    public const string TowfishPosition = "qa.towfishposition";
    public const string SideScanQa = "qa.sidescan";
    public const string MultibeamQa = "qa.multibeam";

    public static ActiveSurveyRequirements GetActive(FieldDataProject project)
    {
        var result = new ActiveSurveyRequirements();
        result.SurveyTypes.AddRange(project.DataTypes.Distinct());

        Add(result, RawData, "HYPACK RAW data", "Survey Data", RequirementDisposition.Required,
            "At least one HYPACK RAW file is required for every survey workflow.");
        Add(result, LineCoverage, "Planned-line coverage", "Survey Lines", RequirementDisposition.Required,
            "Evaluate recorded positions against planned line geometry.");
        Add(result, Offline, "Offline tolerance", "Survey Lines", RequirementDisposition.Required,
            "Evaluate cross-track distance using the confirmed QA position source.");
        Add(result, NavigationIntegrity, "Navigation integrity", "Data Integrity", RequirementDisposition.Required,
            "Check timing, gaps, freezes, duplicate positions, and impossible movement.");
        Add(result, PositionQuality, "GNSS solution quality", "Positioning", RequirementDisposition.Required,
            "Review decoded GNSS solution quality where QUA data are available.");

        if (result.HasSingleBeam)
        {
            Add(result, BinPairing, "Matching BIN files", "Single Beam", RequirementDisposition.Required,
                "BIN pairing applies only to single-beam RAW files containing echosounder records.");
            Add(result, BarCheck, "Bar check / echosounder calibration", "Single Beam", RequirementDisposition.ReviewOrDocumentException,
                "Attach the applicable DSO file or document why it is unavailable or not applicable.");
            Add(result, SoundVelocity, "Sound velocity profile", "Single Beam", RequirementDisposition.ReviewOrDocumentException,
                "Attach the applicable VEL/SVP file or document why it is unavailable or not applicable.");
            Add(result, Tide, "Vertical correction / tide", "Single Beam", RequirementDisposition.Recommended,
                "Review TID records or external vertical-correction documentation when applicable.");
            Add(result, DepthQa, "Single-beam depth QA", "Single Beam", RequirementDisposition.Required,
                "Apply channel-specific checks based on the confirmed high-, low-, or dual-frequency selection.");
        }
        else
        {
            AddNotApplicable(result, BinPairing, "Matching BIN files", "BIN files apply to single-beam acquisition only.");
            AddNotApplicable(result, BarCheck, "Bar check / echosounder calibration", "Bar checks apply to single-beam acquisition only.");
            AddNotApplicable(result, SoundVelocity, "Sound velocity profile", "Sound-velocity casts apply to single-beam or multibeam workflows, not magnetometer-only work.");
            AddNotApplicable(result, DepthQa, "Single-beam depth QA", "Depth-channel QA applies to single-beam acquisition only.");
        }

        if (result.HasMagnetometer)
        {
            Add(result, MagnetometerQa, "Magnetometer signal QA", "Magnetometer", RequirementDisposition.Required,
                "Review record continuity, synchronization, frozen values, and impossible values.");
            Add(result, TowfishPosition, "Towfish position source", "Magnetometer", RequirementDisposition.Required,
                "Confirm the position stream used for coverage and offline checks when a towfish is present.");
        }

        if (result.HasSideScan)
        {
            Add(result, SideScanQa, "Side-scan acquisition QA", "Side Scan", RequirementDisposition.Required,
                "Review towfish positioning, navigation continuity, and side-scan coverage when implemented.");
            Add(result, TowfishPosition, "Towfish position source", "Side Scan", RequirementDisposition.Required,
                "Confirm the position stream used for coverage and offline checks when a towfish is present.");
        }

        if (result.HasMultibeam)
        {
            Add(result, MultibeamQa, "Multibeam acquisition QA", "Multibeam", RequirementDisposition.Required,
                "Review motion, heading, timing, sound velocity, and beam coverage when implemented.");
            Add(result, SoundVelocity, "Sound velocity profile", "Multibeam", RequirementDisposition.ReviewOrDocumentException,
                "Attach the applicable VEL/SVP documentation or record a documented exception.");
        }

        return result;
    }

    public static bool HasSingleBeam(FieldDataProject project) => GetActive(project).HasSingleBeam;
    public static bool HasMagnetometer(FieldDataProject project) => GetActive(project).HasMagnetometer;
    public static bool RequiresBinFiles(FieldDataProject project) => GetActive(project).DispositionOf(BinPairing) == RequirementDisposition.Required;
    public static bool ReviewsBarCheck(FieldDataProject project) => GetActive(project).Applies(BarCheck);
    public static bool ReviewsSoundVelocity(FieldDataProject project) => GetActive(project).Applies(SoundVelocity);
    public static bool UsesDepthQa(FieldDataProject project) => GetActive(project).Applies(DepthQa);
    public static bool UsesMagnetometerQa(FieldDataProject project) => GetActive(project).Applies(MagnetometerQa);
    public static bool RequiresTowfishPositionReview(FieldDataProject project) => GetActive(project).Applies(TowfishPosition);

    public static string SurveyTypeSummary(FieldDataProject project)
    {
        var types = project.DataTypes.Distinct().Select(Friendly).ToList();
        return types.Count == 0 ? "Unresolved" : string.Join(" + ", types);
    }

    public static IReadOnlyList<string> DescribeActiveRules(FieldDataProject project)
    {
        ActiveSurveyRequirements active = GetActive(project);
        var lines = new List<string> { $"Survey types: {SurveyTypeSummary(project)}" };
        lines.AddRange(active.Rules
            .OrderBy(r => r.Category)
            .ThenBy(r => r.Label)
            .Select(r => $"{r.Label}: {DispositionLabel(r.Disposition)}"));
        return lines;
    }

    public static bool HasBarCheckFile(FieldDataProject project) =>
        project.SupportingFiles.Any(file => File.Exists(file.Path) &&
            (Path.GetExtension(file.Path).Equals(".dso", StringComparison.OrdinalIgnoreCase) ||
             CategoryMatches(file.Category, "Bar Check / Echosounder Calibration")));

    public static bool HasSoundVelocityFile(FieldDataProject project) =>
        project.SupportingFiles.Any(file => File.Exists(file.Path) &&
            (IsSoundVelocityExtension(Path.GetExtension(file.Path)) ||
             CategoryMatches(file.Category, "SVP / Sound Velocity")));

    public static bool IsSoundVelocityExtension(string extension) =>
        extension.Equals(".svp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".vel", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".vlt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".sv", StringComparison.OrdinalIgnoreCase);

    public static bool CategoryMatches(string actual, string required)
    {
        static string Normalize(string value) => new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        string a = Normalize(actual);
        string r = Normalize(required);
        if (a == r) return true;
        if (r.Contains("barcheck") || r.Contains("echosoundercalibration"))
            return a.Contains("barcheck") || a.Contains("echosounder") || a.Contains("dso") || a.Contains("calibration");
        if (r.Contains("svp") || r.Contains("soundvelocity"))
            return a.Contains("svp") || a.Contains("soundvelocity") || a.Contains("velocitycast") || a.Contains("soundspeed");
        return false;
    }

    public static bool IsSingleBeamType(SurveyDataType type) =>
        type is SurveyDataType.SingleBeamFrequencyUnknown
            or SurveyDataType.SingleBeamHighFrequency
            or SurveyDataType.SingleBeamLowFrequency
            or SurveyDataType.SingleBeamDualFrequency;

    private static void Add(ActiveSurveyRequirements result, string id, string label, string category, RequirementDisposition disposition, string details)
    {
        int existing = result.Rules.FindIndex(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var rule = new SurveyRequirementRule(id, label, category, disposition, details);
        if (existing < 0) result.Rules.Add(rule);
        else if (Priority(disposition) > Priority(result.Rules[existing].Disposition)) result.Rules[existing] = rule;
    }

    private static void AddNotApplicable(ActiveSurveyRequirements result, string id, string label, string details) =>
        Add(result, id, label, "Not Applicable", RequirementDisposition.NotApplicable, details);

    private static int Priority(RequirementDisposition disposition) => disposition switch
    {
        RequirementDisposition.Required => 4,
        RequirementDisposition.ReviewOrDocumentException => 3,
        RequirementDisposition.Recommended => 2,
        _ => 1
    };

    private static string DispositionLabel(RequirementDisposition disposition) => disposition switch
    {
        RequirementDisposition.Required => "Required",
        RequirementDisposition.ReviewOrDocumentException => "File or documented exception",
        RequirementDisposition.Recommended => "Recommended",
        _ => "Not applicable"
    };

    private static string Friendly(SurveyDataType type) => type switch
    {
        SurveyDataType.SingleBeamFrequencyUnknown => "Single Beam / Frequency Unknown",
        SurveyDataType.SingleBeamHighFrequency => "Single Beam / High Frequency",
        SurveyDataType.SingleBeamLowFrequency => "Single Beam / Low Frequency",
        SurveyDataType.SingleBeamDualFrequency => "Single Beam / Dual Frequency",
        SurveyDataType.TopographicGnss => "Topographic GNSS",
        SurveyDataType.TideOrWaterLevel => "Tide / Water Level",
        SurveyDataType.TowfishPositioning => "Towfish Positioning",
        _ => string.Concat(type.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()))
    };
}
