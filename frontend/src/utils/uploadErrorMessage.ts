import { ApiError } from "@/shared/services/api-error";

export function uploadErrorMessage(uploadError: unknown) {
  if (uploadError instanceof ApiError && uploadError.status === 409) {
    return "This video has already been uploaded.";
  }
  // A 403 from clip creation is the plan's limit; the API's detail says which and what to do.
  if (uploadError instanceof ApiError && uploadError.status === 403) {
    return uploadError.message || "Your plan doesn't allow more clips.";
  }
  return uploadError instanceof Error
    ? uploadError.message
    : "The upload failed.";
}
