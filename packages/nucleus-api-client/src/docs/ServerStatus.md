
# ServerStatus


## Properties

Name | Type
------------ | -------------
`isOnline` | boolean
`onlinePlayers` | number
`maxPlayers` | number
`motd` | string
`version` | string

## Example

```typescript
import type { ServerStatus } from ''

// TODO: Update the object below with actual values
const example = {
  "isOnline": null,
  "onlinePlayers": null,
  "maxPlayers": null,
  "motd": null,
  "version": null,
} satisfies ServerStatus

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ServerStatus
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


