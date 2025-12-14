import {
  Paper,
  Text,
  Stack,
  TextInput,
  Button,
  Group,
  ScrollArea,
  Badge,
  ActionIcon,
  Tooltip,
  Box,
} from '@mantine/core';
import {
  IconTerminal,
  IconSend,
  IconTrash,
  IconRefresh,
  IconWifi,
  IconWifiOff,
} from '@tabler/icons-react';
import { useState, useRef, useEffect, useCallback } from 'react';
import { useMinecraftConsole, type ConsoleEntry } from '../../hooks/useMinecraftConsole';

export function ConsoleTerminal() {
  const {
    isConnected,
    isConnecting,
    entries,
    sendCommand,
    clearEntries,
    connect,
  } = useMinecraftConsole();

  const [command, setCommand] = useState('');
  const [commandHistory, setCommandHistory] = useState<string[]>([]);
  const [historyIndex, setHistoryIndex] = useState(-1);

  const scrollAreaRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Auto-scroll to bottom when new entries arrive
  useEffect(() => {
    if (scrollAreaRef.current) {
      const viewport = scrollAreaRef.current.querySelector('[data-viewport]');
      if (viewport) {
        viewport.scrollTop = viewport.scrollHeight;
      }
    }
  }, [entries]);

  const handleSendCommand = useCallback(() => {
    const trimmedCommand = command.trim();
    if (!trimmedCommand) return;

    if (sendCommand(trimmedCommand)) {
      // Add to command history
      setCommandHistory(prev => {
        const newHistory = [...prev.filter(c => c !== trimmedCommand), trimmedCommand];
        // Keep last 100 commands
        return newHistory.slice(-100);
      });
      setCommand('');
      setHistoryIndex(-1);
    }
  }, [command, sendCommand]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendCommand();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (commandHistory.length > 0) {
        const newIndex = historyIndex < commandHistory.length - 1 ? historyIndex + 1 : historyIndex;
        setHistoryIndex(newIndex);
        setCommand(commandHistory[commandHistory.length - 1 - newIndex] || '');
      }
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (historyIndex > 0) {
        const newIndex = historyIndex - 1;
        setHistoryIndex(newIndex);
        setCommand(commandHistory[commandHistory.length - 1 - newIndex] || '');
      } else if (historyIndex === 0) {
        setHistoryIndex(-1);
        setCommand('');
      }
    }
  }, [commandHistory, historyIndex, handleSendCommand]);

  const getEntryStyle = (entry: ConsoleEntry) => {
    switch (entry.type) {
      case 'command':
        return { color: 'var(--mantine-color-blue-4)', fontWeight: 600 };
      case 'response':
        return { color: 'var(--mantine-color-green-4)' };
      case 'error':
        return { color: 'var(--mantine-color-red-4)' };
      case 'system':
        return { color: 'var(--mantine-color-yellow-4)', fontStyle: 'italic' as const };
      case 'log':
        if (entry.level === 'warning') return { color: 'var(--mantine-color-orange-4)' };
        if (entry.level === 'error') return { color: 'var(--mantine-color-red-4)' };
        if (entry.level === 'debug') return { color: 'var(--mantine-color-gray-5)' };
        return { color: 'var(--mantine-color-gray-3)' };
      default:
        return { color: 'var(--mantine-color-gray-3)' };
    }
  };

  const formatTimestamp = (date: Date) => {
    return date.toLocaleTimeString('en-GB', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  };

  const renderEntry = (entry: ConsoleEntry) => {
    const style = getEntryStyle(entry);
    const timestamp = formatTimestamp(entry.timestamp);

    if (entry.type === 'command') {
      return (
        <Group key={entry.id} gap={4} wrap="nowrap" align="flex-start">
          <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0 }}>
            [{timestamp}]
          </Text>
          <Text size="sm" ff="monospace" style={style}>
            {'>'} {entry.text}
          </Text>
        </Group>
      );
    }

    if (entry.type === 'response' || entry.type === 'error') {
      return (
        <Group key={entry.id} gap={4} wrap="nowrap" align="flex-start" pl="md">
          <Text size="sm" ff="monospace" style={style}>
            {entry.text || '(no response)'}
          </Text>
        </Group>
      );
    }

    return (
      <Group key={entry.id} gap={4} wrap="nowrap" align="flex-start">
        <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0 }}>
          [{timestamp}]
        </Text>
        <Text size="sm" ff="monospace" style={{ ...style, wordBreak: 'break-all' }}>
          {entry.text}
        </Text>
      </Group>
    );
  };

  return (
    <Paper
      p="md"
      radius="md"
      style={{
        height: 'calc(100vh - 100px)',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: 'var(--mantine-color-dark-8)',
      }}
    >
      {/* Header */}
      <Group justify="space-between" mb="sm">
        <Group gap="sm">
          <IconTerminal size={20} />
          <Text fw={600}>Server Console</Text>
          <Badge
            color={isConnected ? 'green' : isConnecting ? 'yellow' : 'red'}
            variant="dot"
            size="sm"
          >
            {isConnected ? 'Connected' : isConnecting ? 'Connecting...' : 'Disconnected'}
          </Badge>
        </Group>
        <Group gap="xs">
          <Tooltip label="Clear console">
            <ActionIcon variant="subtle" onClick={clearEntries} size="sm">
              <IconTrash size={16} />
            </ActionIcon>
          </Tooltip>
          {!isConnected && !isConnecting && (
            <Tooltip label="Reconnect">
              <ActionIcon variant="subtle" onClick={connect} size="sm" color="blue">
                <IconRefresh size={16} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
      </Group>

      {/* Console Output */}
      <ScrollArea
        ref={scrollAreaRef}
        style={{
          flex: 1,
          backgroundColor: 'var(--mantine-color-dark-9)',
          borderRadius: 'var(--mantine-radius-sm)',
        }}
        p="sm"
      >
        <Stack gap={2}>
          {entries.length === 0 ? (
            <Text c="dimmed" size="sm" ff="monospace">
              {isConnected
                ? 'Connected. Waiting for server logs...'
                : 'Not connected. Logs will appear here when connected.'}
            </Text>
          ) : (
            entries.map(renderEntry)
          )}
        </Stack>
      </ScrollArea>

      {/* Input Area */}
      <Box mt="sm">
        <Group gap="sm">
          <TextInput
            ref={inputRef}
            placeholder={isConnected ? 'Enter command...' : 'Connect to send commands...'}
            value={command}
            onChange={(e) => setCommand(e.currentTarget.value)}
            onKeyDown={handleKeyDown}
            disabled={!isConnected}
            style={{ flex: 1 }}
            styles={{
              input: {
                fontFamily: 'var(--mantine-font-family-monospace)',
                backgroundColor: 'var(--mantine-color-dark-7)',
              },
            }}
            leftSection={
              isConnected ? (
                <IconWifi size={16} color="var(--mantine-color-green-5)" />
              ) : (
                <IconWifiOff size={16} color="var(--mantine-color-red-5)" />
              )
            }
          />
          <Button
            leftSection={<IconSend size={16} />}
            onClick={handleSendCommand}
            disabled={!isConnected || !command.trim()}
          >
            Send
          </Button>
        </Group>
        <Text size="xs" c="dimmed" mt={4}>
          Press Enter to send. Use Arrow Up/Down to navigate command history.
        </Text>
      </Box>
    </Paper>
  );
}
