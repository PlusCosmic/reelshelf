You are analyzing screenshots from a single Apex Legends gameplay clip.

You are given:
- one reference sheet containing playable Apex Legends characters and their names
- 6 screenshots sampled from the same gameplay clip

Your task is to identify:
1. the legend being played by the clip owner
2. the clip owner's in-game name
3. the legends being played by each teammate
4. each teammate's in-game name

Use all 6 screenshots together as evidence. Do not analyze each screenshot independently if information from multiple screenshots can be combined.

In normal Apex Legends gameplay, the squad HUD is typically visible in the bottom-left of the screen.

The clip owner's own squad panel is usually the bottom entry in the squad stack, with teammates displayed above it. However, do not assume this is always true if the HUD state clearly differs.

Use the supplied character reference sheet as the authoritative visual reference for legend identification.

Only return a legend name if that legend is present in the supplied reference sheet.

For the clip owner's legend:
- prioritize the player's squad portrait
- also use supporting evidence where available, including tactical ability icons, ultimate icons, passive indicators, or other legend-specific HUD elements

For teammate legends:
- prioritize the portraits visible in the squad HUD
- compare those portraits directly against the supplied reference sheet

For player names:
- read the visible in-game name exactly as shown where possible
- preserve capitalization, spaces, numbers, clan tags, brackets, and symbols
- do not normalize or rewrite names unnecessarily

Use multiple screenshots to resolve unclear portraits or text. A portrait or name may be sharper or less obstructed in one screenshot than another.

Do not infer identities from prior knowledge of usernames, players, streamers, or known squads.

Do not guess when the visual evidence is insufficient.

If a value cannot be identified reliably, return null for that field.

Confidence values must be numbers between 0 and 1 and should reflect confidence based only on visible evidence in the supplied images.

Use confidence above 0.90 only when:
- the visual evidence is clear, or
- the same identification is supported by multiple independent visual cues

If a portrait or name is ambiguous, assign a lower confidence rather than forcing a high-confidence answer.

Return only data matching the provided JSON schema.