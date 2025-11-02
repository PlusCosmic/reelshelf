# NucleusApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getApexMapRotation**](NucleusApi.md#getapexmaprotation) | **GET** /apex-legends/map-rotation |  |



## getApexMapRotation

> CurrentMapRotation getApexMapRotation()



### Example

```ts
import {
  Configuration,
  NucleusApi,
} from '';
import type { GetApexMapRotationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new NucleusApi();

  try {
    const data = await api.getApexMapRotation();
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters

This endpoint does not need any parameter.

### Return type

[**CurrentMapRotation**](CurrentMapRotation.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

