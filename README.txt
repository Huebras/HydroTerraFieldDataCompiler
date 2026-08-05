HydroTerra Field Data Compiler v0.30

HYPACK LOG import and reconciliation
- Step 2 accepts .RAW, .LOG, and .ZIP files.
- LOG files are treated as survey organization sources, not sensor observations.
- Direct LOG files and LOG entries embedded in loaded ZIP archives are parsed.
- Referenced RAW files, listed order, missing files, and loaded-but-unlisted files are reported.
- LOG ordering is used to organize the scanned RAW inventory where possible.
- Original direct LOG files are retained in the compiled package and LOG reconciliation is included in the Word report.

HydroTerra Field Data Compiler v0.7

This build begins the structured HYPACK RAW parsing engine.

New parser foundation:
- Every RAW line is classified by record type.
- Dedicated typed records for QUA, POS, and survey-line records.
- Per-file record-type inventory.
- HYPACK QUA decoding is now the primary GNSS quality source.
- QUA mode codes are mapped to fixed, float, differential, autonomous,
  dead-reckoning, and invalid solutions.
- Quality samples retain device ID, time, HDOP, satellite count, source line,
  and active survey-line association.
- Optional GGA and proprietary-message parsing remains as a fallback.

Build:
1. Run build_windows.bat.
2. Launch bin\HydroTerraFieldDataCompiler.exe.
3. Import the same RAW files and run Scan Files.

The parser is intentionally extensible. Additional typed record classes will be
added as representative HYPACK projects identify the exact formats used for
configuration, devices, offsets, geodesy, depth, motion, tides, and calibration.


v0.8: Driver-aware QUA interpretation. Code 7 remains unresolved for generic streams and is treated as RTK fixed only when Applanix/POS MV or VRS/NTRIP context is detected. Raw QUA fields, profile, confidence, and interpretation notes are preserved.

Version 0.9 additions
- Uses the first loaded RAW file as the INI header baseline.
- Parses every INI key/value pair in each RAW header.
- Compares subsequent files against the baseline.
- Warns on changed, missing, or newly added INI settings.
- Displays baseline/match/warning status in the file inventory.


VERSION 0.11 REFERENCE CALIBRATION
----------------------------------
This build was calibrated against the supplied 050526PL MAG survey package.
Observed QUA layout: QUA device time solutionCode valueCount hdop satellites correctionAge ...
Observed devices: GPS NMEA-0183 (ID 0), Magnetometer Interface (ID 1), Towfish (ID 2).
Observed GPS offsets: starboard -6.400, forward 0.000, vertical -6.562.
Observed quality: code 7 with INI RTKMode=4, interpreted as RTK fixed.


v0.12 additions:
- Single-beam survey recognition from EC2 and ECHOTRAC device records
- Typed EC2, TID, PRD, and FIX parsing
- RAW/BIN pair validation
- Sounding vs navigation time coverage checks
- Single-beam file summary columns


VERSION 0.13 - PROJECT HEALTH
- Project Health dashboard with pass/warning/failure status and percentage score
- Survey-type-specific single-beam completeness rules
- Baseline configuration fingerprint from INI, DEV, and OFF metadata
- Header/configuration mismatch counts
- DSO bar-check attachment requirement for detected ECHOTRAC single-beam surveys
- SVP, tide/vertical-control, RAW/BIN, GNSS quality, device, offset, and geodesy health checks


v0.14 geodesy update
- Parses HYPACK Grid, Projection, ZoneName, ZoneId, UnitsName, Unit, Ellipsoid, Geoid, VDatum, VSurface, and projection parameters.
- Separates horizontal datum from ellipsoid.
- Normalizes LCC/TM/UTM projection names and U.S. survey-foot units.
- Validates POS coordinate ranges against the detected CRS family.
- Displays recorded and approved geodesy separately with evidence and validation messages.
- Includes geodesy validation in Project Health.


VERSION 0.15 DEVICE AND OFFSET REVIEW
- Responsive master/detail device register.
- Recorded versus approved offsets, orientation, and latency.
- Header evidence and integrity warnings per device.
- Accept/restore controls and required correction reasons.
- Offset approval audit history.

v0.19 changes
- Renamed Offset Approval History to Device Offsets.
- Selected-device recorded and approved offsets are shown in an editable table.
- Added Create Edited RAW Copies.
- Edited copies are written to an Edited_RAW export folder; original RAW/ZIP sources are never changed.
- The export includes an Edited_RAW_Manifest.csv and README_EDITED_RAW.txt audit note.

VERSION 0.17 RTK-TIDE SAFE EXPORT
- Verifies the source relationship TID = -(POS vertical + recorded positioning-device vertical offset).
- Requires at least 95 percent of matched epochs to agree within 0.015 source units.
- Recalculates TID only when the same TID/POS device has an approved vertical-offset change.
- Uses new TID = old TID - (approved vertical offset - recorded vertical offset).
- Blocks export when TID records are present but the source relationship cannot be verified.
- Reports OFF changes, TID changes, and validation counts in the export manifest and completion message.


v0.22 changes
- Repeated RAW files with the same normalized line name and matching planned geometry are merged into one line-coverage result.
- The line summary shows segment count and all contributing RAW files.
- Coverage, offline warnings, gaps, plan view, and gap DXF export use all merged segments.
- Step 4 now distinguishes Single Beam / High Frequency, Low Frequency, Dual Frequency, and Frequency Unknown.
- EC2 channel presence is detected independently; missing unused channels do not automatically produce a warning.

v0.22 changes
- Adds merged-line GNSS quality statistics: fixed percentage, non-fixed/unknown counts, average HDOP, and minimum satellites.
- Adds configurable minimum RTK-fixed percentage on the Survey Lines page.
- Adds single-beam depth QA that follows the confirmed High Frequency, Low Frequency, Dual Frequency, or Frequency Unknown selection.
- Reports valid/invalid depth counts, abrupt spikes, frozen-value runs, channel ranges, and dual-channel comparison counts.
- Adds line-level positioning and depth-quality findings to Project Health.
- Preserves the existing wizard, line merging, plan view, gap DXF export, edited RAW export, and RTK TID recalculation workflows.

Version 0.23 restores the Plan View button with a wrapping toolbar and adds supporting-file inventory, duplicate checks, SHA-256 metadata, and compiled submittal ZIP creation.

Version 0.23.4: Depth QA line warnings now use minimum counts and percentages; isolated invalid samples, spikes, or a single frozen run remain visible in the rule inspector but do not highlight the entire line.

VERSION 0.24 ADDITIONS
- Package Review page with required/optional inclusion controls.
- Review and Sign-Off page.
- Final Compile Package page.
- Word field-data QA report generation.
- Word report automatically included under 05_Reports in the compiled ZIP.
- Required package items are checked before compilation.


v0.26 Data Integrity release adds per-line timing, speed, navigation gap, duplicate timestamp, position freeze, impossible jump, and integrity-score checks.


v0.26 UI/QA refinement:
- Report generation is available only on Step 14 and is labeled Generate Report.
- Survey-line rows are highlighted only for active rule failures; isolated navigation timing gaps no longer highlight an entire line unless there are at least 3 gaps, at least 5 estimated missing epochs, a score below 90, or a serious jump/freeze/time reversal.


Version 0.26 workflow update:
- Consolidated the wizard to nine steps.
- Step 9 is Finalize Project and includes supporting-file upload, including bar-check DSO and SVP files.
- Removed separate Bar Check, SVP, Package Review, and Review/Sign-Off pages.
- Removed the premature Compile Submittal button from the supporting-file toolbar.
- Final actions are Generate Report, Compile Package, and Open Output Folder.


v0.27.3 adds explicit readiness sections, package preview controls, and post-build output/checksum actions on Finalize Project.

Version 0.28 survey-type requirements update:
- BIN pairing is evaluated only for detected single-beam data.
- Bar-check and sound-velocity review apply only to single-beam projects.
- Magnetometer-only projects show BIN, bar check, and SVP as not applicable and are not penalized for their absence.
- Mixed single-beam plus magnetometer projects retain the single-beam requirements while also evaluating magnetometer records and configuration.
- HYPACK .VEL files are recognized as sound-velocity profiles.

Version 0.30 reactive survey-type rules update:
- Step 4 confirmed selections are now the source of truth for applicable QA and package rules.
- Automatic detection preselects survey types but no longer overrides manual choices on later scans.
- Checking or unchecking a type immediately refreshes line QA, Project Health, BIN applicability, bar-check/SVP applicability, and active-rule summaries.
- Step 4 shows whether each type was automatically detected.
