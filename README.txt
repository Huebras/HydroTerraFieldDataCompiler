HydroTerra Device-Aware EC1/EC2 Classification v0.34.1

Copy the included src folder into the root of your repository and allow Windows to replace the two matching files.

Changes:
- Parses both EC1 and EC2 records while preserving the HYPACK device ID.
- Treats the number after EC1/EC2 as the source device ID.
- EC records from a magnetometer device are classified as magnetometer observations.
- EC records from a single-beam/echosounder device are classified as depth observations.
- Magnetometer EC records no longer count toward high-frequency or low-frequency depth totals.
- Magnetometer EC records no longer trigger single-beam auto-selection, BIN requirements, depth QA, or single-beam reporting.
- EC records tied to an unknown device create an ECDEV001 review warning instead of being guessed as single beam.

After copying:
1. Run build_windows.bat.
2. Re-import and rescan the magnetometer survey.
3. Confirm Step 4 selects Magnetometer without Single Beam.
4. Regenerate the report and confirm Section 6 no longer lists Single Beam / High Frequency for magnetometer-only data.

Suggested commit summary:
Fix device-aware EC1 and EC2 survey classification
