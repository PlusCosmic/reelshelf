
# BackupListResult


## Properties

Name | Type
------------ | -------------
`isConfigured` | boolean
`localFiles` | Array&lt;any&gt;
`remoteFiles` | Array&lt;any&gt;
`pendingSyncCount` | number

## Example

```typescript
import type { BackupListResult } from ''

// TODO: Update the object below with actual values
const example = {
  "isConfigured": null,
  "localFiles": null,
  "remoteFiles": null,
  "pendingSyncCount": null,
} satisfies BackupListResult

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as BackupListResult
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


