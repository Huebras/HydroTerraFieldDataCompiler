HydroTerra HYPACK LOG Auto-Load Patch

Problem fixed
-------------
The existing LOG implementation parsed and reconciled references only after RAW
files had already been loaded. Selecting a LOG by itself therefore loaded no RAW
survey data.

Behavior after patch
--------------------
- Selecting a direct .LOG file automatically reads its referenced .RAW names.
- Absolute paths are used when valid.
- Relative paths are resolved from the LOG file's folder.
- If a referenced path is stale or contains only a filename, the app searches
  the LOG folder and its subfolders for a matching RAW filename.
- Discovered RAW files are added to ImportedRawFiles and are ready for the normal
  integrity scan.
- Missing references remain visible through existing LOG reconciliation warnings.

Installation
------------
1. Replace:
   src/HydroTerraFieldDataCompiler/Parsing/HypackLogParser.cs

2. Open MainWizardForm_AddPaths_REPLACEMENT.txt and replace only the existing
   AddPaths(IEnumerable<string> paths) method in MainWizardForm.cs.

3. Build locally.
4. Commit with summary:
   Auto-load RAW files referenced by HYPACK LOG
5. Push and confirm the GitHub Actions build is green.
