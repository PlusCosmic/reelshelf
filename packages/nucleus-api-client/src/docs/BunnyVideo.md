
# BunnyVideo


## Properties

Name | Type
------------ | -------------
`videoLibraryId` | number
`guid` | string
`title` | string
`dateUploaded` | Date
`length` | number
`status` | number
`framerate` | number
`thumbnailCount` | number
`encodeProgress` | number
`storageSize` | number
`collectionId` | string
`thumbnailFileName` | string
`thumbnailBlurhash` | string
`category` | string
`moments` | [Array&lt;Moment&gt;](Moment.md)
`metaTags` | [Array&lt;MetaTag&gt;](MetaTag.md)

## Example

```typescript
import type { BunnyVideo } from ''

// TODO: Update the object below with actual values
const example = {
  "videoLibraryId": null,
  "guid": null,
  "title": null,
  "dateUploaded": null,
  "length": null,
  "status": null,
  "framerate": null,
  "thumbnailCount": null,
  "encodeProgress": null,
  "storageSize": null,
  "collectionId": null,
  "thumbnailFileName": null,
  "thumbnailBlurhash": null,
  "category": null,
  "moments": null,
  "metaTags": null,
} satisfies BunnyVideo

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as BunnyVideo
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


