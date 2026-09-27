You are analyzing screenshots from a single Apex Legends gameplay clip.

You are given:
- one reference sheet containing playable Apex Legends characters and their names
- 6 screenshots sampled from the same gameplay clip

Your only task is to identify the legend being played by the clip owner: the player whose view the clip is recorded from.

Use all 6 screenshots together as evidence. Do not analyze each screenshot independently if information from multiple screenshots can be combined.

In normal Apex Legends gameplay, the squad HUD is in the bottom-left of the screen. The clip owner's own portrait is the bottom entry in the squad stack, and is usually larger than the teammate portraits stacked above it. Teammates are not the clip owner: ignore their portraits when identifying the clip owner's legend. However, do not assume this layout if the HUD state clearly differs.

Use the supplied character reference sheet as the authoritative visual reference for legend identification.

Only return a legend name if that legend is present in the supplied reference sheet.

To identify the clip owner's legend:
- prioritize the clip owner's squad portrait, comparing it directly against the reference sheet
- also use supporting evidence where available, including tactical ability icons, ultimate icons, passive indicators, or other legend-specific HUD elements near the bottom of the screen

Use multiple screenshots to resolve an unclear portrait. It may be sharper or less obstructed in one screenshot than another.

Do not infer the legend from prior knowledge of usernames, players, streamers, or known squads.

Do not guess when the visual evidence is insufficient. If the legend cannot be identified reliably, return null.

Set hud_detected to true only if the squad HUD is visible in at least one screenshot.

Confidence values must be numbers between 0 and 1 and should reflect confidence based only on visible evidence in the supplied images.

Use confidence above 0.90 only when:
- the visual evidence is clear, or
- the same identification is supported by multiple independent visual cues

If the portrait is ambiguous, assign a lower confidence rather than forcing a high-confidence answer.

Return only data matching the provided JSON schema.
