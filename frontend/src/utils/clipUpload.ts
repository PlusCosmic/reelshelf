import * as tus from "tus-js-client";
import type { CreateClipResponse } from "@/api-client";
import { ApiError } from "@/shared/services/api-error";
import { createVideoRequest } from "@/shared/services/clips";
import { calculateFileMD5 } from "@/utils/fileHash";

export type CreateVideoForUpload = (request: {
  categoryId: string;
  title: string;
  md5Hash?: string;
  createdAt?: Date;
}) => Promise<CreateClipResponse | null>;

export type TusClipUploadOptions = {
  file: File;
  title: string;
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
  createVideo = ({ categoryId, title, md5Hash, createdAt }) =>
    createVideoRequest(categoryId, title, md5Hash, createdAt),
  md5Hash,
  title,
}: {
  categoryId: string;
  createdAt?: Date;
  createVideo?: CreateVideoForUpload;
  md5Hash: string;
  title: string;
}): Promise<CreateClipResponse> {
  const response = await createVideo({
    categoryId,
    title,
    md5Hash,
    createdAt,
  });

  if (!response?.signature) {
    throw new Error("The upload could not be prepared.");
  }

  return response;
}

export function createTusClipUpload({
  file,
  onError,
  onProgress,
  onSuccess,
  response,
  title,
}: TusClipUploadOptions) {
  return new tus.Upload(file, {
    endpoint: "https://video.bunnycdn.com/tusupload",
    retryDelays: [0, 1000, 3000, 5000, 10000],
    metadata: {
      filename: file.name,
      filetype: file.type || "video/mp4",
      title,
      collection: response.collectionId,
    },
    headers: {
      AuthorizationSignature: response.signature,
      AuthorizationExpire: response.expiration.toString(),
      VideoId: response.videoId,
      LibraryId: response.libraryId,
    },
    onProgress,
    onSuccess,
    onError,
  });
}

export function uploadErrorMessage(uploadError: unknown) {
  if (uploadError instanceof ApiError && uploadError.status === 409) {
    return "This video has already been uploaded.";
  }
  return uploadError instanceof Error
    ? uploadError.message
    : "The upload failed.";
}
