
# ContainerStateResponse


## Properties

Name | Type
------------ | -------------
`_exists` | boolean
`status` | string
`isRunning` | boolean
`containerId` | string
`startedAt` | Date
`cpuPercent` | number
`memoryUsedMb` | number
`memoryLimitMb` | number
`memoryPercent` | number

## Example

```typescript
import type { ContainerStateResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "_exists": null,
  "status": null,
  "isRunning": null,
  "containerId": null,
  "startedAt": null,
  "cpuPercent": null,
  "memoryUsedMb": null,
  "memoryLimitMb": null,
  "memoryPercent": null,
} satisfies ContainerStateResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ContainerStateResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


