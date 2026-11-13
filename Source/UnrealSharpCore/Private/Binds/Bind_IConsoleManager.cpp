#include "CSBindsRegistry.h"
#include "CSManagedAssembly.h"
#include "CSManager.h"
#include "UnrealSharpCore.h"
#include "Engine/World.h"
#include "HAL/IConsoleManager.h"

DECLARE_UNREALSHARP_BINDER(Bind_IConsoleManager)
{
	// Both callbacks are static functions in the non-collectible UnrealSharp assembly, so they stay valid across hot reloads.
	using FManagedVariableChangedCallback = void(*)(int32 ManagedId);
	using FManagedCommandCallback = void(*)(int32 ManagedId, const TArray<FString>* Args, UWorld* World);

	struct FCSConsoleObjectEntry
	{
		FName AssemblyName;
		FDelegateHandle ChangedHandle;
	};

	static TMap<IConsoleObject*, FCSConsoleObjectEntry> RegisteredObjects;

	void UnregisterTrackedObject(IConsoleObject* ConsoleObject, const FCSConsoleObjectEntry& Entry)
	{
		if (IConsoleVariable* Variable = ConsoleObject->AsVariable())
		{
			Variable->OnChangedDelegate().Remove(Entry.ChangedHandle);
		}

		// Variables keep their value while unregistered, so it survives a C# hot reload.
		IConsoleManager::Get().UnregisterConsoleObject(ConsoleObject, true);
	}

	// Managed code normally unregisters its objects while its AssemblyLoadContext unloads.
	// This catches anything left behind; managed code is already gone here, so it must not be called.
	void OnAssemblyUnloaded(UCSManagedAssembly* Assembly)
	{
		const FName AssemblyName = Assembly->GetFName();

		for (auto It = RegisteredObjects.CreateIterator(); It; ++It)
		{
			if (It->Value.AssemblyName != AssemblyName)
			{
				continue;
			}

			UE_LOGFMT(LogUnrealSharp, Verbose, "Unregistering console object left behind by {0}", AssemblyName);
			UnregisterTrackedObject(It->Key, It->Value);
			It.RemoveCurrent();
		}
	}

	void TrackConsoleObject(IConsoleObject* ConsoleObject, const UTF16CHAR* AssemblyName, FDelegateHandle ChangedHandle)
	{
		static bool bBoundAssemblyEvents = false;
		if (!bBoundAssemblyEvents)
		{
			FCSAssemblyEvents::OnAssemblyUnloaded.AddStatic(&OnAssemblyUnloaded);
			bBoundAssemblyEvents = true;
		}

		RegisteredObjects.Add(ConsoleObject, { FName(AssemblyName), ChangedHandle });
	}

	bool CanRegister(const FString& Name, uint32 Flags, bool bIsVariable)
	{
		if (Name.IsEmpty())
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Can't register a console object with an empty name.");
			return false;
		}

		// These combinations trip checks inside FConsoleManager::AddConsoleObject.
		if ((Flags & ECVF_Scalability) && (Flags & (ECVF_Cheat | ECVF_ReadOnly)))
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Console variable '{0}' can't combine Scalability with Cheat or ReadOnly.", Name);
			return false;
		}

		if (!bIsVariable && (Flags & ECVF_RenderThreadSafe))
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Console command '{0}' can't use RenderThreadSafe.", Name);
			return false;
		}

		IConsoleObject* ExistingObject = IConsoleManager::Get().FindConsoleObject(*Name, false);
		if (!ExistingObject)
		{
			return true;
		}

		// Registering a variable over one that was unregistered (e.g. by a hot reload) or set from an ini reuses its value.
		// Anything else would either replace an object owned by someone else or hit a fatal type mismatch.
		const bool bReusableVariable = ExistingObject->AsVariable()
			&& (ExistingObject->TestFlags(ECVF_Unregistered) || ExistingObject->TestFlags(ECVF_CreatedFromIni));
		if (bIsVariable && bReusableVariable)
		{
			return true;
		}

		UE_LOGFMT(LogUnrealSharp, Error, "Can't register console {0} '{1}': a console object with that name already exists.",
			bIsVariable ? TEXT("variable") : TEXT("command"), Name);
		return false;
	}

	template<typename T>
	IConsoleVariable* RegisterVariable(const UTF16CHAR* Name, T DefaultValue, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedVariableChangedCallback OnChanged, int32 ManagedId)
	{
		const FString VariableName(Name);
		if (!CanRegister(VariableName, Flags, true))
		{
			return nullptr;
		}

		IConsoleVariable* Variable = IConsoleManager::Get().RegisterConsoleVariable(*VariableName, DefaultValue, *FString(Help), Flags);
		if (!Variable)
		{
			return nullptr;
		}

		FDelegateHandle ChangedHandle;
		if (OnChanged)
		{
			ChangedHandle = Variable->OnChangedDelegate().AddLambda([OnChanged, ManagedId](IConsoleVariable*)
			{
				OnChanged(ManagedId);
			});
		}

		TrackConsoleObject(Variable, AssemblyName, ChangedHandle);
		return Variable;
	}

	IConsoleVariable* RegisterBoolVariable(const UTF16CHAR* Name, bool DefaultValue, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedVariableChangedCallback OnChanged, int32 ManagedId)
	{
		return RegisterVariable(Name, DefaultValue, Help, Flags, AssemblyName, OnChanged, ManagedId);
	}

	IConsoleVariable* RegisterIntVariable(const UTF16CHAR* Name, int32 DefaultValue, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedVariableChangedCallback OnChanged, int32 ManagedId)
	{
		return RegisterVariable(Name, DefaultValue, Help, Flags, AssemblyName, OnChanged, ManagedId);
	}

	IConsoleVariable* RegisterFloatVariable(const UTF16CHAR* Name, float DefaultValue, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedVariableChangedCallback OnChanged, int32 ManagedId)
	{
		return RegisterVariable(Name, DefaultValue, Help, Flags, AssemblyName, OnChanged, ManagedId);
	}

	IConsoleVariable* RegisterStringVariable(const UTF16CHAR* Name, const UTF16CHAR* DefaultValue, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedVariableChangedCallback OnChanged, int32 ManagedId)
	{
		return RegisterVariable(Name, FString(DefaultValue), Help, Flags, AssemblyName, OnChanged, ManagedId);
	}

	IConsoleCommand* RegisterCommand(const UTF16CHAR* Name, const UTF16CHAR* Help, uint32 Flags,
		const UTF16CHAR* AssemblyName, FManagedCommandCallback Callback, int32 ManagedId)
	{
		const FString CommandName(Name);
		if (!Callback || !CanRegister(CommandName, Flags, false))
		{
			return nullptr;
		}

		FConsoleCommandWithWorldAndArgsDelegate Delegate = FConsoleCommandWithWorldAndArgsDelegate::CreateLambda(
			[Callback, ManagedId](const TArray<FString>& Args, UWorld* World)
			{
				UCSManager::FCSCurrentWorldContext CurrentWorldContext(World);
				Callback(ManagedId, &Args, World);
			});

		IConsoleCommand* Command = IConsoleManager::Get().RegisterConsoleCommand(*CommandName, *FString(Help), Delegate, Flags);
		if (!Command)
		{
			return nullptr;
		}

		TrackConsoleObject(Command, AssemblyName, FDelegateHandle());
		return Command;
	}

	void Unregister(IConsoleObject* ConsoleObject)
	{
		FCSConsoleObjectEntry Entry;
		if (!RegisteredObjects.RemoveAndCopyValue(ConsoleObject, Entry))
		{
			return;
		}

		UnregisterTrackedObject(ConsoleObject, Entry);
	}

	bool GetBool(IConsoleVariable* Variable)
	{
		return Variable->GetBool();
	}

	int32 GetInt(IConsoleVariable* Variable)
	{
		return Variable->GetInt();
	}

	float GetFloat(IConsoleVariable* Variable)
	{
		return Variable->GetFloat();
	}

	void GetString(IConsoleVariable* Variable, FString* OutValue)
	{
		*OutValue = Variable->GetString();
	}

	void SetBool(IConsoleVariable* Variable, bool Value, uint32 SetBy)
	{
		Variable->Set(Value, static_cast<EConsoleVariableFlags>(SetBy));
	}

	void SetInt(IConsoleVariable* Variable, int32 Value, uint32 SetBy)
	{
		Variable->Set(Value, static_cast<EConsoleVariableFlags>(SetBy));
	}

	void SetFloat(IConsoleVariable* Variable, float Value, uint32 SetBy)
	{
		Variable->Set(Value, static_cast<EConsoleVariableFlags>(SetBy));
	}

	void SetString(IConsoleVariable* Variable, const UTF16CHAR* Value, uint32 SetBy)
	{
		Variable->Set(*FString(Value), static_cast<EConsoleVariableFlags>(SetBy));
	}

	BIND_UNREALSHARP_FUNCTION(RegisterBoolVariable)
	BIND_UNREALSHARP_FUNCTION(RegisterIntVariable)
	BIND_UNREALSHARP_FUNCTION(RegisterFloatVariable)
	BIND_UNREALSHARP_FUNCTION(RegisterStringVariable)
	BIND_UNREALSHARP_FUNCTION(RegisterCommand)
	BIND_UNREALSHARP_FUNCTION(Unregister)
	BIND_UNREALSHARP_FUNCTION(GetBool)
	BIND_UNREALSHARP_FUNCTION(GetInt)
	BIND_UNREALSHARP_FUNCTION(GetFloat)
	BIND_UNREALSHARP_FUNCTION(GetString)
	BIND_UNREALSHARP_FUNCTION(SetBool)
	BIND_UNREALSHARP_FUNCTION(SetInt)
	BIND_UNREALSHARP_FUNCTION(SetFloat)
	BIND_UNREALSHARP_FUNCTION(SetString)
}
