HydroTerra consolidated UI fix v0.33.5

This replaces the two earlier partial fixes for:
1. Bottom wizard buttons being clipped at high Windows DPI/text scaling.
2. Step 8 criteria checkboxes not updating an already-open Plan View.

INSTALL
1. Extract this ZIP anywhere.
2. Open PowerShell in the extracted folder.
3. Run, replacing the path with your cloned GitHub repository:

   powershell -ExecutionPolicy Bypass -File .\apply_fix.ps1 -RepositoryRoot "C:\Users\jason\Documents\GitHub\HydroTerraFieldDataCompiler"

4. Build the solution locally.
5. Open Step 8, click Open Plan View, leave it open, then toggle criteria.
   The highlighted remaining portions should change immediately.
6. Confirm the Open / Back / Next / Save buttons are fully visible.

Git summary:
Fix footer clipping and live Step 8 map refresh
