---
description: Export the active document as STEP and STL (or other formats) next to the file
argument-hint: [formats, folder]
---
Export the active SOLIDWORKS document. Formats and folder: $ARGUMENTS (default: STEP AP214 and binary STL in mm, in an `Outputs` folder next to the file).

Follow the `solidworks-api` skill section 8:
- `Extension.SaveAs3` with `swSaveAsOptions_Silent | swSaveAsOptions_Copy`, so the open document is not renamed
- set the STEP AP and the STL options, and restore every preference afterwards
- keep the paths under 260 characters

Report each file with its path and size.
