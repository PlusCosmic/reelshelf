
# FileEntry


## Properties

Name | Type
------------ | -------------
`name` | string
`path` | string
`isDirectory` | boolean
`size` | number
`lastModified` | Date

## Example

```typescript
import type { FileEntry } from ''

// TODO: Update the object below with actual values
const example = {
  "name": null,
  "path": null,
  "isDirectory": null,
  "size": null,
  "lastModified": null,
} satisfies FileEntry

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as FileEntry
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


