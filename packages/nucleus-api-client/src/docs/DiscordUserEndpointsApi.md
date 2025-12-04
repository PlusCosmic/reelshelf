# DiscordUserEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**meGet**](DiscordUserEndpointsApi.md#meget) | **GET** /me |  |
| [**mePreferencesGet**](DiscordUserEndpointsApi.md#mepreferencesget) | **GET** /me/preferences |  |
| [**mePreferencesPatch**](DiscordUserEndpointsApi.md#mepreferencespatch) | **PATCH** /me/preferences |  |
| [**userUserIdGet**](DiscordUserEndpointsApi.md#useruseridget) | **GET** /user/{userId} |  |



## meGet

> DiscordUser meGet()



### Example

```ts
import {
  Configuration,
  DiscordUserEndpointsApi,
} from '';
import type { MeGetRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new DiscordUserEndpointsApi();

  try {
    const data = await api.meGet();
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

[**DiscordUser**](DiscordUser.md)

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


## mePreferencesGet

> UserPreferences mePreferencesGet()



### Example

```ts
import {
  Configuration,
  DiscordUserEndpointsApi,
} from '';
import type { MePreferencesGetRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new DiscordUserEndpointsApi();

  try {
    const data = await api.mePreferencesGet();
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

[**UserPreferences**](UserPreferences.md)

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


## mePreferencesPatch

> UserPreferences mePreferencesPatch(updatePreferencesRequest)



### Example

```ts
import {
  Configuration,
  DiscordUserEndpointsApi,
} from '';
import type { MePreferencesPatchRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new DiscordUserEndpointsApi();

  const body = {
    // UpdatePreferencesRequest
    updatePreferencesRequest: ...,
  } satisfies MePreferencesPatchRequest;

  try {
    const data = await api.mePreferencesPatch(body);
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
| **updatePreferencesRequest** | [UpdatePreferencesRequest](UpdatePreferencesRequest.md) |  | |

### Return type

[**UserPreferences**](UserPreferences.md)

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


## userUserIdGet

> DiscordUser userUserIdGet(userId)



### Example

```ts
import {
  Configuration,
  DiscordUserEndpointsApi,
} from '';
import type { UserUserIdGetRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new DiscordUserEndpointsApi();

  const body = {
    // string
    userId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies UserUserIdGetRequest;

  try {
    const data = await api.userUserIdGet(body);
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
| **userId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**DiscordUser**](DiscordUser.md)

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

