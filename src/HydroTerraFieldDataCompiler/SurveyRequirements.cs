using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

/// <summary>
/// Central survey-type applicability rules used by Project Health, Finalize Project,
/// and package compilation. These rules describe what applies; they do not alter source data.
/// </summary>
public static class SurveyRequirements
{
    // Confirmed Step 4 selections are the source of truth. Detection only preselects these values.
    public static bool HasSingleBeam(FieldDataProject project) =>
        project.DataTypes.Any(IsSingleBeamType);

    public static bool HasMagnetometer(FieldDataProject project) =>
        project.DataTypes.Contains(SurveyDataType.Magnetometer);

    public static bool RequiresBinFiles(FieldDataProject project) => HasSingleBeam(project);
    public static bool ReviewsBarCheck(FieldDataProject project) => HasSingleBeam(project);
    public static bool ReviewsSoundVelocity(FieldDataProject project) => HasSingleBeam(project);

    public static string SurveyTypeSummary(FieldDataProject project)
    {
        var types = new List<string>();
        if (HasSingleBeam(project)) types.Add("Single Beam");
        if (HasMagnetometer(project)) types.Add("Magnetometer");
        if (project.DataTypes.Contains(SurveyDataType.SideScan)) types.Add("Side Scan");
        if (project.DataTypes.Contains(SurveyDataType.Multibeam)) types.Add("Multibeam");
        return types.Count == 0 ? "Unresolved" : string.Join(" + ", types);
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

    private static bool IsSingleBeamType(SurveyDataType type) =>
        type is SurveyDataType.SingleBeamFrequencyUnknown
            or SurveyDataType.SingleBeamHighFrequency
            or SurveyDataType.SingleBeamLowFrequency
            or SurveyDataType.SingleBeamDualFrequency;
}
