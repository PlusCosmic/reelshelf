
# CurrentMapRotation


## Properties

Name | Type
------------ | -------------
`standardMap` | [MapInfo](MapInfo.md)
`standardMapNext` | [MapInfo](MapInfo.md)
`rankedMap` | [MapInfo](MapInfo.md)
`rankedMapNext` | [MapInfo](MapInfo.md)
`correctAsOf` | Date

## Example

```typescript
import type { CurrentMapRotation } from ''

// TODO: Update the object below with actual values
const example = {
  "standardMap": null,
  "standardMapNext": null,
  "rankedMap": null,
  "rankedMapNext": null,
  "correctAsOf": null,
} satisfies CurrentMapRotation

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as CurrentMapRotation
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


