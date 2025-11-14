# BunnyWebhookEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**receiveVideoProgress**](BunnyWebhookEndpointsApi.md#receivevideoprogress) | **POST** /webhooks/bunny/video-progress |  |
| [**testWebhook**](BunnyWebhookEndpointsApi.md#testwebhook) | **GET** /webhooks/bunny/test |  |



## receiveVideoProgress

> receiveVideoProgress(videoProgressUpdate)



### Example

```ts
import {
  Configuration,
  BunnyWebhookEndpointsApi,
} from '';
import type { ReceiveVideoProgressRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new BunnyWebhookEndpointsApi();

  const body = {
    // VideoProgressUpdate
    videoProgressUpdate: ...,
  } satisfies ReceiveVideoProgressRequest;

  try {
    const data = await api.receiveVideoProgress(body);
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
| **videoProgressUpdate** | [VideoProgressUpdate](VideoProgressUpdate.md) |  | |

### Return type

`void` (Empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## testWebhook

> testWebhook()



### Example

```ts
import {
  Configuration,
  BunnyWebhookEndpointsApi,
} from '';
import type { TestWebhookRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new BunnyWebhookEndpointsApi();

  try {
    const data = await api.testWebhook();
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
- **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

