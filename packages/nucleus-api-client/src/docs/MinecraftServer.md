
# MinecraftServer


## Properties

Name | Type
------------ | -------------
`id` | string
`name` | string
`ownerId` | string
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
`createdAt` | Date

## Example

```typescript
import type { MinecraftServer } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "name": null,
  "ownerId": null,
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
  "createdAt": null,
} satisfies MinecraftServer

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as MinecraftServer
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


