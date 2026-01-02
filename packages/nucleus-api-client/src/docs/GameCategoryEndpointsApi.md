# GameCategoryEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**addCustomCategory**](GameCategoryEndpointsApi.md#addcustomcategoryoperation) | **POST** /games/categories/custom |  |
| [**addGameCategoryFromIgdb**](GameCategoryEndpointsApi.md#addgamecategoryfromigdb) | **POST** /games/categories/from-igdb |  |
| [**getGameCategoryById**](GameCategoryEndpointsApi.md#getgamecategorybyid) | **GET** /games/categories/{categoryId} |  |
| [**getUserGameCategories**](GameCategoryEndpointsApi.md#getusergamecategories) | **GET** /games/categories |  |
| [**removeGameCategory**](GameCategoryEndpointsApi.md#removegamecategory) | **DELETE** /games/categories/{categoryId} |  |
| [**searchGames**](GameCategoryEndpointsApi.md#searchgames) | **GET** /games/search |  |



## addCustomCategory

> GameCategoryResponse addCustomCategory(addCustomCategoryRequest)



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { AddCustomCategoryOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  const body = {
    // AddCustomCategoryRequest
    addCustomCategoryRequest: ...,
  } satisfies AddCustomCategoryOperationRequest;

  try {
    const data = await api.addCustomCategory(body);
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
| **addCustomCategoryRequest** | [AddCustomCategoryRequest](AddCustomCategoryRequest.md) |  | |

### Return type

[**GameCategoryResponse**](GameCategoryResponse.md)

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


## addGameCategoryFromIgdb

> GameCategoryResponse addGameCategoryFromIgdb(addGameFromIgdbRequest)



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { AddGameCategoryFromIgdbRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  const body = {
    // AddGameFromIgdbRequest
    addGameFromIgdbRequest: ...,
  } satisfies AddGameCategoryFromIgdbRequest;

  try {
    const data = await api.addGameCategoryFromIgdb(body);
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
| **addGameFromIgdbRequest** | [AddGameFromIgdbRequest](AddGameFromIgdbRequest.md) |  | |

### Return type

[**GameCategoryResponse**](GameCategoryResponse.md)

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


## getGameCategoryById

> GameCategoryResponse getGameCategoryById(categoryId)



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { GetGameCategoryByIdRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  const body = {
    // string
    categoryId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetGameCategoryByIdRequest;

  try {
    const data = await api.getGameCategoryById(body);
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

### Return type

[**GameCategoryResponse**](GameCategoryResponse.md)

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


## getUserGameCategories

> Array&lt;GameCategoryResponse&gt; getUserGameCategories()



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { GetUserGameCategoriesRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  try {
    const data = await api.getUserGameCategories();
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

[**Array&lt;GameCategoryResponse&gt;**](GameCategoryResponse.md)

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


## removeGameCategory

> removeGameCategory(categoryId)



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { RemoveGameCategoryRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  const body = {
    // string
    categoryId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies RemoveGameCategoryRequest;

  try {
    const data = await api.removeGameCategory(body);
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


## searchGames

> Array&lt;GameSearchResult&gt; searchGames(query)



### Example

```ts
import {
  Configuration,
  GameCategoryEndpointsApi,
} from '';
import type { SearchGamesRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new GameCategoryEndpointsApi();

  const body = {
    // string
    query: query_example,
  } satisfies SearchGamesRequest;

  try {
    const data = await api.searchGames(body);
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
| **query** | `string` |  | [Defaults to `undefined`] |

### Return type

[**Array&lt;GameSearchResult&gt;**](GameSearchResult.md)

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

