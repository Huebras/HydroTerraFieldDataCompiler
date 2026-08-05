param(
    [string]$RepositoryRoot = (Get-Location).Path
)

$target = Join-Path $RepositoryRoot "src\HydroTerraFieldDataCompiler\MainWizardForm.cs"
if (-not (Test-Path $target)) {
    throw "Could not find MainWizardForm.cs at: $target"
}

$text = Get-Content -Raw -Path $target
$startMarker = "    private Control BuildProjectSetupPage()"
$endMarker = "    private Control BuildImportPage()"
$start = $text.IndexOf($startMarker)
$end = $text.IndexOf($endMarker)
if ($start -lt 0 -or $end -le $start) {
    throw "Could not locate BuildProjectSetupPage() in MainWizardForm.cs"
}

$replacement = @'
    private Control BuildProjectSetupPage()
    {
        var host = new Panel
        {
            AutoScroll = true,
            Padding = new Padding(24, 20, 32, 28)
        };

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            Dock = DockStyle.Top,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        void AddRow(string labelText, Control editor, int minimumHeight = 38)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 14, 8)
            };

            editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            editor.Margin = new Padding(0, 4, 0, 4);
            editor.MinimumSize = new Size(320, minimumHeight - 8);

            table.Controls.Add(label, 0, row);
            table.Controls.Add(editor, 1, row);
        }

        TextBox TextEditor(string value, Action<string> setter, bool multiline = false)
        {
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Dock = DockStyle.Fill,
                Height = multiline ? 100 : 30
            };
            box.TextChanged += (_, _) => setter(box.Text);
            return box;
        }

        DateTimePicker DateEditor(DateTime? value, Action<DateTime?> setter)
        {
            var picker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                ShowCheckBox = true,
                Checked = value.HasValue,
                Value = value ?? DateTime.Today,
                Width = 220
            };
            picker.ValueChanged += (_, _) => setter(picker.Checked ? picker.Value.Date : null);
            return picker;
        }

        AddRow("Project Name", TextEditor(_project.ProjectName, v => _project.ProjectName = v));
        AddRow("Project Number", TextEditor(_project.ProjectNumber, v => _project.ProjectNumber = v));
        AddRow("Client", TextEditor(_project.Client, v => _project.Client = v));
        AddRow("Location", TextEditor(_project.Location, v => _project.Location = v));
        AddRow("Vessel", TextEditor(_project.Vessel, v => _project.Vessel = v));
        AddRow("Field Crew", TextEditor(_project.FieldCrew, v => _project.FieldCrew = v));
        AddRow("Survey Start", DateEditor(_project.SurveyStartDate, v => _project.SurveyStartDate = v));
        AddRow("Survey End", DateEditor(_project.SurveyEndDate, v => _project.SurveyEndDate = v));
        AddRow("Notes", TextEditor(_project.Notes, v => _project.Notes = v, true), 110);

        host.Controls.Add(table);
        return host;
    }

'@

$newText = $text.Substring(0, $start) + $replacement + $text.Substring($end)
Set-Content -Path $target -Value $newText -Encoding UTF8
Write-Host "Step 1 responsive layout applied to $target"
