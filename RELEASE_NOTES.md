# 0.2.5 preview — 2026-10-16

- Merges newly generated outfits and distribution rules into the existing patch instead of overwriting previous selections.
- Repatching a source plugin updates its generated outfit and rules while keeping other plugins intact.
- Preserves existing plugin masters and adds masters needed by newly selected outfit pieces.
- Uses stable outfit IDs so merged rules continue referencing the right outfit records.
- Clarifies the merge behavior in the confirmation dialog.
