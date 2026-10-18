import * as tus from "tus-js-client";
import type { CreateClipResponse } from "@/api-client";
import { ApiError } from "@/shared/services/api-error";
import { createVideoRequest } from "@/shared/services/clips";
import { calculateFileMD5 } from "@/utils/fileHash";
import { S3MultipartUpload } from "@/utils/s3MultipartUpload";

export type CreateVideoForUpload = (request: {
  categoryId: string;
  title: string;
  fileSize: number;
  md5Hash?: string;
  createdAt?: Date;
}) => Promise<CreateClipResponse | null>;

export type TusClipUploadOptions = {
  file: File;
  response: CreateClipResponse;
  onProgress: (uploaded: number, total: number) => void;
  onSuccess: () => void;
  onError: (error: Error) => void;
};

export function calculateClipUploadMd5(file: File) {
  return calculateFileMD5(file);
}

export async function createPreparedClipUpload({
  categoryId,
  createdAt,
  createVideo = ({ categoryId, title, fileSize, md5Hash, createdAt }) =>
    createVideoRequest(categoryId, title, fileSize, md5Hash, createdAt),
  fileSize,
  md5Hash,
  title,
}: {
  categoryId: string;
  createdAt?: Date;
  createVideo?: CreateVideoForUpload;
  fileSize: number;
  md5Hash: string;
  title: string;
}): Promise<CreateClipResponse> {
  const response = await createVideo({
    categoryId,
    title,
    fileSize,
    md5Hash,
    createdAt,
  });

  if (!response?.upload) {
    throw new Error("The upload could not be prepared.");
  }

  return response;
}

/**
 * Uploads the file to wherever the API prepared the clip's video. The API names the endpoint, the headers that
 * authorise the upload and the host's own metadata; only what describes the file itself is added here.
 */
export function createTusClipUpload({
  file,
  onError,
  onProgress,
  onSuccess,
  response,
}: TusClipUploadOptions) {
  const { upload } = response;
  if (upload.protocol !== "tus") {
    throw new Error(
      `The upload could not start: unsupported upload protocol "${upload.protocol}".`,
    );
  }

  return new tus.Upload(file, {
    endpoint: upload.endpoint,
    retryDelays: [0, 1000, 3000, 5000, 10000],
    metadata: {
      ...upload.metadata,
      filename: file.name,
      filetype: file.type || "video/mp4",
    },
    headers: { ...upload.headers },
    onProgress,
    onSuccess,
    onError,
  });
}

export type S3ClipUploadOptions = TusClipUploadOptions & {
  /** Where its progress is kept for a reload; see `multipartResumeKey`. */
  resumeKey: string;
};

/** Uploads the file as the presigned S3 multipart upload the API started for the clip (R2). */
export function createS3ClipUpload({
  file,
  onError,
  onProgress,
  onSuccess,
  response,
  resumeKey,
}: S3ClipUploadOptions) {
  const { upload } = response;
  if (upload.protocol !== "s3-multipart" || !upload.multipart) {
    throw new Error(
      `The upload could not start: unsupported upload protocol "${upload.protocol}".`,
    );
  }

  return new S3MultipartUpload({
    file,
    clipId: response.clipId,
    multipart: upload.multipart,
    resumeKey,
    onProgress,
    onSuccess,
    onError,
  });
}

export function isS3MultipartUpload(response: CreateClipResponse) {
  return response.upload.protocol === "s3-multipart";
}

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
