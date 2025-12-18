
# UpdateMinecraftServerRequest


## Properties

Name | Type
------------ | -------------
`name` | string
`persistenceLocation` | string
`containerName` | string
`cpuReservation` | number
`ramReservation` | number
`cpuLimit` | number
`ramLimit` | number
`serverType` | number
`minecraftVersion` | string
`modloaderVersion` | string
`curseforgePageUrl` | string
`isActive` | boolean

## Example

```typescript
import type { UpdateMinecraftServerRequest } from ''

// TODO: Update the object below with actual values
const example = {
  "name": null,
  "persistenceLocation": null,
  "containerName": null,
  "cpuReservation": null,
  "ramReservation": null,
  "cpuLimit": null,
  "ramLimit": null,
  "serverType": null,
  "minecraftVersion": null,
  "modloaderVersion": null,
  "curseforgePageUrl": null,
  "isActive": null,
} satisfies UpdateMinecraftServerRequest

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as UpdateMinecraftServerRequest
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


