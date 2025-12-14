import { useState, useCallback } from 'react';
import {
  Paper,
  Box,
  Group,
  Breadcrumbs,
  Anchor,
  ActionIcon,
  Tooltip,
} from '@mantine/core';
import {
  IconHome,
  IconChevronRight,
} from '@tabler/icons-react';
import { FileTree } from './FileTree';
import { MonacoFileEditor } from './MonacoFileEditor';

export function FileBrowser() {
  const [currentPath, setCurrentPath] = useState('/');
  const [selectedFilePath, setSelectedFilePath] = useState<string | null>(null);
  const [selectedFileName, setSelectedFileName] = useState<string | null>(null);

  const handleFileSelect = useCallback((path: string, name: string) => {
    setSelectedFilePath(path);
    setSelectedFileName(name);
  }, []);

  const handlePathChange = useCallback((path: string) => {
    setCurrentPath(path);
  }, []);

  // Build breadcrumb items from current path
  const breadcrumbItems = currentPath
    .split('/')
    .filter(Boolean)
    .map((part, index, arr) => {
      const path = '/' + arr.slice(0, index + 1).join('/');
      return { label: part, path };
    });

  return (
    <Paper
      radius="md"
      style={{
        height: 'calc(100vh - 100px)',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        backgroundColor: 'var(--mantine-color-dark-8)',
      }}
    >
      {/* Breadcrumb Header */}
      <Group
        p="sm"
        style={{
          borderBottom: '1px solid var(--mantine-color-dark-4)',
        }}
      >
        <Breadcrumbs
          separator={<IconChevronRight size={14} />}
          separatorMargin={4}
        >
          <Tooltip label="Root directory">
            <ActionIcon
              variant={currentPath === '/' ? 'filled' : 'subtle'}
              size="sm"
              onClick={() => setCurrentPath('/')}
            >
              <IconHome size={16} />
            </ActionIcon>
          </Tooltip>
          {breadcrumbItems.map((item, index) => (
            <Anchor
              key={item.path}
              size="sm"
              onClick={() => setCurrentPath(item.path)}
              style={{ cursor: 'pointer' }}
              c={index === breadcrumbItems.length - 1 ? undefined : 'dimmed'}
              fw={index === breadcrumbItems.length - 1 ? 500 : undefined}
            >
              {item.label}
            </Anchor>
          ))}
        </Breadcrumbs>
      </Group>

      {/* Main Content - using flexbox instead of Grid for proper height inheritance */}
      <Box style={{ display: 'flex', flex: 1, minHeight: 0 }}>
        {/* File Tree Panel */}
        <Box
          style={{
            width: '25%',
            borderRight: '1px solid var(--mantine-color-dark-4)',
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
          }}
        >
          <FileTree
            currentPath={currentPath}
            selectedFile={selectedFilePath}
            onFileSelect={handleFileSelect}
            onPathChange={handlePathChange}
          />
        </Box>

        {/* Editor Panel */}
        <Box style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
          <MonacoFileEditor
            filePath={selectedFilePath}
            fileName={selectedFileName}
          />
        </Box>
      </Box>
    </Paper>
  );
}
