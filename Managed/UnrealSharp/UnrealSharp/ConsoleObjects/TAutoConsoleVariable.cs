using System.Reflection;
using System.Runtime.CompilerServices;
using UnrealSharp.Core;
using UnrealSharp.Core.Marshallers;
using UnrealSharp.Interop;

namespace UnrealSharp;

/// <summary>
/// A console variable declared from C#, the equivalent of TAutoConsoleVariable in C++.
/// Supported types are bool, int, float and string.
/// </summary>
/// <remarks>
/// Declare it as a static readonly field of a static class, so it is registered as soon as the assembly loads:
/// <code>
/// public static class MyGameCVars
/// {
///     public static readonly TAutoConsoleVariable&lt;int&gt; DebugDraw =
///         new("MyGame.DebugDraw", 0, "0: off, 1: on", EConsoleVariableFlags.Cheat);
/// }
/// </code>
/// Fields declared in other types are registered when that type is first used.
/// The value is kept across C# hot reloads. Use it on the game thread only.
/// </remarks>
public sealed class TAutoConsoleVariable<T> : FConsoleObject, IConsoleVariableChangedHandler
{
    private readonly T _defaultValue;

    /// <summary>
    /// Called after the value changed, from the console, an ini, the command line or code.
    /// </summary>
    public event Action<TAutoConsoleVariable<T>>? Changed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public TAutoConsoleVariable(string name, T defaultValue, string help, EConsoleVariableFlags flags = EConsoleVariableFlags.Default)
        : base(name, RuntimeFeature.IsDynamicCodeSupported ? Assembly.GetCallingAssembly() : null)
    {
        _defaultValue = defaultValue;
        NativeObject = Register(defaultValue, help, flags);
    }

    /// <summary>
    /// The current value. Setting it uses the Code priority, see <see cref="Set"/>.
    /// Returns the default value if the variable isn't registered.
    /// </summary>
    public T Value
    {
        get => Get();
        set => Set(value);
    }

    /// <summary>
    /// Same as <see cref="Value"/>, named like the C++ accessor.
    /// </summary>
    public T GetValueOnGameThread() => Get();

    /// <summary>
    /// Sets the value. It is ignored if the variable was last set with a stronger priority, e.g. from the console.
    /// </summary>
    public unsafe void Set(T value, EConsoleVariableSetBy setBy = EConsoleVariableSetBy.Code)
    {
        if (!IsRegistered)
        {
            return;
        }

        uint setByFlags = (uint) setBy;

        if (typeof(T) == typeof(bool))
        {
            Bind_IConsoleManager.CallSetBool(NativeObject, ((bool) (object) value!).ToNativeBool(), setByFlags);
        }
        else if (typeof(T) == typeof(int))
        {
            Bind_IConsoleManager.CallSetInt(NativeObject, (int) (object) value!, setByFlags);
        }
        else if (typeof(T) == typeof(float))
        {
            Bind_IConsoleManager.CallSetFloat(NativeObject, (float) (object) value!, setByFlags);
        }
        else
        {
            fixed (char* nativeValue = (string?) (object?) value ?? string.Empty)
            {
                Bind_IConsoleManager.CallSetString(NativeObject, nativeValue, setByFlags);
            }
        }
    }

    public override string ToString() => $"{Name} = {Value}";

    private unsafe T Get()
    {
        if (!IsRegistered)
        {
            return _defaultValue;
        }

        if (typeof(T) == typeof(bool))
        {
            return (T) (object) Bind_IConsoleManager.CallGetBool(NativeObject).ToManagedBool();
        }

        if (typeof(T) == typeof(int))
        {
            return (T) (object) Bind_IConsoleManager.CallGetInt(NativeObject);
        }

        if (typeof(T) == typeof(float))
        {
            return (T) (object) Bind_IConsoleManager.CallGetFloat(NativeObject);
        }

        UnmanagedArray nativeString = default;
        try
        {
            Bind_IConsoleManager.CallGetString(NativeObject, &nativeString);
            return (T) (object) StringMarshaller.FromNative((IntPtr) (&nativeString), 0);
        }
        finally
        {
            nativeString.Destroy();
        }
    }

    private unsafe IntPtr Register(T defaultValue, string help, EConsoleVariableFlags flags)
    {
        IntPtr changedCallback = ConsoleObjectRegistry.VariableChangedCallback;
        uint nativeFlags = (uint) flags;

        fixed (char* nativeName = Name)
        fixed (char* nativeHelp = help ?? string.Empty)
        fixed (char* nativeAssemblyName = OwnerAssembly?.GetName().Name ?? string.Empty)
        {
            if (typeof(T) == typeof(bool))
            {
                return Bind_IConsoleManager.CallRegisterBoolVariable(nativeName, ((bool) (object) defaultValue!).ToNativeBool(), nativeHelp,
                    nativeFlags, nativeAssemblyName, changedCallback, Id);
            }

            if (typeof(T) == typeof(int))
            {
                return Bind_IConsoleManager.CallRegisterIntVariable(nativeName, (int) (object) defaultValue!, nativeHelp,
                    nativeFlags, nativeAssemblyName, changedCallback, Id);
            }

            if (typeof(T) == typeof(float))
            {
                return Bind_IConsoleManager.CallRegisterFloatVariable(nativeName, (float) (object) defaultValue!, nativeHelp,
                    nativeFlags, nativeAssemblyName, changedCallback, Id);
            }

            if (typeof(T) == typeof(string))
            {
                fixed (char* nativeDefaultValue = (string?) (object?) defaultValue ?? string.Empty)
                {
                    return Bind_IConsoleManager.CallRegisterStringVariable(nativeName, nativeDefaultValue, nativeHelp,
                        nativeFlags, nativeAssemblyName, changedCallback, Id);
                }
            }
        }

        // Throwing here would fail the whole assembly load when the variable is registered by a module initializer.
        LogUnrealSharp.LogError($"Console variable '{Name}' has unsupported type {typeof(T)}. Use bool, int, float or string.");
        return IntPtr.Zero;
    }

    void IConsoleVariableChangedHandler.BroadcastChanged()
    {
        Changed?.Invoke(this);
    }

    private protected override void OnUnregistered()
    {
        Changed = null;
    }
}
