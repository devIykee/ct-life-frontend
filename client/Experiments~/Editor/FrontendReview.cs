using System;
using System.Linq;
using System.Reflection;
using CTLife.App;
using CTLife.Core;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CTLife.EditorTools
{
    /// <summary>Run in a normal, graphics-enabled Editor with -ctlife-review 1. No scene files are saved.</summary>
    public static class FrontendReview
    {
        private const double StartupTimeoutSeconds = 120;
        private static bool waitingForStartup;
        private static double startupDeadline;

        public static void Launch()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("Visual review needs a normal Editor Game view, not -batchmode.");
            if (!LaunchArgs.Flag("review")) throw new InvalidOperationException("Pass -ctlife-review 1 to isolate sample data from the game flow.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || waitingForStartup)
                throw new InvalidOperationException("A review or Play mode transition is already active.");
            RequireCleanScenes();
            waitingForStartup = true;
            startupDeadline = EditorApplication.timeSinceStartup + StartupTimeoutSeconds;
            EditorApplication.update += WaitForStartup;
            Debug.Log("CTLIFE [review] Waiting for asset imports and Unity Search database initialization before entering Play mode.");
        }

        private static void WaitForStartup()
        {
            try
            {
                if (EditorApplication.timeSinceStartup >= startupDeadline)
                    throw new TimeoutException("Review startup stopped after 120 seconds: asset imports or Unity Search database initialization are not ready. The existing scene has not been replaced.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Play mode was entered before review startup completed.");

                // Public API, verified in Unity 6000.3.25f1 SearchService.cs: enumerating
                // initializes the default database while still in Edit mode.
                // Entering Play first skips first-use index creation, then Search startup
                // indexes an empty database list (SearchDatabase.EnumerateAll).
                // We only need a database, not a completed search index. IsIndexReady waits
                // for all background index work and can import thousands of package files.
                if (!SearchService.EnumerateDatabases().Any()) return;

                StopWaiting();
                RequireCleanScenes();
                Debug.Log("CTLIFE [review] Asset imports settled and Unity Search database initialized; starting review.");
                StartReview();
            }
            catch (Exception exception)
            {
                StopWaiting();
                Debug.LogException(exception);
                if (LaunchArgs.Flag("reviewExit")) EditorApplication.Exit(1);
            }
        }

        private static void StopWaiting()
        {
            EditorApplication.update -= WaitForStartup;
            waitingForStartup = false;
        }

        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Review refused to replace a scene with unsaved changes. Save or close that scene before running review.");
        }

        private static void StartReview()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("ReviewBootstrap").AddComponent<GameBootstrap>();
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var window = EditorWindow.GetWindow(type);
            window.Show();
            window.Focus();
            // Editor-only internal helper, verified against UnityCsReference GameView.SetCustomResolution.
            // Fail visibly on editor upgrades instead of silently capturing a wrong aspect ratio.
            var setResolution = type.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.NonPublic);
            if (setResolution == null) throw new MissingMethodException(type.FullName, "SetCustomResolution");
            bool landscape = LaunchArgs.Flag("reviewLandscape");
            setResolution.Invoke(window, new object[] { landscape ? new Vector2(2670, 1200) : new Vector2(1200, 2670), "CT Life Seeker review" });
            EditorApplication.EnterPlaymode();
        }
    }
}
