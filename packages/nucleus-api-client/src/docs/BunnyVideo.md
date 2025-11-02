
# BunnyVideo


## Properties

Name | Type
------------ | -------------
`videoLibraryId` | [GetVideosByCategoryPageParameter](GetVideosByCategoryPageParameter.md)
`guid` | string
`title` | string
`dateUploaded` | Date
`length` | [GetVideosByCategoryPageParameter](GetVideosByCategoryPageParameter.md)
`status` | [GetVideosByCategoryPageParameter](GetVideosByCategoryPageParameter.md)
`framerate` | [BunnyVideoFramerate](BunnyVideoFramerate.md)
`thumbnailCount` | [GetVideosByCategoryPageParameter](GetVideosByCategoryPageParameter.md)
`encodeProgress` | [GetVideosByCategoryPageParameter](GetVideosByCategoryPageParameter.md)
`storageSize` | [BunnyVideoStorageSize](BunnyVideoStorageSize.md)
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


