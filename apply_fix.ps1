param(
    [string]$RepositoryRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$main = Join-Path $RepositoryRoot 'src\HydroTerraFieldDataCompiler\MainWizardForm.cs'
$plan = Join-Path $RepositoryRoot 'src\HydroTerraFieldDataCompiler\PlanViewForm.cs'
$replacementPlan = Join-Path $PSScriptRoot 'replacement\src\HydroTerraFieldDataCompiler\PlanViewForm.cs'

if (!(Test-Path $main)) { throw "MainWizardForm.cs was not found at $main" }
if (!(Test-Path $plan)) { throw "PlanViewForm.cs was not found at $plan" }

Copy-Item $main "$main.before_v0.33.5.bak" -Force
Copy-Item $plan "$plan.before_v0.33.5.bak" -Force
Copy-Item $replacementPlan $plan -Force

$text = Get-Content $main -Raw

# Add a persistent modeless plan-view field once.
if ($text -notmatch 'private PlanViewForm\? _openPlanView') {
    $text = $text.Replace(
        '    private string _lastPackageSha256 = string.Empty;',
        "    private string _lastPackageSha256 = string.Empty;`r`n    private PlanViewForm? _openPlanView;"
    )
}

# Reserve a real footer row rather than letting AutoSize clip it at high DPI.
$text = $text.Replace(
    'shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));`r`n`r`n        var headerPanel',
    'shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));`r`n`r`n        var headerPanel'
)
$text = $text.Replace(
    'Padding = new Padding(16, 12, 16, 12),',
    'Padding = new Padding(16, 12, 16, 18),'
)
$text = $text.Replace(
    '            Padding = new Padding(0, 8, 0, 0),`r`n            Margin = Padding.Empty,`r`n            AutoSize = true,`r`n            AutoSizeMode = AutoSizeMode.GrowAndShrink,`r`n            MinimumSize = new Size(0, 48)',
    '            Padding = new Padding(0, 8, 0, 10),`r`n            Margin = Padding.Empty,`r`n            AutoSize = false,`r`n            MinimumSize = new Size(0, 58)'
)
$text = $text.Replace(
    'private static Button MakeButton(string text) => new() { Text = text, Size = new Size(110, 34), Margin = new Padding(6, 0, 0, 0) };',
    'private static Button MakeButton(string text) => new() { Text = text, Size = new Size(110, 32), Margin = new Padding(6, 4, 0, 8) };'
)
$text = $text.Replace('_nextButton.Size = new Size(100, 34);','_nextButton.Size = new Size(100, 32);`r`n        _nextButton.Margin = new Padding(6, 4, 0, 8);')
$text = $text.Replace('_backButton.Size = new Size(100, 34);','_backButton.Size = new Size(100, 32);`r`n        _backButton.Margin = new Padding(6, 4, 0, 8);')

$oldHandlers = @'
        useCoverage.CheckedChanged += (_, _) => { _project.UsePositionCoverageForRemainingLines = useCoverage.Checked; useOffline.Enabled = useCoverage.Checked; };
        useOffline.CheckedChanged += (_, _) => _project.UseOfflineToleranceForCoverage = useOffline.Checked;
        useRtk.CheckedChanged += (_, _) => _project.UsePositionQualityForRemainingLines = useRtk.Checked;
        useNav.CheckedChanged += (_, _) => _project.UseNavigationIntegrityForRemainingLines = useNav.Checked;
        useDepth.CheckedChanged += (_, _) => _project.UseDepthQaForRemainingLines = useDepth.Checked;
'@
$newHandlers = @'
        void RefreshRemainingCriteria()
        {
            _project.UsePositionCoverageForRemainingLines = useCoverage.Checked;
            _project.UseOfflineToleranceForCoverage = useOffline.Checked;
            _project.UsePositionQualityForRemainingLines = useRtk.Checked;
            _project.UseNavigationIntegrityForRemainingLines = useNav.Checked;
            _project.UseDepthQaForRemainingLines = useDepth.Checked;
            _project.OfflineToleranceFeet = (double)tolerance.Value;
            _project.CoverageGapFeet = (double)gapSize.Value;
            _project.MinimumFixedPercent = (double)fixedPercent.Value;
            _project.DepthSpikeThresholdFeet = (double)depthSpike.Value;

            RunLineCoverageAnalysis(false);
            if (_openPlanView is { IsDisposed: false })
                _openPlanView.UpdateResults(_project.LineCoverageResults, preserveView: true);
            ShowStep();
        }

        useCoverage.CheckedChanged += (_, _) => { useOffline.Enabled = useCoverage.Checked; RefreshRemainingCriteria(); };
        useOffline.CheckedChanged += (_, _) => RefreshRemainingCriteria();
        useRtk.CheckedChanged += (_, _) => RefreshRemainingCriteria();
        useNav.CheckedChanged += (_, _) => RefreshRemainingCriteria();
        useDepth.CheckedChanged += (_, _) => RefreshRemainingCriteria();
'@
if ($text.Contains($oldHandlers)) {
    $text = $text.Replace($oldHandlers, $newHandlers)
} elseif ($text -notmatch 'void RefreshRemainingCriteria\(\)') {
    throw 'Could not find the Step 8 checkbox handler block. No changes were written.'
}

$oldPlan = @'
        planView.Click += (_, _) =>
        {
            if (_project.LineCoverageResults.Count == 0) RunLineCoverageAnalysis(false);
            using var view = new PlanViewForm(_project.LineCoverageResults);
            view.ShowDialog(this);
        };
'@
$newPlan = @'
        planView.Click += (_, _) =>
        {
            if (_project.LineCoverageResults.Count == 0) RunLineCoverageAnalysis(false);
            if (_openPlanView is null || _openPlanView.IsDisposed)
            {
                _openPlanView = new PlanViewForm(_project.LineCoverageResults);
                _openPlanView.FormClosed += (_, _) => _openPlanView = null;
                _openPlanView.Show(this);
            }
            else
            {
                _openPlanView.UpdateResults(_project.LineCoverageResults, preserveView: true);
                _openPlanView.Activate();
            }
        };
'@
if ($text.Contains($oldPlan)) {
    $text = $text.Replace($oldPlan, $newPlan)
} elseif ($text -notmatch '_openPlanView\.Show\(this\)') {
    throw 'Could not find the Open Plan View handler. No changes were written.'
}

Set-Content $main $text -Encoding UTF8
Write-Host 'Applied the consolidated footer and Step 8 live-map fix.' -ForegroundColor Green
Write-Host 'Backups were created beside both modified source files.'
