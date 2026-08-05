HydroTerra Magnetometer QA v0.34

Copy the contents of the included src folder into the repository root and allow Windows to replace matching files.

Changed files:
- MainWizardForm.cs
- ProjectHealthEvaluator.cs
- WordReportGenerator.cs
- Models/ProjectModels.cs
New file:
- MagnetometerQaAnalyzer.cs

Initial QA checks:
- Magnetometer device identification from DEV metadata
- Record counts by HYPACK LNN line
- Invalid/non-numeric values
- Frozen runs (10 or more identical values)
- Timing gaps above 3x the average interval (minimum 1 second)
- Estimated missing record count
- Per-line minimum and maximum
- QA findings, Project Health rows, and Word report table

Important:
The uploaded towfish sample records magnetometer observations as EC2 records from the Magnetometer Interface device ID. The analyzer therefore classifies data by device ID rather than assuming EC2 always means single-beam depth.

Build validation:
This environment does not contain the .NET SDK. Run build_windows.bat and confirm the GitHub Actions build is green.

Suggested commit summary:
Add initial magnetometer QA analysis
