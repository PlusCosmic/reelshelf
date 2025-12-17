import {
  Box,
  Text,
  Stack,
  TextInput,
  Button,
  Group,
  ScrollArea,
  Badge,
  ActionIcon,
  Tooltip,
  ThemeIcon,
} from '@mantine/core';
import {
  IconTerminal2,
  IconSend,
  IconTrash,
  IconRefresh,
  IconWifi,
  IconWifiOff,
  IconChevronRight,
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
      setCommandHistory(prev => {
        const newHistory = [...prev.filter(c => c !== trimmedCommand), trimmedCommand];
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
        return { color: '#00d4ff', fontWeight: 600 };
      case 'response':
        return { color: '#00ff88' };
      case 'error':
        return { color: '#ff4444' };
      case 'system':
        return { color: '#fbbf24', fontStyle: 'italic' as const };
      case 'log':
        if (entry.level === 'warning') return { color: '#f97316' };
        if (entry.level === 'error') return { color: '#ff4444' };
        if (entry.level === 'debug') return { color: '#6b7280' };
        return { color: '#a0a0a0' };
      default:
        return { color: '#a0a0a0' };
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
        <Group key={entry.id} gap={8} wrap="nowrap" align="flex-start">
          <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0, opacity: 0.6 }}>
            [{timestamp}]
          </Text>
          <IconChevronRight size={14} style={{ color: '#00d4ff', flexShrink: 0, marginTop: 2 }} />
          <Text size="sm" ff="monospace" style={{ ...style, textShadow: '0 0 10px rgba(0, 212, 255, 0.5)' }}>
            {entry.text}
          </Text>
        </Group>
      );
    }

    if (entry.type === 'response' || entry.type === 'error') {
      return (
        <Group key={entry.id} gap={4} wrap="nowrap" align="flex-start" pl={30}>
          <Text size="sm" ff="monospace" style={style}>
            {entry.text || '(no response)'}
          </Text>
        </Group>
      );
    }

    return (
      <Group key={entry.id} gap={8} wrap="nowrap" align="flex-start">
        <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0, opacity: 0.6 }}>
          [{timestamp}]
        </Text>
        <Text size="sm" ff="monospace" style={{ ...style, wordBreak: 'break-all' }}>
          {entry.text}
        </Text>
      </Group>
    );
  };

  return (
    <Box
      style={{
        height: 'calc(100vh - 100px)',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        background: 'linear-gradient(135deg, rgba(10, 10, 18, 0.95) 0%, rgba(15, 15, 28, 0.9) 100%)',
        borderRadius: 14,
        border: '1px solid rgba(0, 212, 255, 0.15)',
        overflow: 'hidden',
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
          background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 50%, #ec4899 100%)',
        }}
      />

      {/* Header */}
      <Box
        p="md"
        style={{
          background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.05) 0%, rgba(168, 85, 247, 0.05) 100%)',
          borderBottom: '1px solid rgba(0, 212, 255, 0.1)',
        }}
      >
        <Group justify="space-between">
          <Group gap="md">
            <ThemeIcon
              size={40}
              radius="md"
              variant="gradient"
              gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
              style={{ boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)' }}
            >
              <IconTerminal2 size={22} />
            </ThemeIcon>
            <Stack gap={2}>
              <Text fw={700} size="lg">Server Console</Text>
              <Text size="xs" c="dimmed">Real-time server output</Text>
            </Stack>
          </Group>
          <Group gap="sm">
            <Badge
              size="lg"
              variant="dot"
              color={isConnected ? 'green' : isConnecting ? 'yellow' : 'red'}
              style={{
                background: isConnected
                  ? 'rgba(0, 255, 136, 0.1)'
                  : isConnecting
                  ? 'rgba(251, 191, 36, 0.1)'
                  : 'rgba(255, 68, 68, 0.1)',
                border: `1px solid ${
                  isConnected
                    ? 'rgba(0, 255, 136, 0.3)'
                    : isConnecting
                    ? 'rgba(251, 191, 36, 0.3)'
                    : 'rgba(255, 68, 68, 0.3)'
                }`,
              }}
            >
              {isConnected ? 'Connected' : isConnecting ? 'Connecting...' : 'Disconnected'}
            </Badge>
            <Tooltip label="Clear console">
              <ActionIcon
                variant="light"
                color="red"
                onClick={clearEntries}
                size="lg"
                radius="md"
                style={{
                  background: 'rgba(255, 68, 68, 0.1)',
                  border: '1px solid rgba(255, 68, 68, 0.2)',
                }}
              >
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
            {!isConnected && !isConnecting && (
              <Tooltip label="Reconnect">
                <ActionIcon
                  variant="light"
                  color="blue"
                  onClick={connect}
                  size="lg"
                  radius="md"
                  style={{
                    background: 'rgba(0, 212, 255, 0.1)',
                    border: '1px solid rgba(0, 212, 255, 0.2)',
                  }}
                >
                  <IconRefresh size={18} />
                </ActionIcon>
              </Tooltip>
            )}
          </Group>
        </Group>
      </Box>

      {/* Console Output */}
      <ScrollArea
        ref={scrollAreaRef}
        style={{ flex: 1 }}
        p="md"
        className="cyber-scrollbar"
      >
        <Stack gap={4}>
          {entries.length === 0 ? (
            <Box py="xl" ta="center">
              <Text c="dimmed" size="sm" ff="monospace">
                {isConnected
                  ? '// Connected. Waiting for server logs...'
                  : '// Not connected. Logs will appear here when connected.'}
              </Text>
            </Box>
          ) : (
            entries.map(renderEntry)
          )}
        </Stack>
      </ScrollArea>

      {/* Input Area */}
      <Box
        p="md"
        style={{
          background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.03) 0%, rgba(168, 85, 247, 0.03) 100%)',
          borderTop: '1px solid rgba(0, 212, 255, 0.1)',
        }}
      >
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
                fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
                background: 'rgba(0, 0, 0, 0.3)',
                border: '1px solid rgba(0, 212, 255, 0.2)',
                color: '#00d4ff',
                '&:focus': {
                  borderColor: '#00d4ff',
                  boxShadow: '0 0 10px rgba(0, 212, 255, 0.2)',
                },
                '&::placeholder': {
                  color: 'rgba(255, 255, 255, 0.3)',
                },
              },
            }}
            leftSection={
              isConnected ? (
                <IconWifi size={16} style={{ color: '#00ff88' }} />
              ) : (
                <IconWifiOff size={16} style={{ color: '#ff4444' }} />
              )
            }
          />
          <Button
            leftSection={<IconSend size={16} />}
            onClick={handleSendCommand}
            disabled={!isConnected || !command.trim()}
            variant="gradient"
            gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
            style={{
              boxShadow: isConnected && command.trim()
                ? '0 0 15px rgba(0, 212, 255, 0.3)'
                : 'none',
            }}
          >
            Send
          </Button>
        </Group>
        <Text size="xs" c="dimmed" mt={8} ta="center">
          Press Enter to send • Arrow Up/Down for history
        </Text>
      </Box>
    </Box>
  );
}
