HydroTerra Step 1 Responsive Layout Fix

Problem fixed:
- At higher Windows DPI or text scaling, Step 1 labels extended underneath the text boxes, hiding the beginning or ending of field labels/content.

What this patch changes:
- Replaces fixed X/Y positions with a two-column TableLayoutPanel.
- Labels receive a reserved DPI-safe column.
- Text boxes expand with the window.
- Notes remains multiline and scrollable.
- The page remains vertically scrollable on smaller displays.

Installation:
1. Copy this patch folder into any convenient location.
2. Open PowerShell in the repository root (the folder containing HydroTerraFieldDataCompiler.sln).
3. Run:

   powershell -ExecutionPolicy Bypass -File "PATH_TO_PATCH\apply_step1_layout_fix.ps1"

4. Build locally and review Step 1.
5. Commit the changed MainWizardForm.cs file.

Suggested commit summary:
Fix Step 1 field overlap at scaled display settings
