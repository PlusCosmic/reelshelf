import { ApiError } from "@/shared/services/api-error";
import { uploadCopy } from "@/copy/upload";

export function uploadErrorMessage(uploadError: unknown) {
  if (uploadError instanceof ApiError && uploadError.status === 409) {
    return uploadCopy.errors.alreadyUploaded;
  }
  // A 403 from clip creation is the plan's limit; the API's detail says which and what to do.
  if (uploadError instanceof ApiError && uploadError.status === 403) {
    return uploadError.message || uploadCopy.errors.planLimit;
  }
  return uploadError instanceof Error
    ? uploadError.message
    : uploadCopy.errors.failed;
}
