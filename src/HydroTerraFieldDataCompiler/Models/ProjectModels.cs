namespace HydroTerraFieldDataCompiler.Models;

public enum SurveyDataType
{
    SingleBeamFrequencyUnknown = 0,
    SingleBeamHighFrequency = 1,
    SingleBeamLowFrequency = 2,
    SingleBeamDualFrequency = 3,
    Multibeam = 4,
    SideScan = 5,
    Magnetometer = 6,
    SubBottom = 7,
    Adcp = 8,
    Lidar = 9,
    TopographicGnss = 10,
    SoundVelocity = 11,
    TideOrWaterLevel = 12,
    TowfishPositioning = 13,
    NavigationOnly = 14,
    Other = 15
}
public enum PositioningMethod { Unknown, DifferentialGps, Vrs, NetworkRtk, BaseRoverRtk, Ppk, StandaloneGnss, Other }
public enum GnssSolutionType { Unknown, Fixed, Float, Differential, Autonomous, DeadReckoning, Invalid, NoSolution }
public enum DetectionConfidence { NotDetected, Low, Medium, High, Conflicting }
public enum GnssQualityProfile { Unknown, GenericNmea, VrsNetwork, ApplanixPosMv, Proprietary }

public sealed class FieldDataProject
{
    public string ProjectFilePath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectNumber { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Vessel { get; set; } = string.Empty;
    public string FieldCrew { get; set; } = string.Empty;
    public DateTime? SurveyStartDate { get; set; }
    public DateTime? SurveyEndDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<SurveyDataType> DataTypes { get; set; } = new();
    public List<SurveyDataType> DetectedDataTypes { get; set; } = new();
    public bool DataTypesManuallyConfirmed { get; set; }
    public List<PositioningMethod> PositioningMethods { get; set; } = new();
    public PositioningMethod DetectedPositioningMethod { get; set; } = PositioningMethod.Unknown;
    public DetectionConfidence PositioningConfidence { get; set; } = DetectionConfidence.NotDetected;
    public List<string> PositioningEvidence { get; set; } = new();
    public List<string> ImportedRawFiles { get; set; } = new();
    public List<string> ImportedLogFiles { get; set; } = new();
    public List<HypackLogSummary> HypackLogSummaries { get; set; } = new();
    public List<RawFileSummary> RawFileSummaries { get; set; } = new();
    public List<SupportingFile> SupportingFiles { get; set; } = new();
    public List<QaFinding> Findings { get; set; } = new();
    public GeodesyConfiguration Geodesy { get; set; } = new();
    public List<DeviceConfiguration> Devices { get; set; } = new();
    public List<OffsetChange> OffsetChanges { get; set; } = new();
    public List<SurveyLineSummary> SurveyLines { get; set; } = new();
    public List<LineCoverageResult> LineCoverageResults { get; set; } = new();
    public List<MagnetometerLineQaResult> MagnetometerQaResults { get; set; } = new();
    public double OfflineToleranceFeet { get; set; } = 20.0;
    public double CoverageGapFeet { get; set; } = 20.0;
    public double GapExportOverlapFeet { get; set; } = 25.0;
    public double MinimumFixedPercent { get; set; } = 95.0;
    public double DepthSpikeThresholdFeet { get; set; } = 2.0;
    public int FrozenDepthSampleCount { get; set; } = 5;
    public double MaximumVesselSpeedKnots { get; set; } = 15.0;
    public double NavigationGapMultiplier { get; set; } = 3.0;
    public bool UsePositionCoverageForRemainingLines { get; set; } = true;
    public bool UseOfflineToleranceForCoverage { get; set; } = true;
    public bool UsePositionQualityForRemainingLines { get; set; }
    public bool UseNavigationIntegrityForRemainingLines { get; set; }
    public bool UseDepthQaForRemainingLines { get; set; }
    public int? QaPositionSourceDeviceId { get; set; }
    public string QaPositionSourceLabel { get; set; } = string.Empty;
    public ProjectHealthSummary ProjectHealth { get; set; } = new();
    public List<string> ExcludedPackageItemKeys { get; set; } = new();
    public string ReviewedBy { get; set; } = string.Empty;
    public string ReviewTitle { get; set; } = string.Empty;
    public DateTime? ReviewDate { get; set; }
    public string ReviewComments { get; set; } = string.Empty;
    public bool PackageApproved { get; set; }
    public string BarCheckExceptionReason { get; set; } = string.Empty;
    public string SvpExceptionReason { get; set; } = string.Empty;
}


public sealed class HypackLogSummary
{
    public string SourcePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsEmbeddedInZip { get; set; }
    public string ArchiveEntryName { get; set; } = string.Empty;
    public int ReferencedRawCount { get; set; }
    public int FoundRawCount { get; set; }
    public int MissingRawCount { get; set; }
    public int UnlistedLoadedRawCount { get; set; }
    public string Status { get; set; } = "Not parsed";
    public List<HypackLogReference> References { get; set; } = new();
}

public sealed class HypackLogReference
{
    public int Order { get; set; }
    public string RawFileName { get; set; } = string.Empty;
    public string ReferencedPath { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public bool Found { get; set; }
    public string SourceText { get; set; } = string.Empty;
}

public sealed class RawFileSummary
{
    public string SourcePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime? SurveyDate { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int RecordCount { get; set; }
    public int MalformedCount { get; set; }
    public int NavigationCount { get; set; }
    public int TimestampCount { get; set; }
    public int TimeReversalCount { get; set; }
    public int LargeGapCount { get; set; }
    public string Status { get; set; } = "Not scanned";
    public List<SurveyDataType> SuggestedDataTypes { get; set; } = new();
    public Dictionary<GnssSolutionType, int> GnssSolutionCounts { get; set; } = new();
    public Dictionary<string, int> RecordTypeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GnssQualitySample> GnssQualitySamples { get; set; } = new();
    public Dictionary<string, int> SurveyLineCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DeviceConfiguration> DetectedDevices { get; set; } = new();
    public List<DetectionEvidence> GeodesyEvidence { get; set; } = new();
    public List<DetectionEvidence> PositioningEvidence { get; set; } = new();
    public Dictionary<string, string> IniSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> IniHeaderLines { get; set; } = new();
    public int IniDifferenceCount { get; set; }
    public bool IsIniBaseline { get; set; }
    public int EchosounderRecordCount { get; set; }
    public int HighFrequencyDepthCount { get; set; }
    public int LowFrequencyDepthCount { get; set; }
    public int TideRecordCount { get; set; }
    public int FixRecordCount { get; set; }
    public int PrdRecordCount { get; set; }
    public double? EchosounderStartSeconds { get; set; }
    public double? EchosounderEndSeconds { get; set; }
    public double? NavigationStartSeconds { get; set; }
    public double? NavigationEndSeconds { get; set; }
    public double? MinimumX { get; set; }
    public double? MaximumX { get; set; }
    public double? MinimumY { get; set; }
    public double? MaximumY { get; set; }
    public bool HasMatchingBin { get; set; }
    public string MatchingBinName { get; set; } = string.Empty;
    public string DetectedSurveyType { get; set; } = string.Empty;
}

public sealed class GnssQualitySample
{
    public string SourceFile { get; set; } = string.Empty;
    public int SourceLineNumber { get; set; }
    public string SurveyLine { get; set; } = string.Empty;
    public int? DeviceId { get; set; }
    public double? SecondsOfDay { get; set; }
    public int? ModeCode { get; set; }
    public List<string> RawQualityFields { get; set; } = new();
    public GnssQualityProfile InterpretationProfile { get; set; } = GnssQualityProfile.Unknown;
    public DetectionConfidence InterpretationConfidence { get; set; } = DetectionConfidence.NotDetected;
    public string InterpretationNote { get; set; } = string.Empty;
    public GnssSolutionType SolutionType { get; set; }
    public double? Hdop { get; set; }
    public int? SatelliteCount { get; set; }
    public double? CorrectionAgeSeconds { get; set; }
}

public sealed class DetectionEvidence
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DetectionConfidence Confidence { get; set; } = DetectionConfidence.Low;
}

public sealed class GeodesyConfiguration
{
    public string RecordedHorizontalDatum { get; set; } = string.Empty;
    public string RecordedGrid { get; set; } = string.Empty;
    public string RecordedProjection { get; set; } = string.Empty;
    public string RecordedZone { get; set; } = string.Empty;
    public string RecordedZoneId { get; set; } = string.Empty;
    public string RecordedUnits { get; set; } = string.Empty;
    public string RecordedEllipsoid { get; set; } = string.Empty;
    public string ApprovedHorizontalDatum { get; set; } = string.Empty;
    public string ApprovedGrid { get; set; } = string.Empty;
    public string ApprovedProjection { get; set; } = string.Empty;
    public string ApprovedZone { get; set; } = string.Empty;
    public string ApprovedZoneId { get; set; } = string.Empty;
    public string ApprovedUnits { get; set; } = string.Empty;
    public string ApprovedEllipsoid { get; set; } = string.Empty;
    public string VerticalDatum { get; set; } = string.Empty;
    public string GeoidModel { get; set; } = string.Empty;
    public double? UnitFactorMeters { get; set; }
    public double? VerticalUnitFactorMeters { get; set; }
    public double? CentralMeridian { get; set; }
    public double? ReferenceLatitude { get; set; }
    public double? FalseEasting { get; set; }
    public double? FalseNorthing { get; set; }
    public double? ScaleFactor { get; set; }
    public string CoordinateRangeSummary { get; set; } = string.Empty;
    public string ValidationStatus { get; set; } = string.Empty;
    public List<string> ValidationMessages { get; set; } = new();
    public string CorrectionReason { get; set; } = string.Empty;
    public DetectionConfidence DetectionConfidence { get; set; } = DetectionConfidence.NotDetected;
    public List<DetectionEvidence> Evidence { get; set; } = new();
}

public sealed class DeviceConfiguration
{
    public int? DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int? InterfaceType { get; set; }
    public string DriverPath { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public double RecordedStarboard { get; set; }
    public double RecordedForward { get; set; }
    public double RecordedVertical { get; set; }
    public double ApprovedStarboard { get; set; }
    public double ApprovedForward { get; set; }
    public double ApprovedVertical { get; set; }
    public double RecordedYaw { get; set; }
    public double RecordedRoll { get; set; }
    public double RecordedPitch { get; set; }
    public double RecordedLatency { get; set; }
    public double ApprovedYaw { get; set; }
    public double ApprovedRoll { get; set; }
    public double ApprovedPitch { get; set; }
    public double ApprovedLatency { get; set; }
    public string RawDeviceHeader { get; set; } = string.Empty;
    public string RawOffsetHeader { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string CorrectionReason { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DetectionConfidence IdentityConfidence { get; set; } = DetectionConfidence.NotDetected;
    public DetectionConfidence OffsetConfidence { get; set; } = DetectionConfidence.NotDetected;
}

public sealed class OffsetChange
{
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
    public string DeviceName { get; set; } = string.Empty;
    public string OriginalValues { get; set; } = string.Empty;
    public string ApprovedValues { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class SurveyLineSummary
{
    public string LineName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int NonFixedCount { get; set; }
    public string Classification { get; set; } = "Production";
    public string Status { get; set; } = "Unreviewed";
    public string Notes { get; set; } = string.Empty;
}

public sealed class LineCoverageResult
{
    public string LineName { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public List<string> SourceFiles { get; set; } = new();
    public int SegmentCount => SourceFiles.Count > 0 ? SourceFiles.Count : (string.IsNullOrWhiteSpace(SourceFile) ? 0 : 1);
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double PlannedLength { get; set; }
    public int PositionCount { get; set; }
    public int OfflinePositionCount { get; set; }
    public int QualityObservationCount { get; set; }
    public int FixedQualityCount { get; set; }
    public int NonFixedQualityCount { get; set; }
    public int UnknownQualityCount { get; set; }
    public double FixedQualityPercent { get; set; }
    public double? AverageHdop { get; set; }
    public int? MinimumSatellites { get; set; }
    public int HighFrequencyCount { get; set; }
    public int LowFrequencyCount { get; set; }
    public int HighFrequencyInvalidCount { get; set; }
    public int LowFrequencyInvalidCount { get; set; }
    public int HighFrequencySpikeCount { get; set; }
    public int LowFrequencySpikeCount { get; set; }
    public int HighFrequencyFrozenRunCount { get; set; }
    public int LowFrequencyFrozenRunCount { get; set; }
    public double? HighFrequencyMinimum { get; set; }
    public double? HighFrequencyMaximum { get; set; }
    public double? LowFrequencyMinimum { get; set; }
    public double? LowFrequencyMaximum { get; set; }
    public int DualFrequencyComparisonCount { get; set; }
    public string DepthQaSummary { get; set; } = string.Empty;
    public bool DepthQaHasWarning { get; set; }
    public double MaximumOfflineDistance { get; set; }
    public double AveragePositionIntervalSeconds { get; set; }
    public double MaximumPositionIntervalSeconds { get; set; }
    public int NavigationGapCount { get; set; }
    public int EstimatedMissingEpochCount { get; set; }
    public double AverageSpeedKnots { get; set; }
    public double MaximumSpeedKnots { get; set; }
    public int SpeedSpikeCount { get; set; }
    public int DuplicateTimestampCount { get; set; }
    public int TimeReversalCount { get; set; }
    public int DuplicatePositionCount { get; set; }
    public int PositionFreezeCount { get; set; }
    public int ImpossibleJumpCount { get; set; }
    public int NavigationIntegrityScore { get; set; } = 100;
    public string NavigationIntegritySummary { get; set; } = string.Empty;
    public bool NavigationIntegrityHasWarning { get; set; }
    public List<LineGap> CoverageGaps { get; set; } = new();
    public List<LineGap> Gaps { get; set; } = new();
    public List<string> RemainingLineReasons { get; set; } = new();
    public List<LinePoint> TrackPoints { get; set; } = new();
    public List<LinePoint> OfflinePoints { get; set; } = new();
    public string QaPositionSource { get; set; } = string.Empty;
    public int? QaPositionSourceDeviceId { get; set; }
    public string Status { get; set; } = "Not analyzed";
}

public sealed class LinePoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double CrossTrackDistance { get; set; }
    public bool IsOffline { get; set; }
    public double? SecondsOfDay { get; set; }
    public bool IsNavigationGapStart { get; set; }
    public bool IsPositionFreeze { get; set; }
    public bool IsImpossibleJump { get; set; }
}

public sealed class LineGap
{
    public int GapNumber { get; set; }
    public double StartChainage { get; set; }
    public double EndChainage { get; set; }
    public double MissingLength => Math.Max(0, EndChainage - StartChainage);
}


public sealed class MagnetometerLineQaResult
{
    public string LineName { get; set; } = string.Empty;
    public List<string> SourceFiles { get; set; } = new();
    public int DeviceId { get; set; } = -1;
    public string DeviceName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int InvalidValueCount { get; set; }
    public int FrozenRunCount { get; set; }
    public int DataGapCount { get; set; }
    public int EstimatedMissingRecordCount { get; set; }
    public double AverageIntervalSeconds { get; set; }
    public double MaximumIntervalSeconds { get; set; }
    public double? MinimumValue { get; set; }
    public double? MaximumValue { get; set; }
    public double NavigationStartOffsetSeconds { get; set; }
    public double NavigationEndOffsetSeconds { get; set; }
    public bool HasWarning => RecordCount == 0 || InvalidValueCount > 0 || FrozenRunCount > 0 || DataGapCount > 0 || Math.Abs(NavigationStartOffsetSeconds) > 2 || Math.Abs(NavigationEndOffsetSeconds) > 2;
    public string Summary { get; set; } = string.Empty;
}

public sealed class SupportingFile { public string Path { get; set; } = string.Empty; public string Category { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty; public DateTime DateAddedUtc { get; set; } = DateTime.UtcNow; }
public sealed class QaFinding { public string RuleId { get; set; } = string.Empty; public string Severity { get; set; } = "Info"; public string Category { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Evidence { get; set; } = string.Empty; public string FileName { get; set; } = string.Empty; public string SurveyLine { get; set; } = string.Empty; }


public enum HealthStatus { NotEvaluated, Pass, Warning, Failure }

public sealed class ProjectHealthSummary
{
    public int Score { get; set; }
    public HealthStatus OverallStatus { get; set; } = HealthStatus.NotEvaluated;
    public DateTime EvaluatedUtc { get; set; }
    public string BaselineConfigurationFingerprint { get; set; } = string.Empty;
    public string BaselineFile { get; set; } = string.Empty;
    public int MatchingConfigurationFiles { get; set; }
    public int DifferentConfigurationFiles { get; set; }
    public List<ProjectHealthItem> Items { get; set; } = new();
}

public sealed class ProjectHealthItem
{
    public string Category { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public HealthStatus Status { get; set; } = HealthStatus.NotEvaluated;
    public string Details { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}


public sealed class PackageReviewItem
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ProposedRelativePath { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool Include { get; set; } = true;
    public string Status { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
