# Video Upload Architecture

## Overview

The Clips app implements a robust video upload system that uses a two-phase approach:
1. **Creation Phase**: Create a video record via the Nucleus backend API
2. **Upload Phase**: Upload the actual video file to BunnyCDN using the TUS protocol

This architecture ensures proper authentication, duplicate detection, and resumable uploads.

---

## Upload Flow

```
User Drops File
    ↓
Calculate MD5 Hash
    ↓
POST /clips/categories/{category}/videos (Nucleus API)
    ↓
Receive Auth Credentials (signature, expiration, videoId, libraryId, collectionId)
    ↓
Initialize TUS Upload (BunnyCDN)
    ↓
Upload with Resumable Chunks
    ↓
Complete
```

---

## Key Components

### 1. Frontend Upload Component
**Location**: `apps/clips/src/components/VideoUpload.tsx`

**Responsibilities**:
- File dropzone UI (drag-and-drop or click to select)
- Upload queue management with status tracking
- MD5 hash calculation before upload
- TUS upload initialization and management
- Progress tracking and error handling
- Pause/resume/cancel operations

**Queue States**:
- `queued` - File added, waiting to start
- `uploading` - Currently uploading
- `paused` - User paused the upload
- `done` - Upload completed successfully
- `error` - Upload failed

### 2. Hash Utility
**Location**: `apps/clips/src/utils/fileHash.ts`

**Purpose**: Calculate MD5 hash of video files for duplicate detection

**Implementation**:
```typescript
export async function calculateFileMD5(file: File): Promise<string>
```

- Reads file as ArrayBuffer
- Converts to CryptoJS WordArray
- Computes MD5 hash
- Returns hex string

**Dependencies**: `crypto-js`, `@types/crypto-js`

### 3. API Service Layer
**Location**: `packages/shared/src/services/apexClips.ts`

**Key Function**:
```typescript
export async function createVideoRequest(
  title: string,
  md5Hash?: string,
): Promise<CreateClipResponse | null>
```

**Purpose**: Wrapper around the auto-generated API client to create video records

### 4. Auto-Generated API Client
**Location**: `packages/nucleus-api-client/src/apis/ClipsEndpointsApi.ts`

**Endpoint**: `POST /clips/categories/{category}/videos`

**Request Schema**:
```typescript
interface CreateVideoRequest {
  category: number;
  videoTitle: string;
  md5Hash?: string;  // Optional - used for duplicate detection
}
```

**Response Schema**:
```typescript
interface CreateClipResponse {
  signature: string;              // SHA256 auth signature
  expiration: number;             // Unix timestamp
  libraryId: string;              // BunnyCDN library ID
  videoId: string;                // Unique video GUID
  collectionId: string;           // Organizational collection ID
}
```

---

## TUS Upload Configuration

### Client Library
**Package**: `tus-js-client` v4.3.1

### BunnyCDN Endpoint
**URL**: `https://video.bunnycdn.com/tusupload`

### Authentication Headers
```typescript
headers: {
  "AuthorizationSignature": response.signature,  // SHA256 signature from backend
  "AuthorizationExpire": response.expiration.toString(),
  "VideoId": response.videoId,                   // GUID from CreateClipResponse
  "LibraryId": response.libraryId,
}
```

### Metadata
```typescript
metadata: {
  filename: entry.file.name,
  filetype: entry.file.type || 'video/mp4',
  title: entry.file.name,
  collection: response.collectionId
}
```

### Retry Strategy
Exponential backoff: `[0, 1000, 3000, 5000, 10000]` ms

### Features
- **Resumable uploads**: TUS protocol allows resuming interrupted uploads
- **Chunked transfer**: Large files are uploaded in chunks
- **Progress tracking**: Real-time progress updates via `onProgress` callback
- **Error handling**: Automatic retries with exponential backoff

---

## Detailed Upload Process

### Step 1: User Selects Files
- User drags/drops files or clicks to select via Mantine Dropzone
- Files are validated:
  - **Max size**: 2 GB (2 × 1024³ bytes)
  - **Allowed types**: MP4 (`video/mp4`) and MKV (`video/x-matroska`)

### Step 2: Queue Management
Each file is added to the queue with:
```typescript
{
  file: File,
  id: string,  // Unique: `${name}-${size}-${lastModified}-${randomUUID()}`
  progress: number,  // 0-100
  status: 'queued' | 'uploading' | 'paused' | 'done' | 'error',
  error?: string
}
```

### Step 3: MD5 Hash Calculation
- **Purpose**: Duplicate detection on the backend
- **Method**: FileReader API → ArrayBuffer → CryptoJS MD5 → Hex string
- **Timing**: Before API call, during `startTusUpload`
- **Error Handling**: Hash calculation failures are caught and shown to user

### Step 4: Create Video API Call
**Request**:
```http
POST https://nucleus.pluscosmic.dev/clips/categories/0/videos
Content-Type: application/json
Credentials: include (cookies)

{
  "category": 0,
  "videoTitle": "video.mp4",
  "md5Hash": "5d41402abc4b2a76b9719d911017c592"
}
```

**Response**:
```json
{
  "signature": "sha256_signature_here",
  "expiration": 1730577600,
  "libraryId": "12345",
  "videoId": "550e8400-e29b-41d4-a716-446655440000",
  "collectionId": "collection_id_here"
}
```

**Backend Processing**:
- Validates MD5 hash against existing videos
- Generates SHA256 signature: `SHA256(libraryId + apiKey + expiration + videoId)`
- Returns credentials for BunnyCDN upload

### Step 5: TUS Upload Initialization
```typescript
const upload = new tus.Upload(file, {
  endpoint: 'https://video.bunnycdn.com/tusupload',
  retryDelays: [0, 1000, 3000, 5000, 10000],
  metadata: { filename, filetype, title, collection },
  headers: { /* auth headers from backend */ },
  onProgress: (bytesUploaded, bytesTotal) => { /* update UI */ },
  onError: (error) => { /* handle error */ },
  onSuccess: () => { /* mark complete */ }
});
```

### Step 6: Resume Support
TUS protocol checks for previous incomplete uploads:
```typescript
upload.findPreviousUploads().then((previous) => {
  if (previous.length) {
    upload.resumeFromPreviousUpload(previous[0]);
  }
  upload.start();
});
```

### Step 7: Upload Execution
- File is uploaded in chunks to BunnyCDN
- Progress updates trigger UI refresh
- User can pause/resume/cancel at any time
- Automatic retries on network failures

---

## Error Handling

### Hash Calculation Errors
```typescript
try {
  const md5Hash = await calculateFileMD5(entry.file);
} catch (error) {
  setItem(entry.id, {
    status: 'error',
    error: `Failed to process file: ${error.message}`
  });
  return;
}
```

### API Request Errors
```typescript
const response = await createVideoRequest(entry.file.name, md5Hash);
if (!response || !response.expiration || !response.signature) {
  setItem(entry.id, {
    status: 'error',
    error: "Failed to create video object"
  });
  return;
}
```

### TUS Upload Errors
```typescript
onError: (error) => {
  setItem(entry.id, {
    status: 'error',
    error: error.message
  });
}
```

---

## User Controls

### Pause Upload
- Calls `upload.abort()` (non-destructive)
- Changes status to `paused`
- Upload can be resumed later

### Resume Upload
- Calls `upload.start()` on existing upload instance
- Changes status back to `uploading`
- Continues from last checkpoint

### Cancel Upload
- Calls `upload.abort(true)` (destructive)
- Removes from upload queue
- Deletes upload instance reference

### Clear Finished
- Removes all items with status `done` or `error`
- Cleans up the queue UI

---

## Configuration

### API Base URL
**Location**: `packages/shared/src/config/apiConfig.ts`

```typescript
export const apiConfig = {
  baseUrl: import.meta.env.VITE_API_BASE_URL || "https://nucleus.pluscosmic.dev",
  bunnyBaseUrl: "https://vz-cd8f9809-39a.b-cdn.net"
};
```

### Environment Variables
- `VITE_API_BASE_URL`: Nucleus backend API base URL (defaults to production)

---

## Dependencies

### Runtime Dependencies
- `tus-js-client@4.3.1` - TUS protocol implementation
- `crypto-js` - MD5 hash calculation
- `@mantine/core` - UI components
- `@mantine/dropzone` - File dropzone component
- `@mantine/hooks` - React hooks (useDisclosure)
- `@tabler/icons-react` - Icon components

### Dev Dependencies
- `@types/crypto-js` - TypeScript types for crypto-js

---

## API Client Generation

The API client is auto-generated from an OpenAPI specification:

**Source**: `packages/nucleus-api-client/Nucleus.json`
**Generator**: OpenAPI Generator
**Config**: `packages/nucleus-api-client/openapitools.json`

To regenerate the client after API changes:
```bash
# Run the OpenAPI generator command (specific command depends on your setup)
```

---

## Security Considerations

### Server-Side Signature
- The SHA256 signature is **generated server-side only**
- Frontend never has access to the BunnyCDN API key
- Signature includes expiration timestamp to prevent replay attacks

### MD5 Hash Purpose
- **NOT for security** - MD5 is cryptographically broken
- Used only for duplicate detection
- Server should validate hash matches uploaded file

### Authentication
- API requests use cookie-based authentication (`credentials: "include"`)
- BunnyCDN uses signature-based authentication
- Signatures expire after a set time period

---

## Performance Considerations

### MD5 Calculation
- Performed client-side before upload starts
- For large videos (2GB), this can take several seconds
- Runs asynchronously to avoid blocking UI
- Consider showing a "Calculating hash..." status

### Chunked Uploads
- TUS protocol handles large files efficiently
- No need to load entire file into memory
- Resume capability prevents re-uploading on failure

### Parallel Uploads
- Multiple files can upload simultaneously
- Each has independent progress tracking
- Queue management prevents overwhelming the system

---

## Future Improvements

### Potential Enhancements
1. **Hash Progress**: Show progress bar during MD5 calculation for large files
2. **Batch Operations**: Pause/resume/cancel multiple uploads at once
3. **Upload Speed**: Display current upload speed (MB/s)
4. **ETA**: Calculate estimated time remaining
5. **Compression**: Optional client-side video compression before upload
6. **Thumbnails**: Generate and display thumbnail previews in queue
7. **Drag Reorder**: Allow users to reorder queue priority
8. **Worker Threads**: Calculate MD5 hash in Web Worker to avoid blocking main thread

### Known Limitations
1. MD5 calculation blocks for large files
2. No upload speed throttling option
3. No automatic retry after page refresh
4. Queue state not persisted across sessions

---

## Troubleshooting

### "Failed to create video object"
- **Cause**: Backend API returned null or incomplete response
- **Solutions**: Check network tab, verify API is reachable, check authentication

### "Failed to process file"
- **Cause**: MD5 hash calculation failed
- **Solutions**: Verify file is not corrupted, check browser console for errors

### Upload stuck at 0%
- **Cause**: TUS upload not starting, network issues
- **Solutions**: Check browser console, verify BunnyCDN endpoint is reachable

### Duplicate video error
- **Cause**: MD5 hash matches existing video in database
- **Solutions**: Expected behavior - backend should return appropriate error message

---

## Related Files

### Frontend
- `apps/clips/src/components/VideoUpload.tsx` - Main upload component
- `apps/clips/src/utils/fileHash.ts` - MD5 utility

### Shared Services
- `packages/shared/src/services/apexClips.ts` - API service wrapper
- `packages/shared/src/config/apiConfig.ts` - Configuration

### API Client
- `packages/nucleus-api-client/src/apis/ClipsEndpointsApi.ts` - Generated API client
- `packages/nucleus-api-client/src/models/CreateClipResponse.ts` - Response model
- `packages/nucleus-api-client/Nucleus.json` - OpenAPI specification

---

## Testing Checklist

- [ ] Upload single small video (< 10MB)
- [ ] Upload single large video (> 500MB)
- [ ] Upload multiple videos simultaneously
- [ ] Pause and resume upload
- [ ] Cancel upload mid-transfer
- [ ] Upload duplicate video (verify MD5 detection)
- [ ] Upload with network interruption
- [ ] Upload with invalid file type
- [ ] Upload exceeding size limit (2GB)
- [ ] Verify progress tracking accuracy
- [ ] Test error message display
- [ ] Verify completed uploads clear properly

---

*Last Updated: 2025-11-02*
