using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// Persistent host for coroutines started by plain C# services (LiveKit instructions are yield-based).
    /// </summary>
    public sealed class Runner : MonoBehaviour
    {
        private static Runner _instance;

        public static Runner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("CTLife.Runner");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Runner>();
                }
                return _instance;
            }
        }

        public static event Action<bool> ApplicationPaused;
        public static event Action Ticked;

        public static Coroutine Run(IEnumerator routine) => Instance.StartCoroutine(routine);

        /// <summary>Runs a coroutine and completes the task when it ends.</summary>
        public static Task RunAsync(IEnumerator routine)
        {
            var tcs = new TaskCompletionSource<bool>();
            Instance.StartCoroutine(Wrap(routine, tcs));
            return tcs.Task;
        }

        private static IEnumerator Wrap(IEnumerator routine, TaskCompletionSource<bool> tcs)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                    yield break;
                }
                yield return current;
            }
            tcs.TrySetResult(true);
        }

        /// <summary>Awaits any yield instruction (e.g. a LiveKit ConnectInstruction).</summary>
        public static Task Await(object yieldInstruction) => RunAsync(Single(yieldInstruction));

        private static IEnumerator Single(object y)
        {
            yield return y;
        }

        private void Update() => Ticked?.Invoke();

        private void OnApplicationPause(bool paused) => ApplicationPaused?.Invoke(paused);
    }
}
