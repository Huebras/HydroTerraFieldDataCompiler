using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class PackageReviewBuilder
{
    public static List<PackageReviewItem> Build(FieldDataProject project)
    {
        var items = new List<PackageReviewItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string UniqueRelative(string folder, string source)
        {
            string name = Path.GetFileName(source);
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string rel = Path.Combine(folder, name);
            int n = 2;
            while (!used.Add(rel)) rel = Path.Combine(folder, $"{stem}_{n++}{ext}");
            return rel;
        }

        foreach (string source in project.ImportedRawFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string rel = UniqueRelative("01_Original_Data", source);
            items.Add(Create($"original|{source}", "Original survey data", source, rel, true));
        }

        foreach (string source in project.ImportedLogFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string rel = UniqueRelative(Path.Combine("01_Original_Data", "HYPACK_LOG"), source);
            items.Add(Create($"original|log|{source}", "HYPACK LOG", source, rel, true,
                "HYPACK survey organization file used to compare referenced RAW files, ordering, and completeness."));
        }

        bool offsetsChanged = project.Devices.Any(HasApprovedChange);
        if (offsetsChanged)
        {
            items.Add(new PackageReviewItem
            {
                Key = "generated|edited-raw",
                Category = "Edited RAW export",
                DisplayName = "Edited RAW copies and correction manifest",
                ProposedRelativePath = "02_Edited_RAW",
                IsRequired = false,
                Include = !project.ExcludedPackageItemKeys.Contains("generated|edited-raw", StringComparer.OrdinalIgnoreCase),
                Status = "Generated during package compilation",
                Details = "Approved OFF values will be written to copies. Applicable RTK TID records will be recalculated after validation."
            });
        }

        foreach (SupportingFile file in project.SupportingFiles)
        {
            string rel = UniqueRelative("03_Supporting_Files", file.Path);
            bool required = IsRequiredSupportingFile(project, file.Category);
            items.Add(Create($"support|{file.Path}", file.Category, file.Path, rel, required, file.Description));
        }

        ActiveSurveyRequirements activeRequirements = SurveyRequirements.GetActive(project);
        if (activeRequirements.Applies(SurveyRequirements.BarCheck))
            AddMissingCategoryIfNeeded("Bar Check / Echosounder Calibration", "required|bar-check", project.BarCheckExceptionReason, "Attach the applicable DSO bar-check file or enter a documented exception on Step 9.");
        if (activeRequirements.Applies(SurveyRequirements.SoundVelocity))
            AddMissingCategoryIfNeeded("SVP / Sound Velocity", "required|svp", project.SvpExceptionReason, "Attach the applicable .VEL/.SVP file or enter a documented exception on Step 9.");

        void AddMissingCategoryIfNeeded(string category, string key, string exceptionReason, string details)
        {
            bool present = project.SupportingFiles.Any(file =>
                File.Exists(file.Path) && SurveyRequirements.CategoryMatches(file.Category, category));
            if (present) return;

            bool documented = !string.IsNullOrWhiteSpace(exceptionReason);
            items.Add(new PackageReviewItem
            {
                Key = key,
                Category = category,
                DisplayName = category,
                ProposedRelativePath = "03_Supporting_Files",
                IsRequired = true,
                Include = true,
                Status = documented ? "Documented exception" : "Reason required",
                Details = documented ? exceptionReason.Trim() : details
            });
        }

        items.Add(new PackageReviewItem
        {
            Key = "generated|line-qa",
            Category = "QA export",
            DisplayName = "Survey_Line_QA.csv",
            ProposedRelativePath = Path.Combine("04_QA_Exports", "Survey_Line_QA.csv"),
            IsRequired = true,
            Include = true,
            Status = project.LineCoverageResults.Count > 0 ? "Ready" : "Not analyzed",
            Details = project.LineCoverageResults.Count > 0 ? $"{project.LineCoverageResults.Count} merged line result(s)." : "Run Analyze Lines before compiling the final package."
        });
        items.Add(new PackageReviewItem
        {
            Key = "generated|health",
            Category = "QA export",
            DisplayName = "Project_Health.csv",
            ProposedRelativePath = Path.Combine("04_QA_Exports", "Project_Health.csv"),
            IsRequired = true,
            Include = true,
            Status = project.ProjectHealth.Items.Count > 0 ? "Ready" : "Not evaluated",
            Details = project.ProjectHealth.Items.Count > 0 ? $"Overall status: {project.ProjectHealth.OverallStatus}; score {project.ProjectHealth.Score}%." : "Open or refresh Project Health before compiling."
        });
        items.Add(new PackageReviewItem
        {
            Key = "generated|word-report",
            Category = "Report",
            DisplayName = "Field_Data_Report.docx",
            ProposedRelativePath = Path.Combine("05_Reports", "Field_Data_Report.docx"),
            IsRequired = true,
            Include = true,
            Status = "Generated during package compilation",
            Details = "Word report generated from reviewed project metadata, QA results, devices, offsets, files, and sign-off."
        });

        return items;
    }

    public static void ApplySelections(FieldDataProject project, IEnumerable<PackageReviewItem> items)
    {
        project.ExcludedPackageItemKeys = items.Where(i => !i.IsRequired && !i.Include).Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static PackageReviewItem Create(string key, string category, string source, string rel, bool required, string details = "")
    {
        bool exists = File.Exists(source);
        return new PackageReviewItem
        {
            Key = key,
            Category = category,
            DisplayName = Path.GetFileName(source),
            SourcePath = source,
            ProposedRelativePath = rel,
            IsRequired = required,
            Include = true,
            Status = exists ? "Ready" : "Missing",
            Details = string.IsNullOrWhiteSpace(details) ? (exists ? "File is available." : "Source file cannot be found at the stored path.") : details,
            SizeBytes = exists ? new FileInfo(source).Length : 0
        };
    }

    private static bool HasApprovedChange(DeviceConfiguration d) =>
        Math.Abs(d.ApprovedStarboard - d.RecordedStarboard) > 1e-9 ||
        Math.Abs(d.ApprovedForward - d.RecordedForward) > 1e-9 ||
        Math.Abs(d.ApprovedVertical - d.RecordedVertical) > 1e-9 ||
        Math.Abs(d.ApprovedYaw - d.RecordedYaw) > 1e-9 ||
        Math.Abs(d.ApprovedRoll - d.RecordedRoll) > 1e-9 ||
        Math.Abs(d.ApprovedPitch - d.RecordedPitch) > 1e-9 ||
        Math.Abs(d.ApprovedLatency - d.RecordedLatency) > 1e-9;

    private static bool IsRequiredSupportingFile(FieldDataProject project, string category)
    {
        ActiveSurveyRequirements active = SurveyRequirements.GetActive(project);
        if (category.Equals("Bar Check / Echosounder Calibration", StringComparison.OrdinalIgnoreCase))
            return active.Applies(SurveyRequirements.BarCheck);
        if (category.Equals("SVP / Sound Velocity", StringComparison.OrdinalIgnoreCase))
            return active.Applies(SurveyRequirements.SoundVelocity);
        return false;
    }


}
