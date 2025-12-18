# PlaylistEndpointsApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**addClipsToPlaylist**](PlaylistEndpointsApi.md#addclipstoplaylist) | **POST** /api/playlists/{id}/clips |  |
| [**addCollaborator**](PlaylistEndpointsApi.md#addcollaboratoroperation) | **POST** /api/playlists/{id}/collaborators |  |
| [**createGamingSessionPlaylist**](PlaylistEndpointsApi.md#creategamingsessionplaylistoperation) | **POST** /api/playlists/gaming-session |  |
| [**createPlaylist**](PlaylistEndpointsApi.md#createplaylistoperation) | **POST** /api/playlists |  |
| [**deletePlaylist**](PlaylistEndpointsApi.md#deleteplaylist) | **DELETE** /api/playlists/{id} |  |
| [**getCollaborators**](PlaylistEndpointsApi.md#getcollaborators) | **GET** /api/playlists/{id}/collaborators |  |
| [**getPlaylistById**](PlaylistEndpointsApi.md#getplaylistbyid) | **GET** /api/playlists/{id} |  |
| [**getPlaylists**](PlaylistEndpointsApi.md#getplaylists) | **GET** /api/playlists |  |
| [**removeClipFromPlaylist**](PlaylistEndpointsApi.md#removeclipfromplaylist) | **DELETE** /api/playlists/{id}/clips/{clipId} |  |
| [**removeCollaborator**](PlaylistEndpointsApi.md#removecollaborator) | **DELETE** /api/playlists/{id}/collaborators/{userId} |  |
| [**reorderPlaylistClips**](PlaylistEndpointsApi.md#reorderplaylistclipsoperation) | **PUT** /api/playlists/{id}/clips/reorder |  |
| [**updatePlaylist**](PlaylistEndpointsApi.md#updateplaylistoperation) | **PUT** /api/playlists/{id} |  |



## addClipsToPlaylist

> PlaylistWithDetails addClipsToPlaylist(id, addClipToPlaylistRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { AddClipsToPlaylistRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // AddClipToPlaylistRequest
    addClipToPlaylistRequest: ...,
  } satisfies AddClipsToPlaylistRequest;

  try {
    const data = await api.addClipsToPlaylist(body);
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
| **addClipToPlaylistRequest** | [AddClipToPlaylistRequest](AddClipToPlaylistRequest.md) |  | |

### Return type

[**PlaylistWithDetails**](PlaylistWithDetails.md)

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


## addCollaborator

> Array&lt;PlaylistCollaborator&gt; addCollaborator(id, addCollaboratorRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { AddCollaboratorOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // AddCollaboratorRequest
    addCollaboratorRequest: ...,
  } satisfies AddCollaboratorOperationRequest;

  try {
    const data = await api.addCollaborator(body);
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
| **addCollaboratorRequest** | [AddCollaboratorRequest](AddCollaboratorRequest.md) |  | |

### Return type

[**Array&lt;PlaylistCollaborator&gt;**](PlaylistCollaborator.md)

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


## createGamingSessionPlaylist

> PlaylistWithDetails createGamingSessionPlaylist(createGamingSessionPlaylistRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { CreateGamingSessionPlaylistOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // CreateGamingSessionPlaylistRequest
    createGamingSessionPlaylistRequest: ...,
  } satisfies CreateGamingSessionPlaylistOperationRequest;

  try {
    const data = await api.createGamingSessionPlaylist(body);
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
| **createGamingSessionPlaylistRequest** | [CreateGamingSessionPlaylistRequest](CreateGamingSessionPlaylistRequest.md) |  | |

### Return type

[**PlaylistWithDetails**](PlaylistWithDetails.md)

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

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## createPlaylist

> Playlist createPlaylist(createPlaylistRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { CreatePlaylistOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // CreatePlaylistRequest
    createPlaylistRequest: ...,
  } satisfies CreatePlaylistOperationRequest;

  try {
    const data = await api.createPlaylist(body);
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
| **createPlaylistRequest** | [CreatePlaylistRequest](CreatePlaylistRequest.md) |  | |

### Return type

[**Playlist**](Playlist.md)

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

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## deletePlaylist

> deletePlaylist(id)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { DeletePlaylistRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies DeletePlaylistRequest;

  try {
    const data = await api.deletePlaylist(body);
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


## getCollaborators

> Array&lt;PlaylistCollaborator&gt; getCollaborators(id)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { GetCollaboratorsRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetCollaboratorsRequest;

  try {
    const data = await api.getCollaborators(body);
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

[**Array&lt;PlaylistCollaborator&gt;**](PlaylistCollaborator.md)

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


## getPlaylistById

> PlaylistWithDetails getPlaylistById(id)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { GetPlaylistByIdRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies GetPlaylistByIdRequest;

  try {
    const data = await api.getPlaylistById(body);
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

[**PlaylistWithDetails**](PlaylistWithDetails.md)

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


## getPlaylists

> Array&lt;PlaylistSummary&gt; getPlaylists()



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { GetPlaylistsRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  try {
    const data = await api.getPlaylists();
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

[**Array&lt;PlaylistSummary&gt;**](PlaylistSummary.md)

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


## removeClipFromPlaylist

> removeClipFromPlaylist(id, clipId)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { RemoveClipFromPlaylistRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    clipId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies RemoveClipFromPlaylistRequest;

  try {
    const data = await api.removeClipFromPlaylist(body);
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
| **204** | No Content |  -  |
| **404** | Not Found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## removeCollaborator

> removeCollaborator(id, userId)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { RemoveCollaboratorRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // string
    userId: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
  } satisfies RemoveCollaboratorRequest;

  try {
    const data = await api.removeCollaborator(body);
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
| **userId** | `string` |  | [Defaults to `undefined`] |

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
| **204** | No Content |  -  |
| **404** | Not Found |  -  |
| **400** | Bad Request |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## reorderPlaylistClips

> PlaylistWithDetails reorderPlaylistClips(id, reorderPlaylistClipsRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { ReorderPlaylistClipsOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // ReorderPlaylistClipsRequest
    reorderPlaylistClipsRequest: ...,
  } satisfies ReorderPlaylistClipsOperationRequest;

  try {
    const data = await api.reorderPlaylistClips(body);
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
| **reorderPlaylistClipsRequest** | [ReorderPlaylistClipsRequest](ReorderPlaylistClipsRequest.md) |  | |

### Return type

[**PlaylistWithDetails**](PlaylistWithDetails.md)

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


## updatePlaylist

> Playlist updatePlaylist(id, updatePlaylistRequest)



### Example

```ts
import {
  Configuration,
  PlaylistEndpointsApi,
} from '';
import type { UpdatePlaylistOperationRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new PlaylistEndpointsApi();

  const body = {
    // string
    id: 38400000-8cf0-11bd-b23e-10b96e4ef00d,
    // UpdatePlaylistRequest
    updatePlaylistRequest: ...,
  } satisfies UpdatePlaylistOperationRequest;

  try {
    const data = await api.updatePlaylist(body);
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
| **updatePlaylistRequest** | [UpdatePlaylistRequest](UpdatePlaylistRequest.md) |  | |

### Return type

[**Playlist**](Playlist.md)

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

