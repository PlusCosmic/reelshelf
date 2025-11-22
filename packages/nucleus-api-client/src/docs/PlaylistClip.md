
# PlaylistClip


## Properties

Name | Type
------------ | -------------
`id` | string
`clipId` | string
`position` | number
`addedByUserId` | string
`addedAt` | Date
`clipDetails` | [Clip2](Clip2.md)

## Example

```typescript
import type { PlaylistClip } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "clipId": null,
  "position": null,
  "addedByUserId": null,
  "addedAt": null,
  "clipDetails": null,
} satisfies PlaylistClip

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as PlaylistClip
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


