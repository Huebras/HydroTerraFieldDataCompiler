using System.IO.Compression;
using System.Security;
using System.Text;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class WordReportGenerator
{
    public static string Generate(FieldDataProject project, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", ContentTypes());
        Write(archive, "_rels/.rels", RootRels());
        Write(archive, "word/_rels/document.xml.rels", DocumentRels());
        Write(archive, "word/styles.xml", Styles());
        Write(archive, "word/settings.xml", Settings());
        Write(archive, "docProps/core.xml", CoreProperties(project));
        Write(archive, "docProps/app.xml", AppProperties());
        Write(archive, "word/document.xml", Document(project));
        return outputPath;
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Document(FieldDataProject p)
    {
        var b = new StringBuilder();
        b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        b.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        b.Append(Paragraph("HYDROTERRA TECHNOLOGIES", "TitleBrand"));
        b.Append(Paragraph("Field Data QA and Package Report", "Title"));
        b.Append(Paragraph(string.IsNullOrWhiteSpace(p.ProjectName) ? "Unnamed Project" : p.ProjectName, "Subtitle"));
        b.Append(Table(new[] { "Field", "Value" }, new[]
        {
            "Project number", p.ProjectNumber,
            "Client", p.Client,
            "Location", p.Location,
            "Vessel", p.Vessel,
            "Survey dates", DateRange(p),
            "Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        }, 2));
        b.Append(PageBreak());

        Section(b, "1. Executive Summary");
        ProjectHealthSummary health = p.ProjectHealth.Items.Count > 0 ? p.ProjectHealth : ProjectHealthEvaluator.Evaluate(p);
        b.Append(Callout($"Project Health: {health.OverallStatus} — {health.Score}%", health.OverallStatus == HealthStatus.Pass ? "Pass" : health.OverallStatus == HealthStatus.Failure ? "Fail" : "Warn"));
        b.Append(Paragraph($"The application reviewed {p.RawFileSummaries.Count} HYPACK RAW file(s), {p.HypackLogSummaries.Count} HYPACK LOG file(s), {p.LineCoverageResults.Count} merged planned line(s), {p.Devices.Count} device(s), and {p.SupportingFiles.Count} supporting file(s).", "Normal"));
        b.Append(Paragraph("The report distinguishes recorded source values from operator-approved values. Original field files remain unchanged; edited RAW data, when created, are separate copies.", "Normal"));

        Section(b, "2. Project Information");
        b.Append(Table(new[] { "Field", "Value" }, new[]
        {
            "Project name", p.ProjectName,
            "Project number", p.ProjectNumber,
            "Client", p.Client,
            "Location", p.Location,
            "Vessel", p.Vessel,
            "Field crew", p.FieldCrew,
            "Survey start", Fmt(p.SurveyStartDate),
            "Survey end", Fmt(p.SurveyEndDate),
            "Notes", p.Notes
        }, 2));

        Section(b, "3. Survey Types and Positioning");
        b.Append(Table(new[] { "Item", "Result" }, new[]
        {
            "Survey type(s)", p.DataTypes.Count == 0 ? "Not confirmed" : string.Join(", ", p.DataTypes.Select(Friendly)),
            "Detected positioning method", Friendly(p.DetectedPositioningMethod.ToString()),
            "Detection confidence", p.PositioningConfidence.ToString(),
            "Approved positioning method(s)", p.PositioningMethods.Count == 0 ? "Not confirmed" : string.Join(", ", p.PositioningMethods.Select(x => Friendly(x.ToString())))
        }, 2));
        if (p.PositioningEvidence.Count > 0) b.Append(Bullets(p.PositioningEvidence.Take(12)));

        Section(b, "4. Geodesy");
        GeodesyConfiguration g = p.Geodesy;
        b.Append(Table(new[] { "Parameter", "Recorded", "Approved" }, new[]
        {
            "Horizontal datum", g.RecordedHorizontalDatum, g.ApprovedHorizontalDatum,
            "Grid", g.RecordedGrid, g.ApprovedGrid,
            "Projection", g.RecordedProjection, g.ApprovedProjection,
            "Zone", g.RecordedZone, g.ApprovedZone,
            "Zone ID", g.RecordedZoneId, g.ApprovedZoneId,
            "Units", g.RecordedUnits, g.ApprovedUnits,
            "Ellipsoid", g.RecordedEllipsoid, g.ApprovedEllipsoid,
            "Vertical datum", g.VerticalDatum, g.VerticalDatum,
            "Geoid", g.GeoidModel, g.GeoidModel
        }, 3));
        b.Append(Paragraph($"Validation status: {g.ValidationStatus}. Coordinate range: {g.CoordinateRangeSummary}", "Normal"));
        if (g.ValidationMessages.Count > 0) b.Append(Bullets(g.ValidationMessages));

        Section(b, "5. Devices and Offsets");
        var deviceRows = new List<string>();
        foreach (DeviceConfiguration d in p.Devices)
        {
            deviceRows.AddRange(new[]
            {
                d.DeviceId?.ToString() ?? "—", d.DeviceName, d.DeviceType,
                $"{d.RecordedStarboard:0.###}, {d.RecordedForward:0.###}, {d.RecordedVertical:0.###}",
                $"{d.ApprovedStarboard:0.###}, {d.ApprovedForward:0.###}, {d.ApprovedVertical:0.###}",
                string.IsNullOrWhiteSpace(d.CorrectionReason) ? "—" : d.CorrectionReason
            });
        }
        b.Append(Table(new[] { "ID", "Device", "Type", "Recorded S/F/V", "Approved S/F/V", "Reason" }, deviceRows.ToArray(), 6));

        Section(b, "6. File Inventory and Configuration Integrity");
        var fileRows = new List<string>();
        foreach (RawFileSummary f in p.RawFileSummaries)
        {
            fileRows.AddRange(new[] { f.DisplayName, f.DetectedSurveyType, f.RecordCount.ToString("N0"), f.NavigationCount.ToString("N0"), f.EchosounderRecordCount.ToString("N0"), f.Status, f.IsIniBaseline ? "Baseline" : f.IniDifferenceCount == 0 ? "Matches" : $"{f.IniDifferenceCount} difference(s)" });
        }
        b.Append(Table(new[] { "File", "Type", "Records", "Nav", "EC2", "Status", "Header" }, fileRows.ToArray(), 7));

        if (p.HypackLogSummaries.Count > 0)
        {
            b.Append(Paragraph("HYPACK LOG reconciliation", "Heading1"));
            var logRows = new List<string>();
            foreach (HypackLogSummary log in p.HypackLogSummaries)
                logRows.AddRange(new[] { log.DisplayName, log.ReferencedRawCount.ToString(), log.FoundRawCount.ToString(), log.MissingRawCount.ToString(), log.UnlistedLoadedRawCount.ToString(), log.Status });
            b.Append(Table(new[] { "LOG File", "Referenced RAW", "Found", "Missing", "Loaded but Unlisted", "Status" }, logRows.ToArray(), 6));
        }

        Section(b, "7. Survey Line QA");
        var lineRows = new List<string>();
        foreach (LineCoverageResult r in p.LineCoverageResults)
        {
            lineRows.AddRange(new[]
            {
                r.LineName, r.QaPositionSource, r.SegmentCount.ToString(), r.PositionCount.ToString("N0"), r.OfflinePositionCount.ToString("N0"),
                r.MaximumOfflineDistance.ToString("0.0"), r.Gaps.Count.ToString(), r.QualityObservationCount == 0 ? "N/E" : r.FixedQualityPercent.ToString("0.0") + "%",
                r.NavigationIntegrityScore.ToString() + "%", r.MaximumSpeedKnots.ToString("0.0"), r.NavigationGapCount.ToString(),
                string.IsNullOrWhiteSpace(r.DepthQaSummary) ? "Not evaluated" : r.DepthQaSummary, r.Status
            });
        }
        b.Append(Table(new[] { "Line", "QA position", "Seg.", "Positions", "Offline", "Max off", "Gaps", "RTK fixed", "Nav score", "Max speed", "Nav gaps", "Depth QA", "Status" }, lineRows.ToArray(), 13));

        Section(b, "8. Project Health Findings");
        var healthRows = new List<string>();
        foreach (ProjectHealthItem i in health.Items.OrderBy(x => x.Status == HealthStatus.Failure ? 0 : x.Status == HealthStatus.Warning ? 1 : 2))
            healthRows.AddRange(new[] { i.Status.ToString(), i.Category, i.Requirement, i.IsRequired ? "Yes" : "No", i.Details });
        b.Append(Table(new[] { "Status", "Category", "Requirement", "Required", "Details" }, healthRows.ToArray(), 5));

        Section(b, "9. Supporting Files");
        var supportingRows = new List<string>();
        foreach (SupportingFile f in p.SupportingFiles)
            supportingRows.AddRange(new[] { f.Category, Path.GetFileName(f.Path), File.Exists(f.Path) ? new FileInfo(f.Path).Length.ToString("N0") : "Missing", Short(f.Sha256), f.Description });
        b.Append(Table(new[] { "Category", "File", "Bytes", "SHA-256", "Description" }, supportingRows.ToArray(), 5));
        b.Append(Paragraph("ECHOTRAC DSO bar-check files and other proprietary supporting files are preserved in the package. Unless specifically decoded by the application, their measurements are not independently validated by this report.", "Note"));

        if (!string.IsNullOrWhiteSpace(p.BarCheckExceptionReason) || !string.IsNullOrWhiteSpace(p.SvpExceptionReason))
        {
            Section(b, "10. Documented Supporting-File Exceptions");
            var exceptionRows = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.BarCheckExceptionReason)) exceptionRows.AddRange(new[] { "Bar Check / Echosounder Calibration", p.BarCheckExceptionReason });
            if (!string.IsNullOrWhiteSpace(p.SvpExceptionReason)) exceptionRows.AddRange(new[] { "SVP / Sound Velocity", p.SvpExceptionReason });
            b.Append(Table(new[] { "Item", "Documented Reason" }, exceptionRows.ToArray(), 2));
        }

        Section(b, "11. Offset Corrections and Edited RAW Data");
        if (p.OffsetChanges.Count == 0) b.Append(Paragraph("No approved offset changes were recorded.", "Normal"));
        else
        {
            var changeRows = new List<string>();
            foreach (OffsetChange c in p.OffsetChanges) changeRows.AddRange(new[] { c.ChangedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), c.DeviceName, c.OriginalValues, c.ApprovedValues, c.Reason });
            b.Append(Table(new[] { "Date", "Device", "Original", "Approved", "Reason" }, changeRows.ToArray(), 5));
        }
        b.Append(Paragraph("When approved positioning-device vertical offsets differ from recorded offsets, edited RAW copies may include verified RTK TID recalculation. The original RAW files remain intact.", "Note"));

        Section(b, "12. Review and Sign-Off");
        b.Append(Table(new[] { "Field", "Value" }, new[]
        {
            "Reviewed by", p.ReviewedBy,
            "Title", p.ReviewTitle,
            "Review date", Fmt(p.ReviewDate),
            "Package approved", p.PackageApproved ? "Yes" : "No",
            "Review comments", p.ReviewComments
        }, 2));
        b.Append(Paragraph("This report documents automated checks and operator review. It does not replace professional judgment, contractual requirements, or client-specific acceptance criteria.", "Note"));

        b.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"360\" w:footer=\"360\"/></w:sectPr>");
        b.Append("</w:body></w:document>");
        return b.ToString();
    }

    private static void Section(StringBuilder b, string title) => b.Append(Paragraph(title, "Heading1"));
    private static string Paragraph(string text, string style) => $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:r><w:t xml:space=\"preserve\">{X(text)}</w:t></w:r></w:p>";
    private static string PageBreak() => "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>";
    private static string Bullets(IEnumerable<string> values) => string.Concat(values.Select(v => $"<w:p><w:pPr><w:pStyle w:val=\"Bullet\"/></w:pPr><w:r><w:t>{X(v)}</w:t></w:r></w:p>"));
    private static string Callout(string text, string kind)
    {
        string fill = kind == "Pass" ? "E2F0D9" : kind == "Fail" ? "F4CCCC" : "FFF2CC";
        return $"<w:tbl><w:tblPr><w:tblW w:w=\"10000\" w:type=\"dxa\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:color=\"9EADBA\"/><w:left w:val=\"single\" w:sz=\"8\" w:color=\"9EADBA\"/><w:bottom w:val=\"single\" w:sz=\"8\" w:color=\"9EADBA\"/><w:right w:val=\"single\" w:sz=\"8\" w:color=\"9EADBA\"/></w:tblBorders></w:tblPr><w:tr><w:tc><w:tcPr><w:shd w:fill=\"{fill}\"/><w:tcMar><w:top w:w=\"140\" w:type=\"dxa\"/><w:left w:w=\"180\" w:type=\"dxa\"/><w:bottom w:w=\"140\" w:type=\"dxa\"/><w:right w:w=\"180\" w:type=\"dxa\"/></w:tcMar></w:tcPr><w:p><w:r><w:rPr><w:b/><w:sz w:val=\"24\"/></w:rPr><w:t>{X(text)}</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
    }

    private static string Table(string[] headers, string[] cells, int columns)
    {
        if (headers.Length != columns) throw new ArgumentException("Header count must match column count.");
        var b = new StringBuilder();
        b.Append("<w:tbl><w:tblPr><w:tblW w:w=\"10000\" w:type=\"dxa\"/><w:tblLayout w:type=\"autofit\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"AAB7C4\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"AAB7C4\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"AAB7C4\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"AAB7C4\"/><w:insideH w:val=\"single\" w:sz=\"3\" w:color=\"D4DCE3\"/><w:insideV w:val=\"single\" w:sz=\"3\" w:color=\"D4DCE3\"/></w:tblBorders><w:tblCellMar><w:top w:w=\"90\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:bottom w:w=\"90\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>");
        b.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
        foreach (string h in headers) b.Append(Cell(h, true));
        b.Append("</w:tr>");
        for (int i = 0; i < cells.Length; i += columns)
        {
            b.Append("<w:tr>");
            for (int c = 0; c < columns; c++) b.Append(Cell(i + c < cells.Length ? cells[i + c] : string.Empty, false));
            b.Append("</w:tr>");
        }
        b.Append("</w:tbl><w:p><w:pPr><w:spacing w:after=\"80\"/></w:pPr></w:p>");
        return b.ToString();
    }

    private static string Cell(string value, bool header)
    {
        string shade = header ? "D9EAF7" : "FFFFFF";
        string run = header ? "<w:rPr><w:b/><w:color w:val=\"17365D\"/></w:rPr>" : string.Empty;
        return $"<w:tc><w:tcPr><w:shd w:fill=\"{shade}\"/><w:vAlign w:val=\"center\"/></w:tcPr><w:p><w:r>{run}<w:t xml:space=\"preserve\">{X(value)}</w:t></w:r></w:p></w:tc>";
    }

    private static string Friendly(SurveyDataType value) => Friendly(value.ToString());
    private static string Friendly(string value) => value switch
    {
        nameof(SurveyDataType.SingleBeamHighFrequency) => "Single Beam / High Frequency",
        nameof(SurveyDataType.SingleBeamLowFrequency) => "Single Beam / Low Frequency",
        nameof(SurveyDataType.SingleBeamDualFrequency) => "Single Beam / Dual Frequency",
        nameof(SurveyDataType.SingleBeamFrequencyUnknown) => "Single Beam / Frequency Unknown",
        _ => System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2")
    };
    private static string DateRange(FieldDataProject p) => p.SurveyStartDate.HasValue || p.SurveyEndDate.HasValue ? $"{Fmt(p.SurveyStartDate)} to {Fmt(p.SurveyEndDate)}" : "Not entered";
    private static string Fmt(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "Not entered";
    private static string Short(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Length <= 16 ? value : value[..16] + "…";
    private static string X(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string ContentTypes() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
    private static string RootRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";
    private static string DocumentRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/></Relationships>";
    private static string Settings() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"720\"/></w:settings>";
    private static string CoreProperties(FieldDataProject p) => $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:title>{X(p.ProjectName)} Field Data QA Report</dc:title><dc:creator>HydroTerra Field Data Compiler</dc:creator><cp:lastModifiedBy>HydroTerra Field Data Compiler</cp:lastModifiedBy><dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified></cp:coreProperties>";
    private static string AppProperties() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>HydroTerra Field Data Compiler</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop><Company>HydroTerra Technologies, LLC</Company><LinksUpToDate>false</LinksUpToDate><SharedDoc>false</SharedDoc><HyperlinksChanged>false</HyperlinksChanged><AppVersion>0.30</AppVersion></Properties>";
    private static string Styles() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Aptos\" w:hAnsi=\"Aptos\"/><w:sz w:val=\"21\"/><w:szCs w:val=\"21\"/><w:color w:val=\"263746\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"TitleBrand\"><w:name w:val=\"Title Brand\"/><w:pPr><w:spacing w:after=\"40\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2F75B5\"/><w:sz w:val=\"24\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:pPr><w:spacing w:before=\"220\" w:after=\"80\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"17365D\"/><w:sz w:val=\"38\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Subtitle\"><w:name w:val=\"Subtitle\"/><w:pPr><w:spacing w:after=\"360\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:color w:val=\"5B6B7A\"/><w:sz w:val=\"25\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"260\" w:after=\"100\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"17365D\"/><w:sz w:val=\"28\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Note\"><w:name w:val=\"Note\"/><w:pPr><w:ind w:left=\"280\"/><w:spacing w:before=\"80\" w:after=\"140\"/></w:pPr><w:rPr><w:i/><w:color w:val=\"5B6B7A\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Bullet\"><w:name w:val=\"Bullet\"/><w:pPr><w:ind w:left=\"480\" w:hanging=\"240\"/></w:pPr><w:rPr/></w:style></w:styles>";
}
