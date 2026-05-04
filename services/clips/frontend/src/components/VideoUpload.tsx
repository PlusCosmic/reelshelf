import { useDisclosure } from "@mantine/hooks";
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Card,
  Group,
  List,
  Modal,
  Progress,
  Stack,
  Text,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  IconAlertCircle,
  IconCheck,
  IconClock,
  IconMovie,
  IconUpload,
  IconX,
} from "@tabler/icons-react";
import { Dropzone, MIME_TYPES } from "@mantine/dropzone";
import { useEffect, useRef, useState } from "react";
import * as tus from "tus-js-client";
import { ResponseError } from "@repo/nucleus-api-client";
import { calculateFileMD5 } from "../utils/fileHash";
import { useCreateVideo } from "../hooks/queries";
import type { Upload } from "tus-js-client";
import type { CreateClipResponse } from "@repo/nucleus-api-client";

type QueueItem = {
  file: File;
  id: string;
  progress: number; // 0..100
  status: "queued" | "processing" | "uploading" | "paused" | "done" | "error";
  error?: string;
  bytesUploaded?: number;
  startTime?: number;
  uploadSpeed?: number; // bytes per second
};

const MAX_CONCURRENT_UPLOADS = 3;

interface VideoUploadProps {
  categoryId: string;
}

export function VideoUpload({ categoryId }: VideoUploadProps) {
  const [opened, { open, close }] = useDisclosure(false);
  const [queue, setQueue] = useState<Array<QueueItem>>([]);
  const uploadsRef = useRef<Record<string, Upload>>({});
  const processingRef = useRef(false);
  const createVideo = useCreateVideo();

  function setItem(id: string, patch: Partial<QueueItem>) {
    setQueue((q) => q.map((it) => (it.id === id ? { ...it, ...patch } : it)));
  }

  // Process the upload queue with concurrency limiting
  useEffect(() => {
    if (processingRef.current) return;

    const processQueue = () => {
      processingRef.current = true;

      // Track which items we've started in this execution to avoid duplicates
      const startedInThisRun = new Set<string>();

      while (true) {
        // Count both 'processing' and 'uploading' towards the concurrent limit
        const activeUploads = queue.filter(
          (item) => item.status === "processing" || item.status === "uploading",
        ).length;
        const nextQueued = queue.find(
          (item) => item.status === "queued" && !startedInThisRun.has(item.id),
        );

        if (!nextQueued || activeUploads >= MAX_CONCURRENT_UPLOADS) {
          break;
        }

        // Mark as started before calling startTusUpload
        startedInThisRun.add(nextQueued.id);

        // Don't await - let uploads run concurrently
        void startTusUpload({ file: nextQueued.file, id: nextQueued.id });
      }

      processingRef.current = false;
    };

    void processQueue();
  }, [queue]);

  async function startTusUpload(entry: { file: File; id: string }) {
    // Mark as processing immediately to prevent duplicate processing
    setItem(entry.id, { status: "processing", progress: 0 });

    let response: CreateClipResponse;
    try {
      // Calculate MD5 hash of the video file
      const md5Hash = await calculateFileMD5(entry.file);

      // Get the file creation date from the lastModified timestamp
      const fileCreatedAt = new Date(entry.file.lastModified);

      const apiResponse = await createVideo.mutateAsync({
        categoryId,
        title: entry.file.name,
        md5Hash,
        createdAt: fileCreatedAt,
      });
      if (!apiResponse || !apiResponse.signature) {
        setItem(entry.id, {
          status: "error",
          error: "Failed to create video object",
        });
        return;
      }
      setItem(entry.id, {
        status: "uploading",
        progress: 0,
        error: undefined,
        startTime: Date.now(),
        bytesUploaded: 0,
        uploadSpeed: 0,
      });
      response = apiResponse;
    } catch (error) {
      // Handle duplicate file error (409 Conflict)
      if (error instanceof ResponseError && error.response.status === 409) {
        setItem(entry.id, {
          status: "error",
          error: "Duplicate file: This video has already been uploaded",
        });
        return;
      }

      // Handle other errors
      setItem(entry.id, {
        status: "error",
        error: `Failed to process file: ${error instanceof Error ? error.message : "Unknown error"}`,
      });
      return;
    }

    // Replace with your tus creation endpoint
    const endpoint = "https://video.bunnycdn.com/tusupload";

    const upload = new tus.Upload(entry.file, {
      endpoint,
      // Optional: if the server uses a different creation route style:
      // uploadUrl: 'https://.../files',
      retryDelays: [0, 1000, 3000, 5000, 10000],
      metadata: {
        filename: entry.file.name,
        filetype: entry.file.type || "video/mp4",
        title: entry.file.name,
        collection: response.collectionId,
      },
      onError: (error) => {
        setItem(entry.id, { status: "error", error: error.message });
      },
      onProgress: (bytesUploaded, bytesTotal) => {
        const pct = Math.round((bytesUploaded / bytesTotal) * 100);

        // Calculate upload speed
        setQueue((q) => {
          const item = q.find((it) => it.id === entry.id);
          if (!item || !item.startTime) return q;

          const elapsedSeconds = (Date.now() - item.startTime) / 1000;
          const uploadSpeed =
            elapsedSeconds > 0 ? bytesUploaded / elapsedSeconds : 0;

          return q.map((it) =>
            it.id === entry.id
              ? { ...it, progress: pct, bytesUploaded, uploadSpeed }
              : it,
          );
        });
      },
      onSuccess: () => {
        setItem(entry.id, { status: "done", progress: 100 });
      },
      headers: {
        AuthorizationSignature: response.signature, // SHA256 signature (library_id + api_key + expiration_time + video_id)
        AuthorizationExpire: response.expiration.toString(), // Expiration time as in the signature,
        VideoId: response.videoId, // The guid of a previously created video object through the Create Video API call
        LibraryId: response.libraryId,
      },
    });

    uploadsRef.current[entry.id] = upload;
    upload.findPreviousUploads().then((previous) => {
      if (previous.length) upload.resumeFromPreviousUpload(previous[0]);
      upload.start();
    });
  }

  function onDrop(files: Array<File>) {
    const newEntries = files.map((file) => ({
      file,
      id: `${file.name}-${file.size}-${file.lastModified}-${crypto.randomUUID()}`,
      progress: 0,
      status: "queued" as const,
    }));

    // Add to queue - the useEffect will handle starting uploads with concurrency limiting
    setQueue((q) => [...newEntries, ...q]);
  }

  function pause(id: string) {
    const up = uploadsRef.current[id];
    up.abort();
    setItem(id, { status: "paused" });
  }

  function resume(id: string) {
    const item = queue.find((x) => x.id === id);
    if (!item) return;
    const existing = uploadsRef.current[id];
    existing.start();
    setItem(id, { status: "uploading" });
  }

  function cancel(id: string) {
    const up = uploadsRef.current[id];
    if (up) {
      up.abort(true);
      delete uploadsRef.current[id];
    }
    setQueue((q) => q.filter((it) => it.id !== id));
  }

  function clearFinished() {
    setQueue((q) =>
      q.filter((it) => it.status !== "done" && it.status !== "error"),
    );
  }

  // Calculate upload statistics
  function getUploadStats() {
    const total = queue.length;
    const completed = queue.filter((item) => item.status === "done").length;
    const failed = queue.filter((item) => item.status === "error").length;
    const inProgress = queue.filter(
      (item) => item.status === "uploading" || item.status === "processing",
    ).length;
    const queued = queue.filter((item) => item.status === "queued").length;

    // Calculate overall progress
    const totalBytes = queue.reduce((sum, item) => sum + item.file.size, 0);
    const uploadedBytes = queue.reduce((sum, item) => {
      if (item.status === "done") return sum + item.file.size;
      if (item.bytesUploaded) return sum + item.bytesUploaded;
      return sum;
    }, 0);
    const overallProgress =
      totalBytes > 0 ? Math.round((uploadedBytes / totalBytes) * 100) : 0;

    // Calculate average upload speed and ETA
    const activeUploads = queue.filter(
      (item) => item.status === "uploading" && item.uploadSpeed,
    );
    const avgSpeed =
      activeUploads.length > 0
        ? activeUploads.reduce(
            (sum, item) => sum + (item.uploadSpeed || 0),
            0,
          ) / activeUploads.length
        : 0;

    const remainingBytes = totalBytes - uploadedBytes;
    const etaSeconds = avgSpeed > 0 ? remainingBytes / avgSpeed : 0;

    return {
      total,
      completed,
      failed,
      inProgress,
      queued,
      overallProgress,
      avgSpeed,
      etaSeconds,
      totalBytes,
      uploadedBytes,
    };
  }

  function formatSpeed(bytesPerSecond: number): string {
    if (bytesPerSecond < 1024) return `${bytesPerSecond.toFixed(0)} B/s`;
    if (bytesPerSecond < 1024 * 1024)
      return `${(bytesPerSecond / 1024).toFixed(1)} KB/s`;
    return `${(bytesPerSecond / (1024 * 1024)).toFixed(1)} MB/s`;
  }

  function formatTime(seconds: number): string {
    if (seconds < 60) return `${Math.round(seconds)}s`;
    if (seconds < 3600)
      return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
    return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
  }

  const stats = getUploadStats();

  return (
    <>
      <Modal
        opened={opened}
        onClose={close}
        title="Upload Clips"
        centered
        radius="lg"
        size="xl"
        styles={{
          content: {
            background:
              "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
            border: "1px solid rgba(0, 212, 255, 0.2)",
            boxShadow:
              "0 20px 60px rgba(0, 0, 0, 0.5), 0 0 40px rgba(0, 212, 255, 0.1)",
          },
          header: {
            background: "transparent",
            borderBottom: "1px solid rgba(0, 212, 255, 0.15)",
          },
          title: {
            color: "#00d4ff",
            fontWeight: 600,
          },
        }}
      >
        <Dropzone
          onDrop={onDrop}
          onReject={(files) => {
            notifications.show({
              title: "Upload rejected",
              message: `Invalid files: ${files.map((f) => f.file.name).join(", ")}`,
              color: "red",
            });
          }}
          maxSize={2 * 1024 ** 3}
          accept={[MIME_TYPES.mp4, "video/x-matroska"]}
        >
          <Group
            justify="center"
            gap="xl"
            mih={220}
            style={{ pointerEvents: "none" }}
          >
            <Dropzone.Accept>
              <IconUpload
                size={52}
                stroke={1.5}
                style={{
                  color: "#00d4ff",
                  filter: "drop-shadow(0 0 20px rgba(0, 212, 255, 0.5))",
                }}
              />
            </Dropzone.Accept>
            <Dropzone.Reject>
              <IconX
                size={52}
                stroke={1.5}
                style={{
                  color: "#ec4899",
                  filter: "drop-shadow(0 0 20px rgba(236, 72, 153, 0.5))",
                }}
              />
            </Dropzone.Reject>
            <Dropzone.Idle>
              <IconMovie
                size={52}
                stroke={1.5}
                style={{
                  color: "#00d4ff",
                  opacity: 0.6,
                  filter: "drop-shadow(0 0 15px rgba(0, 212, 255, 0.3))",
                }}
              />
            </Dropzone.Idle>

            <div>
              <Text size="xl" inline style={{ color: "#f8fafc" }}>
                Drag clips here or click to select files
              </Text>
            </div>
          </Group>
        </Dropzone>

        {queue.length > 0 && (
          <Card
            mt="md"
            padding="md"
            radius="md"
            style={{
              background:
                "linear-gradient(135deg, rgba(15, 15, 25, 0.8) 0%, rgba(20, 20, 35, 0.7) 100%)",
              border: "1px solid rgba(0, 212, 255, 0.15)",
            }}
          >
            <Stack gap="xs">
              <Group justify="space-between">
                <Text
                  size="sm"
                  fw={600}
                  style={{
                    background:
                      "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
                    WebkitBackgroundClip: "text",
                    WebkitTextFillColor: "transparent",
                  }}
                >
                  Upload Overview
                </Text>
                <Badge
                  size="lg"
                  variant="light"
                  style={{
                    background:
                      "linear-gradient(135deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.15) 100%)",
                    border: "1px solid rgba(0, 212, 255, 0.3)",
                    color: "#00d4ff",
                  }}
                >
                  {stats.overallProgress}%
                </Badge>
              </Group>

              <Progress
                value={stats.overallProgress}
                size="lg"
                radius="md"
                style={{ background: "rgba(0, 212, 255, 0.1)" }}
                styles={{
                  section: {
                    background:
                      "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
                    boxShadow: "0 0 20px rgba(0, 212, 255, 0.4)",
                  },
                }}
              />

              <Group justify="space-between" mt="xs">
                <Group gap="lg">
                  <Group gap="xs">
                    <IconCheck size={16} color="#10b981" />
                    <Text size="sm">{stats.completed} completed</Text>
                  </Group>
                  <Group gap="xs">
                    <IconUpload size={16} color="#00d4ff" />
                    <Text size="sm">{stats.inProgress} uploading</Text>
                  </Group>
                  <Group gap="xs">
                    <IconClock size={16} color="#a855f7" />
                    <Text size="sm">{stats.queued} queued</Text>
                  </Group>
                  {stats.failed > 0 && (
                    <Group gap="xs">
                      <IconAlertCircle size={16} color="#ec4899" />
                      <Text size="sm">{stats.failed} failed</Text>
                    </Group>
                  )}
                </Group>
              </Group>

              {stats.inProgress > 0 && stats.avgSpeed > 0 && (
                <Group justify="space-between" mt="xs">
                  <Text size="xs" c="dimmed">
                    Speed: {formatSpeed(stats.avgSpeed)}
                  </Text>
                  <Text size="xs" c="dimmed">
                    ETA: {formatTime(stats.etaSeconds)}
                  </Text>
                </Group>
              )}
            </Stack>
          </Card>
        )}

        <Stack mt="md" gap="sm">
          {queue.length === 0 && (
            <Text size="sm" c="dimmed">
              No files queued yet.
            </Text>
          )}

          {queue.length > 0 && (
            <>
              <div style={{ maxHeight: "400px", overflowY: "auto" }}>
                <List spacing="xs">
                  {queue.map((item) => (
                    <List.Item key={item.id}>
                      <Stack gap={4}>
                        <Group justify="space-between" wrap="nowrap">
                          <Text
                            size="sm"
                            fw={500}
                            style={{ wordBreak: "break-all" }}
                          >
                            {item.file.name}
                          </Text>
                          <Group gap="xs">
                            <Badge
                              style={{
                                background:
                                  item.status === "done"
                                    ? "rgba(16, 185, 129, 0.2)"
                                    : item.status === "error"
                                      ? "rgba(236, 72, 153, 0.2)"
                                      : item.status === "uploading"
                                        ? "rgba(0, 212, 255, 0.2)"
                                        : item.status === "processing"
                                          ? "rgba(0, 212, 255, 0.15)"
                                          : item.status === "paused"
                                            ? "rgba(245, 158, 11, 0.2)"
                                            : "rgba(148, 163, 184, 0.2)",
                                color:
                                  item.status === "done"
                                    ? "#10b981"
                                    : item.status === "error"
                                      ? "#ec4899"
                                      : item.status === "uploading"
                                        ? "#00d4ff"
                                        : item.status === "processing"
                                          ? "#00d4ff"
                                          : item.status === "paused"
                                            ? "#f59e0b"
                                            : "#94a3b8",
                                border: `1px solid ${
                                  item.status === "done"
                                    ? "rgba(16, 185, 129, 0.3)"
                                    : item.status === "error"
                                      ? "rgba(236, 72, 153, 0.3)"
                                      : item.status === "uploading"
                                        ? "rgba(0, 212, 255, 0.3)"
                                        : item.status === "processing"
                                          ? "rgba(0, 212, 255, 0.25)"
                                          : item.status === "paused"
                                            ? "rgba(245, 158, 11, 0.3)"
                                            : "rgba(148, 163, 184, 0.3)"
                                }`,
                              }}
                            >
                              {item.status}
                            </Badge>
                            <Text size="xs" c="dimmed">
                              {(item.file.size / (1024 * 1024)).toFixed(2)} MB
                            </Text>
                          </Group>
                        </Group>

                        {item.status !== "error" && (
                          <Progress
                            value={item.progress}
                            size="sm"
                            striped
                            animated={
                              item.status === "uploading" ||
                              item.status === "processing"
                            }
                            style={{ background: "rgba(0, 212, 255, 0.1)" }}
                            styles={{
                              section: {
                                background:
                                  "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
                              },
                            }}
                          />
                        )}
                        {item.status === "error" && (
                          <Alert
                            color="red"
                            variant="light"
                            title="Upload failed"
                          >
                            {item.error ?? "Unknown error"}
                          </Alert>
                        )}

                        <Group justify="end" gap="xs">
                          {item.status === "uploading" && (
                            <Button
                              variant="light"
                              size="xs"
                              onClick={() => pause(item.id)}
                              style={{
                                background: "rgba(245, 158, 11, 0.15)",
                                border: "1px solid rgba(245, 158, 11, 0.3)",
                                color: "#f59e0b",
                              }}
                            >
                              Pause
                            </Button>
                          )}
                          {(item.status === "paused" ||
                            item.status === "queued") && (
                            <Button
                              variant="light"
                              size="xs"
                              onClick={() => resume(item.id)}
                              style={{
                                background: "rgba(0, 212, 255, 0.15)",
                                border: "1px solid rgba(0, 212, 255, 0.3)",
                                color: "#00d4ff",
                              }}
                            >
                              Resume
                            </Button>
                          )}
                          {(item.status === "uploading" ||
                            item.status === "processing" ||
                            item.status === "paused" ||
                            item.status === "queued") && (
                            <Button
                              variant="subtle"
                              size="xs"
                              onClick={() => cancel(item.id)}
                              style={{
                                color: "#ec4899",
                              }}
                            >
                              Cancel
                            </Button>
                          )}
                        </Group>
                      </Stack>
                    </List.Item>
                  ))}
                </List>
              </div>

              <Group justify="end" mt="sm">
                <Button
                  variant="light"
                  onClick={clearFinished}
                  style={{
                    background: "rgba(0, 212, 255, 0.1)",
                    border: "1px solid rgba(0, 212, 255, 0.3)",
                    color: "#00d4ff",
                  }}
                >
                  Clear finished
                </Button>
              </Group>
            </>
          )}
        </Stack>
      </Modal>

      <ActionIcon
        variant="transparent"
        onClick={open}
        aria-label="Upload clips"
        style={{
          color: "#00d4ff",
          transition: "all 0.2s ease",
        }}
      >
        <IconUpload size={24} />
      </ActionIcon>
    </>
  );
}
