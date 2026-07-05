using System;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.ContentUpdate
{
    public readonly struct ContentUpdateInfo
    {
        public readonly bool IsAvailable;
        /// <summary>无法预估时为 -1。</summary>
        public readonly long EstimatedDownloadSizeBytes;

        public ContentUpdateInfo(bool isAvailable, long estimatedDownloadSizeBytes)
        {
            IsAvailable = isAvailable;
            EstimatedDownloadSizeBytes = estimatedDownloadSizeBytes;
        }
    }

    public readonly struct ContentAvailableEvent
    {
        public readonly ContentUpdateInfo Info;
        public ContentAvailableEvent(ContentUpdateInfo info) => Info = info;
    }

    public readonly struct ContentUpdateAppliedEvent
    {
    }

    public readonly struct ContentUpdateFailedEvent
    {
        public readonly string Reason;
        public ContentUpdateFailedEvent(string reason) => Reason = reason;
    }

    public interface IContentUpdateService
    {
        bool HasChecked { get; }

        /// <summary>启动时已自动检查一次;业务层可在设置页提供"检查更新"按钮再次调用。</summary>
        UniTask<ContentUpdateInfo> CheckAsync();

        /// <summary>下载并应用检测到的更新。未先调用过 CheckAsync 时会内部先检查一次。</summary>
        UniTask<bool> DownloadAndApplyAsync(IProgress<float> progress = null);
    }
}
