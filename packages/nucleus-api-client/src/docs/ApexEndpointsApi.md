# ApexEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**assignAccount**](ApexEndpointsApi.md#assignaccount) | **POST** /apex-legends/assign-account |  |



## assignAccount

> assignAccount(username, platform)



### Example

```ts
import {
  Configuration,
  ApexEndpointsApi,
} from '';
import type { AssignAccountRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ApexEndpointsApi();

  const body = {
    // string
    username: username_example,
    // string
    platform: platform_example,
  } satisfies AssignAccountRequest;

  try {
    const data = await api.assignAccount(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **username** | `string` |  | [Defaults to `undefined`] |
| **platform** | `string` |  | [Defaults to `undefined`] |

### Return type

`void` (Empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

