import type { CreateClipResponse } from "@/api-client";
import { createVideoRequest } from "@/shared/services/clips";
import { S3MultipartUpload } from "@/utils/s3MultipartUpload";
import { uploadCopy } from "@/copy/upload";

export type CreateVideoForUpload = (request: {
  categoryId: string;
  title: string;
  fileSize: number;
  md5Hash?: string;
  createdAt?: Date;
}) => Promise<CreateClipResponse | null>;

export type S3ClipUploadOptions = {
  file: File;
  response: CreateClipResponse;
  onProgress: (uploaded: number, total: number) => void;
  onSuccess: () => void;
  onError: (error: Error) => void;
  /** Where its progress is kept for a reload; see `multipartResumeKey`. */
  resumeKey: string;
};

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
    throw new Error(uploadCopy.errors.notStarted);
  }

  return response;
}

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
    console.warn(`Unsupported upload protocol "${upload.protocol}"`);
    throw new Error(uploadCopy.errors.notStarted);
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
