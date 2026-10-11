// Compile-check stub for com.unity.test-framework: only the attribute CT Life's EditMode tests use. The editor's own
// test runner provides the real type; this out-of-editor harness only needs the symbol to compile coroutine tests.
namespace UnityEngine.TestTools
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnityTestAttribute : System.Attribute { }
}
