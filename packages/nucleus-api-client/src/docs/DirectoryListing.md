
# DirectoryListing


## Properties

Name | Type
------------ | -------------
`currentPath` | string
`entries` | [Array&lt;FileEntry&gt;](FileEntry.md)

## Example

```typescript
import type { DirectoryListing } from ''

// TODO: Update the object below with actual values
const example = {
  "currentPath": null,
  "entries": null,
} satisfies DirectoryListing

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as DirectoryListing
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


