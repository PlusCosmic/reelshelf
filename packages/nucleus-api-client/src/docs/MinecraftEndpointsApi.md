# MinecraftEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**consoleWebSocket**](MinecraftEndpointsApi.md#consolewebsocket) | **GET** /minecraft/servers/{serverId}/console/live |  |
| [**createMinecraftDirectory**](MinecraftEndpointsApi.md#createminecraftdirectory) | **POST** /minecraft/servers/{serverId}/files/mkdir |  |
| [**createMinecraftServer**](MinecraftEndpointsApi.md#createminecraftserveroperation) | **POST** /minecraft/servers |  |
| [**deleteMinecraftFile**](MinecraftEndpointsApi.md#deleteminecraftfile) | **DELETE** /minecraft/servers/{serverId}/files |  |
| [**deleteMinecraftServer**](MinecraftEndpointsApi.md#deleteminecraftserver) | **DELETE** /minecraft/servers/{serverId} |  |
| [**destroyContainer**](MinecraftEndpointsApi.md#destroycontainer) | **DELETE** /minecraft/servers/{serverId}/container |  |
| [**getBackupStatus**](MinecraftEndpointsApi.md#getbackupstatus) | **GET** /minecraft/servers/{serverId}/backups |  |
| [**getCommandHistory**](MinecraftEndpointsApi.md#getcommandhistory) | **GET** /minecraft/servers/{serverId}/console/history |  |
| [**getContainerState**](MinecraftEndpointsApi.md#getcontainerstate) | **GET** /minecraft/servers/{serverId}/container |  |
| [**getMinecraftFileContent**](MinecraftEndpointsApi.md#getminecraftfilecontent) | **GET** /minecraft/servers/{serverId}/files/content |  |
| [**getMinecraftPlayers**](MinecraftEndpointsApi.md#getminecraftplayers) | **GET** /minecraft/servers/{serverId}/players |  |
| [**getMinecraftServer**](MinecraftEndpointsApi.md#getminecraftserver) | **GET** /minecraft/servers/{serverId} |  |
| [**getMinecraftServers**](MinecraftEndpointsApi.md#getminecraftservers) | **GET** /minecraft/servers |  |
| [**getMinecraftStatus**](MinecraftEndpointsApi.md#getminecraftstatus) | **GET** /minecraft/servers/{serverId}/status |  |
| [**listMinecraftFiles**](MinecraftEndpointsApi.md#listminecraftfiles) | **GET** /minecraft/servers/{serverId}/files |  |
| [**provisionContainer**](MinecraftEndpointsApi.md#provisioncontainer) | **POST** /minecraft/servers/{serverId}/container/provision |  |
| [**saveMinecraftFileContent**](MinecraftEndpointsApi.md#saveminecraftfilecontent) | **PUT** /minecraft/servers/{serverId}/files/content |  |
| [**sendMinecraftCommand**](MinecraftEndpointsApi.md#sendminecraftcommand) | **POST** /minecraft/servers/{serverId}/console/command |  |
| [**startContainer**](MinecraftEndpointsApi.md#startcontainer) | **POST** /minecraft/servers/{serverId}/container/start |  |
| [**stopContainer**](MinecraftEndpointsApi.md#stopcontainer) | **POST** /minecraft/servers/{serverId}/container/stop |  |
| [**triggerBackupSync**](MinecraftEndpointsApi.md#triggerbackupsync) | **POST** /minecraft/servers/{serverId}/backups/sync |  |
| [**updateMinecraftServer**](MinecraftEndpointsApi.md#updateminecraftserveroperation) | **PUT** /minecraft/servers/{serverId} |  |



## consoleWebSocket

> consoleWebSocket(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { ConsoleWebSocketRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies ConsoleWebSocketRequest;

  try {
    const data = await api.consoleWebSocket(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

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


## createMinecraftDirectory

> createMinecraftDirectory(serverId, createDirectoryRequest)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { CreateMinecraftDirectoryRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // CreateDirectoryRequest
    createDirectoryRequest: ...,
  } satisfies CreateMinecraftDirectoryRequest;

  try {
    const data = await api.createMinecraftDirectory(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **createDirectoryRequest** | [CreateDirectoryRequest](CreateDirectoryRequest.md) |  | |

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
| **404** | Not Found |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## createMinecraftServer

> MinecraftServer createMinecraftServer(createMinecraftServerRequest)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { CreateMinecraftServerOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // CreateMinecraftServerRequest
    createMinecraftServerRequest: ...,
  } satisfies CreateMinecraftServerOperationRequest;

  try {
    const data = await api.createMinecraftServer(body);
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
| **createMinecraftServerRequest** | [CreateMinecraftServerRequest](CreateMinecraftServerRequest.md) |  | |

### Return type

[**MinecraftServer**](MinecraftServer.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **201** | Created |  -  |
| **400** | Bad Request |  -  |
| **409** | Conflict |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deleteMinecraftFile

> deleteMinecraftFile(serverId, path)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { DeleteMinecraftFileRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    path: path_example,
  } satisfies DeleteMinecraftFileRequest;

  try {
    const data = await api.deleteMinecraftFile(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **path** | `string` |  | [Defaults to `undefined`] |

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
| **404** | Not Found |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deleteMinecraftServer

> deleteMinecraftServer(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { DeleteMinecraftServerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies DeleteMinecraftServerRequest;

  try {
    const data = await api.deleteMinecraftServer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

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


## destroyContainer

> ContainerActionResponse destroyContainer(serverId, removeData)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { DestroyContainerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // boolean (optional)
    removeData: true,
  } satisfies DestroyContainerRequest;

  try {
    const data = await api.destroyContainer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **removeData** | `boolean` |  | [Optional] [Defaults to `false`] |

### Return type

[**ContainerActionResponse**](ContainerActionResponse.md)

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


## getBackupStatus

> BackupListResult getBackupStatus(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetBackupStatusRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetBackupStatusRequest;

  try {
    const data = await api.getBackupStatus(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**BackupListResult**](BackupListResult.md)

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


## getCommandHistory

> Array&lt;CommandLogEntry&gt; getCommandHistory(serverId, limit)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetCommandHistoryRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // number (optional)
    limit: 56,
  } satisfies GetCommandHistoryRequest;

  try {
    const data = await api.getCommandHistory(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **limit** | `number` |  | [Optional] [Defaults to `50`] |

### Return type

[**Array&lt;CommandLogEntry&gt;**](CommandLogEntry.md)

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


## getContainerState

> ContainerStateResponse getContainerState(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetContainerStateRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetContainerStateRequest;

  try {
    const data = await api.getContainerState(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ContainerStateResponse**](ContainerStateResponse.md)

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


## getMinecraftFileContent

> string getMinecraftFileContent(serverId, path)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetMinecraftFileContentRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    path: path_example,
  } satisfies GetMinecraftFileContentRequest;

  try {
    const data = await api.getMinecraftFileContent(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **path** | `string` |  | [Defaults to `undefined`] |

### Return type

**string**

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


## getMinecraftPlayers

> Array&lt;OnlinePlayer&gt; getMinecraftPlayers(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetMinecraftPlayersRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetMinecraftPlayersRequest;

  try {
    const data = await api.getMinecraftPlayers(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**Array&lt;OnlinePlayer&gt;**](OnlinePlayer.md)

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


## getMinecraftServer

> MinecraftServer getMinecraftServer(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetMinecraftServerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetMinecraftServerRequest;

  try {
    const data = await api.getMinecraftServer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**MinecraftServer**](MinecraftServer.md)

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


## getMinecraftServers

> Array&lt;MinecraftServer&gt; getMinecraftServers()



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetMinecraftServersRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  try {
    const data = await api.getMinecraftServers();
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

[**Array&lt;MinecraftServer&gt;**](MinecraftServer.md)

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


## getMinecraftStatus

> ServerStatus getMinecraftStatus(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { GetMinecraftStatusRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetMinecraftStatusRequest;

  try {
    const data = await api.getMinecraftStatus(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ServerStatus**](ServerStatus.md)

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


## listMinecraftFiles

> DirectoryListing listMinecraftFiles(serverId, path)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { ListMinecraftFilesRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string (optional)
    path: path_example,
  } satisfies ListMinecraftFilesRequest;

  try {
    const data = await api.listMinecraftFiles(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **path** | `string` |  | [Optional] [Defaults to `&#39;&#39;`] |

### Return type

[**DirectoryListing**](DirectoryListing.md)

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


## provisionContainer

> ProvisionResponse provisionContainer(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { ProvisionContainerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies ProvisionContainerRequest;

  try {
    const data = await api.provisionContainer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ProvisionResponse**](ProvisionResponse.md)

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
| **409** | Conflict |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## saveMinecraftFileContent

> saveMinecraftFileContent(serverId, saveFileRequest)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { SaveMinecraftFileContentRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // SaveFileRequest
    saveFileRequest: ...,
  } satisfies SaveMinecraftFileContentRequest;

  try {
    const data = await api.saveMinecraftFileContent(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **saveFileRequest** | [SaveFileRequest](SaveFileRequest.md) |  | |

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
| **404** | Not Found |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## sendMinecraftCommand

> RconResponse sendMinecraftCommand(serverId, rconCommand)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { SendMinecraftCommandRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // RconCommand
    rconCommand: ...,
  } satisfies SendMinecraftCommandRequest;

  try {
    const data = await api.sendMinecraftCommand(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **rconCommand** | [RconCommand](RconCommand.md) |  | |

### Return type

[**RconResponse**](RconResponse.md)

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
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## startContainer

> ContainerActionResponse startContainer(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { StartContainerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies StartContainerRequest;

  try {
    const data = await api.startContainer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ContainerActionResponse**](ContainerActionResponse.md)

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


## stopContainer

> ContainerActionResponse stopContainer(serverId, timeout, announce)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { StopContainerRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // number (optional)
    timeout: 56,
    // boolean (optional)
    announce: true,
  } satisfies StopContainerRequest;

  try {
    const data = await api.stopContainer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **timeout** | `number` |  | [Optional] [Defaults to `120`] |
| **announce** | `boolean` |  | [Optional] [Defaults to `true`] |

### Return type

[**ContainerActionResponse**](ContainerActionResponse.md)

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


## triggerBackupSync

> BackupSyncResult triggerBackupSync(serverId)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { TriggerBackupSyncRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies TriggerBackupSyncRequest;

  try {
    const data = await api.triggerBackupSync(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |

### Return type

[**BackupSyncResult**](BackupSyncResult.md)

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


## updateMinecraftServer

> MinecraftServer updateMinecraftServer(serverId, updateMinecraftServerRequest)



### Example

```ts
import {
  Configuration,
  MinecraftEndpointsApi,
} from '';
import type { UpdateMinecraftServerOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new MinecraftEndpointsApi();

  const body = {
    // string
    serverId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // UpdateMinecraftServerRequest
    updateMinecraftServerRequest: ...,
  } satisfies UpdateMinecraftServerOperationRequest;

  try {
    const data = await api.updateMinecraftServer(body);
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
| **serverId** | `string` |  | [Defaults to `undefined`] |
| **updateMinecraftServerRequest** | [UpdateMinecraftServerRequest](UpdateMinecraftServerRequest.md) |  | |

### Return type

[**MinecraftServer**](MinecraftServer.md)

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
| **400** | Bad Request |  -  |
| **409** | Conflict |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

