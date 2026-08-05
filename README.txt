HydroTerra v0.35 - Equipment-First Survey Detection

Copy the included src folder into the repository root and replace matching files.

Changes:
- EC1/EC2 records are classified by their referenced HYPACK device ID.
- For a recognized fathometer/echosounder:
  * Depth1 is always High Frequency.
  * Depth2, when present, is always Low Frequency.
- EC records from a magnetometer device are treated as magnetic observations and are excluded from depth QA and single-beam frequency counts.
- Adds an equipment-first detection model with observation counts and evidence.
- Step 4 displays the evidence supporting each detected survey type.
- The Word report includes a Detected Equipment and Survey-Type Evidence table.
- Generic header text no longer selects single beam or magnetometer by itself; active device-linked observations are required.

After copying:
1. Run the integrity scan again.
2. Verify the mixed MAG/SBES file detects both Magnetometer and Single Beam / Dual Frequency.
3. Verify the magnetometer-only survey no longer detects Single Beam.
4. Build locally and confirm GitHub Actions is green.

Suggested commit summary:
Add equipment-first survey detection

Build note:
The .NET SDK is not installed in the patch-generation environment, so this patch was not compiled here.
