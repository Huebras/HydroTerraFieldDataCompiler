HydroTerra HYPACK LOG Loader Fix v0.33.3
========================================

Purpose
-------
This patch makes a selected HYPACK .LOG file act as an import entry point.
All RAW files referenced by the LOG are resolved and added to the same
ImportedRawFiles collection used by manual RAW selection.

Files
-----
1. Copy:
   src/HydroTerraFieldDataCompiler/Parsing/SurveyImportResolver.cs
   to the matching Parsing folder in your repository.

2. Open MainWizardForm_AddPaths_replacement.txt and replace only the existing
   AddPaths(IEnumerable<string> paths) method in MainWizardForm.cs.

Do not replace your entire MainWizardForm.cs because it may already contain
Requirements Engine and Step 8 live-map changes.

Validated input
---------------
The resolver was designed and checked against the uploaded towfish package,
whose RAW04222026.LOG contains 29 bare RAW filenames located beside the LOG.
The expected result is 1 LOG plus 29 RAW paths added to the import inventory.

Resolution order
----------------
1. Existing absolute RAW path
2. Path relative to the LOG file
3. Recursive filename search under the LOG folder
4. Unresolved warning with the exact LOG/reference pair

GitHub commit summary
---------------------
Fix LOG import to load referenced RAW files
