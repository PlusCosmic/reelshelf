import { useState, useEffect, useCallback } from 'react';
import Editor, { type Monaco } from '@monaco-editor/react';
import {
  Box,
  Group,
  Text,
  Button,
  Badge,
  Loader,
  Stack,
  ActionIcon,
  Tooltip,
  ThemeIcon,
} from '@mantine/core';
import {
  IconDeviceFloppy,
  IconFileCode,
  IconRefresh,
  IconAlertCircle,
  IconCode,
} from '@tabler/icons-react';
import { useFileContent, useSaveFile, getLanguageFromExtension, getFileExtension } from '../../hooks/useFileOperations';

interface MonacoFileEditorProps {
  serverId: string;
  filePath: string | null;
  fileName: string | null;
}

export function MonacoFileEditor({ serverId, filePath, fileName }: MonacoFileEditorProps) {
  const { data: originalContent, isLoading, error, refetch } = useFileContent(serverId, filePath);
  const saveMutation = useSaveFile(serverId);

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

    // Custom theme
    monaco.editor.defineTheme('cyberpunk', {
      base: 'vs-dark',
      inherit: true,
      rules: [
        { token: 'comment', foreground: '6b7280', fontStyle: 'italic' },
        { token: 'keyword', foreground: 'a855f7' },
        { token: 'string', foreground: '00ff88' },
        { token: 'number', foreground: 'ec4899' },
        { token: 'type', foreground: '00d4ff' },
        { token: 'function', foreground: 'fbbf24' },
        { token: 'variable', foreground: 'f8fafc' },
      ],
      colors: {
        'editor.background': '#0a0a0f',
        'editor.foreground': '#f8fafc',
        'editor.lineHighlightBackground': '#0d1117',
        'editor.selectionBackground': '#00d4ff30',
        'editor.inactiveSelectionBackground': '#00d4ff20',
        'editorCursor.foreground': '#00d4ff',
        'editorLineNumber.foreground': '#4b5563',
        'editorLineNumber.activeForeground': '#00d4ff',
        'editor.selectionHighlightBackground': '#a855f720',
        'editorIndentGuide.background1': '#1f2937',
        'editorIndentGuide.activeBackground1': '#374151',
      },
    });
    monaco.editor.setTheme('cyberpunk');
  }, [handleSave]);

  const language = fileName ? getLanguageFromExtension(getFileExtension(fileName)) : 'plaintext';

  if (!filePath || !fileName) {
    return (
      <Box
        style={{
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'transparent',
        }}
      >
        <Stack align="center" gap="lg">
          <ThemeIcon
            size={80}
            radius="xl"
            variant="light"
            color="gray"
            style={{
              background: 'rgba(0, 212, 255, 0.05)',
              border: '1px solid rgba(0, 212, 255, 0.1)',
            }}
          >
            <IconCode size={40} style={{ color: 'rgba(255, 255, 255, 0.3)' }} />
          </ThemeIcon>
          <Stack align="center" gap={4}>
            <Text c="dimmed" size="lg" fw={500}>
              Select a file to edit
            </Text>
            <Text c="dimmed" size="sm">
              Click on a file in the tree to view and edit it
            </Text>
          </Stack>
        </Stack>
      </Box>
    );
  }

  if (isLoading) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Stack align="center" gap="md">
          <Loader size="lg" color="cyberBlue" />
          <Text c="dimmed" size="sm">Loading file...</Text>
        </Stack>
      </Box>
    );
  }

  if (error) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Stack align="center" gap="md">
          <ThemeIcon size={60} radius="xl" color="red" variant="light">
            <IconAlertCircle size={32} />
          </ThemeIcon>
          <Text c="red" size="lg" fw={500}>
            Failed to load file
          </Text>
          <Text c="dimmed" size="sm">
            {error instanceof Error ? error.message : 'Unknown error'}
          </Text>
          <Button
            variant="light"
            onClick={() => refetch()}
            leftSection={<IconRefresh size={16} />}
            color="cyberBlue"
          >
            Retry
          </Button>
        </Stack>
      </Box>
    );
  }

  return (
    <Box
      style={{
        flex: 1,
        minHeight: 0,
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <Group
        justify="space-between"
        px="md"
        py="sm"
        style={{
          borderBottom: '1px solid rgba(0, 212, 255, 0.1)',
          background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.03) 0%, rgba(168, 85, 247, 0.03) 100%)',
        }}
      >
        <Group gap="sm">
          <IconFileCode
            size={18}
            style={{
              color: '#00d4ff',
              filter: 'drop-shadow(0 0 4px rgba(0, 212, 255, 0.5))',
            }}
          />
          <Text fw={600} size="sm" style={{ color: '#f8fafc' }}>
            {fileName}
          </Text>
          {hasChanges && (
            <Badge
              size="sm"
              variant="light"
              color="yellow"
              style={{
                background: 'rgba(251, 191, 36, 0.15)',
                border: '1px solid rgba(251, 191, 36, 0.3)',
              }}
            >
              Unsaved
            </Badge>
          )}
          <Badge
            size="sm"
            variant="light"
            color="cyberPurple"
            style={{
              background: 'rgba(168, 85, 247, 0.1)',
              border: '1px solid rgba(168, 85, 247, 0.2)',
            }}
          >
            {language}
          </Badge>
        </Group>
        <Group gap="xs">
          <Tooltip label="Refresh (discard changes)">
            <ActionIcon
              variant="light"
              size="md"
              radius="md"
              onClick={() => refetch()}
              disabled={saveMutation.isPending}
              color="gray"
              style={{
                background: 'rgba(255, 255, 255, 0.05)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
              }}
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
            variant="gradient"
            gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
            style={{
              boxShadow: hasChanges ? '0 0 15px rgba(0, 212, 255, 0.3)' : 'none',
            }}
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
            padding: { top: 12, bottom: 12 },
            lineNumbers: 'on',
            renderLineHighlight: 'line',
            cursorBlinking: 'smooth',
            smoothScrolling: true,
            tabSize: 2,
            bracketPairColorization: { enabled: true },
            guides: {
              bracketPairs: true,
              indentation: true,
            },
          }}
          loading={
            <Stack align="center" justify="center" h="100%">
              <Loader color="cyberBlue" />
            </Stack>
          }
        />
      </div>

      {/* Footer */}
      <Group
        justify="space-between"
        px="md"
        py={6}
        style={{
          borderTop: '1px solid rgba(0, 212, 255, 0.1)',
          background: 'rgba(0, 0, 0, 0.2)',
        }}
      >
        <Text size="xs" c="dimmed" ff="monospace">
          {filePath}
        </Text>
        <Text size="xs" c="dimmed">
          Ctrl+S to save
        </Text>
      </Group>
    </Box>
  );
}
