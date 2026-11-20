using System.Collections.Generic;
using System.Threading.Tasks;
using EpicGames.UHT.Utils;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue;

public static class TaskManager
{
	private static readonly List<Task> Tasks = new();

	public static Task? StartTask(UhtExportTaskDelegate action, List<Task?>? prerequisites = null)
	{
		Task? task = GeneratorStatics.Factory.CreateTask(prerequisites, action);
		
		if (task == null)
		{
			// Task execution was done synchronously
			return null;
		}
		
		Tasks.Add(task);
		return task;
	}
	
	public static void WaitForTasks()
	{
		try
		{
			Task.WaitAll(Tasks.ToArray());
		}
		finally
		{
			Tasks.Clear();
		}
	}
}
