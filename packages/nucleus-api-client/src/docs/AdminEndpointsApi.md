# AdminEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getAllUsers**](AdminEndpointsApi.md#getallusers) | **GET** /admin/users |  |
| [**getUserById**](AdminEndpointsApi.md#getuserbyid) | **GET** /admin/users/{id} |  |
| [**grantPermission**](AdminEndpointsApi.md#grantpermission) | **POST** /admin/users/{id}/permissions/{permission} |  |
| [**revokePermission**](AdminEndpointsApi.md#revokepermission) | **DELETE** /admin/users/{id}/permissions/{permission} |  |



## getAllUsers

> Array&lt;UserWithPermissions&gt; getAllUsers()



### Example

```ts
import {
  Configuration,
  AdminEndpointsApi,
} from '';
import type { GetAllUsersRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AdminEndpointsApi();

  try {
    const data = await api.getAllUsers();
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

[**Array&lt;UserWithPermissions&gt;**](UserWithPermissions.md)

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


## getUserById

> UserWithPermissions getUserById(id)



### Example

```ts
import {
  Configuration,
  AdminEndpointsApi,
} from '';
import type { GetUserByIdRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AdminEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetUserByIdRequest;

  try {
    const data = await api.getUserById(body);
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

[**UserWithPermissions**](UserWithPermissions.md)

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


## grantPermission

> UserWithPermissions grantPermission(id, permission)



### Example

```ts
import {
  Configuration,
  AdminEndpointsApi,
} from '';
import type { GrantPermissionRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AdminEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    permission: permission_example,
  } satisfies GrantPermissionRequest;

  try {
    const data = await api.grantPermission(body);
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
| **permission** | `string` |  | [Defaults to `undefined`] |

### Return type

[**UserWithPermissions**](UserWithPermissions.md)

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
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## revokePermission

> UserWithPermissions revokePermission(id, permission)



### Example

```ts
import {
  Configuration,
  AdminEndpointsApi,
} from '';
import type { RevokePermissionRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AdminEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    permission: permission_example,
  } satisfies RevokePermissionRequest;

  try {
    const data = await api.revokePermission(body);
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
| **permission** | `string` |  | [Defaults to `undefined`] |

### Return type

[**UserWithPermissions**](UserWithPermissions.md)

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

