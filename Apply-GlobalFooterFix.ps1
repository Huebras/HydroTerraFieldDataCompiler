param(
    [string]$ProjectRoot = "."
)

$target = Join-Path $ProjectRoot "src\HydroTerraFieldDataCompiler\MainWizardForm.cs"
if (-not (Test-Path $target)) {
    throw "Could not find MainWizardForm.cs at: $target"
}

$text = Get-Content -Raw -Path $target
$original = $text

$text = $text.Replace(
'            Padding = new Padding(16, 12, 16, 12),',
'            Padding = new Padding(16, 12, 16, 20),')

$text = $text.Replace(
'        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));`r`n        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));`r`n        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));',
'        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));`r`n        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));`r`n        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));')

$text = $text.Replace(
'            Padding = new Padding(0, 8, 0, 0),`r`n            Margin = Padding.Empty,`r`n            AutoSize = true,`r`n            AutoSizeMode = AutoSizeMode.GrowAndShrink,`r`n            MinimumSize = new Size(0, 48)',
'            Padding = new Padding(0, 6, 0, 8),`r`n            Margin = Padding.Empty,`r`n            AutoSize = false,`r`n            Height = 58,`r`n            MinimumSize = new Size(0, 58)')

$text = $text.Replace(
'        _nextButton.Size = new Size(100, 34);',
'        _nextButton.Size = new Size(100, 32);`r`n        _nextButton.Margin = new Padding(4, 4, 4, 8);')

$text = $text.Replace(
'        _backButton.Size = new Size(100, 34);',
'        _backButton.Size = new Size(100, 32);`r`n        _backButton.Margin = new Padding(4, 4, 4, 8);')

# Apply the same margin to helper-created Open/Save buttons.
$text = $text.Replace(
'        var saveButton = MakeButton("Save Project");',
'        var saveButton = MakeButton("Save Project");`r`n        saveButton.Margin = new Padding(4, 4, 4, 8);')
$text = $text.Replace(
'        var openButton = MakeButton("Open Project");',
'        var openButton = MakeButton("Open Project");`r`n        openButton.Margin = new Padding(4, 4, 4, 8);')

if ($text -eq $original) {
    throw "No matching layout block was found. The file may already be patched or has changed significantly."
}

Copy-Item $target "$target.before-footer-fix" -Force
Set-Content -Path $target -Value $text -Encoding UTF8
Write-Host "Patched: $target"
Write-Host "Backup: $target.before-footer-fix"
