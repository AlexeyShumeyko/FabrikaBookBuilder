using System;
using System.Runtime.InteropServices;
using System.Text;
using PhotoBook.Application;
using PhotoBook.Application.Sites;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>
    /// Keeps the session in the Windows Credential Manager instead of a file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file next to the settings would be readable by any process running as the customer, and
    /// a token is the whole account for seven days. The Credential Manager encrypts the entry
    /// with the customer's own profile through DPAPI, and the program never handles the key -
    /// which is what <see cref="SiteSession"/> promised when it said the password is not kept.
    /// </para>
    /// <para>
    /// The entry is written for the local machine, so it survives a sign-out and stays readable
    /// by the same customer only. It is removed on <see cref="Clear"/> and whenever the stored
    /// session turns out to be expired, so a dead token is not left lying beside the live one.
    /// </para>
    /// </remarks>
    internal sealed class CredentialManagerSiteSessionStore : ISiteSessionStore
    {
        /// <summary>What the entry is called in the Credential Manager.</summary>
        internal const string DefaultTarget = "FabrikaBookBuilder/site-session";

        private const uint CredentialTypeGeneric = 1;
        private const uint PersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        private readonly string _target;
        private readonly Func<DateTimeOffset> _now;
        private readonly IJournal? _journal;

        public CredentialManagerSiteSessionStore(
            string target = DefaultTarget,
            Func<DateTimeOffset>? now = null,
            IJournal? journal = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _now = now ?? (() => DateTimeOffset.Now);
            _journal = journal;
        }

        public SiteSession? Load()
        {
            if (!CredRead(_target, CredentialTypeGeneric, 0, out var handle))
            {
                var error = Marshal.GetLastWin32Error();

                if (error != ErrorNotFound)
                {
                    _journal?.Write(
                        JournalLevel.Warning,
                        "site-session",
                        "Не удалось прочитать сохранённую сессию, код " + error + ".");
                }

                return null;
            }

            string payload;
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(handle);
                var blob = new byte[credential.CredentialBlobSize];

                if (blob.Length > 0)
                    Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);

                payload = Encoding.UTF8.GetString(blob);
            }
            finally
            {
                CredFree(handle);
            }

            var read = SiteSessionTicket.Read(payload, _now());
            if (read.IsSuccess)
                return read.Value;

            // An expired or unreadable entry is removed rather than offered again: keeping it
            // would make the next start ask the same question without a way out.
            _journal?.Write(JournalLevel.Information, "site-session", read.Error!);
            Clear();

            return null;
        }

        public void Save(SiteSession session)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));

            var payload = Encoding.UTF8.GetBytes(SiteSessionTicket.Encode(session));
            var targetName = IntPtr.Zero;
            var userName = IntPtr.Zero;
            var blob = IntPtr.Zero;

            try
            {
                targetName = Marshal.StringToCoTaskMemUni(_target);
                userName = Marshal.StringToCoTaskMemUni(session.Region.DisplayName);
                blob = Marshal.AllocCoTaskMem(payload.Length);
                Marshal.Copy(payload, 0, blob, payload.Length);

                var credential = new Credential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = targetName,
                    CredentialBlobSize = (uint)payload.Length,
                    CredentialBlob = blob,
                    Persist = PersistLocalMachine,
                    UserName = userName
                };

                if (!CredWrite(ref credential, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    _journal?.Write(
                        JournalLevel.Error,
                        "site-session",
                        "Не удалось сохранить сессию, код " + error + ".");
                    throw new InvalidOperationException(
                        "Windows не сохранила сессию, код " + error + ".");
                }
            }
            finally
            {
                Free(targetName);
                Free(userName);
                Free(blob);
            }
        }

        public void Clear()
        {
            if (!CredDelete(_target, CredentialTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();

                if (error != ErrorNotFound)
                {
                    _journal?.Write(
                        JournalLevel.Warning,
                        "site-session",
                        "Не удалось удалить сохранённую сессию, код " + error + ".");
                }
            }
        }

        private static void Free(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pointer);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(
            string target,
            uint type,
            uint flags,
            out IntPtr credential);

        [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32", EntryPoint = "CredFree", SetLastError = false)]
        private static extern void CredFree(IntPtr credential);
    }
}
