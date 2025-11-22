
# ReorderPlaylistClipsRequest


## Properties

Name | Type
------------ | -------------
`clipId` | string
`newPosition` | number
`clipOrdering` | Array&lt;string&gt;

## Example

```typescript
import type { ReorderPlaylistClipsRequest } from ''

// TODO: Update the object below with actual values
const example = {
  "clipId": null,
  "newPosition": null,
  "clipOrdering": null,
} satisfies ReorderPlaylistClipsRequest

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ReorderPlaylistClipsRequest
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


