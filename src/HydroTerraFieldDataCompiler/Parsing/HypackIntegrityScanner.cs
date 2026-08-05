using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler.Parsing;

public sealed class HypackIntegrityScanner
{
    private static readonly Regex TimeRegex = new(@"(?<!\d)(?<h>[0-2]?\d)[: ](?<m>[0-5]\d)[: ](?<s>[0-5]\d(?:\.\d+)?)(?!\d)", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"(?<!\d)(?<m>0?[1-9]|1[0-2])[/\-](?<d>0?[1-9]|[12]\d|3[01])[/\-](?<y>\d{2}|\d{4})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex LineRegex = new(@"(?:LINE|LIN|LNN|SURVEY\s*LINE)[,=:\s]+(?<name>[A-Za-z0-9_\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EpsgRegex = new(@"EPSG\s*[:=]?\s*(?<code>\d{4,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UtmRegex = new(@"UTM\s*(?:ZONE)?\s*(?<zone>\d{1,2})\s*(?<hemi>[NS])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SerialRegex = new(@"(?:SERIAL|S/N|SN)\s*[:=]\s*(?<value>[A-Za-z0-9\-_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OffsetRegex = new(@"(?:OFFSET|OFFSETS|LEVER\s*ARM).*?(?:X|STARBOARD|STBD)\s*[:=]?\s*(?<x>[-+]?\d+(?:\.\d+)?).*?(?:Y|FORWARD|FWD)\s*[:=]?\s*(?<y>[-+]?\d+(?:\.\d+)?).*?(?:Z|VERTICAL|VERT)\s*[:=]?\s*(?<z>[-+]?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ScanResult Scan(IEnumerable<string> paths)
    {
        var result = new ScanResult();
        var seenHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) { result.Findings.Add(Finding("FILE001", "Failure", "File Integrity", "Selected file does not exist.", path, path)); continue; }
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) ScanZip(path, result, seenHashes);
            else { using var stream = File.OpenRead(path); ScanStream(path, Path.GetFileName(path), stream, result, seenHashes, FindSiblingBin(path)); }
        }
        CompareIniHeaders(result);
        FinalizeDetection(result);
        return result;
    }

    private void ScanZip(string zipPath, ScanResult result, Dictionary<string, string> seenHashes)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var rawEntries = archive.Entries.Where(e => e.FullName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)).ToList();
            if (rawEntries.Count == 0) { result.Findings.Add(Finding("ZIP001", "Warning", "File Integrity", "ZIP archive contains no HYPACK RAW files.", zipPath, zipPath)); return; }
            var binNames = archive.Entries.Where(e => e.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                .Select(e => NormalizeBaseName(e.FullName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in rawEntries)
            {
                using var stream = entry.Open();
                string? matchingBin = binNames.Contains(NormalizeBaseName(entry.FullName))
                    ? Path.ChangeExtension(entry.FullName, ".BIN")
                    : null;
                ScanStream(zipPath + "::" + entry.FullName, entry.FullName, stream, result, seenHashes, matchingBin);
            }
        }
        catch (Exception ex) { result.Findings.Add(Finding("ZIP002", "Failure", "File Integrity", "ZIP archive could not be opened.", ex.Message, zipPath)); }
    }

    private void ScanStream(string sourcePath, string displayName, Stream input, ScanResult result, Dictionary<string, string> seenHashes, string? matchingBin)
    {
        var summary = new RawFileSummary { SourcePath = sourcePath, DisplayName = displayName, HasMatchingBin = !string.IsNullOrWhiteSpace(matchingBin), MatchingBinName = matchingBin ?? string.Empty };
        try
        {
            using var memory = new MemoryStream(); input.CopyTo(memory); byte[] bytes = memory.ToArray();
            summary.SizeBytes = bytes.LongLength; summary.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            if (bytes.Length == 0) { summary.Status = "Failure"; result.Findings.Add(Finding("FILE002", "Failure", "File Integrity", "RAW file is empty.", displayName, displayName)); result.Files.Add(summary); return; }
            if (seenHashes.TryGetValue(summary.Sha256, out string? duplicateOf)) result.Findings.Add(Finding("FILE003", "Warning", "File Integrity", "Duplicate file content detected.", $"Duplicate of {duplicateOf}", displayName)); else seenHashes[summary.Sha256] = displayName;

            memory.Position = 0; using var reader = new StreamReader(memory, detectEncodingFromByteOrderMarks: true);
            DateTime? currentDate = null; DateTime? previousTime = null; string? line; int lineNumber = 0;
            DeviceConfiguration? lastDevice = null;
            var devicesById = new Dictionary<int, DeviceConfiguration>();
            bool applanixEvidence = false;
            bool vrsEvidence = false;
            int unresolvedEcRecordCount = 0;
            var ecUsageByDevice = new Dictionary<int, DeviceDataUsage>();
            var positionUsageByDevice = new Dictionary<int, int>();
            var rawReader = new HypackRawReader();
            string activeSurveyLine = string.Empty;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++; if (string.IsNullOrWhiteSpace(line)) continue; summary.RecordCount++;
                ParseIniSetting(line, summary);
                DetectIniPositioning(line, displayName, summary.PositioningEvidence);
                if (line.Contains("APPLANIX", StringComparison.OrdinalIgnoreCase) || line.Contains("POS MV", StringComparison.OrdinalIgnoreCase) || line.Contains("POSMV", StringComparison.OrdinalIgnoreCase)) applanixEvidence = true;
                if (line.Contains("VRS", StringComparison.OrdinalIgnoreCase) || line.Contains("VIRTUAL REFERENCE", StringComparison.OrdinalIgnoreCase) || line.Contains("NTRIP", StringComparison.OrdinalIgnoreCase)) vrsEvidence = true;
                HypackRecord parsedRecord = rawReader.Parse(line, lineNumber);
                string recordType = parsedRecord.RecordType;
                if (recordType.Length == 0) summary.MalformedCount++;
                else summary.RecordTypeCounts[recordType] = summary.RecordTypeCounts.TryGetValue(recordType, out int typeCount) ? typeCount + 1 : 1;
                if (parsedRecord is PositionHypackRecord posRecord)
                {
                    if (posRecord.DeviceId.HasValue) positionUsageByDevice[posRecord.DeviceId.Value] = positionUsageByDevice.GetValueOrDefault(posRecord.DeviceId.Value) + 1;
                    summary.NavigationStartSeconds = !summary.NavigationStartSeconds.HasValue || posRecord.SecondsOfDay < summary.NavigationStartSeconds ? posRecord.SecondsOfDay : summary.NavigationStartSeconds;
                    summary.NavigationEndSeconds = !summary.NavigationEndSeconds.HasValue || posRecord.SecondsOfDay > summary.NavigationEndSeconds ? posRecord.SecondsOfDay : summary.NavigationEndSeconds;
                    if (posRecord.X.HasValue)
                    {
                        summary.MinimumX = !summary.MinimumX.HasValue || posRecord.X.Value < summary.MinimumX.Value ? posRecord.X.Value : summary.MinimumX;
                        summary.MaximumX = !summary.MaximumX.HasValue || posRecord.X.Value > summary.MaximumX.Value ? posRecord.X.Value : summary.MaximumX;
                    }
                    if (posRecord.Y.HasValue)
                    {
                        summary.MinimumY = !summary.MinimumY.HasValue || posRecord.Y.Value < summary.MinimumY.Value ? posRecord.Y.Value : summary.MinimumY;
                        summary.MaximumY = !summary.MaximumY.HasValue || posRecord.Y.Value > summary.MaximumY.Value ? posRecord.Y.Value : summary.MaximumY;
                    }
                }
                if (parsedRecord is EchosounderHypackRecord ecRecord)
                {
                    DeviceConfiguration? sourceDevice = ecRecord.DeviceId.HasValue && devicesById.TryGetValue(ecRecord.DeviceId.Value, out DeviceConfiguration? mappedDevice)
                        ? mappedDevice
                        : null;
                    int usageId = ecRecord.DeviceId ?? -1;
                    if (!ecUsageByDevice.TryGetValue(usageId, out DeviceDataUsage? usage))
                    {
                        usage = new DeviceDataUsage
                        {
                            DeviceId = ecRecord.DeviceId,
                            DeviceName = sourceDevice?.DeviceName ?? $"Device {usageId}",
                            EquipmentType = IsMagnetometerDevice(sourceDevice) ? "Magnetometer" : IsSingleBeamDevice(sourceDevice) ? "Single Beam" : "Unresolved",
                            SourceFile = displayName
                        };
                        ecUsageByDevice[usageId] = usage;
                    }
                    usage.EcRecordCount++;

                    if (IsMagnetometerDevice(sourceDevice))
                    {
                        Add(summary.SuggestedDataTypes, SurveyDataType.Magnetometer);
                        if (ecRecord.Depth1.HasValue) usage.MagnetometerValueCount++;
                        if (ecRecord.Depth2.HasValue) usage.MagnetometerValueCount++;
                    }
                    else if (IsSingleBeamDevice(sourceDevice))
                    {
                        Add(summary.SuggestedDataTypes, SurveyDataType.SingleBeamFrequencyUnknown);
                        summary.EchosounderRecordCount++;
                        // Operational rule: for a fathometer, EC Depth1 is high frequency and Depth2 is low frequency.
                        if (IsUsableDepth(ecRecord.Depth1)) { summary.HighFrequencyDepthCount++; usage.HighFrequencyValueCount++; }
                        if (IsUsableDepth(ecRecord.Depth2)) { summary.LowFrequencyDepthCount++; usage.LowFrequencyValueCount++; }
                        summary.EchosounderStartSeconds = !summary.EchosounderStartSeconds.HasValue || ecRecord.SecondsOfDay < summary.EchosounderStartSeconds ? ecRecord.SecondsOfDay : summary.EchosounderStartSeconds;
                        summary.EchosounderEndSeconds = !summary.EchosounderEndSeconds.HasValue || ecRecord.SecondsOfDay > summary.EchosounderEndSeconds ? ecRecord.SecondsOfDay : summary.EchosounderEndSeconds;
                    }
                    else
                    {
                        unresolvedEcRecordCount++;
                    }
                }
                if (parsedRecord is TideHypackRecord) summary.TideRecordCount++;
                if (parsedRecord is FixHypackRecord) summary.FixRecordCount++;
                if (parsedRecord is PrdHypackRecord) summary.PrdRecordCount++;
                if (parsedRecord is SurveyLineHypackRecord lineRecord && !string.IsNullOrWhiteSpace(lineRecord.LineName)) activeSurveyLine = lineRecord.LineName;
                if (!string.IsNullOrWhiteSpace(activeSurveyLine)) summary.SurveyLineCounts[activeSurveyLine] = summary.SurveyLineCounts.TryGetValue(activeSurveyLine, out int activeCount) ? activeCount + 1 : 1;
                if (IsNavigationRecord(recordType, line)) summary.NavigationCount++;
                DetectDataTypes(line, summary.SuggestedDataTypes);
                GnssSolutionType detectedSolution = parsedRecord is QualityHypackRecord ? GnssSolutionType.Unknown : DetectGnssSolution(line);
                if (parsedRecord is QualityHypackRecord q)
                {
                    summary.GnssQualitySamples.Add(new GnssQualitySample
                    {
                        SourceFile = displayName,
                        SourceLineNumber = lineNumber,
                        SurveyLine = activeSurveyLine,
                        DeviceId = q.DeviceId,
                        SecondsOfDay = q.SecondsOfDay,
                        ModeCode = q.ModeCode,
                        RawQualityFields = q.RawQualityFields,
                        SolutionType = GnssSolutionType.Unknown,
                        Hdop = q.Hdop,
                        SatelliteCount = q.SatelliteCount,
                        CorrectionAgeSeconds = q.CorrectionAgeSeconds
                    });
                }
                if (detectedSolution != GnssSolutionType.Unknown) AddSolution(summary, detectedSolution, displayName, line);
                if (parsedRecord is not SurveyLineHypackRecord) DetectSurveyLine(line, summary.SurveyLineCounts);
                DetectGeodesy(line, displayName, summary.GeodesyEvidence); DetectPositioning(line, displayName, summary.PositioningEvidence);

                if (parsedRecord is DeviceHypackRecord devRecord)
                {
                    lastDevice = UpsertHeaderDevice(devRecord, displayName, summary.DetectedDevices, devicesById);
                }
                else
                {
                    lastDevice = DetectDevice(line, displayName, summary.DetectedDevices) ?? lastDevice;
                    if (lastDevice?.DeviceId is int detectedId) devicesById[detectedId] = lastDevice;
                }

                if (parsedRecord is OffsetHypackRecord offRecord)
                    ApplyHeaderOffset(offRecord, displayName, summary.DetectedDevices, devicesById);
                else
                    DetectOffsets(line, lastDevice, summary.DetectedDevices);

                DateTime? date = TryReadDate(line); if (date.HasValue) { currentDate = date.Value.Date; summary.SurveyDate ??= currentDate; }
                TimeSpan? time = TryReadTime(line);
                if (time.HasValue)
                {
                    summary.TimestampCount++; DateTime timestamp = (currentDate ?? summary.SurveyDate ?? DateTime.Today).Date + time.Value;
                    if (previousTime.HasValue) { double delta = (timestamp - previousTime.Value).TotalSeconds; if (delta < -1) { summary.TimeReversalCount++; result.Findings.Add(Finding("TIME001", "Warning", "Timing", "Timestamp moves backward.", $"Line {lineNumber}: {line}", displayName)); } else if (delta > 30) summary.LargeGapCount++; }
                    previousTime = timestamp; summary.StartTime ??= timestamp; summary.EndTime = timestamp;
                }
            }

            bool headerRtkMode4 = summary.IniSettings.TryGetValue("RTKMode", out string? rtkModeValue) && NormalizeIniValue(rtkModeValue) == "4";
            GnssQualityProfile qualityProfile = applanixEvidence
                ? GnssQualityProfile.ApplanixPosMv
                : vrsEvidence
                    ? GnssQualityProfile.VrsNetwork
                    : headerRtkMode4
                        ? GnssQualityProfile.Proprietary
                        : GnssQualityProfile.GenericNmea;

            foreach (var sample in summary.GnssQualitySamples)
            {
                sample.InterpretationProfile = qualityProfile;
                sample.SolutionType = MapObservedQuality(sample.ModeCode, qualityProfile, headerRtkMode4);
                sample.InterpretationConfidence = sample.SolutionType == GnssSolutionType.Unknown
                    ? DetectionConfidence.Low
                    : sample.ModeCode == 7 && headerRtkMode4
                        ? DetectionConfidence.High
                        : sample.ModeCode == 7
                            ? DetectionConfidence.Medium
                            : DetectionConfidence.High;
                sample.InterpretationNote = sample.ModeCode == 7 && headerRtkMode4
                    ? "QUA solution code 7 interpreted as RTK fixed because the same RAW header records INI RTKMode=4."
                    : sample.ModeCode == 7 && qualityProfile == GnssQualityProfile.ApplanixPosMv
                        ? "QUA solution code 7 interpreted using the Applanix/POS MV profile."
                        : sample.ModeCode == 7 && qualityProfile == GnssQualityProfile.VrsNetwork
                            ? "QUA solution code 7 interpreted using the VRS/network profile."
                            : $"Decoded using the {qualityProfile} profile.";
                if (sample.SolutionType != GnssSolutionType.Unknown)
                    AddSolution(summary, sample.SolutionType, displayName,
                        $"QUA line {sample.SourceLineNumber}, solution code {sample.ModeCode}, HDOP {sample.Hdop}, satellites {sample.SatelliteCount}, correction age {sample.CorrectionAgeSeconds}, profile {qualityProfile}");
            }
            if (summary.GnssQualitySamples.Any(x => x.ModeCode == 7 && x.SolutionType == GnssSolutionType.Unknown))
                result.Findings.Add(Finding("GNSS003", "Warning", "Positioning", "Device-specific QUA solution code 7 was detected but no RTK configuration evidence was found.", "Code 7 was preserved as unresolved. Confirm the receiver/driver or select an interpretation profile.", displayName));
            if (summary.GnssQualitySamples.Any(x => x.ModeCode == 7 && x.SolutionType == GnssSolutionType.Fixed))
                result.Findings.Add(Finding("GNSS004", "Info", "Positioning", "QUA code 7 was interpreted as RTK fixed using device context.", $"Profile: {qualityProfile}. Review the detected receiver/driver before final approval.", displayName));

            int fixedQua = summary.GnssQualitySamples.Count(x => x.SolutionType == GnssSolutionType.Fixed);
            int floatQua = summary.GnssQualitySamples.Count(x => x.SolutionType == GnssSolutionType.Float);
            int unresolvedQua = summary.GnssQualitySamples.Count(x => x.SolutionType == GnssSolutionType.Unknown);
            if (summary.GnssQualitySamples.Count > 0)
                result.Findings.Add(Finding("GNSS000", "Info", "Positioning",
                    $"Decoded {summary.GnssQualitySamples.Count} HYPACK QUA quality records.",
                    $"Fixed: {fixedQua}; Float: {floatQua}; Unresolved: {unresolvedQua}; codes: {string.Join(", ", summary.GnssQualitySamples.GroupBy(x => x.ModeCode).Select(g => $"{g.Key}={g.Count()}"))}; INI RTKMode={summary.IniSettings.GetValueOrDefault("RTKMode", "not recorded")}", displayName));

            var offsetDevices = summary.DetectedDevices.Where(d => d.OffsetConfidence != DetectionConfidence.NotDetected).ToList();
            if (offsetDevices.Count > 0)
                result.Findings.Add(Finding("OFF000", "Info", "Offsets",
                    $"Detected recorded offsets for {offsetDevices.Count} devices.",
                    string.Join(" | ", offsetDevices.Select(d => $"ID {d.DeviceId} {d.DeviceName}: Stbd {d.RecordedStarboard}, Fwd {d.RecordedForward}, Vert {d.RecordedVertical}")), displayName));

            foreach (var usage in ecUsageByDevice.Values)
            {
                if (usage.DeviceId.HasValue) usage.PositionRecordCount = positionUsageByDevice.GetValueOrDefault(usage.DeviceId.Value);
                summary.DeviceDataUsage.Add(usage);
            }
            foreach (var pair in positionUsageByDevice.Where(x => !ecUsageByDevice.ContainsKey(x.Key)))
            {
                devicesById.TryGetValue(pair.Key, out DeviceConfiguration? positionDevice);
                summary.DeviceDataUsage.Add(new DeviceDataUsage
                {
                    DeviceId = pair.Key, DeviceName = positionDevice?.DeviceName ?? $"Device {pair.Key}",
                    EquipmentType = positionDevice?.DeviceType ?? "Positioning", PositionRecordCount = pair.Value, SourceFile = displayName
                });
            }

            DetectSingleBeamFrequencyMode(summary);
            if (unresolvedEcRecordCount > 0)
                result.Findings.Add(Finding("ECDEV001", "Warning", "Survey Type",
                    $"{unresolvedEcRecordCount:N0} EC1/EC2 record(s) could not be assigned to a recognized sensor type.",
                    "The record device ID did not match a recognized magnetometer or single-beam device definition. Confirm the HYPACK DEV header.", displayName));
            if (summary.RecordCount == 0) result.Findings.Add(Finding("FILE004", "Failure", "File Integrity", "RAW file contains no readable records.", displayName, displayName));
            if (summary.TimestampCount == 0) result.Findings.Add(Finding("TIME002", "Warning", "Timing", "No recognizable timestamps were found.", displayName, displayName));
            if (summary.NavigationCount == 0) result.Findings.Add(Finding("NAV001", "Failure", "Navigation", "No recognizable navigation records were found.", displayName, displayName));
            if (summary.RecordTypeCounts.ContainsKey("QUA"))
            {
                int recognizedQua = summary.GnssQualitySamples.Count(x => x.SolutionType != GnssSolutionType.Unknown);
                if (recognizedQua == 0)
                {
                    string examples = string.Join(" | ", summary.GnssQualitySamples.Take(5).Select(x => $"line {x.SourceLineNumber}: {string.Join(" ", x.RawQualityFields)}"));
                    result.Findings.Add(Finding("GNSS001", "Warning", "Positioning", "QUA records were present but their quality mode could not be decoded.", examples.Length > 0 ? examples : displayName, displayName));
                }
            }
            else result.Findings.Add(Finding("GNSS002", "Warning", "Positioning", "No HYPACK QUA records were found; solution-quality detection will use optional NMEA or proprietary messages only.", displayName, displayName));
            if (summary.MalformedCount > 0) result.Findings.Add(Finding("FILE005", "Warning", "File Integrity", $"{summary.MalformedCount} records could not be classified.", displayName, displayName));
            if (summary.LargeGapCount > 0) result.Findings.Add(Finding("TIME003", "Warning", "Timing", $"{summary.LargeGapCount} time gaps greater than 30 seconds were detected.", displayName, displayName));
            if (summary.EchosounderRecordCount > 0)
            {
                result.Findings.Add(Finding("SBES001", "Info", "Survey Type", $"{summary.DetectedSurveyType} data detected.", $"{summary.EchosounderRecordCount:N0} EC2 records; high-frequency values: {summary.HighFrequencyDepthCount:N0}; low-frequency values: {summary.LowFrequencyDepthCount:N0}; device evidence: {string.Join(", ", summary.DetectedDevices.Where(d => d.DeviceType == "Single Beam").Select(d => d.DeviceName))}", displayName));
                if (!summary.HasMatchingBin)
                    result.Findings.Add(Finding("SBES002", "Warning", "File Completeness", "No matching HYPACK BIN file was found for this RAW file.", Path.GetFileNameWithoutExtension(displayName), displayName));
                if (summary.NavigationStartSeconds.HasValue && summary.NavigationEndSeconds.HasValue && summary.EchosounderStartSeconds.HasValue && summary.EchosounderEndSeconds.HasValue)
                {
                    double startDelta = summary.EchosounderStartSeconds.Value - summary.NavigationStartSeconds.Value;
                    double endDelta = summary.NavigationEndSeconds.Value - summary.EchosounderEndSeconds.Value;
                    if (startDelta > 5 || endDelta > 5)
                        result.Findings.Add(Finding("SBES003", "Warning", "Data Completeness", "Echosounder coverage does not span the full navigation period.", $"Soundings begin {startDelta:0.0}s after navigation and end {endDelta:0.0}s before navigation ends.", displayName));
                }
                if (summary.TideRecordCount == 0)
                    result.Findings.Add(Finding("SBES004", "Warning", "Vertical Control", "No TID records were detected for this single-beam file.", "Confirm whether tide or RTK vertical corrections are supplied elsewhere.", displayName));
            }
            bool hasFailure = result.Findings.Any(f => f.FileName == displayName && f.Severity == "Failure"); bool hasWarning = result.Findings.Any(f => f.FileName == displayName && f.Severity == "Warning");
            summary.Status = hasFailure ? "Failure" : hasWarning ? "Warning" : "Pass"; result.Files.Add(summary);
        }
        catch (Exception ex) { summary.Status = "Failure"; result.Findings.Add(Finding("FILE006", "Failure", "File Integrity", "RAW file could not be scanned.", ex.Message, displayName)); result.Files.Add(summary); }
    }


    private static void ParseIniSetting(string line, RawFileSummary summary)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith("INI", StringComparison.OrdinalIgnoreCase)) return;
        string payload = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
        if (payload.Length == 0) return;
        summary.IniHeaderLines.Add(trimmed);

        int equals = payload.IndexOf('=');
        string key;
        string value;
        if (equals >= 0)
        {
            key = payload[..equals].Trim();
            value = payload[(equals + 1)..].Trim();
        }
        else
        {
            int split = payload.IndexOfAny(new[] { ' ', '\t' });
            key = split >= 0 ? payload[..split].Trim() : payload;
            value = split >= 0 ? payload[(split + 1)..].Trim() : string.Empty;
        }
        if (key.Length == 0) return;
        summary.IniSettings[key] = value;
    }

    private static void CompareIniHeaders(ScanResult result)
    {
        if (result.Files.Count == 0) return;
        RawFileSummary baseline = result.Files[0];
        baseline.IsIniBaseline = true;
        result.Findings.Add(Finding(
            "INI000",
            "Info",
            "Header Configuration",
            "The first loaded RAW file is the INI configuration baseline.",
            $"Baseline: {baseline.DisplayName}; INI settings: {baseline.IniSettings.Count}",
            baseline.DisplayName));

        for (int i = 1; i < result.Files.Count; i++)
        {
            RawFileSummary current = result.Files[i];
            var allKeys = baseline.IniSettings.Keys
                .Union(current.IniSettings.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            foreach (string key in allKeys)
            {
                bool baselineHas = baseline.IniSettings.TryGetValue(key, out string? baselineValue);
                bool currentHas = current.IniSettings.TryGetValue(key, out string? currentValue);
                baselineValue ??= string.Empty;
                currentValue ??= string.Empty;

                if (baselineHas && !currentHas)
                {
                    current.IniDifferenceCount++;
                    result.Findings.Add(Finding(
                        "INI001",
                        "Warning",
                        "Header Configuration",
                        $"INI setting '{key}' is missing compared with the first loaded file.",
                        $"Baseline ({baseline.DisplayName}): {key}={baselineValue}; Current ({current.DisplayName}): missing",
                        current.DisplayName));
                }
                else if (!baselineHas && currentHas)
                {
                    current.IniDifferenceCount++;
                    result.Findings.Add(Finding(
                        "INI002",
                        "Warning",
                        "Header Configuration",
                        $"New INI setting '{key}' appears that was not present in the first loaded file.",
                        $"Baseline ({baseline.DisplayName}): missing; Current ({current.DisplayName}): {key}={currentValue}",
                        current.DisplayName));
                }
                else if (!string.Equals(NormalizeIniValue(baselineValue), NormalizeIniValue(currentValue), StringComparison.OrdinalIgnoreCase))
                {
                    current.IniDifferenceCount++;
                    result.Findings.Add(Finding(
                        "INI003",
                        "Warning",
                        "Header Configuration",
                        $"INI setting '{key}' changed from the first loaded file.",
                        $"Baseline ({baseline.DisplayName}): {key}={baselineValue}; Current ({current.DisplayName}): {key}={currentValue}",
                        current.DisplayName));
                }
            }

            if (current.IniDifferenceCount == 0)
            {
                result.Findings.Add(Finding(
                    "INI010",
                    "Info",
                    "Header Configuration",
                    "INI header configuration matches the first loaded file.",
                    $"Compared {current.IniSettings.Count} INI settings with baseline {baseline.DisplayName}.",
                    current.DisplayName));
            }
        }
    }

    private static string NormalizeIniValue(string value)
    {
        return Regex.Replace(value.Trim().Trim('"'), @"\s+", " ");
    }

    private static void FinalizeDetection(ScanResult result)
    {
        result.GeodesyEvidence.AddRange(result.Files.SelectMany(f => f.GeodesyEvidence));
        result.PositioningEvidence.AddRange(result.Files.SelectMany(f => f.PositioningEvidence));
        foreach (var device in result.Files.SelectMany(f => f.DetectedDevices)) MergeDevice(result.Devices, device, result.Findings);
        result.DetectedPositioningMethod = ChoosePositioning(result.PositioningEvidence, out DetectionConfidence pc); result.PositioningConfidence = pc;
        if (result.DetectedPositioningMethod == PositioningMethod.Unknown) result.Findings.Add(Finding("POS001", "Warning", "Positioning", "Positioning method could not be determined automatically.", "Confirm DGPS, VRS, RTK, PPK, or standalone GNSS manually.", string.Empty));
        if (result.GeodesyEvidence.Count == 0) result.Findings.Add(Finding("GEO001", "Warning", "Geodesy", "No explicit geodesy metadata was detected.", "The coordinate system must be confirmed manually.", string.Empty));
        if (result.Devices.Count == 0) result.Findings.Add(Finding("DEV001", "Warning", "Devices", "No device definitions were detected.", "Representative HYPACK configuration records may be required.", string.Empty));
    }

    private static void MergeDevice(List<DeviceConfiguration> devices, DeviceConfiguration incoming, List<QaFinding> findings)
    {
        var existing = incoming.DeviceId.HasValue
            ? devices.FirstOrDefault(d => d.DeviceId == incoming.DeviceId)
            : devices.FirstOrDefault(d => d.DeviceName.Equals(incoming.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (existing == null) { devices.Add(incoming); return; }

        bool existingHasOffset = existing.OffsetConfidence != DetectionConfidence.NotDetected;
        bool incomingHasOffset = incoming.OffsetConfidence != DetectionConfidence.NotDetected;
        bool conflict = existingHasOffset && incomingHasOffset &&
            (Math.Abs(existing.RecordedStarboard - incoming.RecordedStarboard) > 0.0001 ||
             Math.Abs(existing.RecordedForward - incoming.RecordedForward) > 0.0001 ||
             Math.Abs(existing.RecordedVertical - incoming.RecordedVertical) > 0.0001);
        if (conflict)
            findings.Add(Finding("OFF001", "Warning", "Offsets", $"Offset change or conflict detected for {incoming.DeviceName}.",
                $"{existing.SourceFile}: {existing.RecordedStarboard}, {existing.RecordedForward}, {existing.RecordedVertical}; {incoming.SourceFile}: {incoming.RecordedStarboard}, {incoming.RecordedForward}, {incoming.RecordedVertical}", incoming.SourceFile));

        if (!existingHasOffset && incomingHasOffset)
        {
            existing.RecordedStarboard = incoming.RecordedStarboard;
            existing.RecordedForward = incoming.RecordedForward;
            existing.RecordedVertical = incoming.RecordedVertical;
            existing.ApprovedStarboard = incoming.ApprovedStarboard;
            existing.ApprovedForward = incoming.ApprovedForward;
            existing.ApprovedVertical = incoming.ApprovedVertical;
            existing.RecordedYaw = incoming.RecordedYaw;
            existing.RecordedRoll = incoming.RecordedRoll;
            existing.RecordedPitch = incoming.RecordedPitch;
            existing.RecordedLatency = incoming.RecordedLatency;
            existing.ApprovedYaw = incoming.ApprovedYaw;
            existing.ApprovedRoll = incoming.ApprovedRoll;
            existing.ApprovedPitch = incoming.ApprovedPitch;
            existing.ApprovedLatency = incoming.ApprovedLatency;
            existing.RawOffsetHeader = incoming.RawOffsetHeader;
            existing.OffsetConfidence = incoming.OffsetConfidence;
        }
        if (existing.Manufacturer.Length == 0) existing.Manufacturer = incoming.Manufacturer;
        if (existing.Model.Length == 0) existing.Model = incoming.Model;
        if (existing.SerialNumber.Length == 0) existing.SerialNumber = incoming.SerialNumber;
        if (existing.DriverPath.Length == 0) existing.DriverPath = incoming.DriverPath;
        if (existing.DriverVersion.Length == 0) existing.DriverVersion = incoming.DriverVersion;
        existing.InterfaceType ??= incoming.InterfaceType;
    }

    private static GnssSolutionType MapObservedQuality(int? mode, GnssQualityProfile profile, bool headerRtkMode4)
    {
        if (!mode.HasValue) return GnssSolutionType.Unknown;
        if (mode.Value == 7 && headerRtkMode4) return GnssSolutionType.Fixed;
        return HypackRawReader.MapQuality(mode, profile);
    }

    private static PositioningMethod ChoosePositioning(List<DetectionEvidence> evidence, out DetectionConfidence confidence)
    {
        var values = evidence.Select(e => e.Value).ToList(); confidence = DetectionConfidence.NotDetected;
        if (values.Any(v => v.Equals("VRS", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.High; return PositioningMethod.Vrs; }
        if (values.Any(v => v.Equals("Network RTK", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.High; return PositioningMethod.NetworkRtk; }
        if (values.Any(v => v.Equals("Base-Rover RTK", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.High; return PositioningMethod.BaseRoverRtk; }
        if (values.Any(v => v.Equals("PPK", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.High; return PositioningMethod.Ppk; }
        if (values.Any(v => v.Equals("DGPS", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.Medium; return PositioningMethod.DifferentialGps; }
        if (values.Any(v => v.Equals("Standalone GNSS", StringComparison.OrdinalIgnoreCase))) { confidence = DetectionConfidence.Medium; return PositioningMethod.StandaloneGnss; }
        return PositioningMethod.Unknown;
    }

    private static void AddSolution(RawFileSummary summary, GnssSolutionType solution, string file, string evidence)
    {
        summary.GnssSolutionCounts[solution] = summary.GnssSolutionCounts.TryGetValue(solution, out int count) ? count + 1 : 1;
        if (solution == GnssSolutionType.Fixed || solution == GnssSolutionType.Float) AddPositioningEvidence(summary.PositioningEvidence, file, "Network RTK", DetectionConfidence.High, evidence);
        else if (solution == GnssSolutionType.Differential) AddPositioningEvidence(summary.PositioningEvidence, file, "DGPS", DetectionConfidence.High, evidence);
        else if (solution == GnssSolutionType.Autonomous) AddPositioningEvidence(summary.PositioningEvidence, file, "Standalone GNSS", DetectionConfidence.Medium, evidence);
    }

    private static void DetectGeodesy(string line, string file, List<DetectionEvidence> evidence)
    {
        void Add(string category, string value, DetectionConfidence confidence)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!evidence.Any(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && e.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
                evidence.Add(new DetectionEvidence { Category = category, Value = value.Trim(), Evidence = line.Trim(), SourceFile = file, Confidence = confidence });
        }

        string trimmed = line.Trim();
        if (trimmed.StartsWith("INI", StringComparison.OrdinalIgnoreCase))
        {
            string payload = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
            int equals = payload.IndexOf('=');
            if (equals > 0)
            {
                string key = payload[..equals].Trim();
                string value = payload[(equals + 1)..].Trim();
                switch (key.ToUpperInvariant())
                {
                    case "GRID":
                        Add("Grid", value, DetectionConfidence.High);
                        if (value.Contains("NAD-83", StringComparison.OrdinalIgnoreCase) || value.Contains("NAD83", StringComparison.OrdinalIgnoreCase)) Add("Horizontal Datum", "NAD83", DetectionConfidence.High);
                        else if (value.Contains("NAD-27", StringComparison.OrdinalIgnoreCase) || value.Contains("NAD27", StringComparison.OrdinalIgnoreCase)) Add("Horizontal Datum", "NAD27", DetectionConfidence.High);
                        else if (value.Contains("WGS", StringComparison.OrdinalIgnoreCase)) Add("Horizontal Datum", "WGS 84", DetectionConfidence.High);
                        break;
                    case "PROJECTION": Add("Projection", ExpandProjection(value), DetectionConfidence.High); break;
                    case "ZONENAME": Add("Zone", value, DetectionConfidence.High); break;
                    case "ZONEID": Add("Zone ID", value, DetectionConfidence.High); break;
                    case "UNITSNAME": Add("Units", NormalizeUnitName(value), DetectionConfidence.High); break;
                    case "UNIT": Add("Unit Factor", value, DetectionConfidence.High); break;
                    case "VERTUNIT": Add("Vertical Unit Factor", value, DetectionConfidence.High); break;
                    case "ELLIPSOID": Add("Ellipsoid", value, DetectionConfidence.High); break;
                    case "GEOID": Add("Geoid", value, DetectionConfidence.High); break;
                    case "VDATUM": Add("Vertical Datum", string.IsNullOrWhiteSpace(value) ? "Not recorded" : value, string.IsNullOrWhiteSpace(value) ? DetectionConfidence.Low : DetectionConfidence.High); break;
                    case "VSURFACE": Add("Vertical Surface", string.IsNullOrWhiteSpace(value) ? "Not recorded" : value, string.IsNullOrWhiteSpace(value) ? DetectionConfidence.Low : DetectionConfidence.High); break;
                    case "CENTRALMERIDIAN": Add("Central Meridian", value, DetectionConfidence.High); break;
                    case "REFLATITUDE": Add("Reference Latitude", value, DetectionConfidence.High); break;
                    case "FALSEEASTING": Add("False Easting", value, DetectionConfidence.High); break;
                    case "FALSENORTHING": Add("False Northing", value, DetectionConfidence.High); break;
                    case "SCALEFACTOR": Add("Scale Factor", value, DetectionConfidence.High); break;
                }
            }
        }

        Match epsg = EpsgRegex.Match(line); if (epsg.Success) Add("EPSG", "EPSG:" + epsg.Groups["code"].Value, DetectionConfidence.High);
        Match utm = UtmRegex.Match(line); if (utm.Success) Add("Projection", "UTM Zone " + utm.Groups["zone"].Value + utm.Groups["hemi"].Value.ToUpperInvariant(), DetectionConfidence.High);
    }

    private static string ExpandProjection(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "LCC" => "Lambert Conformal Conic",
            "TM" => "Transverse Mercator",
            "UTM" => "Universal Transverse Mercator",
            "OM" => "Oblique Mercator",
            _ => value.Trim()
        };
    }

    private static string NormalizeUnitName(string value)
    {
        if (value.Contains("US Survey Foot", StringComparison.OrdinalIgnoreCase) || value.Contains("US Foot", StringComparison.OrdinalIgnoreCase)) return "U.S. survey feet";
        if (value.Contains("International Foot", StringComparison.OrdinalIgnoreCase)) return "International feet";
        if (value.Contains("Meter", StringComparison.OrdinalIgnoreCase) || value.Contains("Metre", StringComparison.OrdinalIgnoreCase)) return "Meters";
        return value.Trim();
    }

    private static void DetectPositioning(string line, string file, List<DetectionEvidence> evidence)
    {
        void Add(string value, DetectionConfidence confidence) { if (!evidence.Any(e => e.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) evidence.Add(new DetectionEvidence { Category = "Positioning", Value = value, Evidence = line.Trim(), SourceFile = file, Confidence = confidence }); }
        if (line.Contains("VRS", StringComparison.OrdinalIgnoreCase) || line.Contains("VIRTUAL REFERENCE", StringComparison.OrdinalIgnoreCase)) Add("VRS", DetectionConfidence.High);
        if (line.Contains("NETWORK RTK", StringComparison.OrdinalIgnoreCase) || line.Contains("NTRIP", StringComparison.OrdinalIgnoreCase)) Add("Network RTK", DetectionConfidence.High);
        if (line.Contains("BASE ROVER", StringComparison.OrdinalIgnoreCase) || line.Contains("LOCAL BASE", StringComparison.OrdinalIgnoreCase)) Add("Base-Rover RTK", DetectionConfidence.High);
        if (line.Contains("PPK", StringComparison.OrdinalIgnoreCase) || line.Contains("POST PROCESSED KINEMATIC", StringComparison.OrdinalIgnoreCase)) Add("PPK", DetectionConfidence.High);
        if (line.Contains("DGPS", StringComparison.OrdinalIgnoreCase) || line.Contains("DIFFERENTIAL GPS", StringComparison.OrdinalIgnoreCase)) Add("DGPS", DetectionConfidence.Medium);
        if (line.Contains("AUTONOMOUS", StringComparison.OrdinalIgnoreCase) || line.Contains("STANDALONE", StringComparison.OrdinalIgnoreCase)) Add("Standalone GNSS", DetectionConfidence.Medium);
        if (line.Contains("RTK FIX", StringComparison.OrdinalIgnoreCase) || line.Contains("RTK FLOAT", StringComparison.OrdinalIgnoreCase)) Add("Network RTK", DetectionConfidence.Medium);
    }

    private static DeviceConfiguration? DetectDevice(string line, string file, List<DeviceConfiguration> devices)
    {
        string upper = line.ToUpperInvariant(); string type = string.Empty; string manufacturer = string.Empty; string model = string.Empty;
        if (upper.Contains("TRIMBLE")) manufacturer = "Trimble"; else if (upper.Contains("LEICA")) manufacturer = "Leica"; else if (upper.Contains("NOVATEL")) manufacturer = "NovAtel"; else if (upper.Contains("APPLANIX")) manufacturer = "Applanix"; else if (upper.Contains("NORBIT")) manufacturer = "NORBIT"; else if (upper.Contains("EDGETECH")) manufacturer = "EdgeTech"; else if (upper.Contains("KLEIN")) manufacturer = "Klein"; else if (upper.Contains("ECHOTRAC")) manufacturer = "Teledyne Odom"; else if (upper.Contains("SEABAT")) manufacturer = "Teledyne Reson";
        if (upper.Contains("GPS") || upper.Contains("GNSS") || upper.Contains("GGA")) type = "Positioning";
        else if (upper.Contains("MULTIBEAM") || upper.Contains("MBES") || upper.Contains("SEABAT")) type = "Multibeam";
        else if (upper.Contains("ECHOSOUNDER") || upper.Contains("SBES") || upper.Contains("ECHOTRAC")) type = "Single Beam";
        else if (upper.Contains("SIDE SCAN") || upper.Contains("SIDESCAN")) type = "Side Scan";
        else if (upper.Contains("MAGNETOMETER") || upper.Contains("G-882") || upper.Contains("G882")) type = "Magnetometer";
        else if (upper.Contains("MOTION") || upper.Contains("MRU") || upper.Contains("IMU")) type = "Motion";
        else if (upper.Contains("HEADING") || upper.Contains("GYRO")) type = "Heading";
        else if (upper.Contains("SVP") || upper.Contains("SOUND VELOCITY")) type = "Sound Velocity";
        if (type.Length == 0 && manufacturer.Length == 0) return null;
        foreach (string candidate in new[] { "R12", "R12I", "SPS", "POS MV", "POSMV", "G-882", "G882", "EM2040", "ECHOTRAC E20", "ECHOTRAC", "SEABAT", "NORBIT" }) if (upper.Contains(candidate)) { model = candidate; break; }
        Match serial = SerialRegex.Match(line); string serialValue = serial.Success ? serial.Groups["value"].Value : string.Empty;
        string name = string.Join(" ", new[] { manufacturer, model, type }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim(); if (name.Length == 0) name = type;
        var existing = devices.FirstOrDefault(d => d.DeviceName.Equals(name, StringComparison.OrdinalIgnoreCase)); if (existing != null) return existing;
        var device = new DeviceConfiguration { DeviceName = name, DeviceType = type, Manufacturer = manufacturer, Model = model, SerialNumber = serialValue, SourceFile = file, IdentityConfidence = manufacturer.Length > 0 && model.Length > 0 ? DetectionConfidence.High : DetectionConfidence.Medium };
        devices.Add(device); return device;
    }

    private static DeviceConfiguration UpsertHeaderDevice(DeviceHypackRecord record, string file, List<DeviceConfiguration> devices, Dictionary<int, DeviceConfiguration> byId)
    {
        int id = record.DeviceId ?? -1;
        if (id >= 0 && byId.TryGetValue(id, out DeviceConfiguration? existing))
        {
            if (existing.RawDeviceHeader.Length == 0) existing.RawDeviceHeader = record.RawText;
            return existing;
        }

        string description = record.Description.Trim();
        string name = description.Length > 0 ? description : $"Device {id}";
        var device = new DeviceConfiguration
        {
            DeviceId = record.DeviceId,
            DeviceName = name,
            InterfaceType = record.InterfaceType,
            DriverPath = record.DriverPath,
            DriverVersion = record.DriverVersion,
            DeviceType = InferDeviceType(description),
            Manufacturer = InferManufacturer(description),
            Model = InferModel(description),
            SourceFile = file,
            IdentityConfidence = description.Length > 0 ? DetectionConfidence.High : DetectionConfidence.Medium,
            RawDeviceHeader = record.RawText
        };
        devices.Add(device);
        if (id >= 0) byId[id] = device;
        return device;
    }

    private static void ApplyHeaderOffset(OffsetHypackRecord record, string file, List<DeviceConfiguration> devices, Dictionary<int, DeviceConfiguration> byId)
    {
        int id = record.DeviceId ?? -1;
        if (!byId.TryGetValue(id, out DeviceConfiguration? device))
        {
            device = new DeviceConfiguration
            {
                DeviceId = record.DeviceId,
                DeviceName = id >= 0 ? $"Device {id}" : "Unassigned device",
                DeviceType = "Unknown",
                SourceFile = file,
                IdentityConfidence = DetectionConfidence.Low
            };
            devices.Add(device);
            if (id >= 0) byId[id] = device;
        }

        if (record.Starboard.HasValue && record.Forward.HasValue && record.Vertical.HasValue)
        {
            device.RecordedStarboard = record.Starboard.Value;
            device.RecordedForward = record.Forward.Value;
            device.RecordedVertical = record.Vertical.Value;
            device.ApprovedStarboard = record.Starboard.Value;
            device.ApprovedForward = record.Forward.Value;
            device.ApprovedVertical = record.Vertical.Value;
            device.RecordedYaw = record.Yaw ?? 0;
            device.RecordedRoll = record.Roll ?? 0;
            device.RecordedPitch = record.Pitch ?? 0;
            device.RecordedLatency = record.Latency ?? 0;
            device.ApprovedYaw = device.RecordedYaw;
            device.ApprovedRoll = device.RecordedRoll;
            device.ApprovedPitch = device.RecordedPitch;
            device.ApprovedLatency = device.RecordedLatency;
            device.RawOffsetHeader = record.RawText;
            device.OffsetConfidence = DetectionConfidence.High;
        }
    }

    private static string InferManufacturer(string text)
    {
        foreach (string value in new[] { "Trimble", "Leica", "NovAtel", "Applanix", "Hemisphere", "Topcon", "Septentrio", "Teledyne", "Odom", "NORBIT", "EdgeTech", "Klein", "Furuno", "Ashtech" })
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) return value;
        return string.Empty;
    }

    private static string InferModel(string text)
    {
        foreach (string value in new[] { "POS MV", "POSMV", "R12i", "R12", "SPS", "DL-V3", "DLV3", "Z-Extreme", "Zxtreme", "G-882", "G882", "EM2040", "ECHOTRAC E20", "ECHOTRAC" })
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) return value;
        return string.Empty;
    }

    private static string InferDeviceType(string text)
    {
        string upper = text.ToUpperInvariant();
        if (upper.Contains("GPS") || upper.Contains("GNSS") || upper.Contains("RTK") || upper.Contains("APPLANIX") || upper.Contains("POS MV")) return "Positioning";
        if (upper.Contains("MULTIBEAM") || upper.Contains("MBES")) return "Multibeam";
        if (upper.Contains("ECHOSOUNDER") || upper.Contains("SBES") || upper.Contains("BATHY") || upper.Contains("ECHOTRAC")) return "Single Beam";
        if (upper.Contains("MOTION") || upper.Contains("MRU") || upper.Contains("IMU")) return "Motion";
        if (upper.Contains("GYRO") || upper.Contains("HEADING")) return "Heading";
        if (upper.Contains("SIDE SCAN") || upper.Contains("SIDESCAN")) return "Side Scan";
        if (upper.Contains("MAG")) return "Magnetometer";
        return "Unknown";
    }

    private static void DetectIniPositioning(string line, string file, List<DetectionEvidence> evidence)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith("INI", StringComparison.OrdinalIgnoreCase)) return;
        int equals = trimmed.IndexOf('=');
        if (equals < 0) return;
        string key = trimmed[3..equals].Trim();
        string value = trimmed[(equals + 1)..].Trim();
        if (!key.Equals("RTKMode", StringComparison.OrdinalIgnoreCase)) return;
        string method = value switch
        {
            "4" => "RTK configured",
            "5" => "RTK float mode configured",
            "7" => "Device-specific RTK mode configured",
            _ => $"RTKMode={value}"
        };
        if (!evidence.Any(e => e.Category == "Positioning Configuration" && e.Value.Equals(method, StringComparison.OrdinalIgnoreCase)))
            evidence.Add(new DetectionEvidence { Category = "Positioning Configuration", Value = method, Evidence = trimmed, SourceFile = file, Confidence = DetectionConfidence.High });
    }

    private static void DetectOffsets(string line, DeviceConfiguration? lastDevice, List<DeviceConfiguration> devices)
    {
        Match match = OffsetRegex.Match(line); if (!match.Success) return;
        DeviceConfiguration device = lastDevice ?? devices.LastOrDefault() ?? new DeviceConfiguration { DeviceName = "Unassigned detected device", DeviceType = "Unknown", IdentityConfidence = DetectionConfidence.Low };
        if (!devices.Contains(device)) devices.Add(device);
        device.RecordedStarboard = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture); device.RecordedForward = double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture); device.RecordedVertical = double.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture);
        device.ApprovedStarboard = device.RecordedStarboard; device.ApprovedForward = device.RecordedForward; device.ApprovedVertical = device.RecordedVertical; device.OffsetConfidence = DetectionConfidence.High;
    }

    private static string ReadRecordType(string line) { string trimmed = line.TrimStart(); if (trimmed.Length == 0) return string.Empty; int stop = trimmed.IndexOfAny(new[] { ' ', '\t', ',' }); string token = stop >= 0 ? trimmed[..stop] : trimmed; return token.Length <= 12 ? token : string.Empty; }
    private static string NormalizeBaseName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_0001", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
    }

    private static string? FindSiblingBin(string rawPath)
    {
        if (!rawPath.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) return null;
        string directory = Path.GetDirectoryName(rawPath) ?? string.Empty;
        string baseName = NormalizeBaseName(rawPath);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.bin").FirstOrDefault(p => NormalizeBaseName(p).Equals(baseName, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static bool IsNavigationRecord(string type, string line) => type.Equals("POS", StringComparison.OrdinalIgnoreCase) || type.Equals("GPS", StringComparison.OrdinalIgnoreCase) || type.Equals("RAW", StringComparison.OrdinalIgnoreCase) || line.Contains("$GPGGA", StringComparison.OrdinalIgnoreCase) || line.Contains("$GNGGA", StringComparison.OrdinalIgnoreCase) || line.Contains("EASTING", StringComparison.OrdinalIgnoreCase) || line.Contains("NORTHING", StringComparison.OrdinalIgnoreCase);
    private static void DetectDataTypes(string line, List<SurveyDataType> types) { AddIf(line, types, SurveyDataType.Multibeam, "MULTIBEAM", "MBES", "SEABAT", "EM2040", "NORBIT", "RMB "); AddIf(line, types, SurveyDataType.SideScan, "SIDE SCAN", "SIDESCAN", "EDGETECH", "KLEIN", "STARFISH", "RSS "); AddIf(line, types, SurveyDataType.SubBottom, "SUBBOTTOM", "SUB-BOTTOM", "CHIRP"); AddIf(line, types, SurveyDataType.Adcp, "ADCP"); AddIf(line, types, SurveyDataType.SoundVelocity, "SVP", "SOUND VELOCITY", "SVC"); AddIf(line, types, SurveyDataType.TideOrWaterLevel, "TIDE", "WATER LEVEL"); AddIf(line, types, SurveyDataType.TowfishPositioning, "TOWFISH", "LAYBACK", "CABLE OUT"); if (line.Contains("$GPGGA", StringComparison.OrdinalIgnoreCase) || line.Contains("$GNGGA", StringComparison.OrdinalIgnoreCase)) Add(types, SurveyDataType.NavigationOnly); }
    private static void AddIf(string line, List<SurveyDataType> types, SurveyDataType type, params string[] terms) { if (terms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase))) Add(types, type); }
    private static void Add(List<SurveyDataType> types, SurveyDataType type) { if (!types.Contains(type)) types.Add(type); }
    private static void DetectSurveyLine(string line, Dictionary<string, int> counts) { Match match = LineRegex.Match(line); if (!match.Success) return; string name = match.Groups["name"].Value.Trim(); if (name.Length == 0 || name.Length > 40) return; counts[name] = counts.TryGetValue(name, out int value) ? value + 1 : 1; }
    private static GnssSolutionType DetectGnssSolution(string line)
    {
        // NMEA GGA field 6 is the GPS quality indicator. HYPACK RAW records may
        // prepend interface/time fields, so locate the NMEA sentence first.
        int ggaStart = FindGgaStart(line);
        if (ggaStart >= 0)
        {
            string sentence = line[ggaStart..];
            int checksum = sentence.IndexOf('*');
            if (checksum >= 0) sentence = sentence[..checksum];
            string[] fields = sentence.Split(',');
            if (fields.Length > 6 && int.TryParse(fields[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality))
            {
                return quality switch
                {
                    0 => GnssSolutionType.Invalid,
                    1 => GnssSolutionType.Autonomous,
                    2 => GnssSolutionType.Differential,
                    4 => GnssSolutionType.Fixed,
                    5 => GnssSolutionType.Float,
                    6 => GnssSolutionType.DeadReckoning,
                    7 => GnssSolutionType.Invalid,
                    8 => GnssSolutionType.Invalid,
                    _ => GnssSolutionType.Unknown
                };
            }
        }

        // Fallbacks for decoded/proprietary receiver records. Use precise terms
        // to avoid counting unrelated occurrences of words such as "fixed".
        if (Regex.IsMatch(line, @"\b(?:RTK[ _-]?FIX(?:ED)?|INTEGER[ _-]?FIX(?:ED)?|SOL(?:UTION)?[=: ]+FIXED)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.Fixed;
        if (Regex.IsMatch(line, @"\b(?:RTK[ _-]?FLOAT|FLOAT[ _-]?RTK|SOL(?:UTION)?[=: ]+FLOAT)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.Float;
        if (Regex.IsMatch(line, @"\b(?:DGPS|DGNSS|DIFFERENTIAL[ _-]?(?:GPS|GNSS)|SOL(?:UTION)?[=: ]+DIFFERENTIAL)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.Differential;
        if (Regex.IsMatch(line, @"\b(?:AUTONOMOUS|STANDALONE|SINGLE[ _-]?POINT|SOL(?:UTION)?[=: ]+SINGLE)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.Autonomous;
        if (Regex.IsMatch(line, @"\b(?:DEAD[ _-]?RECKONING|DR[ _-]?SOLUTION)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.DeadReckoning;
        if (Regex.IsMatch(line, @"\b(?:NO[ _-]?SOLUTION|NO[ _-]?FIX)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.NoSolution;
        if (Regex.IsMatch(line, @"\b(?:INVALID[ _-]?(?:FIX|SOLUTION)|SOLUTION[=: ]+INVALID)\b", RegexOptions.IgnoreCase)) return GnssSolutionType.Invalid;
        return GnssSolutionType.Unknown;
    }

    private static int FindGgaStart(string line)
    {
        for (int i = 0; i <= line.Length - 6; i++)
        {
            if (line[i] != '$') continue;
            if (i + 6 <= line.Length &&
                char.IsLetter(line[i + 1]) && char.IsLetter(line[i + 2]) &&
                line[i + 3] == 'G' && line[i + 4] == 'G' && line[i + 5] == 'A') return i;
        }
        return -1;
    }

    private static void AddPositioningEvidence(List<DetectionEvidence> evidence, string file, string value, DetectionConfidence confidence, string sourceLine)
    {
        if (evidence.Any(e => e.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        evidence.Add(new DetectionEvidence
        {
            Category = "Positioning",
            Value = value,
            Evidence = sourceLine.Trim(),
            SourceFile = file,
            Confidence = confidence
        });
    }
    private static DateTime? TryReadDate(string line) { Match match = DateRegex.Match(line); if (!match.Success) return null; int month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture); int day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture); int year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture); if (year < 100) year += year >= 80 ? 1900 : 2000; try { return new DateTime(year, month, day); } catch { return null; } }
    private static TimeSpan? TryReadTime(string line) { Match match = TimeRegex.Match(line); if (!match.Success) return null; int h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture); int m = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture); double s = double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture); return TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s); }

    private static bool IsMagnetometerDevice(DeviceConfiguration? device)
    {
        if (device == null) return false;
        return device.DeviceType.Contains("Magnet", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceName.Contains("Magnet", StringComparison.OrdinalIgnoreCase) ||
               device.Model.Contains("G-882", StringComparison.OrdinalIgnoreCase) ||
               device.Model.Contains("G882", StringComparison.OrdinalIgnoreCase) ||
               device.DriverPath.Contains("Magnet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSingleBeamDevice(DeviceConfiguration? device)
    {
        if (device == null) return false;
        return device.DeviceType.Contains("Single Beam", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceType.Contains("Echosounder", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceName.Contains("Echosounder", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceName.Contains("EchoTrac", StringComparison.OrdinalIgnoreCase) ||
               device.DriverPath.Contains("Echosounder", StringComparison.OrdinalIgnoreCase) ||
               device.DriverPath.Contains("EchoTrac", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableDepth(double? value)
    {
        return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value > 0.001;
    }

    private static void DetectSingleBeamFrequencyMode(RawFileSummary summary)
    {
        if (summary.EchosounderRecordCount <= 0) return;
        double minimumPresence = Math.Max(1, summary.EchosounderRecordCount * 0.05);
        bool highPresent = summary.HighFrequencyDepthCount >= minimumPresence;
        bool lowPresent = summary.LowFrequencyDepthCount >= minimumPresence;
        SurveyDataType type;
        if (highPresent && lowPresent)
        {
            type = SurveyDataType.SingleBeamDualFrequency;
            summary.DetectedSurveyType = "Single Beam / Dual Frequency";
        }
        else if (highPresent)
        {
            type = SurveyDataType.SingleBeamHighFrequency;
            summary.DetectedSurveyType = "Single Beam / High Frequency";
        }
        else if (lowPresent)
        {
            type = SurveyDataType.SingleBeamLowFrequency;
            summary.DetectedSurveyType = "Single Beam / Low Frequency";
        }
        else
        {
            type = SurveyDataType.SingleBeamFrequencyUnknown;
            summary.DetectedSurveyType = "Single Beam / Frequency Unknown";
        }
        summary.SuggestedDataTypes.RemoveAll(IsSingleBeamType);
        summary.SuggestedDataTypes.Add(type);
    }

    private static bool IsSingleBeamType(SurveyDataType type)
    {
        return type == SurveyDataType.SingleBeamFrequencyUnknown ||
               type == SurveyDataType.SingleBeamHighFrequency ||
               type == SurveyDataType.SingleBeamLowFrequency ||
               type == SurveyDataType.SingleBeamDualFrequency;
    }

    private static QaFinding Finding(string id, string severity, string category, string description, string evidence, string fileName) => new() { RuleId = id, Severity = severity, Category = category, Description = description, Evidence = evidence, FileName = fileName };
}

public sealed class ScanResult
{
    public List<RawFileSummary> Files { get; } = new();
    public List<QaFinding> Findings { get; } = new();
    public List<DeviceConfiguration> Devices { get; } = new();
    public List<DetectionEvidence> GeodesyEvidence { get; } = new();
    public List<DetectionEvidence> PositioningEvidence { get; } = new();
    public PositioningMethod DetectedPositioningMethod { get; set; } = PositioningMethod.Unknown;
    public DetectionConfidence PositioningConfidence { get; set; } = DetectionConfidence.NotDetected;


}
