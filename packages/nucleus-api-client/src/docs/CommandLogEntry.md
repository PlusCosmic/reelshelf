
# CommandLogEntry


## Properties

Name | Type
------------ | -------------
`id` | string
`userId` | string
`command` | string
`response` | string
`success` | boolean
`error` | string
`executedAt` | Date

## Example

```typescript
import type { CommandLogEntry } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "userId": null,
  "command": null,
  "response": null,
  "success": null,
  "error": null,
  "executedAt": null,
} satisfies CommandLogEntry

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as CommandLogEntry
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


