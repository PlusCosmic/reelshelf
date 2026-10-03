# Clip summary resources

These files are embedded in the API and sent to the model on every summary request: `prompt.md` as the system
message, then a user message built by `ClipSummaryInput` with the clip's game, title, date, the detected legend
for Apex clips, and the transcript, with the reply constrained to `response-schema.json`.

- `prompt.md`: the instructions given to the model.
- `response-schema.json`: the JSON Schema for the structured output.

Each run records a prompt version, a hash of both files and the request layout, so editing either automatically
separates new results from old ones. The mood tags in the schema's enum must match `ClipMoodTags.All`; tags the
model returns that are not in that list are discarded, as are quotes that don't appear in the transcript.
