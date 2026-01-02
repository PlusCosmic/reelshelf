
# Clip


## Properties

Name | Type
------------ | -------------
`clipId` | string
`ownerId` | string
`videoId` | string
`gameCategoryId` | string
`categorySlug` | string
`createdAt` | Date
`video` | [BunnyVideo](BunnyVideo.md)
`tags` | Array&lt;string&gt;
`isViewed` | boolean
`gameMetadata` | any

## Example

```typescript
import type { Clip } from ''

// TODO: Update the object below with actual values
const example = {
  "clipId": null,
  "ownerId": null,
  "videoId": null,
  "gameCategoryId": null,
  "categorySlug": null,
  "createdAt": null,
  "video": null,
  "tags": null,
  "isViewed": null,
  "gameMetadata": null,
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


