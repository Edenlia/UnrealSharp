using System.Collections.Generic;
using System.Threading.Tasks;
using EpicGames.UHT.Utils;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue;

public class TaskManager
{
	private readonly List<Task> Tasks = new();
	private readonly IUhtExportFactory Factory;

	public TaskManager(IUhtExportFactory factory)
	{
		Factory = factory;
	}

	public void StartTask(UhtExportTaskDelegate action)
	{
		Task? task = Factory.CreateTask(action);
		
		if (task == null)
		{
			// Task execution was done synchronously
			return;
		}
		
		Tasks.Add(task);
	}
	
	public void WaitForTasks()
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
