using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MemeDodge
{
    public sealed class GameAssets : IDisposable
    {
        readonly List<AsyncOperationHandle> handles = new();
        bool disposed;

        public async UniTask<T> Load<T>(string key, CancellationToken cancellation) where T : UnityEngine.Object
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameAssets));
            var handle = Addressables.LoadAssetAsync<T>(key);
            handles.Add(handle);
            try
            {
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellation);
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException($"Cannot load asset '{key}'", handle.OperationException);
                cancellation.ThrowIfCancellationRequested();
                return handle.Result;
            }
            catch
            {
                if (handles.Remove(handle) && handle.IsValid()) Addressables.Release(handle);
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var handle in handles) if (handle.IsValid()) Addressables.Release(handle);
            handles.Clear();
        }
    }
}
