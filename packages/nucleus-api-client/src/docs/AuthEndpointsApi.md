# AuthEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**login**](AuthEndpointsApi.md#login) | **GET** /auth/discord/login |  |
| [**postLoginRedirect**](AuthEndpointsApi.md#postloginredirect) | **GET** /auth/post-login-redirect |  |



## login

> login(returnUrl)



### Example

```ts
import {
  Configuration,
  AuthEndpointsApi,
} from '';
import type { LoginRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AuthEndpointsApi();

  const body = {
    // string (optional)
    returnUrl: returnUrl_example,
  } satisfies LoginRequest;

  try {
    const data = await api.login(body);
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
| **returnUrl** | `string` |  | [Optional] [Defaults to `undefined`] |

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


## postLoginRedirect

> postLoginRedirect(returnUrl)



### Example

```ts
import {
  Configuration,
  AuthEndpointsApi,
} from '';
import type { PostLoginRedirectRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AuthEndpointsApi();

  const body = {
    // string (optional)
    returnUrl: returnUrl_example,
  } satisfies PostLoginRedirectRequest;

  try {
    const data = await api.postLoginRedirect(body);
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
| **returnUrl** | `string` |  | [Optional] [Defaults to `undefined`] |

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

