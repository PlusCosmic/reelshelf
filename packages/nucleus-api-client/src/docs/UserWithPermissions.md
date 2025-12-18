
# UserWithPermissions


## Properties

Name | Type
------------ | -------------
`id` | string
`discordId` | string
`username` | string
`globalName` | string
`avatar` | string
`role` | number
`additionalPermissions` | Array&lt;string&gt;
`effectivePermissions` | Array&lt;string&gt;

## Example

```typescript
import type { UserWithPermissions } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "discordId": null,
  "username": null,
  "globalName": null,
  "avatar": null,
  "role": null,
  "additionalPermissions": null,
  "effectivePermissions": null,
} satisfies UserWithPermissions

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as UserWithPermissions
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


