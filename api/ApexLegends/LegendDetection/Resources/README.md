# Legend detection resources

These files are embedded in the API and sent to the model on every detection request: `prompt.md` as the
system message, then `reference-sheet.png`, then the clip's six Bunny thumbnails, with the reply constrained
to `response-schema.json`.

- `prompt.md`: the instructions given to the model.
- `response-schema.json`: the JSON Schema for the structured output.
- `reference-sheet.png`: the legend portraits labelled with their names.

Each run records a prompt version, a hash of all three files, so editing any of them automatically separates
new results from old ones. When a legend is added to the sheet, add it to `ApexLegendNames` too; legends the
model returns that are not in that list are discarded.
