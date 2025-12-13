import { useState, useCallback } from 'react';
import {
  Paper,
  Grid,
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
        height: 'calc(100vh - 180px)',
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

      {/* Main Content */}
      <Grid gutter={0} style={{ flex: 1, minHeight: 0 }}>
        {/* File Tree Panel */}
        <Grid.Col
          span={3}
          style={{
            borderRight: '1px solid var(--mantine-color-dark-4)',
            height: '100%',
            display: 'flex',
            flexDirection: 'column',
          }}
        >
          <FileTree
            currentPath={currentPath}
            selectedFile={selectedFilePath}
            onFileSelect={handleFileSelect}
            onPathChange={handlePathChange}
          />
        </Grid.Col>

        {/* Editor Panel */}
        <Grid.Col span={9} style={{ height: '100%' }}>
          <MonacoFileEditor
            filePath={selectedFilePath}
            fileName={selectedFileName}
          />
        </Grid.Col>
      </Grid>
    </Paper>
  );
}
