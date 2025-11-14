# ApexDetectionEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**queueAllUnprocessedItems**](ApexDetectionEndpointsApi.md#queueallunprocesseditems) | **POST** /api/apexdetection/enqueue-all |  |
| [**queueDetection**](ApexDetectionEndpointsApi.md#queuedetection) | **POST** /api/apexdetection/enqueue |  |



## queueAllUnprocessedItems

> queueAllUnprocessedItems()



### Example

```ts
import {
  Configuration,
  ApexDetectionEndpointsApi,
} from '';
import type { QueueAllUnprocessedItemsRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ApexDetectionEndpointsApi();

  try {
    const data = await api.queueAllUnprocessedItems();
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

`void` (Empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## queueDetection

> queueDetection(videoDetectionRequest)



### Example

```ts
import {
  Configuration,
  ApexDetectionEndpointsApi,
} from '';
import type { QueueDetectionRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ApexDetectionEndpointsApi();

  const body = {
    // VideoDetectionRequest
    videoDetectionRequest: ...,
  } satisfies QueueDetectionRequest;

  try {
    const data = await api.queueDetection(body);
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
| **videoDetectionRequest** | [VideoDetectionRequest](VideoDetectionRequest.md) |  | |

### Return type

`void` (Empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

