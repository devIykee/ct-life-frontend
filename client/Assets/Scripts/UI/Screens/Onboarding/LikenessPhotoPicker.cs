using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.UI
{
    public static class LikenessPhotoPicker
    {
        public static async Task<byte[]> Pick(CancellationToken ct)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var source = new TaskCompletionSource<string>();
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var bridge = new AndroidJavaClass("com.ctlife.likeness.LikenessPhotoActivity");
            var receiver = new PickerResult(source);
            bridge.CallStatic("pick", activity, receiver);
            // Keep the native proxy alive until callback. Cancelled UI still cleans up the returned private file.
            string path = await source.Task;
            GC.KeepAlive(receiver);
            if (string.IsNullOrEmpty(path)) return null;
            try { ct.ThrowIfCancellationRequested(); return await File.ReadAllBytesAsync(path, ct); }
            finally { if (File.Exists(path)) File.Delete(path); }
#elif UNITY_EDITOR
            ct.ThrowIfCancellationRequested();
            string path = UnityEditor.EditorUtility.OpenFilePanel("Select your photo", "", "jpg,jpeg,png");
            if (string.IsNullOrEmpty(path)) return null;
            if (new FileInfo(path).Length > CTLife.Net.LikenessClient.MaxPhotoBytes) throw new InvalidDataException("Photo too large");
            return await File.ReadAllBytesAsync(path, ct);
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException("Photo selection is supported on Android");
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class PickerResult : AndroidJavaProxy
        {
            private readonly TaskCompletionSource<string> _source;
            public PickerResult(TaskCompletionSource<string> source) : base("com.ctlife.likeness.LikenessPhotoActivity$Result") { _source = source; }
            [UnityEngine.Scripting.Preserve]
            public void complete(string path, string error)
            {
                if (error == "cancelled") _source.TrySetResult(null);
                else if (!string.IsNullOrEmpty(error)) _source.TrySetException(new InvalidDataException("Photo picker: " + error));
                else _source.TrySetResult(path);
            }
        }
#endif
    }
}
