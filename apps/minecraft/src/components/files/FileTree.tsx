import { useState, useCallback } from 'react';
import {
  Stack,
  Group,
  Text,
  UnstyledButton,
  Loader,
  ActionIcon,
  Menu,
  Tooltip,
  TextInput,
  Modal,
  Button,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  IconFolder,
  IconFolderOpen,
  IconFile,
  IconChevronRight,
  IconChevronDown,
  IconDots,
  IconTrash,
  IconFolderPlus,
  IconRefresh,
  IconAlertCircle,
} from '@tabler/icons-react';
import { useDirectoryListing, useDeleteFile, useCreateDirectory, isEditableFile } from '../../hooks/useFileOperations';
import type { FileEntry } from '@repo/nucleus-api-client';

interface FileTreeProps {
  currentPath: string;
  selectedFile: string | null;
  onFileSelect: (path: string, name: string) => void;
  onPathChange: (path: string) => void;
}

interface FileTreeItemProps {
  entry: FileEntry;
  basePath: string;
  selectedFile: string | null;
  onFileSelect: (path: string, name: string) => void;
  onPathChange: (path: string) => void;
  level: number;
}

function FileTreeItem({
  entry,
  basePath,
  selectedFile,
  onFileSelect,
  onPathChange,
  level,
}: FileTreeItemProps) {
  const [isExpanded, setIsExpanded] = useState(false);
  const fullPath = basePath === '/' ? `/${entry.name}` : `${basePath}/${entry.name}`;
  const isDirectory = entry.isDirectory;
  const isSelected = selectedFile === fullPath;
  const editable = !isDirectory && isEditableFile(entry.name);

  const deleteMutation = useDeleteFile();

  const handleClick = useCallback(() => {
    if (isDirectory) {
      setIsExpanded(!isExpanded);
    } else if (editable) {
      onFileSelect(fullPath, entry.name);
    }
  }, [isDirectory, isExpanded, editable, fullPath, entry.name, onFileSelect]);

  const handleDelete = useCallback(() => {
    if (confirm(`Are you sure you want to delete "${entry.name}"?`)) {
      deleteMutation.mutate(fullPath);
    }
  }, [fullPath, entry.name, deleteMutation]);

  return (
    <Stack gap={0}>
      <UnstyledButton
        onClick={handleClick}
        style={{
          display: 'block',
          width: '100%',
          padding: '4px 8px',
          paddingLeft: `${8 + level * 16}px`,
          borderRadius: 'var(--mantine-radius-sm)',
          backgroundColor: isSelected
            ? 'var(--mantine-color-dark-5)'
            : 'transparent',
          cursor: editable || isDirectory ? 'pointer' : 'default',
          opacity: !isDirectory && !editable ? 0.5 : 1,
        }}
        className="file-tree-item"
      >
        <Group gap="xs" justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
            {isDirectory ? (
              <>
                {isExpanded ? (
                  <IconChevronDown size={14} style={{ flexShrink: 0 }} />
                ) : (
                  <IconChevronRight size={14} style={{ flexShrink: 0 }} />
                )}
                {isExpanded ? (
                  <IconFolderOpen size={16} color="var(--mantine-color-blue-4)" style={{ flexShrink: 0 }} />
                ) : (
                  <IconFolder size={16} color="var(--mantine-color-blue-4)" style={{ flexShrink: 0 }} />
                )}
              </>
            ) : (
              <>
                <span style={{ width: 14 }} />
                <IconFile
                  size={16}
                  color={editable ? 'var(--mantine-color-gray-4)' : 'var(--mantine-color-gray-6)'}
                  style={{ flexShrink: 0 }}
                />
              </>
            )}
            <Text
              size="sm"
              truncate
              style={{
                flex: 1,
                minWidth: 0,
              }}
            >
              {entry.name}
            </Text>
          </Group>

          <Menu shadow="md" width={160} position="right-start" withinPortal>
            <Menu.Target>
              <ActionIcon
                variant="subtle"
                size="xs"
                onClick={(e) => e.stopPropagation()}
                style={{ opacity: 0.6 }}
              >
                <IconDots size={14} />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item
                color="red"
                leftSection={<IconTrash size={14} />}
                onClick={(e) => {
                  e.stopPropagation();
                  handleDelete();
                }}
                disabled={deleteMutation.isPending}
              >
                Delete
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>
      </UnstyledButton>

      {isDirectory && isExpanded && (
        <DirectoryContents
          path={fullPath}
          selectedFile={selectedFile}
          onFileSelect={onFileSelect}
          onPathChange={onPathChange}
          level={level + 1}
        />
      )}
    </Stack>
  );
}

interface DirectoryContentsProps {
  path: string;
  selectedFile: string | null;
  onFileSelect: (path: string, name: string) => void;
  onPathChange: (path: string) => void;
  level: number;
}

function DirectoryContents({
  path,
  selectedFile,
  onFileSelect,
  onPathChange,
  level,
}: DirectoryContentsProps) {
  const { data, isLoading, error } = useDirectoryListing(path);

  if (isLoading) {
    return (
      <Group gap="xs" pl={8 + level * 16} py={4}>
        <Loader size="xs" />
        <Text size="xs" c="dimmed">Loading...</Text>
      </Group>
    );
  }

  if (error) {
    return (
      <Group gap="xs" pl={8 + level * 16} py={4}>
        <IconAlertCircle size={14} color="var(--mantine-color-red-5)" />
        <Text size="xs" c="red">Failed to load</Text>
      </Group>
    );
  }

  if (!data?.entries?.length) {
    return (
      <Text size="xs" c="dimmed" pl={8 + level * 16} py={4}>
        Empty directory
      </Text>
    );
  }

  // Sort: directories first, then files, both alphabetically
  const sortedEntries = [...data.entries].sort((a, b) => {
    if (a.isDirectory !== b.isDirectory) {
      return a.isDirectory ? -1 : 1;
    }
    return a.name.localeCompare(b.name);
  });

  return (
    <Stack gap={0}>
      {sortedEntries.map((entry) => (
        <FileTreeItem
          key={entry.name}
          entry={entry}
          basePath={path}
          selectedFile={selectedFile}
          onFileSelect={onFileSelect}
          onPathChange={onPathChange}
          level={level}
        />
      ))}
    </Stack>
  );
}

export function FileTree({
  currentPath,
  selectedFile,
  onFileSelect,
  onPathChange,
}: FileTreeProps) {
  const { data, isLoading, error, refetch } = useDirectoryListing(currentPath);
  const createDirMutation = useCreateDirectory();

  const [newDirModalOpened, { open: openNewDirModal, close: closeNewDirModal }] = useDisclosure(false);
  const [newDirName, setNewDirName] = useState('');

  const handleCreateDirectory = useCallback(() => {
    if (!newDirName.trim()) return;

    const newPath = currentPath === '/'
      ? `/${newDirName.trim()}`
      : `${currentPath}/${newDirName.trim()}`;

    createDirMutation.mutate(newPath, {
      onSuccess: () => {
        setNewDirName('');
        closeNewDirModal();
      },
    });
  }, [currentPath, newDirName, createDirMutation, closeNewDirModal]);

  return (
    <Stack gap={0} h="100%">
      {/* Header */}
      <Group
        justify="space-between"
        p="sm"
        style={{
          borderBottom: '1px solid var(--mantine-color-dark-4)',
        }}
      >
        <Text size="sm" fw={600}>
          Files
        </Text>
        <Group gap={4}>
          <Tooltip label="New folder">
            <ActionIcon variant="subtle" size="sm" onClick={openNewDirModal}>
              <IconFolderPlus size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Refresh">
            <ActionIcon variant="subtle" size="sm" onClick={() => refetch()}>
              <IconRefresh size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {/* Tree */}
      <div style={{ flex: 1, overflow: 'auto', padding: '8px 0' }}>
        {isLoading ? (
          <Group justify="center" py="xl">
            <Loader size="sm" />
          </Group>
        ) : error ? (
          <Stack align="center" py="xl" gap="sm">
            <IconAlertCircle size={32} color="var(--mantine-color-red-5)" />
            <Text size="sm" c="red">Failed to load files</Text>
            <Button size="xs" variant="light" onClick={() => refetch()}>
              Retry
            </Button>
          </Stack>
        ) : !data?.entries?.length ? (
          <Text size="sm" c="dimmed" ta="center" py="xl">
            No files found
          </Text>
        ) : (
          <Stack gap={0}>
            {[...data.entries]
              .sort((a, b) => {
                if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
                return a.name.localeCompare(b.name);
              })
              .map((entry) => (
                <FileTreeItem
                  key={entry.name}
                  entry={entry}
                  basePath={currentPath}
                  selectedFile={selectedFile}
                  onFileSelect={onFileSelect}
                  onPathChange={onPathChange}
                  level={0}
                />
              ))}
          </Stack>
        )}
      </div>

      {/* New Directory Modal */}
      <Modal
        opened={newDirModalOpened}
        onClose={closeNewDirModal}
        title="Create New Folder"
        size="sm"
      >
        <Stack gap="md">
          <TextInput
            label="Folder name"
            placeholder="Enter folder name"
            value={newDirName}
            onChange={(e) => setNewDirName(e.currentTarget.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') handleCreateDirectory();
            }}
          />
          <Group justify="flex-end" gap="sm">
            <Button variant="subtle" onClick={closeNewDirModal}>
              Cancel
            </Button>
            <Button
              onClick={handleCreateDirectory}
              loading={createDirMutation.isPending}
              disabled={!newDirName.trim()}
            >
              Create
            </Button>
          </Group>
        </Stack>
      </Modal>

      <style>{`
        .file-tree-item:hover {
          background-color: var(--mantine-color-dark-5) !important;
        }
      `}</style>
    </Stack>
  );
}
