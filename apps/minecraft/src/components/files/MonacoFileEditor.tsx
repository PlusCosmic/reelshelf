import { useState, useEffect, useCallback } from 'react';
import Editor, { type Monaco } from '@monaco-editor/react';
import {
  Paper,
  Group,
  Text,
  Button,
  Badge,
  Loader,
  Center,
  Stack,
  ActionIcon,
  Tooltip,
} from '@mantine/core';
import {
  IconDeviceFloppy,
  IconFile,
  IconRefresh,
  IconAlertCircle,
} from '@tabler/icons-react';
import { useFileContent, useSaveFile, getLanguageFromExtension, getFileExtension } from '../../hooks/useFileOperations';

interface MonacoFileEditorProps {
  filePath: string | null;
  fileName: string | null;
}

export function MonacoFileEditor({ filePath, fileName }: MonacoFileEditorProps) {
  const { data: originalContent, isLoading, error, refetch } = useFileContent(filePath);
  const saveMutation = useSaveFile();

  const [editorContent, setEditorContent] = useState<string>('');
  const [hasChanges, setHasChanges] = useState(false);

  // Sync editor content with fetched content
  useEffect(() => {
    if (originalContent !== undefined) {
      setEditorContent(originalContent);
      setHasChanges(false);
    }
  }, [originalContent]);

  const handleEditorChange = useCallback((value: string | undefined) => {
    const newContent = value || '';
    setEditorContent(newContent);
    setHasChanges(newContent !== originalContent);
  }, [originalContent]);

  const handleSave = useCallback(() => {
    if (!filePath) return;

    saveMutation.mutate(
      { path: filePath, content: editorContent },
      {
        onSuccess: () => {
          setHasChanges(false);
        },
      }
    );
  }, [filePath, editorContent, saveMutation]);

  // Keyboard shortcut for save (Ctrl+S / Cmd+S)
  const handleEditorMount = useCallback((editor: any, monaco: Monaco) => {
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
      handleSave();
    });
  }, [handleSave]);

  const language = fileName ? getLanguageFromExtension(getFileExtension(fileName)) : 'plaintext';

  if (!filePath || !fileName) {
    return (
      <Paper
        p="xl"
        radius="md"
        style={{
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: 'var(--mantine-color-dark-7)',
        }}
      >
        <Stack align="center" gap="md">
          <IconFile size={48} color="var(--mantine-color-dimmed)" />
          <Text c="dimmed" size="lg">
            Select a file to edit
          </Text>
          <Text c="dimmed" size="sm">
            Click on a file in the tree to view and edit it
          </Text>
        </Stack>
      </Paper>
    );
  }

  if (isLoading) {
    return (
      <Paper
        p="xl"
        radius="md"
        style={{
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: 'var(--mantine-color-dark-7)',
        }}
      >
        <Center>
          <Loader size="lg" />
        </Center>
      </Paper>
    );
  }

  if (error) {
    return (
      <Paper
        p="xl"
        radius="md"
        style={{
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: 'var(--mantine-color-dark-7)',
        }}
      >
        <Stack align="center" gap="md">
          <IconAlertCircle size={48} color="var(--mantine-color-red-5)" />
          <Text c="red" size="lg">
            Failed to load file
          </Text>
          <Text c="dimmed" size="sm">
            {error instanceof Error ? error.message : 'Unknown error'}
          </Text>
          <Button variant="light" onClick={() => refetch()} leftSection={<IconRefresh size={16} />}>
            Retry
          </Button>
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper
      radius="md"
      style={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: 'var(--mantine-color-dark-7)',
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <Group
        justify="space-between"
        p="sm"
        style={{
          borderBottom: '1px solid var(--mantine-color-dark-4)',
          backgroundColor: 'var(--mantine-color-dark-8)',
        }}
      >
        <Group gap="sm">
          <IconFile size={18} />
          <Text fw={500} size="sm">
            {fileName}
          </Text>
          {hasChanges && (
            <Badge color="yellow" size="xs" variant="filled">
              Unsaved
            </Badge>
          )}
          <Badge color="gray" size="xs" variant="outline">
            {language}
          </Badge>
        </Group>
        <Group gap="xs">
          <Tooltip label="Refresh (discard changes)">
            <ActionIcon
              variant="subtle"
              size="sm"
              onClick={() => refetch()}
              disabled={saveMutation.isPending}
            >
              <IconRefresh size={16} />
            </ActionIcon>
          </Tooltip>
          <Button
            size="xs"
            leftSection={<IconDeviceFloppy size={16} />}
            onClick={handleSave}
            loading={saveMutation.isPending}
            disabled={!hasChanges}
          >
            Save
          </Button>
        </Group>
      </Group>

      {/* Editor */}
      <div style={{ flex: 1, minHeight: 0 }}>
        <Editor
          height="100%"
          language={language}
          value={editorContent}
          onChange={handleEditorChange}
          onMount={handleEditorMount}
          theme="vs-dark"
          options={{
            minimap: { enabled: false },
            fontSize: 14,
            fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
            wordWrap: 'on',
            scrollBeyondLastLine: false,
            automaticLayout: true,
            padding: { top: 8, bottom: 8 },
            lineNumbers: 'on',
            renderLineHighlight: 'line',
            cursorBlinking: 'smooth',
            smoothScrolling: true,
            tabSize: 2,
          }}
          loading={
            <Center h="100%">
              <Loader />
            </Center>
          }
        />
      </div>

      {/* Footer */}
      <Group
        justify="space-between"
        px="sm"
        py={4}
        style={{
          borderTop: '1px solid var(--mantine-color-dark-4)',
          backgroundColor: 'var(--mantine-color-dark-8)',
        }}
      >
        <Text size="xs" c="dimmed">
          {filePath}
        </Text>
        <Text size="xs" c="dimmed">
          Press Ctrl+S to save
        </Text>
      </Group>
    </Paper>
  );
}
