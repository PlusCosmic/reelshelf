using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Windows;

/// <summary>
/// The device token in Windows Credential Manager, as the generic credential "Reelshelf Uploader" for this user
/// (<c>CredWrite</c>, <c>CredRead</c>, <c>CredDelete</c>). It is never written anywhere else, logs included.
/// </summary>
public sealed partial class WindowsCredentialStore(string targetName = WindowsCredentialStore.TargetName) : ICredentialStore
{
    public const string TargetName = "Reelshelf Uploader";

    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? ReadToken()
    {
        if (!CredRead(targetName, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            byte[] blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void WriteToken(string token)
    {
        byte[] blob = Encoding.Unicode.GetBytes(token);
        IntPtr blobPointer = Marshal.AllocHGlobal(blob.Length);
        IntPtr targetPointer = Marshal.StringToHGlobalUni(targetName);
        IntPtr userPointer = Marshal.StringToHGlobalUni(Environment.UserName);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            Credential credential = new()
            {
                Type = CredTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = userPointer
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            // Don't leave the token in unmanaged memory.
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(targetPointer);
            Marshal.FreeHGlobal(userPointer);
        }
    }

    public void DeleteToken()
    {
        if (!CredDelete(targetName, CredTypeGeneric, 0))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, int type, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}
