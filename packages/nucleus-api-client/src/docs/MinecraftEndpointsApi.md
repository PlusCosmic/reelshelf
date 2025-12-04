# MinecraftEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**consoleWebSocket**](MinecraftEndpointsApi.md#consolewebsocket) | **GET** /minecraft/console/live |  |
| [**createMinecraftDirectory**](MinecraftEndpointsApi.md#createminecraftdirectory) | **POST** /minecraft/files/mkdir |  |
| [**deleteMinecraftFile**](MinecraftEndpointsApi.md#deleteminecraftfile) | **DELETE** /minecraft/files |  |
| [**getCommandHistory**](MinecraftEndpointsApi.md#getcommandhistory) | **GET** /minecraft/console/history |  |
| [**getMinecraftFileContent**](MinecraftEndpointsApi.md#getminecraftfilecontent) | **GET** /minecraft/files/content |  |
| [**getMinecraftPlayers**](MinecraftEndpointsApi.md#getminecraftplayers) | **GET** /minecraft/players |  |
| [**getMinecraftStatus**](MinecraftEndpointsApi.md#getminecraftstatus) | **GET** /minecraft/status |  |
| [**listMinecraftFiles**](MinecraftEndpointsApi.md#listminecraftfiles) | **GET** /minecraft/files |  |
| [**saveMinecraftFileContent**](MinecraftEndpointsApi.md#saveminecraftfilecontent) | **PUT** /minecraft/files/content |  |
| [**sendMinecraftCommand**](MinecraftEndpointsApi.md#sendminecraftcommand) | **POST** /minecraft/console/command |  |



## consoleWebSocket

> consoleWebSocket()



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

  try {
    const data = await api.consoleWebSocket();
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


## createMinecraftDirectory

> createMinecraftDirectory(createDirectoryRequest)



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
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deleteMinecraftFile

> deleteMinecraftFile(path)



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
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getCommandHistory

> Array&lt;CommandLogEntry&gt; getCommandHistory(limit)



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

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getMinecraftFileContent

> string getMinecraftFileContent(path)



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
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getMinecraftPlayers

> Array&lt;OnlinePlayer&gt; getMinecraftPlayers()



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

  try {
    const data = await api.getMinecraftPlayers();
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

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getMinecraftStatus

> ServerStatus getMinecraftStatus()



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

  try {
    const data = await api.getMinecraftStatus();
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

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## listMinecraftFiles

> DirectoryListing listMinecraftFiles(path)



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
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## saveMinecraftFileContent

> saveMinecraftFileContent(saveFileRequest)



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
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## sendMinecraftCommand

> RconResponse sendMinecraftCommand(rconCommand)



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
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

