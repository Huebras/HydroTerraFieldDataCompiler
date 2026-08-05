using System.Globalization;
using System.IO.Compression;
using HydroTerraFieldDataCompiler.Models;
using HydroTerraFieldDataCompiler.Parsing;

namespace HydroTerraFieldDataCompiler;

public sealed class LineCoverageAnalyzer
{
    private sealed class LineSegment
    {
        public string LineName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public List<NavPoint> Positions { get; set; } = new();
        public List<QualityPoint> Quality { get; set; } = new();
        public List<DepthPoint> Depths { get; set; } = new();
    }

    private sealed class NavPoint
    {
        public double? Time { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed class QualityPoint
    {
        public int? Code { get; set; }
        public GnssSolutionType Solution { get; set; }
        public double? Hdop { get; set; }
        public int? Satellites { get; set; }
    }

    private sealed class DepthPoint
    {
        public double? High { get; set; }
        public double? Low { get; set; }
    }

    public List<LineCoverageResult> Analyze(FieldDataProject project)
    {
        var segments = new List<LineSegment>();
        double unitFactor = project.Geodesy.UnitFactorMeters.GetValueOrDefault(0.3048006096012192);
        if (unitFactor <= 0) unitFactor = 0.3048006096012192;
        double feetToSource = 0.3048 / unitFactor;
        double offlineTolerance = project.OfflineToleranceFeet * feetToSource;
        double coverageGap = project.CoverageGapFeet * feetToSource;
        double depthSpike = project.DepthSpikeThresholdFeet * feetToSource;

        var availableSources = GetPositionSources(project);
        int? coverageDevice = project.QaPositionSourceDeviceId;
        if (!coverageDevice.HasValue || !availableSources.Any(x => x.DeviceId == coverageDevice.Value))
        {
            coverageDevice = ChooseDefaultPositionSource(project, availableSources)?.DeviceId ?? 0;
            project.QaPositionSourceDeviceId = coverageDevice;
        }
        string coverageLabel = availableSources.FirstOrDefault(x => x.DeviceId == coverageDevice)?.DisplayName
            ?? $"Device {coverageDevice.GetValueOrDefault()}";
        project.QaPositionSourceLabel = coverageLabel;

        int? qualityDevice = project.Devices
            .Where(d => d.DeviceType.Contains("Position", StringComparison.OrdinalIgnoreCase) || d.DeviceName.Contains("GPS", StringComparison.OrdinalIgnoreCase) || d.DeviceName.Contains("GNSS", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.DeviceId).FirstOrDefault(id => id.HasValue) ?? 0;

        foreach (string path in project.ImportedRawFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = entry.Open();
                    ReadSegment(path + "::" + entry.FullName, stream, coverageDevice, qualityDevice, segments);
                }
            }
            else
            {
                using var stream = File.OpenRead(path);
                ReadSegment(path, stream, coverageDevice, qualityDevice, segments);
            }
        }

        var grouped = new List<List<LineSegment>>();
        foreach (var segment in segments)
        {
            var match = grouped.FirstOrDefault(g => g[0].LineName.Equals(segment.LineName, StringComparison.OrdinalIgnoreCase) && SameGeometry(g[0], segment));
            if (match == null) grouped.Add(new List<LineSegment> { segment });
            else match.Add(segment);
        }

        SurveyDataType singleBeamMode = GetSingleBeamMode(project);
        return grouped.Select(g => AnalyzeGroup(g, offlineTolerance, coverageGap, depthSpike, project.FrozenDepthSampleCount, project.MinimumFixedPercent, singleBeamMode, unitFactor, project.MaximumVesselSpeedKnots, project.NavigationGapMultiplier, coverageDevice, coverageLabel,
                project.UsePositionCoverageForRemainingLines, project.UseOfflineToleranceForCoverage, project.UsePositionQualityForRemainingLines, project.UseNavigationIntegrityForRemainingLines, project.UseDepthQaForRemainingLines))
            .OrderBy(r => r.LineName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public sealed class PositionSourceOption
    {
        public int DeviceId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int PositionCount { get; set; }
        public bool IsTowfish { get; set; }
        public bool IsPrimaryGnss { get; set; }
        public override string ToString() => $"{DisplayName} ({PositionCount:N0} positions)";
    }

    public static List<PositionSourceOption> GetPositionSources(FieldDataProject project)
    {
        var counts = new Dictionary<int, int>();
        foreach (string path in project.ImportedRawFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = entry.Open();
                    CountPositionDevices(stream, counts);
                }
            }
            else
            {
                using var stream = File.OpenRead(path);
                CountPositionDevices(stream, counts);
            }
        }

        var byId = project.Devices.Where(d => d.DeviceId.HasValue)
            .GroupBy(d => d.DeviceId!.Value).ToDictionary(g => g.Key, g => g.First());
        return counts.OrderBy(k => k.Key).Select(k =>
        {
            byId.TryGetValue(k.Key, out var d);
            string name = d == null || string.IsNullOrWhiteSpace(d.DeviceName) ? $"Device {k.Key}" : d.DeviceName;
            bool towfish = name.Contains("towfish", StringComparison.OrdinalIgnoreCase) || (d?.DeviceType?.Contains("towfish", StringComparison.OrdinalIgnoreCase) ?? false);
            bool gnss = name.Contains("GPS", StringComparison.OrdinalIgnoreCase) || name.Contains("GNSS", StringComparison.OrdinalIgnoreCase) || (d?.DeviceType?.Contains("Position", StringComparison.OrdinalIgnoreCase) ?? false);
            return new PositionSourceOption { DeviceId = k.Key, DisplayName = $"Device {k.Key} — {name}", PositionCount = k.Value, IsTowfish = towfish, IsPrimaryGnss = gnss };
        }).ToList();
    }

    public static PositionSourceOption? ChooseDefaultPositionSource(FieldDataProject project, IReadOnlyList<PositionSourceOption> sources)
    {
        bool towfishWorkflow = project.DataTypes.Contains(SurveyDataType.Magnetometer) || project.DataTypes.Contains(SurveyDataType.SideScan) || project.DataTypes.Contains(SurveyDataType.TowfishPositioning);
        if (towfishWorkflow)
        {
            var tow = sources.Where(x => x.IsTowfish).OrderByDescending(x => x.PositionCount).FirstOrDefault();
            if (tow != null) return tow;
        }
        return sources.Where(x => x.IsPrimaryGnss).OrderByDescending(x => x.PositionCount).FirstOrDefault()
            ?? sources.OrderByDescending(x => x.PositionCount).FirstOrDefault();
    }

    private static void CountPositionDevices(Stream stream, Dictionary<int, int> counts)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string t = line.Trim();
            if (!t.StartsWith("POS ", StringComparison.OrdinalIgnoreCase)) continue;
            var f = Split(t);
            if (int.TryParse(f.ElementAtOrDefault(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
        }
    }

    private static void ReadSegment(string sourceName, Stream stream, int? coverageDevice, int? qualityDevice, List<LineSegment> output)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var vertices = new List<(double X, double Y)>();
        string lineName = string.Empty;
        bool inLineBlock = false;
        bool rtkMode4 = false;
        var positions = new List<NavPoint>();
        var rawQuality = new List<(int? Code, double? Hdop, int? Satellites)>();
        var depths = new List<DepthPoint>();
        string? text;
        while ((text = reader.ReadLine()) != null)
        {
            string trimmed = text.Trim();
            if (trimmed.StartsWith("INI", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("RTKMode=4", StringComparison.OrdinalIgnoreCase)) rtkMode4 = true;
            if (trimmed.StartsWith("LIN ", StringComparison.OrdinalIgnoreCase)) { inLineBlock = true; vertices.Clear(); lineName = string.Empty; continue; }
            if (inLineBlock && trimmed.StartsWith("PTS ", StringComparison.OrdinalIgnoreCase))
            {
                var f = Split(trimmed); if (TryDouble(f, 1, out double x) && TryDouble(f, 2, out double y)) vertices.Add((x, y)); continue;
            }
            if (inLineBlock && trimmed.StartsWith("LNN ", StringComparison.OrdinalIgnoreCase)) { lineName = trimmed.Length > 4 ? trimmed[4..].Trim().Trim('"') : string.Empty; continue; }
            if (inLineBlock && trimmed.Equals("EOL", StringComparison.OrdinalIgnoreCase)) { inLineBlock = false; continue; }
            if (trimmed.StartsWith("POS ", StringComparison.OrdinalIgnoreCase))
            {
                var f = Split(trimmed);
                if (!int.TryParse(f.ElementAtOrDefault(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int device)) continue;
                if (coverageDevice.HasValue && device != coverageDevice.Value) continue;
                if (TryDouble(f, 3, out double x) && TryDouble(f, 4, out double y)) positions.Add(new NavPoint { Time = TryNullableDouble(f, 2), X = x, Y = y });
            }
            else if (trimmed.StartsWith("QUA ", StringComparison.OrdinalIgnoreCase))
            {
                var f = Split(trimmed);
                int? device = TryInt(f, 1);
                if (qualityDevice.HasValue && device.HasValue && device.Value != qualityDevice.Value) continue;
                int? code = TryInt(f, 3); double? hdop = TryNullableDouble(f, 5); int? sats = TryInt(f, 6);
                rawQuality.Add((code, hdop, sats));
            }
            else if (trimmed.StartsWith("EC2 ", StringComparison.OrdinalIgnoreCase))
            {
                var f = Split(trimmed);
                depths.Add(new DepthPoint { High = TryNullableDouble(f, 3), Low = TryNullableDouble(f, 4) });
            }
        }

        if (vertices.Count < 2 || positions.Count == 0 || string.IsNullOrWhiteSpace(lineName)) return;
        var quality = rawQuality.Select(q => new QualityPoint
        {
            Code = q.Code,
            Solution = q.Code == 7 && rtkMode4 ? GnssSolutionType.Fixed : HypackRawReader.MapQuality(q.Code, GnssQualityProfile.GenericNmea),
            Hdop = q.Hdop,
            Satellites = q.Satellites
        }).ToList();
        output.Add(new LineSegment { LineName = NormalizeLineName(lineName), SourceFile = sourceName, StartX = vertices[0].X, StartY = vertices[0].Y, EndX = vertices[^1].X, EndY = vertices[^1].Y, Positions = positions, Quality = quality, Depths = depths });
    }

    private static LineCoverageResult AnalyzeGroup(List<LineSegment> segments, double tolerance, double gapThreshold, double depthSpikeThreshold, int frozenCount, double minimumFixedPercent, SurveyDataType singleBeamMode, double unitFactorMeters, double maximumSpeedKnots, double gapMultiplier, int? coverageDeviceId, string coverageDeviceLabel, bool usePositionCoverage, bool useOfflineTolerance, bool usePositionQuality, bool useNavigationIntegrity, bool useDepthQa)
    {
        var baseline = segments[0];
        double dx = baseline.EndX - baseline.StartX, dy = baseline.EndY - baseline.StartY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        var chainages = new List<double>(); var trackPoints = new List<LinePoint>(); var offlinePoints = new List<LinePoint>();
        int offline = 0; double maxOffline = 0;
        foreach (var segment in segments)
        foreach (var p in segment.Positions)
        {
            double t = ((p.X - baseline.StartX) * dx + (p.Y - baseline.StartY) * dy) / (length * length);
            double projectedX = baseline.StartX + t * dx, projectedY = baseline.StartY + t * dy;
            double cross = Math.Sqrt((p.X - projectedX) * (p.X - projectedX) + (p.Y - projectedY) * (p.Y - projectedY));
            maxOffline = Math.Max(maxOffline, cross);
            var linePoint = new LinePoint { X = p.X, Y = p.Y, SecondsOfDay = p.Time, CrossTrackDistance = cross, IsOffline = cross > tolerance };
            trackPoints.Add(linePoint);
            if (cross > tolerance)
            {
                offline++;
                offlinePoints.Add(linePoint);
                if (useOfflineTolerance) continue;
            }
            if (t >= 0 && t <= 1) chainages.Add(t * length);
        }

        chainages.Sort(); var covered = new List<(double Start, double End)>();
        if (chainages.Count > 0)
        {
            double start = chainages[0], end = chainages[0];
            for (int i = 1; i < chainages.Count; i++)
            {
                if (chainages[i] - end <= gapThreshold) end = chainages[i];
                else { covered.Add((start, end)); start = end = chainages[i]; }
            }
            covered.Add((start, end));
        }
        var coverageGaps = new List<LineGap>();
        if (covered.Count == 0) coverageGaps.Add(new LineGap { GapNumber = 1, StartChainage = 0, EndChainage = length });
        else
        {
            double cursor = 0; int gapNo = 1;
            foreach (var c in covered) { if (c.Start - cursor > gapThreshold) coverageGaps.Add(new LineGap { GapNumber = gapNo++, StartChainage = cursor, EndChainage = c.Start }); cursor = Math.Max(cursor, c.End); }
            if (length - cursor > gapThreshold) coverageGaps.Add(new LineGap { GapNumber = gapNo, StartChainage = cursor, EndChainage = length });
        }

        var intervals = new List<double>();
        var speedsKnots = new List<double>();
        int duplicateTimes = 0, reversals = 0, duplicatePositions = 0, freezes = 0, impossibleJumps = 0, speedSpikes = 0;
        foreach (var segment in segments)
        {
            var nav = segment.Positions.Where(p => p.Time.HasValue).ToList();
            int freezeRun = 0;
            for (int i = 1; i < nav.Count; i++)
            {
                double dt = nav[i].Time!.Value - nav[i - 1].Time!.Value;
                if (dt < 0) { reversals++; continue; }
                if (Math.Abs(dt) < 0.000001) { duplicateTimes++; continue; }
                intervals.Add(dt);
                double ddx = nav[i].X - nav[i - 1].X, ddy = nav[i].Y - nav[i - 1].Y;
                double distanceSource = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (distanceSource < 0.000001) { duplicatePositions++; freezeRun++; }
                else
                {
                    if (freezeRun >= 2) freezes++;
                    freezeRun = 0;
                }
                double speedKnots = distanceSource * unitFactorMeters / dt * 1.9438444924406;
                speedsKnots.Add(speedKnots);
                if (speedKnots > maximumSpeedKnots) { speedSpikes++; impossibleJumps++; }
            }
            if (freezeRun >= 2) freezes++;
        }
        double typicalInterval = Median(intervals);
        double maxInterval = intervals.Count == 0 ? 0 : intervals.Max();
        // HYPACK POS records are often logged at a nominal 10 Hz but may naturally appear at
        // 0.2-0.8 second spacing because of driver scheduling and record interleaving. Do not
        // classify that normal jitter as missing navigation. A true navigation gap must exceed
        // both five nominal intervals and one full second.
        double gapLimit = typicalInterval > 0 ? Math.Max(1.0, typicalInterval * Math.Max(5.0, gapMultiplier)) : 0;
        int navGaps = gapLimit > 0 ? intervals.Count(v => v > gapLimit) : 0;
        int missingEpochs = typicalInterval > 0 ? intervals.Where(v => v > gapLimit).Sum(v => Math.Max(0, (int)Math.Floor(v / typicalInterval) - 1)) : 0;
        int navScore = 100;
        navScore -= Math.Min(30, navGaps * 5);
        navScore -= Math.Min(25, impossibleJumps * 10);
        navScore -= Math.Min(15, freezes * 5);
        navScore -= Math.Min(15, duplicateTimes * 3 + reversals * 8);
        navScore -= Math.Min(15, speedSpikes * 3);
        navScore = Math.Clamp(navScore, 0, 100);
        bool navWarning = navScore < 90 || navGaps >= 3 || missingEpochs >= 5 || impossibleJumps > 0 || freezes > 0 || reversals > 0;
        string navSummary = $"Score {navScore}%; typical interval {typicalInterval:0.###} s; largest interval {maxInterval:0.###} s; avg speed {(speedsKnots.Count == 0 ? 0 : speedsKnots.Average()):0.00} kn; max speed {(speedsKnots.Count == 0 ? 0 : speedsKnots.Max()):0.00} kn; gaps {navGaps}; freezes {freezes}; jumps {impossibleJumps}; duplicate times {duplicateTimes}; reversals {reversals}.";

        var allQuality = segments.SelectMany(s => s.Quality).ToList();
        int fixedQuality = allQuality.Count(q => q.Solution == GnssSolutionType.Fixed);
        int unknownQuality = allQuality.Count(q => q.Solution == GnssSolutionType.Unknown);
        int nonFixedQuality = allQuality.Count - fixedQuality - unknownQuality;
        double fixedPercent = allQuality.Count == 0 ? 0 : 100.0 * fixedQuality / allQuality.Count;
        var allDepths = segments.SelectMany(s => s.Depths).ToList();
        var high = AnalyzeDepth(allDepths.Select(d => d.High).ToList(), depthSpikeThreshold, frozenCount);
        var low = AnalyzeDepth(allDepths.Select(d => d.Low).ToList(), depthSpikeThreshold, frozenCount);
        var pairedDifferences = allDepths.Where(d => IsValidDepth(d.High) && IsValidDepth(d.Low)).Select(d => Math.Abs(d.High!.Value - d.Low!.Value)).ToList();

        bool qualityWarning = allQuality.Count == 0 || fixedPercent < minimumFixedPercent || nonFixedQuality > 0 || unknownQuality > 0;
        bool depthWarning = DepthWarning(singleBeamMode, high, low);

        var remainingReasons = new List<string>();
        var remainingIntervals = new List<(double Start, double End)>();
        if (usePositionCoverage && coverageGaps.Count > 0)
        {
            remainingIntervals.AddRange(coverageGaps.Select(g => (g.StartChainage, g.EndChainage)));
            remainingReasons.Add($"Position coverage: {coverageGaps.Count} gap(s)");
        }
        if (usePositionQuality && (allQuality.Count == 0 || fixedPercent < minimumFixedPercent))
        {
            remainingIntervals.Add((0, length));
            remainingReasons.Add(allQuality.Count == 0 ? "Position quality: not evaluated" : $"Position quality: fixed {fixedPercent:0.0}%");
        }
        if (useNavigationIntegrity && navWarning)
        {
            remainingIntervals.Add((0, length));
            remainingReasons.Add($"Navigation integrity: {navScore}%");
        }
        if (useDepthQa && depthWarning)
        {
            remainingIntervals.Add((0, length));
            remainingReasons.Add("Depth QA warning");
        }
        var remainingGaps = MergeGapIntervals(remainingIntervals, length);

        var sources = segments.Select(s => s.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LineCoverageResult
        {
            LineName = baseline.LineName, SourceFile = string.Join("; ", sources), SourceFiles = sources,
            StartX = baseline.StartX, StartY = baseline.StartY, EndX = baseline.EndX, EndY = baseline.EndY,
            PlannedLength = length, PositionCount = segments.Sum(s => s.Positions.Count), OfflinePositionCount = offline, MaximumOfflineDistance = maxOffline,
            QualityObservationCount = allQuality.Count, FixedQualityCount = fixedQuality, NonFixedQualityCount = nonFixedQuality, UnknownQualityCount = unknownQuality, FixedQualityPercent = fixedPercent,
            AverageHdop = allQuality.Where(q => q.Hdop.HasValue).Select(q => q.Hdop!.Value).DefaultIfEmpty().Average(),
            MinimumSatellites = allQuality.Where(q => q.Satellites.HasValue).Select(q => q.Satellites!.Value).DefaultIfEmpty().Min(),
            HighFrequencyCount = high.ValidCount, LowFrequencyCount = low.ValidCount, HighFrequencyInvalidCount = high.InvalidCount, LowFrequencyInvalidCount = low.InvalidCount,
            HighFrequencySpikeCount = high.SpikeCount, LowFrequencySpikeCount = low.SpikeCount, HighFrequencyFrozenRunCount = high.FrozenRuns, LowFrequencyFrozenRunCount = low.FrozenRuns,
            HighFrequencyMinimum = high.Minimum, HighFrequencyMaximum = high.Maximum, LowFrequencyMinimum = low.Minimum, LowFrequencyMaximum = low.Maximum,
            DualFrequencyComparisonCount = pairedDifferences.Count,
            DepthQaSummary = BuildDepthSummary(singleBeamMode, high, low, pairedDifferences.Count),
            DepthQaHasWarning = depthWarning,
            AveragePositionIntervalSeconds = typicalInterval, MaximumPositionIntervalSeconds = maxInterval,
            NavigationGapCount = navGaps, EstimatedMissingEpochCount = missingEpochs,
            AverageSpeedKnots = speedsKnots.Count == 0 ? 0 : speedsKnots.Average(), MaximumSpeedKnots = speedsKnots.Count == 0 ? 0 : speedsKnots.Max(),
            SpeedSpikeCount = speedSpikes, DuplicateTimestampCount = duplicateTimes, TimeReversalCount = reversals,
            DuplicatePositionCount = duplicatePositions, PositionFreezeCount = freezes, ImpossibleJumpCount = impossibleJumps,
            NavigationIntegrityScore = navScore, NavigationIntegritySummary = navSummary, NavigationIntegrityHasWarning = navWarning,
            CoverageGaps = coverageGaps, Gaps = remainingGaps, RemainingLineReasons = remainingReasons, TrackPoints = trackPoints, OfflinePoints = offlinePoints,
            QaPositionSource = coverageDeviceLabel, QaPositionSourceDeviceId = coverageDeviceId,
            Status = offline > 0 || remainingGaps.Count > 0 || qualityWarning || depthWarning || navWarning ? "Warning" : "Pass"
        };
    }

    private static List<LineGap> MergeGapIntervals(IEnumerable<(double Start, double End)> intervals, double lineLength)
    {
        var normalized = intervals
            .Select(x => (Start: Math.Max(0, Math.Min(lineLength, x.Start)), End: Math.Max(0, Math.Min(lineLength, x.End))))
            .Where(x => x.End > x.Start)
            .OrderBy(x => x.Start)
            .ToList();
        var output = new List<LineGap>();
        if (normalized.Count == 0) return output;
        double start = normalized[0].Start, end = normalized[0].End;
        int number = 1;
        for (int i = 1; i < normalized.Count; i++)
        {
            if (normalized[i].Start <= end) end = Math.Max(end, normalized[i].End);
            else
            {
                output.Add(new LineGap { GapNumber = number++, StartChainage = start, EndChainage = end });
                start = normalized[i].Start; end = normalized[i].End;
            }
        }
        output.Add(new LineGap { GapNumber = number, StartChainage = start, EndChainage = end });
        return output;
    }

    private sealed class DepthStats
    {
        public int ValidCount { get; set; }
        public int InvalidCount { get; set; }
        public int SpikeCount { get; set; }
        public int FrozenRuns { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
    }

    private static DepthStats AnalyzeDepth(List<double?> values, double spikeThreshold, int frozenCount)
    {
        var stats = new DepthStats(); var valid = values.Where(IsValidDepth).Select(v => v!.Value).ToList();
        stats.ValidCount = valid.Count; stats.InvalidCount = values.Count - valid.Count;
        if (valid.Count > 0) { stats.Minimum = valid.Min(); stats.Maximum = valid.Max(); }
        double? previous = null; int sameRun = 1; bool countedRun = false;
        foreach (var value in values)
        {
            if (!IsValidDepth(value)) { previous = null; sameRun = 1; countedRun = false; continue; }
            if (previous.HasValue)
            {
                if (Math.Abs(value!.Value - previous.Value) > spikeThreshold) stats.SpikeCount++;
                if (Math.Abs(value.Value - previous.Value) < 0.000001)
                {
                    sameRun++;
                    if (sameRun >= frozenCount && !countedRun) { stats.FrozenRuns++; countedRun = true; }
                }
                else { sameRun = 1; countedRun = false; }
            }
            previous = value;
        }
        return stats;
    }

    private static bool DepthWarning(SurveyDataType mode, DepthStats high, DepthStats low)
    {
        // A line-level warning should represent a meaningful acquisition problem, not one isolated sample.
        // Require either no usable data, or repeated/percentage-based anomalies.
        bool ChannelBad(DepthStats stats)
        {
            int total = stats.ValidCount + stats.InvalidCount;
            int invalidLimit = Math.Max(5, (int)Math.Ceiling(total * 0.02));
            int spikeLimit = Math.Max(5, (int)Math.Ceiling(Math.Max(1, stats.ValidCount) * 0.02));
            return stats.ValidCount == 0
                || stats.InvalidCount >= invalidLimit
                || stats.SpikeCount >= spikeLimit
                || stats.FrozenRuns >= 2;
        }

        bool highBad = ChannelBad(high);
        bool lowBad = ChannelBad(low);
        return mode switch
        {
            SurveyDataType.SingleBeamHighFrequency => highBad,
            SurveyDataType.SingleBeamLowFrequency => lowBad,
            SurveyDataType.SingleBeamDualFrequency => highBad || lowBad,
            SurveyDataType.SingleBeamFrequencyUnknown => high.ValidCount == 0 && low.ValidCount == 0,
            _ => false
        };
    }

    private static string BuildDepthSummary(SurveyDataType mode, DepthStats high, DepthStats low, int pairedCount)
    {
        string hf = $"HF {high.ValidCount:N0} valid, {high.InvalidCount:N0} invalid, {high.SpikeCount:N0} spikes, {high.FrozenRuns:N0} frozen runs";
        string lf = $"LF {low.ValidCount:N0} valid, {low.InvalidCount:N0} invalid, {low.SpikeCount:N0} spikes, {low.FrozenRuns:N0} frozen runs";
        string comparison = pairedCount > 0 ? $"; paired {pairedCount:N0}" : string.Empty;
        return mode switch
        {
            SurveyDataType.SingleBeamHighFrequency => hf,
            SurveyDataType.SingleBeamLowFrequency => lf,
            SurveyDataType.SingleBeamDualFrequency => hf + "; " + lf + comparison,
            SurveyDataType.SingleBeamFrequencyUnknown => hf + "; " + lf,
            _ => "Not applicable"
        };
    }

    private static SurveyDataType GetSingleBeamMode(FieldDataProject project)
    {
        foreach (var type in new[] { SurveyDataType.SingleBeamDualFrequency, SurveyDataType.SingleBeamHighFrequency, SurveyDataType.SingleBeamLowFrequency, SurveyDataType.SingleBeamFrequencyUnknown })
            if (project.DataTypes.Contains(type)) return type;
        return SurveyDataType.Other;
    }

    private static bool IsValidDepth(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value > 0;
    private static bool SameGeometry(LineSegment a, LineSegment b)
    {
        const double tolerance = 0.05;
        bool sameDirection = Distance(a.StartX, a.StartY, b.StartX, b.StartY) <= tolerance && Distance(a.EndX, a.EndY, b.EndX, b.EndY) <= tolerance;
        bool reverseDirection = Distance(a.StartX, a.StartY, b.EndX, b.EndY) <= tolerance && Distance(a.EndX, a.EndY, b.StartX, b.StartY) <= tolerance;
        return sameDirection || reverseDirection;
    }
    private static double Distance(double x1, double y1, double x2, double y2) { double dx = x2 - x1, dy = y2 - y1; return Math.Sqrt(dx * dx + dy * dy); }
    private static string NormalizeLineName(string name) => name.Trim().Trim('"');
    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private static string[] Split(string text) => text.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
    private static bool TryDouble(string[] fields, int index, out double value) => double.TryParse(fields.ElementAtOrDefault(index), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static double? TryNullableDouble(string[] fields, int index) => double.TryParse(fields.ElementAtOrDefault(index), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    private static int? TryInt(string[] fields, int index) => int.TryParse(fields.ElementAtOrDefault(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : double.TryParse(fields.ElementAtOrDefault(index), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (int)Math.Round(d) : null;
}
