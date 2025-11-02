import { useDisclosure } from '@mantine/hooks';
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  List,
  Modal,
  Progress,
  Stack,
  Text,
} from "@mantine/core";
import { IconMovie, IconUpload, IconX } from "@tabler/icons-react";
import { Dropzone, MIME_TYPES } from "@mantine/dropzone";
import { useRef, useState } from "react";
import * as tus from 'tus-js-client'
import { createVideoRequest } from "@repo/shared"
import { calculateFileMD5 } from "../utils/fileHash";
import type { Upload } from "tus-js-client";
import type { CreateClipResponse } from '@repo/nucleus-api-client';

type QueueItem = {
  file: File;
  id: string;
  progress: number; // 0..100
  status: 'queued' | 'uploading' | 'paused' | 'done' | 'error';
  error?: string;
};

export function VideoUpload() {
  const [opened, { open, close }] = useDisclosure(false);
  const [queue, setQueue] = useState<Array<QueueItem>>([]);
  const uploadsRef = useRef<Record<string, Upload>>({});

  function setItem(id: string, patch: Partial<QueueItem>) {
    setQueue((q) => q.map((it) => (it.id === id ? { ...it, ...patch } : it)));
  }

  async function startTusUpload(entry: { file: File; id: string }) {
    let response: CreateClipResponse;
    try {
      // Calculate MD5 hash of the video file
      const md5Hash = await calculateFileMD5(entry.file);

      const apiResponse = await createVideoRequest(entry.file.name, md5Hash);
      if(!apiResponse  || !apiResponse.signature) {
        setItem(entry.id, { status: 'error', error: "Failed to create video object" });
        return
      }
      setItem(entry.id, { status: 'uploading', progress: 0, error: undefined });
      response = apiResponse;
    } catch (error) {
      setItem(entry.id, { status: 'error', error: `Failed to process file: ${error instanceof Error ? error.message : 'Unknown error'}` });
      return;
    }

    // Replace with your tus creation endpoint
    const endpoint = 'https://video.bunnycdn.com/tusupload';

    const upload = new tus.Upload(entry.file, {
      endpoint,
      // Optional: if the server uses a different creation route style:
      // uploadUrl: 'https://.../files',
      retryDelays: [0, 1000, 3000, 5000, 10000],
      metadata: {
        filename: entry.file.name,
        filetype: entry.file.type || 'video/mp4',
        title: entry.file.name,
        collection: response.collectionId
      },
      onError: (error) => {
        setItem(entry.id, { status: 'error', error: error.message });
      },
      onProgress: (bytesUploaded, bytesTotal) => {
        const pct = Math.round((bytesUploaded / bytesTotal) * 100);
        setItem(entry.id, { progress: pct });
      },
      onSuccess: () => {
        setItem(entry.id, { status: 'done', progress: 100 });
      },
      headers: {
        "AuthorizationSignature": response.signature, // SHA256 signature (library_id + api_key + expiration_time + video_id)
        "AuthorizationExpire": response.expiration.toString(), // Expiration time as in the signature,
        "VideoId": response.videoId, // The guid of a previously created video object through the Create Video API call
        "LibraryId": response.libraryId,
      }
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
      status: 'queued' as const,
    }));

    setQueue((q) => [...newEntries, ...q]);

    newEntries.forEach((entry) => {
      void startTusUpload(entry);
    });
  }

  function pause(id: string) {
    const up = uploadsRef.current[id];
    up.abort();
    setItem(id, { status: 'paused' });
  }

  function resume(id: string) {
    const item = queue.find((x) => x.id === id);
    if (!item) return;
    const existing = uploadsRef.current[id];
    existing.start();
    setItem(id, { status: 'uploading' });
  }

  function cancel(id: string) {
    const up = uploadsRef.current[id];
    up.abort(true);
    delete uploadsRef.current[id];
    setQueue((q) => q.filter((it) => it.id !== id));
  }

  function clearFinished() {
    setQueue((q) => q.filter((it) => it.status !== 'done' && it.status !== 'error'));
  }

  return (
    <>
      <Modal opened={opened} onClose={close} title="Upload Clips" centered radius="lg">
        <Dropzone
          onDrop={onDrop}
          onReject={(files) => console.log('rejected files', files)}
          maxSize={2 * 1024 ** 3}
          accept={[MIME_TYPES.mp4, "video/x-matroska"]}
        >
          <Group justify="center" gap="xl" mih={220} style={{ pointerEvents: 'none' }}>
            <Dropzone.Accept>
              <IconUpload size={52} color="var(--mantine-color-blue-6)" stroke={1.5} />
            </Dropzone.Accept>
            <Dropzone.Reject>
              <IconX size={52} color="var(--mantine-color-red-6)" stroke={1.5} />
            </Dropzone.Reject>
            <Dropzone.Idle>
              <IconMovie size={52} color="var(--mantine-color-dimmed)" stroke={1.5} />
            </Dropzone.Idle>

            <div>
              <Text size="xl" inline>
                Drag clips here or click to select files
              </Text>
            </div>
          </Group>
        </Dropzone>

        <Stack mt="md" gap="sm">
          {queue.length === 0 && (
            <Text size="sm" c="dimmed">
              No files queued yet.
            </Text>
          )}

          {queue.length > 0 && (
            <>
              <List spacing="xs">
                {queue.map((item) => (
                  <List.Item key={item.id}>
                    <Stack gap={4}>
                      <Group justify="space-between" wrap="nowrap">
                        <Text size="sm" fw={500} style={{ wordBreak: 'break-all' }}>
                          {item.file.name}
                        </Text>
                        <Group gap="xs">
                          <Badge color={
                            item.status === 'done' ? 'green'
                              : item.status === 'error' ? 'red'
                                : item.status === 'uploading' ? 'blue'
                                  : item.status === 'paused' ? 'yellow'
                                    : 'gray'
                          }>
                            {item.status}
                          </Badge>
                          <Text size="xs" c="dimmed">
                            {(item.file.size / (1024 * 1024)).toFixed(2)} MB
                          </Text>
                        </Group>
                      </Group>

                      {item.status !== 'error' && (
                        <Progress value={item.progress} size="sm" striped animated={item.status === 'uploading'} />
                      )}
                      {item.status === 'error' && (
                        <Alert color="red" variant="light" title="Upload failed">
                          {item.error ?? 'Unknown error'}
                        </Alert>
                      )}

                      <Group justify="end" gap="xs">
                        {(item.status === 'uploading') && (
                          <Button variant="light" size="xs" onClick={() => pause(item.id)}>
                            Pause
                          </Button>
                        )}
                        {(item.status === 'paused' || item.status === 'queued') && (
                          <Button variant="light" size="xs" onClick={() => resume(item.id)}>
                            Resume
                          </Button>
                        )}
                        {(item.status === 'uploading' || item.status === 'paused' || item.status === 'queued') && (
                          <Button variant="subtle" size="xs" color="red" onClick={() => cancel(item.id)}>
                            Cancel
                          </Button>
                        )}
                      </Group>
                    </Stack>
                  </List.Item>
                ))}
              </List>

              <Group justify="end">
                <Button variant="light" onClick={clearFinished}>
                  Clear finished
                </Button>
              </Group>
            </>
          )}
        </Stack>
      </Modal>

      <ActionIcon variant="filled" size="lg" onClick={open}>
        <IconUpload style={{ width: '70%', height: '70%' }} stroke={1.5} />
      </ActionIcon>
    </>
  );
}