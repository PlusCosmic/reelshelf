import { useState, useCallback } from 'react';
import {
  Box,
  Group,
  Breadcrumbs,
  Anchor,
  ActionIcon,
  Tooltip,
  Text,
  ThemeIcon,
} from '@mantine/core';
import {
  IconHome,
  IconChevronRight,
  IconFolderCode,
} from '@tabler/icons-react';
import { useParams } from '@tanstack/react-router';
import { FileTree } from './FileTree';
import { MonacoFileEditor } from './MonacoFileEditor';

export function FileBrowser() {
  const { serverId } = useParams({ from: '/servers/$serverId/files' });
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
    <Box
      style={{
        height: 'calc(100vh - 100px)',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        background: 'linear-gradient(135deg, rgba(10, 10, 18, 0.95) 0%, rgba(15, 15, 28, 0.9) 100%)',
        borderRadius: 14,
        border: '1px solid rgba(168, 85, 247, 0.15)',
        position: 'relative',
      }}
    >
      {/* Top gradient accent */}
      <Box
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          height: 2,
          background: 'linear-gradient(90deg, #a855f7 0%, #ec4899 50%, #00d4ff 100%)',
        }}
      />

      {/* Breadcrumb Header */}
      <Box
        p="md"
        style={{
          background: 'linear-gradient(90deg, rgba(168, 85, 247, 0.05) 0%, rgba(236, 72, 153, 0.05) 100%)',
          borderBottom: '1px solid rgba(168, 85, 247, 0.1)',
        }}
      >
        <Group gap="md">
          <ThemeIcon
            size={40}
            radius="md"
            variant="gradient"
            gradient={{ from: 'cyberPurple', to: 'cyberPink', deg: 135 }}
            style={{ boxShadow: '0 0 15px rgba(168, 85, 247, 0.3)' }}
          >
            <IconFolderCode size={22} />
          </ThemeIcon>
          <Box flex={1}>
            <Text fw={700} size="lg" mb={4}>File Manager</Text>
            <Breadcrumbs
              separator={<IconChevronRight size={14} style={{ color: 'rgba(255,255,255,0.3)' }} />}
              separatorMargin={4}
            >
              <Tooltip label="Root directory">
                <ActionIcon
                  variant={currentPath === '/' ? 'gradient' : 'subtle'}
                  gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
                  size="sm"
                  onClick={() => setCurrentPath('/')}
                  style={{
                    boxShadow: currentPath === '/' ? '0 0 10px rgba(0, 212, 255, 0.3)' : 'none',
                  }}
                >
                  <IconHome size={14} />
                </ActionIcon>
              </Tooltip>
              {breadcrumbItems.map((item, index) => (
                <Anchor
                  key={item.path}
                  size="sm"
                  onClick={() => setCurrentPath(item.path)}
                  style={{
                    cursor: 'pointer',
                    color: index === breadcrumbItems.length - 1 ? '#00d4ff' : 'rgba(255,255,255,0.5)',
                    textDecoration: 'none',
                    transition: 'color 0.2s ease',
                  }}
                  fw={index === breadcrumbItems.length - 1 ? 600 : undefined}
                >
                  {item.label}
                </Anchor>
              ))}
            </Breadcrumbs>
          </Box>
        </Group>
      </Box>

      {/* Main Content */}
      <Box style={{ display: 'flex', flex: 1, minHeight: 0 }}>
        {/* File Tree Panel */}
        <Box
          style={{
            width: '28%',
            minWidth: 220,
            maxWidth: 350,
            borderRight: '1px solid rgba(168, 85, 247, 0.1)',
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
            background: 'rgba(0, 0, 0, 0.2)',
          }}
        >
          <FileTree
            serverId={serverId}
            currentPath={currentPath}
            selectedFile={selectedFilePath}
            onFileSelect={handleFileSelect}
            onPathChange={handlePathChange}
          />
        </Box>

        {/* Editor Panel */}
        <Box style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
          <MonacoFileEditor
            serverId={serverId}
            filePath={selectedFilePath}
            fileName={selectedFileName}
          />
        </Box>
      </Box>
    </Box>
  );
}
