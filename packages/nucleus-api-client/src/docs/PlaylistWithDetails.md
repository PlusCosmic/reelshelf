
# PlaylistWithDetails


## Properties

Name | Type
------------ | -------------
`id` | string
`name` | string
`description` | string
`creatorUserId` | string
`createdAt` | Date
`updatedAt` | Date
`collaborators` | [Array&lt;PlaylistCollaborator&gt;](PlaylistCollaborator.md)
`clips` | [Array&lt;PlaylistClip&gt;](PlaylistClip.md)

## Example

```typescript
import type { PlaylistWithDetails } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "name": null,
  "description": null,
  "creatorUserId": null,
  "createdAt": null,
  "updatedAt": null,
  "collaborators": null,
  "clips": null,
} satisfies PlaylistWithDetails

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as PlaylistWithDetails
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


