
# BackupSyncResult


## Properties

Name | Type
------------ | -------------
`success` | boolean
`message` | string
`filesUploaded` | number
`filesSkipped` | number
`bytesUploaded` | number
`filesDeleted` | number
`bytesDeleted` | number

## Example

```typescript
import type { BackupSyncResult } from ''

// TODO: Update the object below with actual values
const example = {
  "success": null,
  "message": null,
  "filesUploaded": null,
  "filesSkipped": null,
  "bytesUploaded": null,
  "filesDeleted": null,
  "bytesDeleted": null,
} satisfies BackupSyncResult

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as BackupSyncResult
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


