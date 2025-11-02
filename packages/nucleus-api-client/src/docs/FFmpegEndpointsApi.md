# FFmpegEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**downloadVideo**](FFmpegEndpointsApi.md#downloadvideo) | **GET** /ffmpeg/download/{videoId} |  |



## downloadVideo

> downloadVideo(videoId)



### Example

```ts
import {
  Configuration,
  FFmpegEndpointsApi,
} from '';
import type { DownloadVideoRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new FFmpegEndpointsApi();

  const body = {
    // string
    videoId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies DownloadVideoRequest;

  try {
    const data = await api.downloadVideo(body);
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
| **videoId** | `string` |  | [Defaults to `undefined`] |

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
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

