import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import type { DirectoryListing, FileEntry } from '@repo/minecraft-api-client';
import {
  listDirectory,
  getFileContent,
  saveFile,
  deleteFile,
  createDirectory,
} from '@repo/shared/services/minecraft';
import { getFileExtension } from '@repo/shared/utils/format';

// ============================================================================
// Query Hooks
// ============================================================================

/**
 * Fetches the contents of a directory on the Minecraft server
 * Cached with 30 second stale time - directory listings don't change frequently
 * @param serverId - The server ID to fetch files from
 * @param path - The directory path to list (e.g., "/" or "/plugins")
 */
export function useDirectoryListing(serverId: string | undefined, path: string) {
  return useQuery({
    queryKey: ['minecraft', 'files', serverId, path],
    queryFn: () => listDirectory(serverId!, path),
    staleTime: 30000, // 30 seconds
    retry: 1,
    enabled: !!serverId,
  });
}

/**
 * Fetches the content of a specific file
 * Always fetches fresh content (staleTime: 0) to ensure editor shows latest version
 * Only enabled when a path is provided
 * @param serverId - The server ID to fetch files from
 * @param path - The full path to the file, or null if no file is selected
 */
export function useFileContent(serverId: string | undefined, path: string | null) {
  return useQuery({
    queryKey: ['minecraft', 'files', 'content', serverId, path],
    queryFn: () => (path ? getFileContent(serverId!, path) : Promise.resolve('')),
    enabled: !!path && !!serverId,
    staleTime: 0, // Always fetch fresh content when selecting a file
    retry: 1,
  });
}

// ============================================================================
// Mutation Hooks
// ============================================================================

/**
 * Saves file content to the Minecraft server
 * Uses optimistic updates to immediately show updated modified timestamp
 * On success: invalidates caches to ensure server-client consistency
 * On error: rolls back optimistic update and shows error notification
 * @param serverId - The server ID to save files to
 */
export function useSaveFile(serverId: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ path, content }: { path: string; content: string }) =>
      saveFile(serverId!, path, content),
    onMutate: async ({ path, content }) => {
      const dirPath = path.substring(0, path.lastIndexOf('/')) || '/';
      const fileName = path.split('/').pop() || '';

      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: ['minecraft', 'files', serverId, dirPath] });
      await queryClient.cancelQueries({ queryKey: ['minecraft', 'files', 'content', serverId, path] });

      // Snapshot the previous values
      const previousDirListing = queryClient.getQueryData<DirectoryListing>(['minecraft', 'files', serverId, dirPath]);
      const previousContent = queryClient.getQueryData<string>(['minecraft', 'files', 'content', serverId, path]);

      // Optimistically update the directory listing with new modified time
      if (previousDirListing) {
        queryClient.setQueryData<DirectoryListing>(['minecraft', 'files', serverId, dirPath], {
          ...previousDirListing,
          entries: previousDirListing.entries.map((entry) =>
            entry.name === fileName
              ? { ...entry, lastModified: new Date(), size: new Blob([content]).size }
              : entry
          ),
        });
      }

      // Optimistically update the file content
      queryClient.setQueryData<string>(['minecraft', 'files', 'content', serverId, path], content);

      return { previousDirListing, previousContent, dirPath };
    },
    onError: (error: Error, { path }, context) => {
      // Rollback on error
      if (context?.previousDirListing) {
        queryClient.setQueryData(['minecraft', 'files', serverId, context.dirPath], context.previousDirListing);
      }
      if (context?.previousContent !== undefined) {
        queryClient.setQueryData(['minecraft', 'files', 'content', serverId, path], context.previousContent);
      }

      notifications.show({
        title: 'Save failed',
        message: error.message || 'Failed to save file',
        color: 'red',
      });
    },
    onSuccess: (_, { path }) => {
      const dirPath = path.substring(0, path.lastIndexOf('/')) || '/';

      // Invalidate to ensure consistency with server
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'files', 'content', serverId, path] });
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'files', serverId, dirPath] });

      notifications.show({
        title: 'File saved',
        message: `Successfully saved ${path.split('/').pop()}`,
        color: 'green',
      });
    },
  });
}

/**
 * Deletes a file from the Minecraft server
 * Uses optimistic updates to immediately remove the file from the listing
 * On error: rolls back to show the file again and displays error notification
 * @param serverId - The server ID to delete files from
 */
export function useDeleteFile(serverId: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (path: string) => deleteFile(serverId!, path),
    onMutate: async (path) => {
      const dirPath = path.substring(0, path.lastIndexOf('/')) || '/';
      const fileName = path.split('/').pop() || '';

      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: ['minecraft', 'files', serverId, dirPath] });

      // Snapshot the previous directory listing
      const previousDirListing = queryClient.getQueryData<DirectoryListing>(['minecraft', 'files', serverId, dirPath]);

      // Optimistically remove the file from the listing
      if (previousDirListing) {
        queryClient.setQueryData<DirectoryListing>(['minecraft', 'files', serverId, dirPath], {
          ...previousDirListing,
          entries: previousDirListing.entries.filter((entry) => entry.name !== fileName),
        });
      }

      // Remove the file content from cache
      queryClient.removeQueries({ queryKey: ['minecraft', 'files', 'content', serverId, path] });

      return { previousDirListing, dirPath };
    },
    onError: (error: Error, _path, context) => {
      // Rollback on error
      if (context?.previousDirListing) {
        queryClient.setQueryData(['minecraft', 'files', serverId, context.dirPath], context.previousDirListing);
      }

      notifications.show({
        title: 'Delete failed',
        message: error.message || 'Failed to delete file',
        color: 'red',
      });
    },
    onSuccess: (_, path) => {
      const dirPath = path.substring(0, path.lastIndexOf('/')) || '/';

      // Invalidate to ensure consistency
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'files', serverId, dirPath] });

      notifications.show({
        title: 'File deleted',
        message: `Successfully deleted ${path.split('/').pop()}`,
        color: 'green',
      });
    },
  });
}

/**
 * Creates a new directory on the Minecraft server
 * Uses optimistic updates to immediately show the new directory in the listing
 * On error: rolls back to remove the optimistically added directory
 * @param serverId - The server ID to create directories on
 */
export function useCreateDirectory(serverId: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (path: string) => createDirectory(serverId!, path),
    onMutate: async (path) => {
      const parentPath = path.substring(0, path.lastIndexOf('/')) || '/';
      const dirName = path.split('/').pop() || '';

      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: ['minecraft', 'files', serverId, parentPath] });

      // Snapshot the previous directory listing
      const previousDirListing = queryClient.getQueryData<DirectoryListing>(['minecraft', 'files', serverId, parentPath]);

      // Optimistically add the new directory to the listing
      if (previousDirListing) {
        const newEntry: FileEntry = {
          name: dirName,
          path: path,
          isDirectory: true,
          size: 0,
          lastModified: new Date(),
        };

        queryClient.setQueryData<DirectoryListing>(['minecraft', 'files', serverId, parentPath], {
          ...previousDirListing,
          entries: [...previousDirListing.entries, newEntry].sort((a, b) => {
            // Directories first, then alphabetically
            if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
            return a.name.localeCompare(b.name);
          }),
        });
      }

      return { previousDirListing, parentPath };
    },
    onError: (error: Error, _path, context) => {
      // Rollback on error
      if (context?.previousDirListing) {
        queryClient.setQueryData(['minecraft', 'files', serverId, context.parentPath], context.previousDirListing);
      }

      notifications.show({
        title: 'Failed to create directory',
        message: error.message || 'Failed to create directory',
        color: 'red',
      });
    },
    onSuccess: (_, path) => {
      const parentPath = path.substring(0, path.lastIndexOf('/')) || '/';

      // Invalidate to ensure consistency
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'files', serverId, parentPath] });

      notifications.show({
        title: 'Directory created',
        message: `Successfully created ${path.split('/').pop()}`,
        color: 'green',
      });
    },
  });
}

// ============================================================================
// Utility Functions
// ============================================================================

// Re-export shared utilities for convenience
export { formatFileSize, getFileExtension } from '@repo/shared/utils/format';

/**
 * Maps file extensions to Monaco Editor language IDs for syntax highlighting
 * Supports common Minecraft server file types (JSON, YAML, properties, etc.)
 * @param extension - The file extension including dot (e.g., ".json")
 * @returns The Monaco language ID (e.g., "json", "yaml", "ini")
 */
export function getLanguageFromExtension(extension: string): string {
  const languageMap: Record<string, string> = {
    '.json': 'json',
    '.json5': 'json',
    '.properties': 'ini',
    '.yaml': 'yaml',
    '.yml': 'yaml',
    '.toml': 'ini',
    '.cfg': 'ini',
    '.conf': 'ini',
    '.ini': 'ini',
    '.txt': 'plaintext',
    '.log': 'plaintext',
    '.md': 'markdown',
    '.xml': 'xml',
    '.html': 'html',
    '.htm': 'html',
    '.css': 'css',
    '.js': 'javascript',
    '.ts': 'typescript',
    '.sh': 'shell',
    '.bash': 'shell',
    '.zsh': 'shell',
    '.bat': 'bat',
    '.cmd': 'bat',
    '.ps1': 'powershell',
    '.java': 'java',
    '.py': 'python',
    '.rb': 'ruby',
    '.lua': 'lua',
    '.sql': 'sql',
  };

  return languageMap[extension.toLowerCase()] || 'plaintext';
}

/**
 * Checks if a file can be opened in the text editor
 * Returns true for text-based files that can be safely edited
 * Includes Minecraft-specific extensions like .mcfunction and .mcmeta
 * @param filename - The filename to check
 * @returns True if the file is editable, false for binary files
 */
export function isEditableFile(filename: string): boolean {
  const extension = getFileExtension(filename).toLowerCase();
  const editableExtensions = new Set([
    '.json', '.json5', '.properties', '.yaml', '.yml', '.toml',
    '.cfg', '.conf', '.ini', '.txt', '.log', '.md', '.xml',
    '.html', '.htm', '.css', '.js', '.ts', '.sh', '.bash',
    '.zsh', '.bat', '.cmd', '.ps1', '.java', '.py', '.rb',
    '.lua', '.sql', '.mcfunction', '.mcmeta',
  ]);

  return editableExtensions.has(extension);
}

/**
 * Formats an ISO date string into a human-readable format
 * Uses absolute date format suitable for file management (e.g., "25 Dec 2024, 14:30")
 * @param dateString - The ISO date string to format
 * @returns Formatted date string
 */
export function formatDate(dateString: string): string {
  try {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return dateString;
  }
}
