import { ApiError } from "@/shared/services/api-error";

export function uploadErrorMessage(uploadError: unknown) {
  if (uploadError instanceof ApiError && uploadError.status === 409) {
    return "This video has already been uploaded.";
  }
  // A 403 from clip creation is the storage limit; the API's detail says how much space remains.
  if (uploadError instanceof ApiError && uploadError.status === 403) {
    return uploadError.message || "You have reached your storage limit.";
  }
  return uploadError instanceof Error
    ? uploadError.message
    : "The upload failed.";
}
