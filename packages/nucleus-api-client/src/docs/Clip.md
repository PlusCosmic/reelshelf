
# Clip


## Properties

Name | Type
------------ | -------------
`clipId` | string
`ownerId` | string
`videoId` | string
`categoryEnum` | number
`createdAt` | Date
`video` | [BunnyVideo](BunnyVideo.md)
`tags` | Array&lt;string&gt;
`isViewed` | boolean
`detectedLegend` | number
`detectedLegendCard` | string

## Example

```typescript
import type { Clip } from ''

// TODO: Update the object below with actual values
const example = {
  "clipId": null,
  "ownerId": null,
  "videoId": null,
  "categoryEnum": null,
  "createdAt": null,
  "video": null,
  "tags": null,
  "isViewed": null,
  "detectedLegend": null,
  "detectedLegendCard": null,
} satisfies Clip

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as Clip
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


