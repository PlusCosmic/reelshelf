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
  Box,
  ThemeIcon,
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
  IconFileCode,
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
          padding: '6px 10px',
          paddingLeft: `${10 + level * 16}px`,
          borderRadius: 8,
          background: isSelected
            ? 'linear-gradient(90deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.1) 100%)'
            : 'transparent',
          borderLeft: isSelected ? '2px solid #00d4ff' : '2px solid transparent',
          cursor: editable || isDirectory ? 'pointer' : 'default',
          opacity: !isDirectory && !editable ? 0.4 : 1,
          transition: 'all 0.15s ease',
        }}
        className="file-tree-item"
      >
        <Group gap="xs" justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
            {isDirectory ? (
              <>
                {isExpanded ? (
                  <IconChevronDown size={14} style={{ flexShrink: 0, color: '#a855f7' }} />
                ) : (
                  <IconChevronRight size={14} style={{ flexShrink: 0, color: '#a855f7' }} />
                )}
                {isExpanded ? (
                  <IconFolderOpen
                    size={16}
                    style={{
                      flexShrink: 0,
                      color: '#a855f7',
                      filter: 'drop-shadow(0 0 4px rgba(168, 85, 247, 0.5))',
                    }}
                  />
                ) : (
                  <IconFolder
                    size={16}
                    style={{
                      flexShrink: 0,
                      color: '#a855f7',
                    }}
                  />
                )}
              </>
            ) : (
              <>
                <span style={{ width: 14 }} />
                {editable ? (
                  <IconFileCode
                    size={16}
                    style={{
                      flexShrink: 0,
                      color: isSelected ? '#00d4ff' : 'rgba(255, 255, 255, 0.6)',
                      filter: isSelected ? 'drop-shadow(0 0 4px rgba(0, 212, 255, 0.5))' : 'none',
                    }}
                  />
                ) : (
                  <IconFile
                    size={16}
                    style={{ flexShrink: 0, color: 'rgba(255, 255, 255, 0.3)' }}
                  />
                )}
              </>
            )}
            <Text
              size="sm"
              truncate
              style={{
                flex: 1,
                minWidth: 0,
                color: isSelected ? '#00d4ff' : 'inherit',
              }}
              fw={isSelected ? 600 : 400}
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
                style={{ opacity: 0.5 }}
                color="gray"
              >
                <IconDots size={14} />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown
              style={{
                background: 'rgba(15, 15, 25, 0.95)',
                border: '1px solid rgba(0, 212, 255, 0.2)',
                backdropFilter: 'blur(10px)',
              }}
            >
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
      <Group gap="xs" pl={10 + level * 16} py={6}>
        <Loader size="xs" color="cyberBlue" />
        <Text size="xs" c="dimmed">Loading...</Text>
      </Group>
    );
  }

  if (error) {
    return (
      <Group gap="xs" pl={10 + level * 16} py={6}>
        <IconAlertCircle size={14} color="#ff4444" />
        <Text size="xs" c="red">Failed to load</Text>
      </Group>
    );
  }

  if (!data?.entries?.length) {
    return (
      <Text size="xs" c="dimmed" pl={10 + level * 16} py={6}>
        Empty directory
      </Text>
    );
  }

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
          borderBottom: '1px solid rgba(168, 85, 247, 0.1)',
          background: 'rgba(168, 85, 247, 0.03)',
        }}
      >
        <Text size="sm" fw={600} c="dimmed" tt="uppercase" style={{ letterSpacing: '0.05em' }}>
          Files
        </Text>
        <Group gap={4}>
          <Tooltip label="New folder">
            <ActionIcon
              variant="light"
              size="sm"
              onClick={openNewDirModal}
              color="cyberPurple"
              style={{
                background: 'rgba(168, 85, 247, 0.1)',
                border: '1px solid rgba(168, 85, 247, 0.2)',
              }}
            >
              <IconFolderPlus size={14} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Refresh">
            <ActionIcon
              variant="light"
              size="sm"
              onClick={() => refetch()}
              color="cyberBlue"
              style={{
                background: 'rgba(0, 212, 255, 0.1)',
                border: '1px solid rgba(0, 212, 255, 0.2)',
              }}
            >
              <IconRefresh size={14} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {/* Tree */}
      <Box style={{ flex: 1, overflow: 'auto', padding: '8px 4px' }} className="cyber-scrollbar">
        {isLoading ? (
          <Stack align="center" py="xl">
            <Loader size="md" color="cyberBlue" />
            <Text size="sm" c="dimmed">Loading files...</Text>
          </Stack>
        ) : error ? (
          <Stack align="center" py="xl" gap="sm">
            <ThemeIcon size={48} radius="xl" color="red" variant="light">
              <IconAlertCircle size={24} />
            </ThemeIcon>
            <Text size="sm" c="red">Failed to load files</Text>
            <Button
              size="xs"
              variant="light"
              onClick={() => refetch()}
              leftSection={<IconRefresh size={14} />}
              color="cyberBlue"
            >
              Retry
            </Button>
          </Stack>
        ) : !data?.entries?.length ? (
          <Stack align="center" py="xl" gap="sm">
            <ThemeIcon size={48} radius="xl" color="gray" variant="light">
              <IconFolder size={24} />
            </ThemeIcon>
            <Text size="sm" c="dimmed">No files found</Text>
          </Stack>
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
      </Box>

      {/* New Directory Modal */}
      <Modal
        opened={newDirModalOpened}
        onClose={closeNewDirModal}
        title="Create New Folder"
        size="sm"
        styles={{
          header: {
            background: 'linear-gradient(90deg, rgba(168, 85, 247, 0.1) 0%, rgba(0, 212, 255, 0.1) 100%)',
            borderBottom: '1px solid rgba(168, 85, 247, 0.2)',
          },
          content: {
            background: 'rgba(15, 15, 25, 0.98)',
            border: '1px solid rgba(168, 85, 247, 0.2)',
          },
        }}
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
            styles={{
              input: {
                background: 'rgba(0, 0, 0, 0.3)',
                border: '1px solid rgba(168, 85, 247, 0.2)',
              },
            }}
          />
          <Group justify="flex-end" gap="sm">
            <Button variant="subtle" onClick={closeNewDirModal} color="gray">
              Cancel
            </Button>
            <Button
              onClick={handleCreateDirectory}
              loading={createDirMutation.isPending}
              disabled={!newDirName.trim()}
              variant="gradient"
              gradient={{ from: 'cyberPurple', to: 'cyberBlue', deg: 135 }}
            >
              Create
            </Button>
          </Group>
        </Stack>
      </Modal>

      <style>{`
        .file-tree-item:hover {
          background: rgba(0, 212, 255, 0.05) !important;
        }
      `}</style>
    </Stack>
  );
}
