# LinksEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**addLink**](LinksEndpointsApi.md#addlink) | **POST** /links |  |
| [**deleteLink**](LinksEndpointsApi.md#deletelink) | **DELETE** /links/{id} |  |
| [**getLinksForUser**](LinksEndpointsApi.md#getlinksforuser) | **GET** /links |  |



## addLink

> addLink(linkRequest)



### Example

```ts
import {
  Configuration,
  LinksEndpointsApi,
} from '';
import type { AddLinkRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new LinksEndpointsApi();

  const body = {
    // LinkRequest
    linkRequest: ...,
  } satisfies AddLinkRequest;

  try {
    const data = await api.addLink(body);
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
| **linkRequest** | [LinkRequest](LinkRequest.md) |  | |

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
| **201** | Created |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deleteLink

> deleteLink(id)



### Example

```ts
import {
  Configuration,
  LinksEndpointsApi,
} from '';
import type { DeleteLinkRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new LinksEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies DeleteLinkRequest;

  try {
    const data = await api.deleteLink(body);
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
| **id** | `string` |  | [Defaults to `undefined`] |

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
| **204** | No Content |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getLinksForUser

> Array&lt;UserFrequentLink&gt; getLinksForUser()



### Example

```ts
import {
  Configuration,
  LinksEndpointsApi,
} from '';
import type { GetLinksForUserRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new LinksEndpointsApi();

  try {
    const data = await api.getLinksForUser();
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

[**Array&lt;UserFrequentLink&gt;**](UserFrequentLink.md)

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

