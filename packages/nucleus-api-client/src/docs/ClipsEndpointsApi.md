# ClipsEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**addTagToClip**](ClipsEndpointsApi.md#addtagtoclip) | **POST** /clips/videos/{clipId}/tags |  |
| [**backfillClipMetadata**](ClipsEndpointsApi.md#backfillclipmetadata) | **POST** /clips/backfill-metadata |  |
| [**createVideo**](ClipsEndpointsApi.md#createvideo) | **POST** /clips/categories/{categoryId}/videos |  |
| [**deleteClip**](ClipsEndpointsApi.md#deleteclip) | **DELETE** /clips/videos/{clipId} |  |
| [**getTopTags**](ClipsEndpointsApi.md#gettoptags) | **GET** /clips/tags/top |  |
| [**getVideoById**](ClipsEndpointsApi.md#getvideobyid) | **GET** /clips/videos/{clipId} |  |
| [**getVideosByCategory**](ClipsEndpointsApi.md#getvideosbycategory) | **GET** /clips/categories/{categoryId}/videos |  |
| [**markVideoAsViewed**](ClipsEndpointsApi.md#markvideoasviewed) | **POST** /clips/videos/{clipId}/view |  |
| [**removeTagFromClip**](ClipsEndpointsApi.md#removetagfromclip) | **DELETE** /clips/videos/{clipId}/tags/{tag} |  |
| [**updateClipTitle**](ClipsEndpointsApi.md#updatecliptitle) | **PATCH** /clips/videos/{clipId}/title |  |



## addTagToClip

> Clip addTagToClip(clipId, addTagRequest)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { AddTagToClipRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // AddTagRequest
    addTagRequest: ...,
  } satisfies AddTagToClipRequest;

  try {
    const data = await api.addTagToClip(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |
| **addTagRequest** | [AddTagRequest](AddTagRequest.md) |  | |

### Return type

[**Clip**](Clip.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## backfillClipMetadata

> BackfillResult backfillClipMetadata()



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { BackfillClipMetadataRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  try {
    const data = await api.backfillClipMetadata();
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

[**BackfillResult**](BackfillResult.md)

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


## createVideo

> CreateClipResponse createVideo(categoryId, videoTitle, createdAt, md5Hash)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { CreateVideoRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    categoryId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    videoTitle: videoTitle_example,
    // Date (optional)
    createdAt: 2013-10-20T19:20:30+01:00,
    // string (optional)
    md5Hash: md5Hash_example,
  } satisfies CreateVideoRequest;

  try {
    const data = await api.createVideo(body);
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
| **categoryId** | `string` |  | [Defaults to `undefined`] |
| **videoTitle** | `string` |  | [Defaults to `undefined`] |
| **createdAt** | `Date` |  | [Optional] [Defaults to `undefined`] |
| **md5Hash** | `string` |  | [Optional] [Defaults to `undefined`] |

### Return type

[**CreateClipResponse**](CreateClipResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **409** | Conflict |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deleteClip

> deleteClip(clipId)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { DeleteClipRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies DeleteClipRequest;

  try {
    const data = await api.deleteClip(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |

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
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getTopTags

> Array&lt;TopTag&gt; getTopTags()



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { GetTopTagsRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  try {
    const data = await api.getTopTags();
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

[**Array&lt;TopTag&gt;**](TopTag.md)

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


## getVideoById

> Clip getVideoById(clipId)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { GetVideoByIdRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetVideoByIdRequest;

  try {
    const data = await api.getVideoById(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**Clip**](Clip.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getVideosByCategory

> PagedClipsResponse getVideosByCategory(categoryId, page, pageSize, tags, titleSearch, unviewedOnly, sortOrder, startDate, endDate)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { GetVideosByCategoryRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    categoryId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // number
    page: 56,
    // number
    pageSize: 56,
    // Array<string> (optional)
    tags: ...,
    // string (optional)
    titleSearch: titleSearch_example,
    // boolean (optional)
    unviewedOnly: true,
    // number (optional)
    sortOrder: 56,
    // Date (optional)
    startDate: 2013-10-20T19:20:30+01:00,
    // Date (optional)
    endDate: 2013-10-20T19:20:30+01:00,
  } satisfies GetVideosByCategoryRequest;

  try {
    const data = await api.getVideosByCategory(body);
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
| **categoryId** | `string` |  | [Defaults to `undefined`] |
| **page** | `number` |  | [Defaults to `undefined`] |
| **pageSize** | `number` |  | [Defaults to `undefined`] |
| **tags** | `Array<string>` |  | [Optional] |
| **titleSearch** | `string` |  | [Optional] [Defaults to `undefined`] |
| **unviewedOnly** | `boolean` |  | [Optional] [Defaults to `false`] |
| **sortOrder** | `number` |  | [Optional] [Defaults to `0`] |
| **startDate** | `Date` |  | [Optional] [Defaults to `undefined`] |
| **endDate** | `Date` |  | [Optional] [Defaults to `undefined`] |

### Return type

[**PagedClipsResponse**](PagedClipsResponse.md)

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


## markVideoAsViewed

> markVideoAsViewed(clipId)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { MarkVideoAsViewedRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies MarkVideoAsViewedRequest;

  try {
    const data = await api.markVideoAsViewed(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |

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
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## removeTagFromClip

> Clip removeTagFromClip(clipId, tag)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { RemoveTagFromClipRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    tag: tag_example,
  } satisfies RemoveTagFromClipRequest;

  try {
    const data = await api.removeTagFromClip(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |
| **tag** | `string` |  | [Defaults to `undefined`] |

### Return type

[**Clip**](Clip.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## updateClipTitle

> Clip updateClipTitle(clipId, updateTitleRequest)



### Example

```ts
import {
  Configuration,
  ClipsEndpointsApi,
} from '';
import type { UpdateClipTitleRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ClipsEndpointsApi();

  const body = {
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // UpdateTitleRequest
    updateTitleRequest: ...,
  } satisfies UpdateClipTitleRequest;

  try {
    const data = await api.updateClipTitle(body);
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
| **clipId** | `string` |  | [Defaults to `undefined`] |
| **updateTitleRequest** | [UpdateTitleRequest](UpdateTitleRequest.md) |  | |

### Return type

[**Clip**](Clip.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

