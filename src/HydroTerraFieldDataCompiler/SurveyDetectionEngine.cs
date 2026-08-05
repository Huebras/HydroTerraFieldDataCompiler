using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class SurveyDetectionEngine
{
    public static void Apply(FieldDataProject project)
    {
        project.DetectedEquipment = BuildEquipment(project.RawFileSummaries);
        project.SurveyTypeDetections = BuildSurveyTypes(project.RawFileSummaries, project.DetectedEquipment);
        project.DetectedDataTypes = project.SurveyTypeDetections
            .Where(x => x.Confidence != DetectionConfidence.NotDetected)
            .Select(x => x.SurveyType)
            .Distinct()
            .ToList();

        if (!project.DataTypesManuallyConfirmed)
            project.DataTypes = project.DetectedDataTypes.ToList();
    }

    private static List<EquipmentDetection> BuildEquipment(IEnumerable<RawFileSummary> files)
    {
        return files.SelectMany(f => f.DeviceDataUsage)
            .GroupBy(x => new { x.DeviceId, x.DeviceName, x.EquipmentType })
            .Select(g => new EquipmentDetection
            {
                DeviceId = g.Key.DeviceId,
                DeviceName = g.Key.DeviceName,
                EquipmentType = g.Key.EquipmentType,
                Confidence = g.Sum(x => x.EcRecordCount + x.PositionRecordCount) > 0 ? DetectionConfidence.High : DetectionConfidence.Medium,
                ObservationCount = g.Sum(x => x.EcRecordCount + x.PositionRecordCount),
                Evidence = BuildEquipmentEvidence(g)
            })
            .OrderBy(x => x.DeviceId ?? int.MaxValue)
            .ThenBy(x => x.EquipmentType)
            .ToList();
    }

    private static List<string> BuildEquipmentEvidence(IEnumerable<DeviceDataUsage> values)
    {
        var rows = values.ToList();
        var evidence = new List<string>();
        int ec = rows.Sum(x => x.EcRecordCount);
        int hf = rows.Sum(x => x.HighFrequencyValueCount);
        int lf = rows.Sum(x => x.LowFrequencyValueCount);
        int mag = rows.Sum(x => x.MagnetometerValueCount);
        int pos = rows.Sum(x => x.PositionRecordCount);
        if (ec > 0) evidence.Add($"{ec:N0} EC1/EC2 records");
        if (hf > 0) evidence.Add($"{hf:N0} Depth 1 / high-frequency values");
        if (lf > 0) evidence.Add($"{lf:N0} Depth 2 / low-frequency values");
        if (mag > 0) evidence.Add($"{mag:N0} magnetometer values");
        if (pos > 0) evidence.Add($"{pos:N0} position records");
        foreach (string file in rows.Select(x => x.SourceFile).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            evidence.Add($"Source: {Path.GetFileName(file)}");
        return evidence;
    }

    private static List<SurveyTypeDetection> BuildSurveyTypes(IEnumerable<RawFileSummary> files, IEnumerable<EquipmentDetection> equipment)
    {
        var results = new List<SurveyTypeDetection>();
        var usages = files.SelectMany(f => f.DeviceDataUsage).ToList();
        int hf = usages.Where(x => x.EquipmentType == "Single Beam").Sum(x => x.HighFrequencyValueCount);
        int lf = usages.Where(x => x.EquipmentType == "Single Beam").Sum(x => x.LowFrequencyValueCount);
        int mag = usages.Where(x => x.EquipmentType == "Magnetometer").Sum(x => x.MagnetometerValueCount);

        if (hf > 0 || lf > 0)
        {
            SurveyDataType type = hf > 0 && lf > 0
                ? SurveyDataType.SingleBeamDualFrequency
                : hf > 0 ? SurveyDataType.SingleBeamHighFrequency : SurveyDataType.SingleBeamLowFrequency;
            results.Add(new SurveyTypeDetection
            {
                SurveyType = type,
                Confidence = DetectionConfidence.High,
                Evidence = new List<string>
                {
                    $"Fathometer-linked Depth 1 values (high frequency): {hf:N0}",
                    $"Fathometer-linked Depth 2 values (low frequency): {lf:N0}",
                    "EC channel meaning was assigned from the referenced HYPACK device ID."
                }
            });
        }

        if (mag > 0)
            results.Add(new SurveyTypeDetection
            {
                SurveyType = SurveyDataType.Magnetometer,
                Confidence = DetectionConfidence.High,
                Evidence = new List<string>
                {
                    $"Magnetometer-device EC observations: {mag:N0}",
                    "Magnetometer EC values were excluded from single-beam depth counts."
                }
            });

        foreach (SurveyDataType type in files.SelectMany(x => x.SuggestedDataTypes)
                     .Where(x => x is SurveyDataType.Multibeam or SurveyDataType.SideScan or SurveyDataType.SubBottom or SurveyDataType.Adcp or SurveyDataType.SoundVelocity or SurveyDataType.TideOrWaterLevel or SurveyDataType.TowfishPositioning)
                     .Distinct())
        {
            results.Add(new SurveyTypeDetection
            {
                SurveyType = type,
                Confidence = DetectionConfidence.Medium,
                Evidence = new List<string> { "Detected from device or record-type evidence in the imported survey data." }
            });
        }
        return results.GroupBy(x => x.SurveyType).Select(g => g.OrderByDescending(x => x.Confidence).First()).ToList();
    }
}
