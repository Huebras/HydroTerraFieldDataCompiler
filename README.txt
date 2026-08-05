HydroTerra Step 8 Live Map Update Patch

This patch fixes the issue where checking or unchecking the Step 8 remaining-line criteria did not update the plan view.

Changes:
- Criteria checkboxes immediately rerun the remaining-line analysis.
- The Unsurveyed Portions table refreshes immediately.
- Plan View is now modeless, so it can remain open while criteria are changed.
- An open Plan View receives the recalculated gaps and highlights immediately.
- DXF and LNW exports continue to use the same current results shown in the map.

INSTALLATION
1. Close Visual Studio and the HydroTerra application.
2. Back up or commit your current work in GitHub Desktop.
3. Replace:
   src\HydroTerraFieldDataCompiler\PlanViewForm.cs
   with the PlanViewForm.cs included in this patch.
4. Apply MainWizardForm_live_map.patch to MainWizardForm.cs, or manually make the edits shown in the patch file.
   Do NOT replace your entire MainWizardForm.cs, because it may contain the Requirements Engine and LOG auto-load updates.
5. Build locally and test Step 8.
6. Commit summary:
   Make Step 8 criteria update plan view live

Expected behavior:
- Open Plan View.
- Leave it open.
- Check or uncheck Coverage, Offline, RTK, Navigation Integrity, or Depth QA.
- The line table, remaining portions, and open map update automatically.
