using System.Text.Json;
using System.Text.RegularExpressions;
using HydroTerraFieldDataCompiler.Models;
using HydroTerraFieldDataCompiler.Parsing;

namespace HydroTerraFieldDataCompiler;

public sealed class MainWizardForm : Form
{
    private FieldDataProject _project = new();
    private readonly Panel _content = new();
    private readonly Label _stepTitle = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Label _projectStatus = new();
    private int _stepIndex;
    private string _lastOutputFolder = string.Empty;
    private string _lastPackageZipPath = string.Empty;
    private string _lastReportPath = string.Empty;
    private string _lastPackageSha256 = string.Empty;

    private readonly string[] _steps =
    {
        "Project Setup", "Import HYPACK Data", "Project Health", "Data Types", "Positioning Method", "Geodesy",
        "Offsets and Draft", "Survey Lines", "Finalize Project"
    };

    public MainWizardForm()
    {
        Text = "HydroTerra Field Data Compiler";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 700);
        Size = new Size(1200, 800);
        AutoScaleMode = AutoScaleMode.Dpi;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 12),
            Margin = Padding.Empty,
            AutoSize = false
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 6)
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = "HydroTerra Field Data Compiler",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _projectStatus.AutoSize = true;
        _projectStatus.Anchor = AnchorStyles.Right;
        _projectStatus.TextAlign = ContentAlignment.MiddleRight;
        _stepTitle.AutoSize = true;
        _stepTitle.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        _stepTitle.Anchor = AnchorStyles.Left;

        headerPanel.Controls.Add(header, 0, 0);
        headerPanel.Controls.Add(_projectStatus, 1, 0);
        headerPanel.Controls.Add(_stepTitle, 0, 1);
        headerPanel.SetColumnSpan(_stepTitle, 2);

        _content.Dock = DockStyle.Fill;
        _content.Margin = new Padding(0, 4, 0, 6);
        _content.BorderStyle = BorderStyle.FixedSingle;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 48)
        };
        var saveButton = MakeButton("Save Project");
        saveButton.Click += (_, _) => SaveProject();
        _nextButton.Text = "Next >";
        _nextButton.Size = new Size(100, 34);
        _nextButton.Click += (_, _) => MoveStep(1);
        _backButton.Text = "< Back";
        _backButton.Size = new Size(100, 34);
        _backButton.Click += (_, _) => MoveStep(-1);
        var openButton = MakeButton("Open Project");
        openButton.Click += (_, _) => OpenProject();

        footer.Controls.Add(saveButton);
        footer.Controls.Add(_nextButton);
        footer.Controls.Add(_backButton);
        footer.Controls.Add(openButton);

        shell.Controls.Add(headerPanel, 0, 0);
        shell.Controls.Add(_content, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        Controls.Add(shell);

        ShowStep();
    }

    private static Button MakeButton(string text) => new() { Text = text, Size = new Size(110, 34), Margin = new Padding(6, 0, 0, 0) };
    private void MoveStep(int direction) { _stepIndex = Math.Clamp(_stepIndex + direction, 0, _steps.Length - 1); ShowStep(); }

    private void ShowStep()
    {
        _content.Controls.Clear();
        _stepTitle.Text = $"Step {_stepIndex + 1} of {_steps.Length}: {_steps[_stepIndex]}";
        _projectStatus.Text = string.IsNullOrWhiteSpace(_project.ProjectFilePath) ? "Unsaved project" : Path.GetFileName(_project.ProjectFilePath);
        _backButton.Enabled = _stepIndex > 0; _nextButton.Enabled = _stepIndex < _steps.Length - 1;
        Control page = _stepIndex switch
        {
            0 => BuildProjectSetupPage(), 1 => BuildImportPage(), 2 => BuildProjectHealthPage(), 3 => BuildDataTypesPage(), 4 => BuildPositioningPage(), 5 => BuildGeodesyPage(), 6 => BuildOffsetsPage(), 7 => BuildSurveyLinesPage(), 8 => BuildFinalizeProjectPage(), _ => BuildPlaceholderPage(_steps[_stepIndex])
        };
        page.Dock = DockStyle.Fill; _content.Controls.Add(page);
    }

    private Control BuildProjectSetupPage()
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24, 20, 24, 24)
        };

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        void AddTextRow(string labelText, string value, Action<string> setter, bool multiline = false)
        {
            int row = form.RowCount++;
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, multiline ? 112F : 42F));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 3, 12, 3)
            };

            var text = new TextBox
            {
                Text = value,
                Dock = DockStyle.Fill,
                Multiline = multiline,
                WordWrap = true,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Margin = new Padding(0, 5, 4, 5)
            };
            text.TextChanged += (_, _) => setter(text.Text);

            form.Controls.Add(label, 0, row);
            form.Controls.Add(text, 1, row);
        }

        void AddDateRow(string labelText, DateTime? value, Action<DateTime?> setter)
        {
            int row = form.RowCount++;
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 3, 12, 3)
            };

            var picker = new DateTimePicker
            {
                Dock = DockStyle.Left,
                Width = 230,
                Format = DateTimePickerFormat.Short,
                ShowCheckBox = true,
                Checked = value.HasValue,
                Value = value ?? DateTime.Today,
                Margin = new Padding(0, 5, 4, 5)
            };
            picker.ValueChanged += (_, _) => setter(picker.Checked ? picker.Value.Date : null);

            form.Controls.Add(label, 0, row);
            form.Controls.Add(picker, 1, row);
        }

        AddTextRow("Project Name", _project.ProjectName, v => _project.ProjectName = v);
        AddTextRow("Project Number", _project.ProjectNumber, v => _project.ProjectNumber = v);
        AddTextRow("Client", _project.Client, v => _project.Client = v);
        AddTextRow("Location", _project.Location, v => _project.Location = v);
        AddTextRow("Vessel", _project.Vessel, v => _project.Vessel = v);
        AddTextRow("Field Crew", _project.FieldCrew, v => _project.FieldCrew = v);
        AddDateRow("Survey Start", _project.SurveyStartDate, v => _project.SurveyStartDate = v);
        AddDateRow("Survey End", _project.SurveyEndDate, v => _project.SurveyEndDate = v);
        AddTextRow("Notes", _project.Notes, v => _project.Notes = v, true);

        scrollHost.Controls.Add(form);
        return scrollHost;
    }

    private Control BuildImportPage()
    {
        var panel = new Panel();
        var addFiles = new Button { Text = "Add RAW / LOG / ZIP Files", Location = new Point(20, 18), Size = new Size(205, 34) };
        var addFolder = new Button { Text = "Add Survey Folder", Location = new Point(235, 18), Size = new Size(145, 34) };
        var scan = new Button { Text = "Run Integrity Check", Location = new Point(390, 18), Size = new Size(160, 34) };
        var remove = new Button { Text = "Remove Selected", Location = new Point(560, 18), Size = new Size(150, 34) };
        var tabs = new TabControl { Location = new Point(20, 65), Size = new Size(1085, 490), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        var filesTab = new TabPage("Survey Data");
        var logsTab = new TabPage("HYPACK LOG");
        var findingsTab = new TabPage("Findings");
        tabs.TabPages.Add(filesTab); tabs.TabPages.Add(logsTab); tabs.TabPages.Add(findingsTab);
        var fileGrid = BuildFileGrid(); fileGrid.Dock = DockStyle.Fill; filesTab.Controls.Add(fileGrid);
        var logGrid = BuildLogGrid(); logGrid.Dock = DockStyle.Fill; logsTab.Controls.Add(logGrid);
        var findingGrid = BuildFindingGrid(); findingGrid.Dock = DockStyle.Fill; findingsTab.Controls.Add(findingGrid);

        addFiles.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "HYPACK RAW, LOG, or ZIP|*.raw;*.log;*.zip|HYPACK RAW|*.raw|HYPACK LOG|*.log|ZIP archives|*.zip|All files|*.*", Multiselect = true };
            if (d.ShowDialog(this) == DialogResult.OK) { AddPaths(d.FileNames); ShowStep(); }
        };
        addFolder.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                AddPaths(Directory.EnumerateFiles(d.SelectedPath, "*.*", SearchOption.AllDirectories).Where(p =>
                    p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));
                ShowStep();
            }
        };
        remove.Click += (_, _) =>
        {
            if (tabs.SelectedTab == logsTab)
            {
                if (logGrid.CurrentRow?.Tag is HypackLogSummary log)
                    _project.ImportedLogFiles.RemoveAll(p => p.Equals(log.SourcePath, StringComparison.OrdinalIgnoreCase));
                else if (logGrid.CurrentRow != null)
                    _project.ImportedLogFiles.RemoveAll(p => Path.GetFileName(p).Equals(logGrid.CurrentRow.Cells[0].Value?.ToString(), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (fileGrid.CurrentRow?.Tag is RawFileSummary raw)
                    _project.ImportedRawFiles.RemoveAll(p => p.Equals(raw.SourcePath, StringComparison.OrdinalIgnoreCase));
                else if (fileGrid.CurrentRow != null)
                    _project.ImportedRawFiles.RemoveAll(p => Path.GetFileName(p).Equals(fileGrid.CurrentRow.Cells[0].Value?.ToString(), StringComparison.OrdinalIgnoreCase));
            }
            ShowStep();
        };
        scan.Click += (_, _) => RunScan();
        panel.Controls.AddRange(new Control[] { addFiles, addFolder, scan, remove, tabs });
        return panel;
    }

    private DataGridView BuildFileGrid()
    {
        var g = NewGrid();
        g.Columns.Add("File", "File"); g.Columns.Add("Type", "Detected Type"); g.Columns.Add("Size", "Size"); g.Columns.Add("Records", "Records"); g.Columns.Add("Nav", "Nav"); g.Columns.Add("Soundings", "EC2 Soundings"); g.Columns.Add("Tide", "TID"); g.Columns.Add("BIN", "BIN Pair"); g.Columns.Add("INI", "INI Check"); g.Columns.Add("Status", "Status");
        if (_project.RawFileSummaries.Count > 0)
        {
            foreach (var s in _project.RawFileSummaries) { string iniStatus = s.IsIniBaseline ? "Baseline" : s.IniDifferenceCount > 0 ? $"{s.IniDifferenceCount} warning(s)" : s.IniSettings.Count > 0 ? "Matches" : "No INI"; string binStatus = s.EchosounderRecordCount == 0 ? "N/A" : s.HasMatchingBin ? "Matched" : "Missing"; int i = g.Rows.Add(s.DisplayName, s.DetectedSurveyType, FormatBytes(s.SizeBytes), s.RecordCount, s.NavigationCount, s.EchosounderRecordCount, s.TideRecordCount, binStatus, iniStatus, s.Status); g.Rows[i].Tag = s; }
        }
        else foreach (var p in _project.ImportedRawFiles) g.Rows.Add(Path.GetFileName(p), "", "", "", "", "", "", "Not scanned", "Not scanned", "Not scanned");
        return g;
    }

    private DataGridView BuildLogGrid()
    {
        var g = NewGrid();
        g.Columns.Add("File", "LOG File");
        g.Columns.Add("Referenced", "Referenced RAW");
        g.Columns.Add("Found", "Found");
        g.Columns.Add("Missing", "Missing");
        g.Columns.Add("Unlisted", "Loaded but Unlisted");
        g.Columns.Add("Status", "Status");
        if (_project.HypackLogSummaries.Count > 0)
        {
            foreach (HypackLogSummary log in _project.HypackLogSummaries)
            {
                int row = g.Rows.Add(log.DisplayName, log.ReferencedRawCount, log.FoundRawCount, log.MissingRawCount, log.UnlistedLoadedRawCount, log.Status);
                g.Rows[row].Tag = log;
                if (log.MissingRawCount > 0) g.Rows[row].DefaultCellStyle.BackColor = Color.MistyRose;
                else if (log.UnlistedLoadedRawCount > 0) g.Rows[row].DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
        }
        else
        {
            foreach (string path in _project.ImportedLogFiles)
                g.Rows.Add(Path.GetFileName(path), "", "", "", "", "Not parsed");
        }
        return g;
    }

    private DataGridView BuildFindingGrid()
    {
        var g = NewGrid(); g.Columns.Add("Severity", "Severity"); g.Columns.Add("Rule", "Rule"); g.Columns.Add("Category", "Category"); g.Columns.Add("File", "File"); g.Columns.Add("Description", "Description"); g.Columns.Add("Evidence", "Evidence");
        foreach (var f in _project.Findings) g.Rows.Add(f.Severity, f.RuleId, f.Category, f.FileName, f.Description, f.Evidence);
        return g;
    }

    private static DataGridView NewGrid() => new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };

    private void RunScan()
    {
        if (_project.ImportedRawFiles.Count == 0) { MessageBox.Show(this, "Add at least one RAW or ZIP file first."); return; }
        Cursor = Cursors.WaitCursor;
        try
        {
            var result = new HypackIntegrityScanner().Scan(_project.ImportedRawFiles);
            _project.RawFileSummaries = result.Files;
            _project.Findings = result.Findings;
            _project.HypackLogSummaries = HypackLogParser.Parse(_project.ImportedLogFiles, _project.ImportedRawFiles, _project.RawFileSummaries, _project.Findings);
            HypackLogParser.ApplyLogOrder(_project.RawFileSummaries, _project.HypackLogSummaries);
            _project.Devices = result.Devices;
            _project.DetectedPositioningMethod = result.DetectedPositioningMethod;
            _project.PositioningConfidence = result.PositioningConfidence;
            _project.PositioningEvidence = result.PositioningEvidence.Select(e => $"{e.Value}: {e.Evidence}").Distinct().ToList();
            if (result.DetectedPositioningMethod != PositioningMethod.Unknown && !_project.PositioningMethods.Contains(result.DetectedPositioningMethod))
                _project.PositioningMethods.Add(result.DetectedPositioningMethod);
            ApplyDetectedGeodesy(result.GeodesyEvidence);
            _project.DetectedDataTypes = result.Files.SelectMany(f => f.SuggestedDataTypes).Distinct().ToList();
            if (!_project.DataTypesManuallyConfirmed)
            {
                _project.DataTypes = _project.DetectedDataTypes.ToList();
            }
            MergeSurveyLines(result.Files);
            RunLineCoverageAnalysis(false);
            var nonFixed = result.Files.SelectMany(f => f.GnssSolutionCounts).Where(k => k.Key is GnssSolutionType.Float or GnssSolutionType.Autonomous or GnssSolutionType.Invalid or GnssSolutionType.NoSolution).Sum(k => k.Value);
            if (nonFixed > 0) _project.Findings.Add(new QaFinding { RuleId = "GNSS001", Severity = "Warning", Category = "Positioning", Description = $"{nonFixed} potentially non-fixed or invalid GNSS solution records were detected.", Evidence = "Review the Positioning Method page and affected files." });
            MessageBox.Show(this, $"Scan complete: {result.Files.Count} RAW files, {_project.HypackLogSummaries.Count} LOG files, {_project.Devices.Count} devices, and {_project.Findings.Count} findings.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowStep();
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ApplyDetectedGeodesy(IEnumerable<DetectionEvidence> evidence)
    {
        var all = evidence.ToList();
        _project.Geodesy.Evidence = all;
        string Pick(string category) => all.Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.Confidence).Select(e => e.Value).FirstOrDefault() ?? string.Empty;
        double? PickDouble(string category)
        {
            string text = Pick(category);
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : null;
        }

        _project.Geodesy.RecordedHorizontalDatum = Pick("Horizontal Datum");
        _project.Geodesy.RecordedGrid = Pick("Grid");
        _project.Geodesy.RecordedProjection = Pick("Projection");
        _project.Geodesy.RecordedZone = Pick("Zone");
        _project.Geodesy.RecordedZoneId = Pick("Zone ID");
        _project.Geodesy.RecordedUnits = Pick("Units");
        _project.Geodesy.RecordedEllipsoid = Pick("Ellipsoid");
        _project.Geodesy.VerticalDatum = Pick("Vertical Datum");
        if (_project.Geodesy.VerticalDatum.Equals("Not recorded", StringComparison.OrdinalIgnoreCase)) _project.Geodesy.VerticalDatum = string.Empty;
        _project.Geodesy.GeoidModel = Pick("Geoid");
        _project.Geodesy.UnitFactorMeters = PickDouble("Unit Factor");
        _project.Geodesy.VerticalUnitFactorMeters = PickDouble("Vertical Unit Factor");
        _project.Geodesy.CentralMeridian = PickDouble("Central Meridian");
        _project.Geodesy.ReferenceLatitude = PickDouble("Reference Latitude");
        _project.Geodesy.FalseEasting = PickDouble("False Easting");
        _project.Geodesy.FalseNorthing = PickDouble("False Northing");
        _project.Geodesy.ScaleFactor = PickDouble("Scale Factor");

        var conflicts = all.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key).ToList();
        _project.Geodesy.DetectionConfidence = conflicts.Count > 0 ? DetectionConfidence.Conflicting : all.Any() ? DetectionConfidence.High : DetectionConfidence.NotDetected;

        var xs = _project.RawFileSummaries.SelectMany(f => new[] { f.MinimumX, f.MaximumX }).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var ys = _project.RawFileSummaries.SelectMany(f => new[] { f.MinimumY, f.MaximumY }).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        _project.Geodesy.ValidationMessages.Clear();
        if (xs.Count > 0 && ys.Count > 0)
        {
            double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
            _project.Geodesy.CoordinateRangeSummary = $"X {minX:0.###} to {maxX:0.###}; Y {minY:0.###} to {maxY:0.###}";
            bool statePlane = _project.Geodesy.RecordedGrid.Contains("State Plane", StringComparison.OrdinalIgnoreCase);
            bool feet = _project.Geodesy.RecordedUnits.Contains("feet", StringComparison.OrdinalIgnoreCase);
            bool utm = _project.Geodesy.RecordedProjection.Contains("UTM", StringComparison.OrdinalIgnoreCase) || _project.Geodesy.RecordedZone.Contains("UTM", StringComparison.OrdinalIgnoreCase);
            if (statePlane && feet && (minX < 10000 || minY < 10000 || maxX > 10000000 || maxY > 10000000))
                _project.Geodesy.ValidationMessages.Add("Recorded coordinate magnitudes are unusual for a U.S.-foot State Plane system.");
            if (utm && (minX < 100000 || maxX > 900000 || minY < 0 || maxY > 10000000))
                _project.Geodesy.ValidationMessages.Add("Recorded coordinate magnitudes are outside the normal UTM range.");
            if (!statePlane && !utm && minX >= -180 && maxX <= 180 && minY >= -90 && maxY <= 90)
                _project.Geodesy.ValidationMessages.Add("Coordinates appear geographic (longitude/latitude). Confirm that the project is not projected.");
        }
        else _project.Geodesy.ValidationMessages.Add("No POS coordinate range was available for geodesy validation.");

        if (string.IsNullOrWhiteSpace(_project.Geodesy.RecordedHorizontalDatum)) _project.Geodesy.ValidationMessages.Add("Horizontal datum was not identified from the HYPACK header.");
        if (string.IsNullOrWhiteSpace(_project.Geodesy.RecordedProjection)) _project.Geodesy.ValidationMessages.Add("Projection was not identified from the HYPACK header.");
        if (string.IsNullOrWhiteSpace(_project.Geodesy.RecordedZone)) _project.Geodesy.ValidationMessages.Add("Zone name was not identified from the HYPACK header.");
        if (string.IsNullOrWhiteSpace(_project.Geodesy.RecordedUnits)) _project.Geodesy.ValidationMessages.Add("Horizontal units were not identified from the HYPACK header.");
        if (conflicts.Count > 0) _project.Geodesy.ValidationMessages.Add("Conflicting values were detected for: " + string.Join(", ", conflicts));
        _project.Geodesy.ValidationStatus = _project.Geodesy.ValidationMessages.Count == 0 ? "Pass" : conflicts.Count > 0 ? "Failure" : "Warning";

        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedHorizontalDatum)) _project.Geodesy.ApprovedHorizontalDatum = _project.Geodesy.RecordedHorizontalDatum;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedGrid)) _project.Geodesy.ApprovedGrid = _project.Geodesy.RecordedGrid;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedProjection)) _project.Geodesy.ApprovedProjection = _project.Geodesy.RecordedProjection;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedZone)) _project.Geodesy.ApprovedZone = _project.Geodesy.RecordedZone;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedZoneId)) _project.Geodesy.ApprovedZoneId = _project.Geodesy.RecordedZoneId;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedUnits)) _project.Geodesy.ApprovedUnits = _project.Geodesy.RecordedUnits;
        if (string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedEllipsoid)) _project.Geodesy.ApprovedEllipsoid = _project.Geodesy.RecordedEllipsoid;
    }


    private Control BuildProjectHealthPage()
    {
        var health = ProjectHealthEvaluator.Evaluate(_project);
        var panel = new Panel { AutoScroll = true };
        var title = new Label { Text = $"PROJECT HEALTH: {health.OverallStatus.ToString().ToUpperInvariant()}", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
        var score = new Label { Text = $"{health.Score}%", Font = new Font(Font.FontFamily, 32, FontStyle.Bold), AutoSize = true, Location = new Point(820, 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var refresh = new Button { Text = "Refresh Health", Location = new Point(24, 65), Size = new Size(140, 34) };
        refresh.Click += (_, _) => ShowStep();
        var fingerprint = new Label { Text = string.IsNullOrWhiteSpace(health.BaselineConfigurationFingerprint) ? "Configuration fingerprint: not available" : $"Baseline: {health.BaselineFile}   Fingerprint: {health.BaselineConfigurationFingerprint[..Math.Min(16, health.BaselineConfigurationFingerprint.Length)]}...   Matches: {health.MatchingConfigurationFiles}   Different: {health.DifferentConfigurationFiles}", AutoSize = true, Location = new Point(185, 74) };
        var grid = NewGrid(); grid.Location = new Point(24, 115); grid.Size = new Size(1065, 420); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.Columns.Add("Status", "Status"); grid.Columns.Add("Category", "Category"); grid.Columns.Add("Requirement", "Requirement"); grid.Columns.Add("Required", "Required"); grid.Columns.Add("Details", "Details");
        foreach (var item in health.Items.OrderBy(i => i.Status == HealthStatus.Failure ? 0 : i.Status == HealthStatus.Warning ? 1 : 2).ThenBy(i => i.Category))
            grid.Rows.Add(item.Status, item.Category, item.Requirement, item.IsRequired ? "Yes" : "No", item.Details);
        panel.Controls.AddRange(new Control[] { title, score, refresh, fingerprint, grid });
        return panel;
    }

    private Control BuildDataTypesPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Detected types are preselected after an integrity scan. Your checked selections control all QA, readiness, supporting-file, package, and report rules.",
            AutoSize = true,
            MaximumSize = new Size(1000, 0),
            Margin = new Padding(3, 3, 3, 10)
        }, 0, 0);

        var selectionState = new Label
        {
            Text = _project.DataTypesManuallyConfirmed ? "Rule source: Confirmed Step 4 selections" : "Rule source: Automatic detection (not yet manually changed)",
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 12)
        };
        root.Controls.Add(selectionState, 0, 1);

        var choices = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var checks = new Dictionary<SurveyDataType, CheckBox>();

        int row = 0;
        foreach (SurveyDataType type in Enum.GetValues(typeof(SurveyDataType)))
        {
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var check = new CheckBox
            {
                Text = Friendly(type.ToString()),
                AutoSize = true,
                Checked = _project.DataTypes.Contains(type),
                Margin = new Padding(3, 4, 3, 4)
            };
            checks[type] = check;
            string detection = _project.DetectedDataTypes.Contains(type) ? "Detected from imported data" : "Not automatically detected";
            var source = new Label { Text = detection, AutoSize = true, ForeColor = _project.DetectedDataTypes.Contains(type) ? Color.DarkGreen : SystemColors.GrayText, Margin = new Padding(3, 6, 3, 4) };
            choices.Controls.Add(check, 0, row);
            choices.Controls.Add(source, 1, row);
            row++;
        }
        root.Controls.Add(choices, 0, 2);

        var rules = new GroupBox { Text = "Active Rules", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 0) };
        var rulesText = new Label { AutoSize = true, MaximumSize = new Size(1000, 0), Dock = DockStyle.Top };
        rules.Controls.Add(rulesText);
        root.Controls.Add(rules, 0, 3);

        void RefreshRules()
        {
            selectionState.Text = "Rule source: Confirmed Step 4 selections";
            rulesText.Text = string.Join(Environment.NewLine, SurveyRequirements.DescribeActiveRules(_project));
        }

        foreach (var pair in checks)
        {
            SurveyDataType type = pair.Key;
            CheckBox check = pair.Value;
            check.CheckedChanged += (_, _) =>
            {
                if (check.Checked && !_project.DataTypes.Contains(type)) _project.DataTypes.Add(type);
                if (!check.Checked) _project.DataTypes.Remove(type);
                _project.DataTypesManuallyConfirmed = true;

                // Recalculate all rule-driven results immediately from the confirmed selections.
                RunLineCoverageAnalysis(false);
                _project.ProjectHealth = ProjectHealthEvaluator.Evaluate(_project);
                RefreshRules();
            };
        }

        RefreshRules();
        return root;
    }

    private Control BuildPositioningPage()
    {
        var panel = new Panel();
        var detected = new GroupBox { Text = "Automatic Detection", Location = new Point(20, 18), Size = new Size(315, 170) };
        detected.Controls.Add(new Label { Text = $"Detected: {Friendly(_project.DetectedPositioningMethod.ToString())}", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Location = new Point(14, 28) });
        detected.Controls.Add(new Label { Text = $"Confidence: {_project.PositioningConfidence}", AutoSize = true, Location = new Point(14, 55) });
        var evidence = new ListBox { Location = new Point(14, 82), Size = new Size(285, 72) };
        foreach (string item in _project.PositioningEvidence) evidence.Items.Add(item);
        detected.Controls.Add(evidence);

        var methods = new CheckedListBox { Location = new Point(20, 205), Size = new Size(315, 315), CheckOnClick = true };
        foreach (PositioningMethod method in Enum.GetValues<PositioningMethod>().Where(m => m != PositioningMethod.Unknown))
        {
            int index = methods.Items.Add(Friendly(method.ToString())); methods.SetItemChecked(index, _project.PositioningMethods.Contains(method));
        }
        methods.ItemCheck += (_, e) => BeginInvoke(new Action(() =>
        {
            _project.PositioningMethods.Clear();
            for (int i = 0; i < methods.Items.Count; i++) if (methods.GetItemChecked(i)) _project.PositioningMethods.Add(Enum.GetValues<PositioningMethod>().Where(m => m != PositioningMethod.Unknown).ElementAt(i));
        }));

        var grid = NewGrid(); grid.Location = new Point(360, 20); grid.Size = new Size(730, 500); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.Columns.Add("File", "File"); grid.Columns.Add("Fixed", "Fixed"); grid.Columns.Add("Float", "Float"); grid.Columns.Add("Differential", "Differential"); grid.Columns.Add("Autonomous", "Autonomous"); grid.Columns.Add("Invalid", "Invalid/None"); grid.Columns.Add("Status", "Status");
        foreach (var f in _project.RawFileSummaries)
        {
            int fixedCount = Count(f, GnssSolutionType.Fixed), floatCount = Count(f, GnssSolutionType.Float), diffCount = Count(f, GnssSolutionType.Differential), autoCount = Count(f, GnssSolutionType.Autonomous), bad = Count(f, GnssSolutionType.Invalid) + Count(f, GnssSolutionType.NoSolution);
            string status = floatCount + autoCount + bad > 0 ? "Review" : fixedCount + diffCount > 0 ? "Pass" : "Unknown";
            grid.Rows.Add(f.DisplayName, fixedCount, floatCount, diffCount, autoCount, bad, status);
        }
        panel.Controls.Add(detected); panel.Controls.Add(methods); panel.Controls.Add(grid); return panel;
    }

    private static int Count(RawFileSummary f, GnssSolutionType t) => f.GnssSolutionCounts.TryGetValue(t, out int n) ? n : 0;

    private Control BuildGeodesyPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 14, 20, 14),
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var status = new Label
        {
            Text = $"GEODESY VALIDATION: {_project.Geodesy.ValidationStatus.ToUpperInvariant()}    Confidence: {_project.Geodesy.DetectionConfidence}",
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(status, 0, 0);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.Controls.Add(split, 0, 1);

        var leftScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 0)
        };
        split.Controls.Add(leftScroll, 0, 0);

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftScroll.Controls.Add(fields);

        void AddSection(string text)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            var label = new Label
            {
                Text = text,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 7)
            };
            fields.Controls.Add(label, 0, row);
            fields.SetColumnSpan(label, 2);
        }

        void AddField(string labelText, string initialValue, Action<string> setter, bool readOnly = false, bool multiline = false)
        {
            int row = fields.RowCount++;
            int height = multiline ? 86 : 36;
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 8, 0),
                AutoEllipsis = true
            };
            var text = new TextBox
            {
                Text = initialValue,
                Dock = DockStyle.Fill,
                ReadOnly = readOnly,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Margin = new Padding(0, 4, 4, 5),
                BackColor = readOnly ? SystemColors.ControlLightLight : SystemColors.Window
            };
            if (!readOnly) text.TextChanged += (_, _) => setter(text.Text);
            fields.Controls.Add(label, 0, row);
            fields.Controls.Add(text, 1, row);
        }

        AddSection("Detected / Recorded HYPACK Geodesy");
        AddField("Horizontal Datum", _project.Geodesy.RecordedHorizontalDatum, v => _project.Geodesy.RecordedHorizontalDatum = v, true);
        AddField("Grid", _project.Geodesy.RecordedGrid, v => _project.Geodesy.RecordedGrid = v, true);
        AddField("Projection", _project.Geodesy.RecordedProjection, v => _project.Geodesy.RecordedProjection = v, true);
        AddField("Zone", _project.Geodesy.RecordedZone, v => _project.Geodesy.RecordedZone = v, true);
        AddField("Zone ID", _project.Geodesy.RecordedZoneId, v => _project.Geodesy.RecordedZoneId = v, true);
        AddField("Units", _project.Geodesy.RecordedUnits, v => _project.Geodesy.RecordedUnits = v, true);
        AddField("Ellipsoid", _project.Geodesy.RecordedEllipsoid, v => _project.Geodesy.RecordedEllipsoid = v, true);

        AddSection("Approved Project Geodesy");
        AddField("Horizontal Datum", _project.Geodesy.ApprovedHorizontalDatum, v => _project.Geodesy.ApprovedHorizontalDatum = v);
        AddField("Grid", _project.Geodesy.ApprovedGrid, v => _project.Geodesy.ApprovedGrid = v);
        AddField("Projection", _project.Geodesy.ApprovedProjection, v => _project.Geodesy.ApprovedProjection = v);
        AddField("Zone", _project.Geodesy.ApprovedZone, v => _project.Geodesy.ApprovedZone = v);
        AddField("Zone ID", _project.Geodesy.ApprovedZoneId, v => _project.Geodesy.ApprovedZoneId = v);
        AddField("Units", _project.Geodesy.ApprovedUnits, v => _project.Geodesy.ApprovedUnits = v);
        AddField("Ellipsoid", _project.Geodesy.ApprovedEllipsoid, v => _project.Geodesy.ApprovedEllipsoid = v);
        AddField("Vertical Datum", _project.Geodesy.VerticalDatum, v => _project.Geodesy.VerticalDatum = v);
        AddField("Geoid Model", _project.Geodesy.GeoidModel, v => _project.Geodesy.GeoidModel = v);
        AddField("Correction Reason", _project.Geodesy.CorrectionReason, v => _project.Geodesy.CorrectionReason = v, false, true);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(8, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        split.Controls.Add(right, 1, 0);

        var evidenceGroup = new GroupBox
        {
            Text = "Detected Header Evidence",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var evidenceText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = string.Join(Environment.NewLine, _project.Geodesy.Evidence.Select(item =>
                $"{item.Category}: {item.Value}  [{Path.GetFileName(item.SourceFile)}]"))
        };
        evidenceGroup.Controls.Add(evidenceText);
        right.Controls.Add(evidenceGroup, 0, 0);

        var rangeGroup = new GroupBox
        {
            Text = "Recorded Coordinate Range",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8)
        };
        rangeGroup.Controls.Add(new Label
        {
            Text = _project.Geodesy.CoordinateRangeSummary,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        });
        right.Controls.Add(rangeGroup, 0, 1);

        var validationGroup = new GroupBox
        {
            Text = "Validation Results",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var validationText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = _project.Geodesy.ValidationMessages.Count == 0
                ? "Header geodesy is complete and consistent, and the recorded coordinate range is plausible for the detected coordinate system."
                : string.Join(Environment.NewLine + Environment.NewLine, _project.Geodesy.ValidationMessages.Select(message => "• " + message))
        };
        validationGroup.Controls.Add(validationText);
        right.Controls.Add(validationGroup, 0, 2);

        return root;
    }


    private Control BuildOffsetsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 28));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3)
        };
        var acceptAll = new Button { Text = "Accept All Recorded", AutoSize = true, Height = 32 };
        var saveSelected = new Button { Text = "Save Selected Changes", AutoSize = true, Height = 32 };
        var restoreSelected = new Button { Text = "Restore Selected", AutoSize = true, Height = 32 };
        var addDevice = new Button { Text = "Add Device", AutoSize = true, Height = 32 };
        var removeDevice = new Button { Text = "Remove Device", AutoSize = true, Height = 32 };
        var exportEditedRaw = new Button { Text = "Create Edited RAW Copies", AutoSize = true, Height = 32 };
        var summaryLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(24, 8, 0, 0),
            Text = $"{_project.Devices.Count} detected device(s) • {_project.Findings.Count(f => f.Category.Equals("Offsets", StringComparison.OrdinalIgnoreCase) || f.Category.Equals("Devices", StringComparison.OrdinalIgnoreCase))} configuration finding(s)"
        };
        toolbar.Controls.AddRange(new Control[] { acceptAll, saveSelected, restoreSelected, addDevice, removeDevice, exportEditedRaw, summaryLabel });
        root.Controls.Add(toolbar, 0, 0);

        var masterDetail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        masterDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        masterDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        masterDetail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Width = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Device", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Recorded", HeaderText = "Recorded S / F / V", Width = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Review", HeaderText = "Review", Width = 78 });
        masterDetail.Controls.Add(grid, 0, 0);

        var detail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));

        var formGroup = new GroupBox { Text = "Selected Device", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(4) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        TextBox AddField(string label, bool readOnly = false, bool multiline = false)
        {
            int row = form.RowCount++;
            form.RowStyles.Add(new RowStyle(multiline ? SizeType.Absolute : SizeType.AutoSize, multiline ? 72 : 31));
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = readOnly,
                BackColor = readOnly ? SystemColors.Control : SystemColors.Window,
                Multiline = multiline,
                WordWrap = true,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Margin = new Padding(3, 3, 6, 3)
            };
            form.Controls.Add(lbl, 0, row);
            form.Controls.Add(box, 1, row);
            return box;
        }

        var deviceId = AddField("Device ID", true);
        var deviceName = AddField("Device name");
        var deviceType = AddField("Device type");
        var manufacturer = AddField("Manufacturer");
        var model = AddField("Model");
        var serial = AddField("Serial number");
        var interfaceType = AddField("Interface type", true);
        var driver = AddField("Driver", true);
        var source = AddField("Baseline source", true);
        var identityConfidence = AddField("Identity confidence", true);
        var offsetConfidence = AddField("Offset confidence", true);

        int recordedTitleRow = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        var recordedTitle = new Label { Text = "Recorded Configuration", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left };
        form.Controls.Add(recordedTitle, 0, recordedTitleRow);
        form.SetColumnSpan(recordedTitle, 2);

        var recStbd = AddField("Starboard", true);
        var recFwd = AddField("Forward", true);
        var recVert = AddField("Vertical", true);
        var recYaw = AddField("Yaw", true);
        var recRoll = AddField("Roll", true);
        var recPitch = AddField("Pitch", true);
        var recLatency = AddField("Latency", true);

        int approvedTitleRow = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        var approvedTitle = new Label { Text = "Approved Project Configuration", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left };
        form.Controls.Add(approvedTitle, 0, approvedTitleRow);
        form.SetColumnSpan(approvedTitle, 2);

        var appStbd = AddField("Starboard");
        var appFwd = AddField("Forward");
        var appVert = AddField("Vertical");
        var appYaw = AddField("Yaw");
        var appRoll = AddField("Roll");
        var appPitch = AddField("Pitch");
        var appLatency = AddField("Latency");
        var correctionReason = AddField("Correction reason", false, true);
        formGroup.Controls.Add(form);
        detail.Controls.Add(formGroup, 0, 0);

        var evidenceTabs = new TabControl { Dock = DockStyle.Fill };
        var rawTab = new TabPage("Header Evidence");
        var rawText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Both, BackColor = SystemColors.Window };
        rawTab.Controls.Add(rawText);
        var warningsTab = new TabPage("Integrity Warnings");
        var warningsText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
        warningsTab.Controls.Add(warningsText);
        evidenceTabs.TabPages.Add(rawTab);
        evidenceTabs.TabPages.Add(warningsTab);
        detail.Controls.Add(evidenceTabs, 1, 0);
        masterDetail.Controls.Add(detail, 1, 0);
        root.Controls.Add(masterDetail, 0, 1);

        var offsetsGroup = new GroupBox { Text = "Device Offsets", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var offsetsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        offsetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Component", HeaderText = "Offset component", ReadOnly = true });
        offsetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecordedValue", HeaderText = "Recorded", ReadOnly = true });
        offsetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ApprovedValue", HeaderText = "Approved / Exported", ReadOnly = false });
        offsetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Units", HeaderText = "Units", ReadOnly = true });
        offsetsGroup.Controls.Add(offsetsGrid);
        root.Controls.Add(offsetsGroup, 0, 2);

        DeviceConfiguration? selected = null;

        string FormatNumber(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        double ParseNumber(TextBox box, double fallback) => double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : fallback;
        bool OffsetChanged(DeviceConfiguration d) =>
            Math.Abs(d.RecordedStarboard - d.ApprovedStarboard) > 0.0001 ||
            Math.Abs(d.RecordedForward - d.ApprovedForward) > 0.0001 ||
            Math.Abs(d.RecordedVertical - d.ApprovedVertical) > 0.0001 ||
            Math.Abs(d.RecordedYaw - d.ApprovedYaw) > 0.0001 ||
            Math.Abs(d.RecordedRoll - d.ApprovedRoll) > 0.0001 ||
            Math.Abs(d.RecordedPitch - d.ApprovedPitch) > 0.0001 ||
            Math.Abs(d.RecordedLatency - d.ApprovedLatency) > 0.0001;

        void RefreshDeviceOffsets(DeviceConfiguration? d)
        {
            offsetsGrid.Rows.Clear();
            if (d == null) return;
            offsetsGrid.Rows.Add("Starboard (+ right / - left)", FormatNumber(d.RecordedStarboard), FormatNumber(d.ApprovedStarboard), _project.Geodesy.RecordedUnits);
            offsetsGrid.Rows.Add("Forward (+ forward / - aft)", FormatNumber(d.RecordedForward), FormatNumber(d.ApprovedForward), _project.Geodesy.RecordedUnits);
            offsetsGrid.Rows.Add("Vertical", FormatNumber(d.RecordedVertical), FormatNumber(d.ApprovedVertical), _project.Geodesy.RecordedUnits);
            offsetsGrid.Rows.Add("Yaw", FormatNumber(d.RecordedYaw), FormatNumber(d.ApprovedYaw), "degrees");
            offsetsGrid.Rows.Add("Roll", FormatNumber(d.RecordedRoll), FormatNumber(d.ApprovedRoll), "degrees");
            offsetsGrid.Rows.Add("Pitch", FormatNumber(d.RecordedPitch), FormatNumber(d.ApprovedPitch), "degrees");
            offsetsGrid.Rows.Add("Latency", FormatNumber(d.RecordedLatency), FormatNumber(d.ApprovedLatency), "seconds");
        }

        double GridApproved(int row, double fallback)
        {
            if (row < 0 || row >= offsetsGrid.Rows.Count) return fallback;
            string text = offsetsGrid.Rows[row].Cells["ApprovedValue"].Value?.ToString() ?? string.Empty;
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : fallback;
        }

        void RefreshGrid(int? selectId = null)
        {
            grid.Rows.Clear();
            foreach (var d in _project.Devices.OrderBy(d => d.DeviceId ?? int.MaxValue).ThenBy(d => d.DeviceName))
            {
                int index = grid.Rows.Add(d.DeviceId?.ToString() ?? "—", d.DeviceName, d.DeviceType,
                    $"{FormatNumber(d.RecordedStarboard)} / {FormatNumber(d.RecordedForward)} / {FormatNumber(d.RecordedVertical)}",
                    OffsetChanged(d) ? "Changed" : "Accepted");
                grid.Rows[index].Tag = d;
                if (OffsetChanged(d)) grid.Rows[index].DefaultCellStyle.BackColor = Color.LemonChiffon;
                if (selectId.HasValue && d.DeviceId == selectId) grid.CurrentCell = grid.Rows[index].Cells[0];
            }
            if (grid.Rows.Count > 0 && grid.CurrentRow == null) grid.CurrentCell = grid.Rows[0].Cells[0];
        }

        void LoadSelected(DeviceConfiguration? d)
        {
            selected = d;
            bool enabled = d != null;
            foreach (var box in new[] { deviceName, deviceType, manufacturer, model, serial, appStbd, appFwd, appVert, appYaw, appRoll, appPitch, appLatency, correctionReason }) box.Enabled = enabled;
            if (d == null)
            {
                foreach (var box in new[] { deviceId, deviceName, deviceType, manufacturer, model, serial, interfaceType, driver, source, identityConfidence, offsetConfidence, recStbd, recFwd, recVert, recYaw, recRoll, recPitch, recLatency, appStbd, appFwd, appVert, appYaw, appRoll, appPitch, appLatency, correctionReason, rawText, warningsText }) box.Text = string.Empty;
                return;
            }
            deviceId.Text = d.DeviceId?.ToString() ?? "Not assigned";
            deviceName.Text = d.DeviceName;
            deviceType.Text = d.DeviceType;
            manufacturer.Text = d.Manufacturer;
            model.Text = d.Model;
            serial.Text = d.SerialNumber;
            interfaceType.Text = d.InterfaceType?.ToString() ?? string.Empty;
            driver.Text = string.Join(" ", new[] { d.DriverPath, d.DriverVersion }.Where(v => !string.IsNullOrWhiteSpace(v)));
            source.Text = d.SourceFile;
            identityConfidence.Text = d.IdentityConfidence.ToString();
            offsetConfidence.Text = d.OffsetConfidence.ToString();
            recStbd.Text = FormatNumber(d.RecordedStarboard);
            recFwd.Text = FormatNumber(d.RecordedForward);
            recVert.Text = FormatNumber(d.RecordedVertical);
            recYaw.Text = FormatNumber(d.RecordedYaw);
            recRoll.Text = FormatNumber(d.RecordedRoll);
            recPitch.Text = FormatNumber(d.RecordedPitch);
            recLatency.Text = FormatNumber(d.RecordedLatency);
            appStbd.Text = FormatNumber(d.ApprovedStarboard);
            appFwd.Text = FormatNumber(d.ApprovedForward);
            appVert.Text = FormatNumber(d.ApprovedVertical);
            appYaw.Text = FormatNumber(d.ApprovedYaw);
            appRoll.Text = FormatNumber(d.ApprovedRoll);
            appPitch.Text = FormatNumber(d.ApprovedPitch);
            appLatency.Text = FormatNumber(d.ApprovedLatency);
            correctionReason.Text = d.CorrectionReason;
            RefreshDeviceOffsets(d);
            rawText.Text = $"DEV header{Environment.NewLine}{d.RawDeviceHeader}{Environment.NewLine}{Environment.NewLine}OFF header{Environment.NewLine}{d.RawOffsetHeader}";
            var related = _project.Findings.Where(f =>
                (f.Category.Equals("Offsets", StringComparison.OrdinalIgnoreCase) || f.Category.Equals("Devices", StringComparison.OrdinalIgnoreCase)) &&
                (f.Description.Contains(d.DeviceName, StringComparison.OrdinalIgnoreCase) || f.Evidence.Contains(d.DeviceName, StringComparison.OrdinalIgnoreCase) || (d.DeviceId.HasValue && f.Evidence.Contains($"ID {d.DeviceId}", StringComparison.OrdinalIgnoreCase)))).ToList();
            warningsText.Text = related.Count == 0
                ? "No device or offset integrity differences were detected for this device."
                : string.Join(Environment.NewLine + Environment.NewLine, related.Select(f => $"{f.Severity}: {f.Description}{Environment.NewLine}{f.Evidence}"));
        }

        void SaveSelected(bool logChange)
        {
            if (selected == null) return;
            string previous = $"S {FormatNumber(selected.ApprovedStarboard)}; F {FormatNumber(selected.ApprovedForward)}; V {FormatNumber(selected.ApprovedVertical)}; Yaw {FormatNumber(selected.ApprovedYaw)}; Roll {FormatNumber(selected.ApprovedRoll)}; Pitch {FormatNumber(selected.ApprovedPitch)}; Lat {FormatNumber(selected.ApprovedLatency)}";
            selected.DeviceName = deviceName.Text.Trim();
            selected.DeviceType = deviceType.Text.Trim();
            selected.Manufacturer = manufacturer.Text.Trim();
            selected.Model = model.Text.Trim();
            selected.SerialNumber = serial.Text.Trim();
            selected.ApprovedStarboard = GridApproved(0, ParseNumber(appStbd, selected.ApprovedStarboard));
            selected.ApprovedForward = GridApproved(1, ParseNumber(appFwd, selected.ApprovedForward));
            selected.ApprovedVertical = GridApproved(2, ParseNumber(appVert, selected.ApprovedVertical));
            selected.ApprovedYaw = GridApproved(3, ParseNumber(appYaw, selected.ApprovedYaw));
            selected.ApprovedRoll = GridApproved(4, ParseNumber(appRoll, selected.ApprovedRoll));
            selected.ApprovedPitch = GridApproved(5, ParseNumber(appPitch, selected.ApprovedPitch));
            selected.ApprovedLatency = GridApproved(6, ParseNumber(appLatency, selected.ApprovedLatency));
            selected.CorrectionReason = correctionReason.Text.Trim();
            selected.Notes = selected.CorrectionReason;
            string revised = $"S {FormatNumber(selected.ApprovedStarboard)}; F {FormatNumber(selected.ApprovedForward)}; V {FormatNumber(selected.ApprovedVertical)}; Yaw {FormatNumber(selected.ApprovedYaw)}; Roll {FormatNumber(selected.ApprovedRoll)}; Pitch {FormatNumber(selected.ApprovedPitch)}; Lat {FormatNumber(selected.ApprovedLatency)}";
            if (logChange && !previous.Equals(revised, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(selected.CorrectionReason))
            {
                MessageBox.Show(this, "Enter a correction reason before saving an offset change.", "Correction reason required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (logChange && !previous.Equals(revised, StringComparison.Ordinal))
                _project.OffsetChanges.Add(new OffsetChange { DeviceName = selected.DeviceName, OriginalValues = previous, ApprovedValues = revised, Reason = selected.CorrectionReason });
            RefreshGrid(selected.DeviceId);
            LoadSelected(selected);
        }

        grid.SelectionChanged += (_, _) => LoadSelected(grid.CurrentRow?.Tag as DeviceConfiguration);
        saveSelected.Click += (_, _) => { SaveSelected(true); MessageBox.Show(this, "The selected device configuration was saved.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        restoreSelected.Click += (_, _) =>
        {
            if (selected == null) return;
            selected.ApprovedStarboard = selected.RecordedStarboard;
            selected.ApprovedForward = selected.RecordedForward;
            selected.ApprovedVertical = selected.RecordedVertical;
            selected.ApprovedYaw = selected.RecordedYaw;
            selected.ApprovedRoll = selected.RecordedRoll;
            selected.ApprovedPitch = selected.RecordedPitch;
            selected.ApprovedLatency = selected.RecordedLatency;
            selected.CorrectionReason = string.Empty;
            LoadSelected(selected);
            RefreshGrid(selected.DeviceId);
        };
        acceptAll.Click += (_, _) =>
        {
            foreach (var d in _project.Devices)
            {
                d.ApprovedStarboard = d.RecordedStarboard;
                d.ApprovedForward = d.RecordedForward;
                d.ApprovedVertical = d.RecordedVertical;
                d.ApprovedYaw = d.RecordedYaw;
                d.ApprovedRoll = d.RecordedRoll;
                d.ApprovedPitch = d.RecordedPitch;
                d.ApprovedLatency = d.RecordedLatency;
                d.CorrectionReason = string.Empty;
            }
            RefreshGrid(selected?.DeviceId);
            LoadSelected(selected);
        };
        exportEditedRaw.Click += (_, _) =>
        {
            SaveSelected(false);
            using var folder = new FolderBrowserDialog { Description = "Select the export folder for edited RAW copies" };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var result = EditedRawExporter.Export(_project, folder.SelectedPath);
                MessageBox.Show(this,
                    $"Created {result.ExportedRawCount} edited RAW file(s).\n" +
                    $"Changed OFF records: {result.ModifiedOffsetRecordCount}\n" +
                    $"Recalculated TID records: {result.RecalculatedTideRecordCount}\n" +
                    $"Files with RTK-tide recalculation: {result.FilesWithTideRecalculation}\n" +
                    $"TID relationship checks: {result.TideValidationMatchedCount}/{result.TideValidationComparedCount} matched\n\n" +
                    result.OutputDirectory,
                    "Edited RAW export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not create edited RAW files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        addDevice.Click += (_, _) =>
        {
            int nextId = _project.Devices.Where(d => d.DeviceId.HasValue).Select(d => d.DeviceId!.Value).DefaultIfEmpty(-1).Max() + 1;
            var d = new DeviceConfiguration { DeviceId = nextId, DeviceName = $"New Device {nextId}", DeviceType = "Other", IdentityConfidence = DetectionConfidence.NotDetected, OffsetConfidence = DetectionConfidence.NotDetected, SourceFile = "Operator added" };
            _project.Devices.Add(d);
            RefreshGrid(nextId);
            LoadSelected(d);
        };
        removeDevice.Click += (_, _) =>
        {
            if (selected == null) return;
            if (MessageBox.Show(this, $"Remove '{selected.DeviceName}' from the approved project device register?", "Remove device", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _project.Devices.Remove(selected);
            selected = null;
            RefreshGrid();
            LoadSelected(grid.CurrentRow?.Tag as DeviceConfiguration);
        };

        RefreshGrid();
        if (grid.Rows.Count > 0) LoadSelected(grid.Rows[0].Tag as DeviceConfiguration);
        else LoadSelected(null);
        return root;
    }

    private Control BuildSurveyLinesPage()
    {
        var panel = new Panel { Padding = new Padding(16) };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 150), FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        controls.Controls.Add(new Label { Text = "QA position source", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var positionSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 5, 18, 0) };
        var positionSources = LineCoverageAnalyzer.GetPositionSources(_project);
        foreach (var source in positionSources) positionSource.Items.Add(source);
        if (positionSources.Count > 0)
        {
            var selected = positionSources.FirstOrDefault(x => x.DeviceId == _project.QaPositionSourceDeviceId)
                ?? LineCoverageAnalyzer.ChooseDefaultPositionSource(_project, positionSources)
                ?? positionSources[0];
            positionSource.SelectedItem = selected;
            _project.QaPositionSourceDeviceId = selected.DeviceId;
            _project.QaPositionSourceLabel = selected.DisplayName;
        }
        controls.Controls.Add(positionSource);
        controls.Controls.Add(new Label { Text = "Offline tolerance (ft)", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var tolerance = new NumericUpDown { DecimalPlaces = 1, Minimum = 1, Maximum = 500, Value = (decimal)_project.OfflineToleranceFeet, Width = 80, Margin = new Padding(0, 5, 18, 0) };
        controls.Controls.Add(tolerance);
        controls.Controls.Add(new Label { Text = "Coverage gap (ft)", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var gapSize = new NumericUpDown { DecimalPlaces = 1, Minimum = 1, Maximum = 500, Value = (decimal)_project.CoverageGapFeet, Width = 80, Margin = new Padding(0, 5, 18, 0) };
        controls.Controls.Add(gapSize);
        controls.Controls.Add(new Label { Text = "Export overlap (ft)", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var overlap = new NumericUpDown { DecimalPlaces = 1, Minimum = 0, Maximum = 1000, Value = (decimal)_project.GapExportOverlapFeet, Width = 80, Margin = new Padding(0, 5, 18, 0) };
        controls.Controls.Add(overlap);
        controls.Controls.Add(new Label { Text = "Min fixed (%)", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var fixedPercent = new NumericUpDown { DecimalPlaces = 1, Minimum = 0, Maximum = 100, Value = (decimal)_project.MinimumFixedPercent, Width = 70, Margin = new Padding(0, 5, 12, 0) };
        controls.Controls.Add(fixedPercent);
        controls.Controls.Add(new Label { Text = "Depth spike (ft)", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        var depthSpike = new NumericUpDown { DecimalPlaces = 1, Minimum = 0.1M, Maximum = 100, Value = (decimal)_project.DepthSpikeThresholdFeet, Width = 70, Margin = new Padding(0, 5, 12, 0) };
        controls.Controls.Add(depthSpike);
        var criteriaGroup = new GroupBox { Text = "Criteria used to define remaining / unsurveyed portions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 4, 12, 4) };
        var criteriaFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, MaximumSize = new Size(1020, 0) };
        var useCoverage = new CheckBox { Text = "Position coverage gaps", Checked = _project.UsePositionCoverageForRemainingLines, AutoSize = true, Margin = new Padding(4, 4, 14, 4) };
        var useOffline = new CheckBox { Text = "Exclude positions outside offline tolerance", Checked = _project.UseOfflineToleranceForCoverage, AutoSize = true, Margin = new Padding(4, 4, 14, 4), Enabled = useCoverage.Checked };
        var useRtk = new CheckBox { Text = "RTK fixed threshold", Checked = _project.UsePositionQualityForRemainingLines, AutoSize = true, Margin = new Padding(4, 4, 14, 4) };
        var useNav = new CheckBox { Text = "Navigation integrity", Checked = _project.UseNavigationIntegrityForRemainingLines, AutoSize = true, Margin = new Padding(4, 4, 14, 4) };
        var useDepth = new CheckBox { Text = "Depth QA", Checked = _project.UseDepthQaForRemainingLines, AutoSize = true, Margin = new Padding(4, 4, 14, 4) };
        criteriaFlow.Controls.AddRange(new Control[] { useCoverage, useOffline, useRtk, useNav, useDepth });
        criteriaGroup.Controls.Add(criteriaFlow);
        controls.Controls.Add(criteriaGroup);
        controls.SetFlowBreak(criteriaGroup, true);

        var analyze = new Button { Text = "Analyze Lines", Size = new Size(125, 32), Margin = new Padding(0, 2, 8, 0) };
        var export = new Button { Text = "Export Remaining DXF", Size = new Size(155, 32), Margin = new Padding(0, 2, 8, 0) };
        var exportLnw = new Button { Text = "Export Remaining LNW", Size = new Size(160, 32), Margin = new Padding(0, 2, 8, 0) };
        var planView = new Button { Text = "Open Plan View", Size = new Size(135, 32), Margin = new Padding(0, 2, 0, 0) };
        controls.Controls.Add(analyze); controls.Controls.Add(export); controls.Controls.Add(exportLnw); controls.Controls.Add(planView);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var summaryTab = new TabPage("Line Summary");
        var gapsTab = new TabPage("Unsurveyed Portions");
        tabs.TabPages.Add(summaryTab); tabs.TabPages.Add(gapsTab);

        var summaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        var summaryGrid = NewGrid(); summaryGrid.Dock = DockStyle.Fill;
        summaryGrid.Columns.Add("Line", "Line"); summaryGrid.Columns.Add("SourceDevice", "QA Position Source"); summaryGrid.Columns.Add("Segments", "Segments"); summaryGrid.Columns.Add("File", "Source RAW Files"); summaryGrid.Columns.Add("Length", "Planned Length"); summaryGrid.Columns.Add("Positions", "Positions"); summaryGrid.Columns.Add("Offline", "> Tolerance"); summaryGrid.Columns.Add("Max", "Max Offline"); summaryGrid.Columns.Add("Gaps", "Gaps"); summaryGrid.Columns.Add("Fixed", "RTK Fixed %"); summaryGrid.Columns.Add("NonFixed", "Non-Fixed"); summaryGrid.Columns.Add("HF", "HF Valid"); summaryGrid.Columns.Add("LF", "LF Valid"); summaryGrid.Columns.Add("NavScore", "Nav Integrity"); summaryGrid.Columns.Add("MaxSpeed", "Max Speed (kn)"); summaryGrid.Columns.Add("NavGaps", "Nav Gaps"); summaryGrid.Columns.Add("Warnings", "Warnings"); summaryGrid.Columns.Add("Status", "Status");

        var inspectorGroup = new GroupBox { Text = "Selected Line Rule Inspector", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var inspector = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, Font = new Font("Segoe UI", 9F), DetectUrls = false };
        inspectorGroup.Controls.Add(inspector);

        string WarningSummary(LineCoverageResult r)
        {
            var warnings = new List<string>();
            if (r.OfflinePositionCount > 0) warnings.Add($"Offline: {r.OfflinePositionCount}");
            if (r.Gaps.Count > 0) warnings.Add($"Gaps: {r.Gaps.Count}");
            if (r.QualityObservationCount > 0 && r.FixedQualityPercent < _project.MinimumFixedPercent) warnings.Add($"RTK: {r.FixedQualityPercent:0.0}%");
            if (r.DepthQaHasWarning) warnings.Add("Depth QA");
            if (r.NavigationIntegrityHasWarning) warnings.Add($"Nav: {r.NavigationIntegrityScore}%");
            return warnings.Count == 0 ? "None" : string.Join("; ", warnings);
        }

        string RuleInspectorText(LineCoverageResult r)
        {
            var lines = new List<string>
            {
                $"Line: {r.LineName}",
                $"Source segments: {r.SegmentCount} ({string.Join(", ", r.SourceFiles.Select(SourceDisplayName))})",
                string.Empty,
                "REMAINING / UNSURVEYED PORTIONS",
                r.Gaps.Count == 0 ? "PASS  No portions remain under the selected criteria." : $"WARNING  {r.Gaps.Count} remaining portion(s): {string.Join("; ", r.Gaps.Select(g => $"{g.StartChainage:0.0}-{g.EndChainage:0.0} ft"))}",
                r.RemainingLineReasons.Count == 0 ? "Criteria results: no selected criterion created a remaining portion." : $"Criteria results: {string.Join("; ", r.RemainingLineReasons)}",
                $"Position coverage criterion: {(_project.UsePositionCoverageForRemainingLines ? "On" : "Off")}; offline exclusion: {(_project.UseOfflineToleranceForCoverage ? "On" : "Off")}; RTK: {(_project.UsePositionQualityForRemainingLines ? "On" : "Off")}; navigation: {(_project.UseNavigationIntegrityForRemainingLines ? "On" : "Off")}; depth: {(_project.UseDepthQaForRemainingLines ? "On" : "Off")}.",
                string.Empty,
                "OFFLINE TOLERANCE",
                r.OfflinePositionCount == 0 ? $"PASS  All positions were within {_project.OfflineToleranceFeet:0.#} ft of the planned line." : $"WARNING  {r.OfflinePositionCount} position(s) exceeded {_project.OfflineToleranceFeet:0.#} ft; maximum {r.MaximumOfflineDistance:0.0} ft.",
                string.Empty,
                "POSITION QUALITY"
            };
            if (r.QualityObservationCount == 0)
                lines.Add("NOT EVALUATED  No line-level QUA observations were decoded. This does not by itself mark the line as failed.");
            else
            {
                lines.Add(r.FixedQualityPercent >= _project.MinimumFixedPercent
                    ? $"PASS  RTK fixed {r.FixedQualityPercent:0.00}% (minimum {_project.MinimumFixedPercent:0.0}%)."
                    : $"WARNING  RTK fixed {r.FixedQualityPercent:0.00}% (minimum {_project.MinimumFixedPercent:0.0}%).");
                lines.Add($"Fixed {r.FixedQualityCount}; non-fixed {r.NonFixedQualityCount}; unknown {r.UnknownQualityCount}; average HDOP {r.AverageHdop:0.00}; minimum satellites {r.MinimumSatellites}.");
            }
            lines.Add(string.Empty);
            lines.Add("NAVIGATION INTEGRITY");
            lines.Add(r.NavigationIntegrityHasWarning ? $"WARNING  {r.NavigationIntegritySummary}" : $"PASS  {r.NavigationIntegritySummary}");
            lines.Add($"Estimated missing epochs {r.EstimatedMissingEpochCount}; duplicate positions {r.DuplicatePositionCount}; speed spikes {r.SpeedSpikeCount}.");
            lines.Add(string.Empty);
            lines.Add("DEPTH QUALITY");
            lines.Add(r.DepthQaHasWarning ? $"WARNING  {r.DepthQaSummary}" : $"PASS  {r.DepthQaSummary}");
            lines.Add(string.Empty);
            lines.Add("CONFIGURATION");
            lines.Add($"PASS  Line gaps and offline checks used {r.QaPositionSource}.");
            lines.Add("PASS  Planned geometry and merged source segments were evaluated under the current project configuration.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var r in _project.LineCoverageResults)
        {
            string fixedText = r.QualityObservationCount > 0 ? $"{r.FixedQualityPercent:0.00}%" : "No QUA";
            string warningText = WarningSummary(r);
            string status = warningText == "None" ? "Pass" : "Warning";
            int index = summaryGrid.Rows.Add(r.LineName, r.QaPositionSource, r.SegmentCount, string.Join(", ", r.SourceFiles.Select(SourceDisplayName)), $"{r.PlannedLength:0.0}", r.PositionCount, r.OfflinePositionCount, $"{r.MaximumOfflineDistance:0.0}", r.Gaps.Count, fixedText, r.NonFixedQualityCount + r.UnknownQualityCount, r.HighFrequencyCount, r.LowFrequencyCount, $"{r.NavigationIntegrityScore}%", $"{r.MaximumSpeedKnots:0.00}", r.NavigationGapCount, warningText, status);
            summaryGrid.Rows[index].Tag = r;
            if (status == "Warning")
            {
                summaryGrid.Rows[index].DefaultCellStyle.BackColor = Color.LemonChiffon;
                summaryGrid.Rows[index].DefaultCellStyle.SelectionBackColor = Color.Goldenrod;
            }
        }
        summaryGrid.SelectionChanged += (_, _) =>
        {
            if (summaryGrid.CurrentRow?.Tag is LineCoverageResult result) inspector.Text = RuleInspectorText(result);
            else inspector.Text = "Select a survey line to see every QA rule evaluated for that line.";
        };
        summaryLayout.Controls.Add(summaryGrid, 0, 0);
        summaryLayout.Controls.Add(inspectorGroup, 0, 1);
        summaryTab.Controls.Add(summaryLayout);
        if (summaryGrid.Rows.Count > 0) summaryGrid.CurrentCell = summaryGrid.Rows[0].Cells[0];
        else inspector.Text = "Run Analyze Lines to populate the rule inspector.";

        var gapGrid = NewGrid(); gapGrid.Dock = DockStyle.Fill;
        gapGrid.Columns.Add("Line", "Line"); gapGrid.Columns.Add("Gap", "Gap"); gapGrid.Columns.Add("Start", "Start Chainage"); gapGrid.Columns.Add("End", "End Chainage"); gapGrid.Columns.Add("Length", "Missing Length"); gapGrid.Columns.Add("Reason", "Selected Criteria"); gapGrid.Columns.Add("File", "Source RAW Files");
        foreach (var r in _project.LineCoverageResults) foreach (var g in r.Gaps)
        {
            int index = gapGrid.Rows.Add(r.LineName, g.GapNumber, $"{g.StartChainage:0.0}", $"{g.EndChainage:0.0}", $"{g.MissingLength:0.0}", string.Join("; ", r.RemainingLineReasons), string.Join(", ", r.SourceFiles.Select(SourceDisplayName)));
            gapGrid.Rows[index].DefaultCellStyle.BackColor = Color.MistyRose;
            gapGrid.Rows[index].DefaultCellStyle.SelectionBackColor = Color.IndianRed;
        }
        gapsTab.Controls.Add(gapGrid);

        positionSource.SelectedIndexChanged += (_, _) =>
        {
            if (positionSource.SelectedItem is not LineCoverageAnalyzer.PositionSourceOption source) return;
            _project.QaPositionSourceDeviceId = source.DeviceId;
            _project.QaPositionSourceLabel = source.DisplayName;
            if (_project.LineCoverageResults.Count > 0)
            {
                RunLineCoverageAnalysis(false);
                ShowStep();
            }
        };
        useCoverage.CheckedChanged += (_, _) => { _project.UsePositionCoverageForRemainingLines = useCoverage.Checked; useOffline.Enabled = useCoverage.Checked; };
        useOffline.CheckedChanged += (_, _) => _project.UseOfflineToleranceForCoverage = useOffline.Checked;
        useRtk.CheckedChanged += (_, _) => _project.UsePositionQualityForRemainingLines = useRtk.Checked;
        useNav.CheckedChanged += (_, _) => _project.UseNavigationIntegrityForRemainingLines = useNav.Checked;
        useDepth.CheckedChanged += (_, _) => _project.UseDepthQaForRemainingLines = useDepth.Checked;
        tolerance.ValueChanged += (_, _) => _project.OfflineToleranceFeet = (double)tolerance.Value;
        gapSize.ValueChanged += (_, _) => _project.CoverageGapFeet = (double)gapSize.Value;
        overlap.ValueChanged += (_, _) => _project.GapExportOverlapFeet = (double)overlap.Value;
        fixedPercent.ValueChanged += (_, _) => _project.MinimumFixedPercent = (double)fixedPercent.Value;
        depthSpike.ValueChanged += (_, _) => _project.DepthSpikeThresholdFeet = (double)depthSpike.Value;
        analyze.Click += (_, _) => { if (positionSource.SelectedItem is LineCoverageAnalyzer.PositionSourceOption ps) { _project.QaPositionSourceDeviceId = ps.DeviceId; _project.QaPositionSourceLabel = ps.DisplayName; } _project.UsePositionCoverageForRemainingLines = useCoverage.Checked; _project.UseOfflineToleranceForCoverage = useOffline.Checked; _project.UsePositionQualityForRemainingLines = useRtk.Checked; _project.UseNavigationIntegrityForRemainingLines = useNav.Checked; _project.UseDepthQaForRemainingLines = useDepth.Checked; _project.OfflineToleranceFeet = (double)tolerance.Value; _project.CoverageGapFeet = (double)gapSize.Value; _project.MinimumFixedPercent = (double)fixedPercent.Value; _project.DepthSpikeThresholdFeet = (double)depthSpike.Value; RunLineCoverageAnalysis(true); };
        planView.Click += (_, _) =>
        {
            if (_project.LineCoverageResults.Count == 0) RunLineCoverageAnalysis(false);
            using var view = new PlanViewForm(_project.LineCoverageResults);
            view.ShowDialog(this);
        };
        export.Click += (_, _) =>
        {
            if (_project.LineCoverageResults.Sum(r => r.Gaps.Count) == 0) { MessageBox.Show(this, "No remaining line portions are available to export."); return; }
            _project.GapExportOverlapFeet = (double)overlap.Value;
            using var dialog = new SaveFileDialog { Filter = "DXF drawing|*.dxf", DefaultExt = "dxf", FileName = "Remaining_Line_Portions.dxf" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LineGapDxfExporter.Export(_project.LineCoverageResults, dialog.FileName, _project.GapExportOverlapFeet, _project.Geodesy.UnitFactorMeters.GetValueOrDefault(0.3048006096012192));
            MessageBox.Show(this, "The remaining-line DXF and companion CSV were created. Original RAW files were not modified.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        exportLnw.Click += (_, _) =>
        {
            if (_project.LineCoverageResults.Sum(r => r.Gaps.Count) == 0) { MessageBox.Show(this, "No remaining line portions are available to export."); return; }
            _project.GapExportOverlapFeet = (double)overlap.Value;
            using var dialog = new SaveFileDialog { Filter = "HYPACK planned line file|*.lnw", DefaultExt = "lnw", FileName = "Remaining_Line_Portions.LNW" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LineGapLnwExporter.Export(_project.LineCoverageResults, dialog.FileName, _project.GapExportOverlapFeet, _project.Geodesy.UnitFactorMeters.GetValueOrDefault(0.3048006096012192));
            MessageBox.Show(this, "The HYPACK LNW and companion CSV were created. Each exported gap is a separate two-point planned line extended by the selected overlap.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        panel.Controls.Add(tabs); panel.Controls.Add(controls);
        return panel;
    }

    private void RunLineCoverageAnalysis(bool showMessage)
    {
        try
        {
            _project.LineCoverageResults = new LineCoverageAnalyzer().Analyze(_project);
            _project.Findings.RemoveAll(f => f.RuleId.StartsWith("LINE", StringComparison.OrdinalIgnoreCase));
            foreach (var result in _project.LineCoverageResults)
            {
                if (result.OfflinePositionCount > 0) _project.Findings.Add(new QaFinding { RuleId = "LINE001", Severity = "Warning", Category = "Line Coverage", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = $"{result.OfflinePositionCount} vessel positions were more than {_project.OfflineToleranceFeet:0.#} ft off the planned line.", Evidence = $"Maximum cross-track distance: {result.MaximumOfflineDistance:0.##} coordinate units." });
                if (result.Gaps.Count > 0) _project.Findings.Add(new QaFinding { RuleId = "LINE002", Severity = "Warning", Category = "Line Coverage", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = $"{result.Gaps.Count} unsurveyed portion(s) were detected.", Evidence = string.Join("; ", result.Gaps.Select(g => $"{g.StartChainage:0.0}-{g.EndChainage:0.0} ({g.MissingLength:0.0})")) });
                if (result.QualityObservationCount == 0)
                    _project.Findings.Add(new QaFinding { RuleId = "LINE003", Severity = "Information", Category = "Positioning", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = "No line-level GNSS quality observations were decoded.", Evidence = "Position quality was not evaluated for this line; this condition does not by itself mark the line as failed." });
                else if (result.FixedQualityPercent < _project.MinimumFixedPercent)
                    _project.Findings.Add(new QaFinding { RuleId = "LINE003", Severity = "Warning", Category = "Positioning", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = $"RTK fixed coverage was {result.FixedQualityPercent:0.00}% (minimum {_project.MinimumFixedPercent:0.0}%).", Evidence = $"Fixed {result.FixedQualityCount}; non-fixed {result.NonFixedQualityCount}; unknown {result.UnknownQualityCount}; average HDOP {result.AverageHdop:0.00}; minimum satellites {result.MinimumSatellites}." });
                if (result.NavigationIntegrityHasWarning) _project.Findings.Add(new QaFinding { RuleId = "LINE005", Severity = "Warning", Category = "Navigation Integrity", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = $"Navigation integrity score was {result.NavigationIntegrityScore}%.", Evidence = result.NavigationIntegritySummary });
                if (result.DepthQaHasWarning) _project.Findings.Add(new QaFinding { RuleId = "LINE004", Severity = "Warning", Category = "Single Beam", FileName = string.Join(", ", result.SourceFiles.Select(SourceDisplayName)), SurveyLine = result.LineName, Description = "Single-beam depth-quality warnings were detected.", Evidence = result.DepthQaSummary });
            }
            if (showMessage) { MessageBox.Show(this, $"Line analysis complete: {_project.LineCoverageResults.Count} planned lines and {_project.LineCoverageResults.Sum(r => r.Gaps.Count)} unsurveyed portions.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information); ShowStep(); }
        }
        catch (Exception ex) { if (showMessage) MessageBox.Show(this, ex.Message, "Line analysis failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void MergeSurveyLines(IEnumerable<RawFileSummary> files)
    {
        var existing = _project.SurveyLines.ToDictionary(x => x.LineName, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in files.SelectMany(f => f.SurveyLineCounts).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            int records = pair.Sum(x => x.Value);
            if (existing.TryGetValue(pair.Key, out var line)) line.RecordCount = records;
            else _project.SurveyLines.Add(new SurveyLineSummary { LineName = pair.Key, RecordCount = records });
        }
    }

    private void SaveSurveyLineRows(DataGridView grid)
    {
        var lines = new List<SurveyLineSummary>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            string name = Cell(row, 0); if (string.IsNullOrWhiteSpace(name)) continue;
            lines.Add(new SurveyLineSummary { LineName = name, RecordCount = (int)Number(row, 1), NonFixedCount = (int)Number(row, 2), Classification = Cell(row, 3), Status = Cell(row, 4), Notes = Cell(row, 5) });
        }
        _project.SurveyLines = lines;
    }

    private static string SourceDisplayName(string source)
    {
        int marker = source.IndexOf("::", StringComparison.Ordinal);
        return marker >= 0 ? Path.GetFileName(source[(marker + 2)..]) : Path.GetFileName(source);
    }

    private static string Cell(DataGridViewRow row, int index) => row.Cells[index].Value?.ToString() ?? string.Empty;
    private static double Number(DataGridViewRow row, int index) => double.TryParse(Cell(row, index), out double value) ? value : 0;
    private static void AddSectionLabel(Control parent, string text, int y) => parent.Controls.Add(new Label { Text = text, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Location = new Point(24, y) });

    private Control BuildFinalizeProjectPage()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 0, 8) };
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 9,
            Dock = DockStyle.Top,
            Padding = new Padding(16),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        for (int i = 0; i < 9; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label { Text = "Finalize Project", Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };

        var readinessGroup = new GroupBox { Text = "Project Readiness", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(0, 2, 0, 8) };
        var readinessGrid = NewGrid();
        readinessGrid.Dock = DockStyle.Fill;
        readinessGrid.Height = 178;
        readinessGrid.ReadOnly = true;
        readinessGrid.AllowUserToAddRows = false;
        readinessGrid.Columns.Add("Area", "Review Area");
        readinessGrid.Columns.Add("Status", "Status");
        readinessGrid.Columns.Add("Details", "Details / Required Action");
        readinessGrid.Columns[0].Width = 190;
        readinessGrid.Columns[1].Width = 90;
        readinessGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        readinessGroup.Controls.Add(readinessGrid);

        var supportingToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 6) };
        var addButton = new Button { Text = "Add Supporting Files", Size = new Size(165, 34) };
        var removeButton = new Button { Text = "Remove Selected", Size = new Size(145, 34) };
        supportingToolbar.Controls.Add(addButton);
        supportingToolbar.Controls.Add(removeButton);

        var supportingGrid = NewGrid();
        supportingGrid.Dock = DockStyle.Fill;
        supportingGrid.AllowUserToAddRows = false;
        var categoryColumn = new DataGridViewComboBoxColumn { Name = "Category", HeaderText = "Category", FlatStyle = FlatStyle.Flat };
        categoryColumn.Items.AddRange(new object[]
        {
            "Bar Check / Echosounder Calibration", "SVP / Sound Velocity", "Tide / Water Level",
            "PPK / Base Station", "Photos", "Field Notes", "Calibration Certificate", "Other Project Document"
        });
        supportingGrid.Columns.Add(categoryColumn);
        supportingGrid.Columns.Add("File", "File");
        supportingGrid.Columns.Add("Size", "Size");
        supportingGrid.Columns.Add("Sha", "SHA-256");
        supportingGrid.Columns.Add("Description", "Description");
        supportingGrid.Columns.Add("Status", "Status");
        supportingGrid.Columns[0].Width = 215;
        supportingGrid.Columns[1].Width = 275;
        supportingGrid.Columns[2].Width = 85;
        supportingGrid.Columns[3].Width = 170;
        supportingGrid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        supportingGrid.Columns[5].Width = 95;
        supportingGrid.Columns[0].ReadOnly = false;
        supportingGrid.Columns[4].ReadOnly = false;

        bool isSingleBeamProject = _project.DataTypes.Any(t => t is SurveyDataType.SingleBeamFrequencyUnknown or SurveyDataType.SingleBeamHighFrequency or SurveyDataType.SingleBeamLowFrequency or SurveyDataType.SingleBeamDualFrequency);
        var exceptionGroup = new GroupBox { Text = "Missing Single-Beam Documentation", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(0, 4, 0, 8), Visible = isSingleBeamProject };
        var exceptionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 3 };
        exceptionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        exceptionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var exceptionNote = new Label { Text = "When a bar-check or SVP file is unavailable, enter a reason. The reason will be included in the report and package README. Missing files with documented reasons are warnings, not export blockers.", AutoSize = true, MaximumSize = new Size(1000, 0) };
        var barException = new TextBox { Text = _project.BarCheckExceptionReason, Dock = DockStyle.Fill, Multiline = true, Height = 48, ScrollBars = ScrollBars.Vertical };
        var svpException = new TextBox { Text = _project.SvpExceptionReason, Dock = DockStyle.Fill, Multiline = true, Height = 48, ScrollBars = ScrollBars.Vertical };
        exceptionLayout.Controls.Add(exceptionNote, 0, 0); exceptionLayout.SetColumnSpan(exceptionNote, 2);
        exceptionLayout.Controls.Add(new Label { Text = "Bar Check Reason", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        exceptionLayout.Controls.Add(barException, 1, 1);
        exceptionLayout.Controls.Add(new Label { Text = "SVP Reason", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        exceptionLayout.Controls.Add(svpException, 1, 2);
        exceptionGroup.Controls.Add(exceptionLayout);
        exceptionGroup.Visible = SurveyRequirements.HasSingleBeam(_project);

        var signoff = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 6) };
        signoff.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        signoff.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        signoff.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        signoff.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        var reviewedBy = new TextBox { Text = _project.ReviewedBy, Dock = DockStyle.Fill, MinimumSize = new Size(170, 0) };
        var reviewTitle = new TextBox { Text = _project.ReviewTitle, Dock = DockStyle.Fill, MinimumSize = new Size(170, 0) };
        reviewedBy.TextChanged += (_, _) => _project.ReviewedBy = reviewedBy.Text;
        reviewTitle.TextChanged += (_, _) => _project.ReviewTitle = reviewTitle.Text;
        var approved = new CheckBox { Text = "Approve package for compilation", Checked = _project.PackageApproved, AutoSize = true };
        signoff.Controls.Add(new Label { Text = "Reviewed By", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        signoff.Controls.Add(reviewedBy, 1, 0);
        signoff.Controls.Add(new Label { Text = "Title / Role", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        signoff.Controls.Add(reviewTitle, 3, 0);
        signoff.Controls.Add(approved, 1, 1);
        signoff.SetColumnSpan(approved, 3);

        var previewGroup = new GroupBox { Text = "Package Preview", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 4, 0, 8) };
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewSummary = new Label { AutoSize = true, MaximumSize = new Size(1050, 0), Padding = new Padding(0, 0, 0, 5) };
        var previewGrid = NewGrid();
        previewGrid.Dock = DockStyle.Fill;
        previewGrid.ReadOnly = false;
        previewGrid.AllowUserToAddRows = false;
        previewGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Include", Width = 60 });
        previewGrid.Columns.Add("Required", "Required");
        previewGrid.Columns.Add("Category", "Category");
        previewGrid.Columns.Add("File", "File / Generated Item");
        previewGrid.Columns.Add("Destination", "Package Destination");
        previewGrid.Columns.Add("Size", "Size");
        previewGrid.Columns.Add("Status", "Status");
        previewGrid.Columns[1].Width = 65;
        previewGrid.Columns[2].Width = 165;
        previewGrid.Columns[3].Width = 235;
        previewGrid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        previewGrid.Columns[5].Width = 80;
        previewGrid.Columns[6].Width = 105;
        foreach (DataGridViewColumn column in previewGrid.Columns) column.ReadOnly = column.Name != "Include";
        previewLayout.Controls.Add(previewSummary, 0, 0);
        previewLayout.Controls.Add(previewGrid, 0, 1);
        previewGroup.Controls.Add(previewLayout);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 9, 12, 8),
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle
        };
        var refreshButton = new Button { Text = "Refresh Readiness", Size = new Size(150, 38) };
        var reportButton = new Button { Text = "Generate Report", Size = new Size(145, 38) };
        var packageButton = new Button { Text = "Compile Package", Size = new Size(145, 38) };
        var openOutputButton = new Button { Text = "Open Output Folder", Size = new Size(155, 38) };
        var finishButton = new Button { Text = "Finish", Size = new Size(105, 38) };
        actions.Controls.Add(refreshButton);
        actions.Controls.Add(reportButton);
        actions.Controls.Add(packageButton);
        actions.Controls.Add(openOutputButton);
        actions.Controls.Add(finishButton);

        var resultGroup = new GroupBox { Text = "Latest Output", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(0, 4, 0, 6) };
        var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 5 };
        resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var packagePathValue = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        var reportPathValue = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        var checksumValue = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        var outputButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var openPackageFolder = new Button { Text = "Open Package Folder", AutoSize = true };
        var openReport = new Button { Text = "Open Report", AutoSize = true };
        var copyPackagePath = new Button { Text = "Copy Package Path", AutoSize = true };
        var copyChecksum = new Button { Text = "Copy Checksum", AutoSize = true };
        outputButtons.Controls.Add(openPackageFolder);
        outputButtons.Controls.Add(openReport);
        outputButtons.Controls.Add(copyPackagePath);
        outputButtons.Controls.Add(copyChecksum);
        resultLayout.Controls.Add(new Label { Text = "Package ZIP", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        resultLayout.Controls.Add(packagePathValue, 1, 0);
        resultLayout.Controls.Add(new Label { Text = "Word Report", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        resultLayout.Controls.Add(reportPathValue, 1, 1);
        resultLayout.Controls.Add(new Label { Text = "ZIP SHA-256", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        resultLayout.Controls.Add(checksumValue, 1, 2);
        resultLayout.Controls.Add(outputButtons, 1, 3);
        resultGroup.Controls.Add(resultLayout);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1080, 0),
            Text = "Requirements follow the detected survey types. BIN pairing, bar-check review, and SVP/.VEL review apply to single-beam data only. They are not requirements for magnetometer-only projects. Missing single-beam bar-check or SVP files may be resolved with a documented reason. Original source data remain unchanged; edited RAW files are written as separate copies."
        };

        string Hash(string path) { try { return PackageCompiler.ComputeSha256(path); } catch { return string.Empty; } }
        List<PackageReviewItem> previewItems = new();

        void SaveSupportingEdits()
        {
            supportingGrid.EndEdit();
            _project.BarCheckExceptionReason = barException.Text.Trim();
            _project.SvpExceptionReason = svpException.Text.Trim();
            foreach (DataGridViewRow row in supportingGrid.Rows)
            {
                if (row.Tag is not SupportingFile file) continue;
                file.Category = row.Cells[0].Value?.ToString() ?? file.Category;
                file.Description = row.Cells[4].Value?.ToString() ?? string.Empty;
            }
        }

        void RefreshSupportingGrid()
        {
            supportingGrid.Rows.Clear();
            var hashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (SupportingFile f in _project.SupportingFiles)
            {
                if (string.IsNullOrWhiteSpace(f.Sha256) && File.Exists(f.Path)) f.Sha256 = Hash(f.Path);
                if (!string.IsNullOrWhiteSpace(f.Sha256)) hashes[f.Sha256] = hashes.GetValueOrDefault(f.Sha256) + 1;
            }
            foreach (SupportingFile f in _project.SupportingFiles)
            {
                long size = File.Exists(f.Path) ? new FileInfo(f.Path).Length : 0;
                string status = !File.Exists(f.Path) ? "Missing" : (!string.IsNullOrWhiteSpace(f.Sha256) && hashes.GetValueOrDefault(f.Sha256) > 1 ? "Duplicate" : "Ready");
                int rowIndex = supportingGrid.Rows.Add(f.Category, Path.GetFileName(f.Path), FormatBytes(size), ShortHash(f.Sha256), f.Description, status);
                supportingGrid.Rows[rowIndex].Tag = f;
                if (status != "Ready") supportingGrid.Rows[rowIndex].DefaultCellStyle.BackColor = status == "Missing" ? Color.MistyRose : Color.LemonChiffon;
            }
        }

        void RefreshPreview()
        {
            SaveSupportingEdits();
            previewItems = PackageReviewBuilder.Build(_project);
            foreach (PackageReviewItem item in previewItems)
                item.Include = item.IsRequired || !_project.ExcludedPackageItemKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase);
            previewGrid.Rows.Clear();
            foreach (PackageReviewItem item in previewItems)
            {
                int rowIndex = previewGrid.Rows.Add(item.Include, item.IsRequired ? "Yes" : "No", item.Category, item.DisplayName, item.ProposedRelativePath, FormatBytes(item.SizeBytes), item.Status);
                DataGridViewRow row = previewGrid.Rows[rowIndex];
                row.Tag = item;
                if (item.IsRequired) row.Cells[0].ReadOnly = true;
                if (item.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated") row.DefaultCellStyle.BackColor = Color.MistyRose;
                else if (!item.Include) row.DefaultCellStyle.BackColor = Color.Gainsboro;
                else if (item.Status != "Ready" && !item.Status.StartsWith("Generated", StringComparison.OrdinalIgnoreCase)) row.DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
            int requiredProblems = previewItems.Count(i => i.IsRequired && (i.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated"));
            long knownBytes = previewItems.Where(i => i.Include).Sum(i => i.SizeBytes);
            previewSummary.Text = $"Included: {previewItems.Count(i => i.Include)} of {previewItems.Count}   Required problems: {requiredProblems}   Known size: {FormatBytes(knownBytes)}\n" +
                                  (requiredProblems == 0 ? "The package preview has no blocking file problems." : "Resolve the red required items before compiling.");
        }

        void AddReadinessRow(string area, string status, string details, int stepIndex)
        {
            int rowIndex = readinessGrid.Rows.Add(area, status, details);
            readinessGrid.Rows[rowIndex].Tag = stepIndex;
            readinessGrid.Rows[rowIndex].DefaultCellStyle.BackColor = status == "Pass" ? Color.Honeydew : status == "Failure" ? Color.MistyRose : Color.LemonChiffon;
        }

        void RefreshReadiness()
        {
            _project.ProjectHealth = ProjectHealthEvaluator.Evaluate(_project);
            readinessGrid.Rows.Clear();
            AddReadinessRow("Survey Data", _project.RawFileSummaries.Count > 0 ? "Pass" : "Failure", _project.RawFileSummaries.Count > 0 ? $"{_project.RawFileSummaries.Count} RAW file(s) scanned." : "Import and scan survey data.", 1);
            AddReadinessRow("Geodesy", !string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedHorizontalDatum) ? "Pass" : "Warning", !string.IsNullOrWhiteSpace(_project.Geodesy.ApprovedHorizontalDatum) ? $"{_project.Geodesy.ApprovedHorizontalDatum}; {_project.Geodesy.ApprovedZone}." : "Review and approve project geodesy.", 5);
            AddReadinessRow("Devices & Offsets", _project.Devices.Count > 0 ? "Pass" : "Warning", _project.Devices.Count > 0 ? $"{_project.Devices.Count} device(s) detected." : "Review detected devices and offsets.", 6);
            AddReadinessRow("Survey Lines", _project.LineCoverageResults.Count > 0 ? (_project.LineCoverageResults.Any(r => r.Status.Contains("Failure", StringComparison.OrdinalIgnoreCase)) ? "Failure" : _project.LineCoverageResults.Any(r => r.Status.Contains("Warning", StringComparison.OrdinalIgnoreCase)) ? "Warning" : "Pass") : "Warning", _project.LineCoverageResults.Count > 0 ? $"{_project.LineCoverageResults.Count} merged line(s) analyzed." : "Run Analyze Lines.", 7);
            int navWarnings = _project.LineCoverageResults.Count(r => r.NavigationIntegrityScore < 90 || r.NavigationGapCount >= 3 || r.PositionFreezeCount > 0 || r.ImpossibleJumpCount > 0 || r.TimeReversalCount > 0);
            AddReadinessRow("Navigation Integrity", _project.LineCoverageResults.Count == 0 ? "Warning" : navWarnings == 0 ? "Pass" : "Warning", _project.LineCoverageResults.Count == 0 ? "Line navigation has not been analyzed." : navWarnings == 0 ? "No merged lines require navigation review." : $"{navWarnings} line(s) require navigation review.", 7);
            bool singleBeamApplies = SurveyRequirements.HasSingleBeam(_project);
            bool magnetometerApplies = SurveyRequirements.HasMagnetometer(_project);
            string applicability = singleBeamApplies
                ? magnetometerApplies
                    ? "Single Beam + Magnetometer: BIN pairing, bar-check review, and sound-velocity review apply to the single-beam data; they are not magnetometer requirements."
                    : "Single Beam: BIN pairing, bar-check review, and sound-velocity review apply."
                : magnetometerApplies
                    ? "Magnetometer: BIN files, bar checks, and sound-velocity casts are not applicable."
                    : "Survey-type requirements are unresolved; confirm the detected data types.";
            AddReadinessRow("Survey Requirements", singleBeamApplies || magnetometerApplies ? "Pass" : "Warning", applicability, 3);

            List<PackageReviewItem> requirements = PackageReviewBuilder.Build(_project);
            int supportProblems = requirements.Count(i => i.IsRequired && i.Key.StartsWith("required|", StringComparison.OrdinalIgnoreCase) && i.Status == "Reason required");
            int documentedExceptions = requirements.Count(i => i.Key.StartsWith("required|", StringComparison.OrdinalIgnoreCase) && i.Status == "Documented exception");
            string supportingDetails = !singleBeamApplies
                ? magnetometerApplies
                    ? $"{_project.SupportingFiles.Count} supporting file(s) attached. Bar check and SVP are not applicable to this magnetometer-only project."
                    : $"{_project.SupportingFiles.Count} supporting file(s) attached; survey-type requirements require confirmation."
                : supportProblems > 0
                    ? $"{supportProblems} missing single-beam item(s) require a file or written reason."
                    : documentedExceptions > 0
                        ? $"{documentedExceptions} supporting item(s) have documented exceptions."
                        : $"{_project.SupportingFiles.Count} supporting file(s) attached; single-beam documentation requirements are satisfied.";
            AddReadinessRow("Supporting Files", supportProblems > 0 ? "Failure" : documentedExceptions > 0 ? "Warning" : "Pass", supportingDetails, 8);
            AddReadinessRow("Package Approval", _project.PackageApproved ? "Pass" : "Warning", _project.PackageApproved ? $"Approved by {_project.ReviewedBy}." : "Reviewer approval has not been checked.", 8);
        }

        void RefreshOutput()
        {
            packagePathValue.Text = _lastPackageZipPath;
            reportPathValue.Text = _lastReportPath;
            checksumValue.Text = _lastPackageSha256;
            openPackageFolder.Enabled = !string.IsNullOrWhiteSpace(_lastOutputFolder) && Directory.Exists(_lastOutputFolder);
            openReport.Enabled = !string.IsNullOrWhiteSpace(_lastReportPath) && File.Exists(_lastReportPath);
            copyPackagePath.Enabled = !string.IsNullOrWhiteSpace(_lastPackageZipPath);
            copyChecksum.Enabled = !string.IsNullOrWhiteSpace(_lastPackageSha256);
        }

        void RefreshAll()
        {
            RefreshSupportingGrid();
            RefreshReadiness();
            RefreshPreview();
            RefreshOutput();
        }

        readinessGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || readinessGrid.Rows[e.RowIndex].Tag is not int targetStep) return;
            _stepIndex = Math.Clamp(targetStep, 0, _steps.Length - 1);
            ShowStep();
        };
        supportingGrid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || supportingGrid.Rows[e.RowIndex].Tag is not SupportingFile file) return;
            file.Category = supportingGrid.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? file.Category;
            file.Description = supportingGrid.Rows[e.RowIndex].Cells[4].Value?.ToString() ?? string.Empty;
            RefreshReadiness();
            RefreshPreview();
        };
        supportingGrid.CurrentCellDirtyStateChanged += (_, _) => { if (supportingGrid.IsCurrentCellDirty) supportingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        barException.TextChanged += (_, _) => { _project.BarCheckExceptionReason = barException.Text.Trim(); RefreshReadiness(); RefreshPreview(); };
        svpException.TextChanged += (_, _) => { _project.SvpExceptionReason = svpException.Text.Trim(); RefreshReadiness(); RefreshPreview(); };
        previewGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || previewGrid.Rows[e.RowIndex].Tag is not PackageReviewItem item || item.IsRequired) return;
            item.Include = Convert.ToBoolean(previewGrid.Rows[e.RowIndex].Cells[0].Value ?? false);
            PackageReviewBuilder.ApplySelections(_project, previewItems);
            RefreshPreview();
        };
        previewGrid.CurrentCellDirtyStateChanged += (_, _) => { if (previewGrid.IsCurrentCellDirty) previewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        addButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Supporting files|*.dso;*.svp;*.vel;*.vlt;*.sv;*.csv;*.txt;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.jpg;*.jpeg;*.png;*.heic;*.obs;*.nav;*.rnx;*.zip|All files|*.*", Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (string path in dialog.FileNames)
            {
                if (_project.SupportingFiles.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                _project.SupportingFiles.Add(new SupportingFile { Path = path, Category = GuessCategory(path), Sha256 = Hash(path) });
            }
            RefreshAll();
        };
        removeButton.Click += (_, _) =>
        {
            if (supportingGrid.CurrentRow?.Tag is SupportingFile file) _project.SupportingFiles.Remove(file);
            RefreshAll();
        };
        reportButton.Click += (_, _) =>
        {
            SaveSupportingEdits();
            string? path = GenerateWordReportAndReturnPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _lastReportPath = path;
                _lastOutputFolder = Path.GetDirectoryName(path) ?? _lastOutputFolder;
                RefreshOutput();
            }
        };
        packageButton.Click += (_, _) =>
        {
            SaveSupportingEdits();
            PackageCompileResult? result = CompilePackageAndReturnResult();
            if (result != null)
            {
                _lastPackageZipPath = result.ZipPath;
                _lastReportPath = result.WordReportPath;
                _lastOutputFolder = Path.GetDirectoryName(result.ZipPath) ?? result.WorkDirectory;
                _lastPackageSha256 = result.ZipSha256;
                RefreshAll();
            }
        };
        openOutputButton.Click += (_, _) =>
        {
            string folder = !string.IsNullOrWhiteSpace(_lastOutputFolder) ? _lastOutputFolder : (!string.IsNullOrWhiteSpace(_project.ProjectFilePath) ? Path.GetDirectoryName(_project.ProjectFilePath) ?? string.Empty : string.Empty);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                using var choose = new FolderBrowserDialog { Description = "Select an output folder" };
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                folder = choose.SelectedPath;
                _lastOutputFolder = folder;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        };
        openPackageFolder.Click += (_, _) =>
        {
            string? folder = Path.GetDirectoryName(_lastPackageZipPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        };
        openReport.Click += (_, _) => { if (File.Exists(_lastReportPath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _lastReportPath, UseShellExecute = true }); };
        copyPackagePath.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_lastPackageZipPath)) Clipboard.SetText(_lastPackageZipPath); };
        copyChecksum.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_lastPackageSha256)) Clipboard.SetText(_lastPackageSha256); };
        refreshButton.Click += (_, _) => { SaveSupportingEdits(); RefreshAll(); };
        finishButton.Click += (_, _) =>
        {
            SaveSupportingEdits();
            RefreshAll();
            int blocking = previewItems.Count(i => i.IsRequired && (i.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated"));
            if (blocking > 0)
            {
                MessageBox.Show(this, $"The project still has {blocking} blocking required item(s). Review the red rows before finishing.", "Project Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, "Project finalization is complete. Generate the report or compile the package when ready.", "Finalize Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        approved.CheckedChanged += (_, _) => { _project.PackageApproved = approved.Checked; RefreshAll(); };

        RefreshAll();
        supportingGrid.MinimumSize = new Size(0, 220);
        previewGroup.MinimumSize = new Size(0, 260);
        body.Controls.Add(heading, 0, 0);
        body.Controls.Add(readinessGroup, 0, 1);
        body.Controls.Add(supportingToolbar, 0, 2);
        body.Controls.Add(supportingGrid, 0, 3);
        body.Controls.Add(exceptionGroup, 0, 4);
        body.Controls.Add(signoff, 0, 5);
        body.Controls.Add(previewGroup, 0, 6);
        body.Controls.Add(resultGroup, 0, 7);
        body.Controls.Add(note, 0, 8);
        scrollHost.Controls.Add(body);
        root.Controls.Add(scrollHost);
        root.Controls.Add(actions);
        actions.BringToFront();
        return root;
    }


    private Control BuildPackageReviewPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label { Text = "Review the proposed package contents before compilation.", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var summary = new Label { AutoSize = true, MaximumSize = new Size(1050, 0), Margin = new Padding(0, 0, 0, 8) };
        var grid = NewGrid();
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Include", Width = 65, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        grid.Columns.Add("Required", "Required");
        grid.Columns.Add("Category", "Category");
        grid.Columns.Add("File", "File / Generated Item");
        grid.Columns.Add("Destination", "Package Destination");
        grid.Columns.Add("Size", "Size");
        grid.Columns.Add("Status", "Status");
        grid.Columns.Add("Details", "Details");
        foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = column.Name != "Include";
        grid.Columns[0].Width = 65; grid.Columns[1].Width = 70; grid.Columns[2].Width = 180; grid.Columns[3].Width = 230; grid.Columns[4].Width = 260; grid.Columns[5].Width = 80; grid.Columns[6].Width = 100;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        var includeOptional = new Button { Text = "Include All Optional", Size = new Size(155, 34) };
        var excludeOptional = new Button { Text = "Exclude All Optional", Size = new Size(160, 34) };
        var refresh = new Button { Text = "Refresh Review", Size = new Size(130, 34) };
        toolbar.Controls.Add(includeOptional); toolbar.Controls.Add(excludeOptional); toolbar.Controls.Add(refresh);

        List<PackageReviewItem> items = new();
        void RefreshReview()
        {
            items = PackageReviewBuilder.Build(_project);
            foreach (PackageReviewItem item in items)
                item.Include = item.IsRequired || !_project.ExcludedPackageItemKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase);
            grid.Rows.Clear();
            foreach (PackageReviewItem item in items)
            {
                int rowIndex = grid.Rows.Add(item.Include, item.IsRequired ? "Yes" : "No", item.Category, item.DisplayName, item.ProposedRelativePath, FormatBytes(item.SizeBytes), item.Status, item.Details);
                DataGridViewRow row = grid.Rows[rowIndex]; row.Tag = item;
                if (item.IsRequired) row.Cells[0].ReadOnly = true;
                if (item.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated") row.DefaultCellStyle.BackColor = Color.MistyRose;
                else if (!item.Include) row.DefaultCellStyle.BackColor = Color.Gainsboro;
                else if (item.Status != "Ready" && !item.Status.StartsWith("Generated", StringComparison.OrdinalIgnoreCase)) row.DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
            int requiredProblems = items.Count(i => i.IsRequired && (i.Status is "Missing" or "Reason required" or "Not analyzed" or "Not evaluated"));
            long includedBytes = items.Where(i => i.Include).Sum(i => i.SizeBytes);
            summary.Text = $"Items: {items.Count}   Included: {items.Count(i => i.Include)}   Required problems: {requiredProblems}   Known file size: {FormatBytes(includedBytes)}\n" +
                           (requiredProblems == 0 ? "Required package items are ready for compilation." : "Resolve the red required items before compiling the final package.");
        }

        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || grid.Rows[e.RowIndex].Tag is not PackageReviewItem item || item.IsRequired) return;
            item.Include = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value ?? false);
            PackageReviewBuilder.ApplySelections(_project, items);
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = item.Include ? Color.White : Color.Gainsboro;
        };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        includeOptional.Click += (_, _) => { _project.ExcludedPackageItemKeys.Clear(); RefreshReview(); };
        excludeOptional.Click += (_, _) => { _project.ExcludedPackageItemKeys = items.Where(i => !i.IsRequired).Select(i => i.Key).ToList(); RefreshReview(); };
        refresh.Click += (_, _) => RefreshReview();

        RefreshReview();
        root.Controls.Add(heading, 0, 0); root.Controls.Add(summary, 0, 1); root.Controls.Add(grid, 0, 2); root.Controls.Add(toolbar, 0, 3);
        return root;
    }

    private Control BuildReviewSignOffPage()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 25;
        AddTextField(panel, "Reviewed By", _project.ReviewedBy, y, v => _project.ReviewedBy = v); y += 48;
        AddTextField(panel, "Title / Role", _project.ReviewTitle, y, v => _project.ReviewTitle = v); y += 48;
        AddDateField(panel, "Review Date", _project.ReviewDate, y, v => _project.ReviewDate = v); y += 48;
        var approved = new CheckBox { Text = "I approve this field-data package for compilation", Checked = _project.PackageApproved, AutoSize = true, Location = new Point(180, y + 3) };
        approved.CheckedChanged += (_, _) => _project.PackageApproved = approved.Checked;
        panel.Controls.Add(new Label { Text = "Package Approval", AutoSize = true, Location = new Point(30, y + 5) }); panel.Controls.Add(approved); y += 48;
        AddTextField(panel, "Review Comments", _project.ReviewComments, y, v => _project.ReviewComments = v, true); y += 115;
        panel.Controls.Add(new Label { Text = "The sign-off information is included in the final report and package README. Approval is an operator decision and does not suppress unresolved QA findings.", AutoSize = true, MaximumSize = new Size(780, 0), Location = new Point(180, y + 8) });
        return panel;
    }

    private Control BuildCompilePackagePage()
    {
        var panel = new Panel { Padding = new Padding(28) };
        ProjectHealthSummary health = ProjectHealthEvaluator.Evaluate(_project);
        var title = new Label { Text = "Finalize Project", Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24) };
        var status = new Label { Text = $"Project Health: {health.OverallStatus} ({health.Score}%)\nPackage approved: {(_project.PackageApproved ? "Yes" : "No")}", AutoSize = true, Location = new Point(30, 72) };
        var report = new Button { Text = "Generate Report", Location = new Point(30, 135), Size = new Size(185, 38) };
        var compile = new Button { Text = "Compile Submittal ZIP", Location = new Point(230, 135), Size = new Size(185, 38) };
        var note = new Label { Text = "Compilation creates the original-data folder, optional edited RAW copies, supporting files, QA CSV exports, the Word report, a SHA-256 manifest, and README. Original source files are never modified.", AutoSize = true, MaximumSize = new Size(860, 0), Location = new Point(30, 195) };
        report.Click += (_, _) => GenerateWordReport();
        compile.Click += (_, _) => CompilePackage();
        panel.Controls.AddRange(new Control[] { title, status, report, compile, note });
        return panel;
    }

    private string? GenerateWordReportAndReturnPath()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Word document|*.docx",
            DefaultExt = "docx",
            FileName = string.IsNullOrWhiteSpace(_project.ProjectName) ? "Field_Data_Report.docx" : Regex.Replace(_project.ProjectName, "[^A-Za-z0-9_-]+", "_") + "_Field_Data_Report.docx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        try
        {
            PackageCompiler.GenerateWordReport(_project, dialog.FileName);
            MessageBox.Show(this, $"Report created successfully.\n\n{dialog.FileName}", "Generate Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not create Word report", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private void GenerateWordReport() => GenerateWordReportAndReturnPath();


    private PackageCompileResult? CompilePackageAndReturnResult()
    {
        using var folder = new FolderBrowserDialog { Description = "Select the folder for the compiled submittal package" };
        if (folder.ShowDialog(this) != DialogResult.OK) return null;
        try
        {
            PackageReviewBuilder.ApplySelections(_project, PackageReviewBuilder.Build(_project).Select(i => { i.Include = i.IsRequired || !_project.ExcludedPackageItemKeys.Contains(i.Key, StringComparer.OrdinalIgnoreCase); return i; }));
            PackageCompileResult result = PackageCompiler.Compile(_project, folder.SelectedPath);
            MessageBox.Show(this, $"Package created successfully.\n\nFiles: {result.FileCount}\nWord report: {result.WordReportPath}\nZIP: {result.ZipPath}\nSHA-256: {result.ZipSha256}", "Compile Submittal Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return result;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Package compilation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private void CompilePackage() => CompilePackageAndReturnResult();


    private static string GuessCategory(string path)
    {
        string n = Path.GetFileName(path).ToLowerInvariant(); string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dso" || n.Contains("bar")) return "Bar Check / Echosounder Calibration";
        if (ext is ".svp" or ".vel" or ".vlt" or ".sv" || n.Contains("svp") || n.Contains("velocity") || n.Contains("sound speed") || n.Contains("soundspeed") || n.Contains("cast")) return "SVP / Sound Velocity";
        if (n.Contains("tide") || n.Contains("water level")) return "Tide / Water Level";
        if (n.Contains("ppk") || n.Contains("rinex") || ext is ".obs" or ".nav" or ".rnx") return "PPK / Base Station";
        if (ext is ".jpg" or ".jpeg" or ".png" or ".heic") return "Photos";
        if (n.Contains("calib") || n.Contains("certificate")) return "Calibration Certificate";
        if (n.Contains("field") || n.Contains("note") || n.Contains("log")) return "Field Notes";
        return "Other Project Document";
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024L ? $"{bytes / 1024d / 1024d:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    private static string ShortHash(string hash) => string.IsNullOrWhiteSpace(hash) ? "" : hash.Length <= 16 ? hash : hash[..16] + "...";

    private static Control BuildPlaceholderPage(string title) => new Panel { Controls = { new Label { Text = $"{title}\n\nThis page remains in the wizard and will be connected during the next milestone.", AutoSize = true, MaximumSize = new Size(900, 0), Location = new Point(30, 30) } } };

    private static void AddTextField(Control parent, string labelText, string initialValue, int y, Action<string> setter, bool multiline = false) { var label = new Label { Text = labelText, Location = new Point(30, y + 5), AutoSize = true }; var text = new TextBox { Text = initialValue, Location = new Point(180, y), Width = 650, Multiline = multiline, Height = multiline ? 90 : 26, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None }; text.TextChanged += (_, _) => setter(text.Text); parent.Controls.Add(label); parent.Controls.Add(text); }
    private static void AddDateField(Control parent, string labelText, DateTime? value, int y, Action<DateTime?> setter) { var label = new Label { Text = labelText, Location = new Point(30, y + 5), AutoSize = true }; var picker = new DateTimePicker { Location = new Point(180, y), Width = 220, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = value.HasValue, Value = value ?? DateTime.Today }; picker.ValueChanged += (_, _) => setter(picker.Checked ? picker.Value.Date : null); parent.Controls.Add(label); parent.Controls.Add(picker); }
    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".log", StringComparison.OrdinalIgnoreCase))
            {
                if (!_project.ImportedLogFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _project.ImportedLogFiles.Add(path);
            }
            else if (extension.Equals(".raw", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!_project.ImportedRawFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _project.ImportedRawFiles.Add(path);
            }
        }
    }

    private void SaveProject()
    {
        string path = _project.ProjectFilePath;
        if (string.IsNullOrWhiteSpace(path)) { using var d = new SaveFileDialog { Filter = "HydroTerra field projects|*.htfdc", DefaultExt = "htfdc" }; if (d.ShowDialog(this) != DialogResult.OK) return; path = d.FileName; }
        _project.ProjectFilePath = path; File.WriteAllText(path, JsonSerializer.Serialize(_project, new JsonSerializerOptions { WriteIndented = true })); _projectStatus.Text = Path.GetFileName(path); MessageBox.Show(this, "Project saved.", "HydroTerra", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenProject()
    {
        using var d = new OpenFileDialog { Filter = "HydroTerra field projects|*.htfdc" }; if (d.ShowDialog(this) != DialogResult.OK) return;
        try { var loaded = JsonSerializer.Deserialize<FieldDataProject>(File.ReadAllText(d.FileName)); if (loaded == null) throw new InvalidDataException("Project file is empty."); _project = loaded; _project.ProjectFilePath = d.FileName; _stepIndex = 0; ShowStep(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open project", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string Friendly(string value)
    {
        return value switch
        {
            nameof(SurveyDataType.SingleBeamHighFrequency) => "Single Beam / High Frequency",
            nameof(SurveyDataType.SingleBeamLowFrequency) => "Single Beam / Low Frequency",
            nameof(SurveyDataType.SingleBeamDualFrequency) => "Single Beam / Dual Frequency",
            nameof(SurveyDataType.SingleBeamFrequencyUnknown) => "Single Beam / Frequency Unknown",
            _ => Regex.Replace(value, "([a-z])([A-Z])", "$1 $2")
        };
    }
}
