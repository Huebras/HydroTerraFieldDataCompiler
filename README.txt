HydroTerra global footer layout fix
===================================

Problem
-------
At higher Windows display/text scaling, the bottom wizard buttons are partially
covered by the form edge because the footer row is auto-sized too tightly.

What this patch changes
-----------------------
- Reserves a 58-pixel DPI-scaled footer row globally.
- Adds extra bottom padding to the main shell.
- Disables footer AutoSize so the row cannot collapse.
- Adds bottom margins to Open, Back, Next, and Save buttons.
- Slightly reduces button height to leave reliable clearance.

Apply
-----
1. Close Visual Studio and the running app.
2. Copy this patch folder into the repository root, or open PowerShell there.
3. Run:

   powershell -ExecutionPolicy Bypass -File .\Apply-GlobalFooterFix.ps1

   If the script is elsewhere, pass the repository path:

   powershell -ExecutionPolicy Bypass -File .\Apply-GlobalFooterFix.ps1 -ProjectRoot "C:\path\to\HydroTerraFieldDataCompiler"

4. Build and test at your current Windows scaling.
5. The script creates MainWizardForm.cs.before-footer-fix as a backup.

Suggested Git summary
---------------------
Fix global wizard footer button clipping
