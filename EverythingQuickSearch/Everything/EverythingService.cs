using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace EverythingQuickSearch
{
    /// <summary>
    /// Async wrapper around the Everything search engine SDK.
    /// Communicates with the Everything service via Windows messages (IPC).
    /// Requires Everything 1.4.1+ service to be running and Everything64.dll present.
    /// </summary>
    public class EverythingService : IDisposable
    {
        private readonly Dictionary<int, TaskCompletionSource<List<FileItem>>> _pendingQueries = new();
        private int _nextReplyId = 1000;
        private readonly HwndSource _source;
        private readonly IntPtr _hwnd;
        // Serialises concurrent SearchAsync callers so that the single-threaded Everything
        // query state (SetSearch / SetOffset / SetMax / QueryW) is never interleaved.
        private readonly SemaphoreSlim _searchSemaphore = new SemaphoreSlim(1, 1);

        // MAX_PATH is 260, but Everything supports long paths up to 32767 characters.
        private const int MaxPathLength = 32767;

        /// <summary>
        /// Initialises the service and registers the window as the IPC reply target.
        /// </summary>
        /// <param name="window">The WPF window whose HWND will receive Everything reply messages.</param>
        public EverythingService(Window window)
        {
            _hwnd = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);

            Everything_SetReplyWindow(_hwnd);
            Everything_SetReplyID(_nextReplyId);
        }

        /// <summary>
        /// Sends a search query to Everything asynchronously and returns the matching file items.
        /// The call is serialised internally so callers do not need to coordinate.
        /// </summary>
        /// <param name="searchText">The search string (supports Everything syntax and optional regex).</param>
        /// <param name="setSort">Sort order constant from the Everything SDK (e.g. 1 = name ascending).</param>
        /// <param name="offset">Zero-based result offset for pagination.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <param name="enableRegex">When <see langword="true"/>, the search string is treated as a regular expression.</param>
        /// <param name="cancellationToken">Token used to cancel the pending query.</param>
        /// <returns>A task that resolves to the list of matching <see cref="FileItem"/> objects.</returns>
        public async Task<List<FileItem>> SearchAsync(
            string searchText,
            int setSort,
            int offset,
            int maxResults,
            bool enableRegex,
            CancellationToken cancellationToken = default)
        {
            await _searchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int replyId = Interlocked.Increment(ref _nextReplyId);

                var tcs = new TaskCompletionSource<List<FileItem>>();

                // Register cancellation so the pending query is cleaned up if the token fires.
                using var reg = cancellationToken.Register(() =>
                {
                    if (_pendingQueries.Remove(replyId))
                        tcs.TrySetCanceled(cancellationToken);
                });

                _pendingQueries[replyId] = tcs;

                // Configure the Everything query and dispatch it asynchronously (bWait=false).
                // The reply arrives as a Windows message to WndProc on the UI thread.
                Everything_SetSearchW(searchText);
                Everything_SetOffset((uint)offset);
                Everything_SetMax((uint)maxResults);
                Everything_SetRequestFlags(
                    EVERYTHING_REQUEST_FILE_NAME |
                    EVERYTHING_REQUEST_PATH |
                    EVERYTHING_REQUEST_DATE_MODIFIED |
                    EVERYTHING_REQUEST_SIZE);
                Everything_SetRegex(enableRegex);
                Everything_SetReplyID(replyId);
                Everything_SetSort(setSort);
                Everything_QueryW(false); // async IPC; reply arrives as a window message

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _searchSemaphore.Release();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            foreach (var kvp in _pendingQueries.ToList())
            {
                if (Everything_IsQueryReply((uint)msg, wParam, lParam, kvp.Key))
                {
                    handled = true;
                    ProcessResults(kvp.Key);
                    break;
                }
            }
            return IntPtr.Zero;
        }

        private void ProcessResults(int replyId)
        {
            if (!_pendingQueries.TryGetValue(replyId, out var tcs))
                return;

            try
            {
                uint count = Everything_GetNumResults();
                var list = new List<FileItem>((int)count);
                var sb = new StringBuilder(MaxPathLength);

                for (uint i = 0; i < count; i++)
                {
                    sb.Clear();
                    Everything_GetResultFullPathName(i, sb, MaxPathLength);
                    string fullPath = sb.ToString();

                    string fileName = Path.GetFileName(fullPath);

                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    Everything_GetResultDateModified(i, out long modDate);
                    Everything_GetResultSize(i, out long size);

                    list.Add(new FileItem
                    {
                        FullPath = fullPath,
                        Name = fileName,
                        ModificationDate = modDate > 0
                            ? DateTime.FromFileTime(modDate).ToString()
                            : "N/A",
                        Size = size
                    });
                }

                tcs.TrySetResult(list);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                _pendingQueries.Remove(replyId);
            }
        }

        public void Dispose()
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _searchSemaphore.Dispose();
        }

        /// <summary>
        /// Returns the version of the connected Everything service as (Major, Minor, Revision).
        /// </summary>
        public (uint Major, uint Minor, uint Revision) GetVersion()
        {
            return (Everything_GetMajorVersion(), Everything_GetMinorVersion(), Everything_GetRevision());
        }

        #region Everything SDK Imports

        [DllImport("Everything64.dll")]
        public static extern void Everything_SetSort(int dwSortType);

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
        private static extern void Everything_SetSearchW(string lpSearchString);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetOffset(uint dwOffset);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetMax(uint dwMax);

        [DllImport("Everything64.dll")]
        public static extern void Everything_SetRegex(bool bEnable);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetRequestFlags(uint dwFlags);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetReplyWindow(IntPtr hwnd);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetReplyID(int nId);

        [DllImport("Everything64.dll")]
        private static extern bool Everything_QueryW(bool bWait);

        [DllImport("Everything64.dll")]
        private static extern bool Everything_IsQueryReply(
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            int nId);

        [DllImport("Everything64.dll")]
        private static extern uint Everything_GetNumResults();

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
        private static extern void Everything_GetResultFullPathName(
            uint index,
            StringBuilder sb,
            int max);

        [DllImport("Everything64.dll")]
        private static extern void Everything_GetResultDateModified(
            uint index,
            out long dateModified);

        [DllImport("Everything64.dll")]
        private static extern void Everything_GetResultSize(
            uint index,
            out long size);

        private const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
        private const uint EVERYTHING_REQUEST_PATH = 0x00000002;
        private const uint EVERYTHING_REQUEST_SIZE = 0x00000010;
        private const uint EVERYTHING_REQUEST_DATE_MODIFIED = 0x00000040;

        [DllImport("Everything64.dll")]
        private static extern uint Everything_GetMajorVersion();

        [DllImport("Everything64.dll")]
        private static extern uint Everything_GetMinorVersion();

        [DllImport("Everything64.dll")]
        private static extern uint Everything_GetRevision();

        #endregion
    }

}