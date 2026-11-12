using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Wraps a real implementation of an interface and records the name of every member invoked on it, so a test can assert
/// "this was read once" without a hand-written decorator for a large interface.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal class RecordingProxy<T> : DispatchProxy where T : class
{
	private readonly ConcurrentQueue<string> _calls = new();

	private T _target = null!;

	public static T Wrap(T target, out RecordingProxy<T> recorder)
	{
		var proxy = Create<T, RecordingProxy<T>>();
		recorder = (RecordingProxy<T>)(object)proxy;
		recorder._target = target;
		return proxy;
	}

	public int CountOf(string memberName) => _calls.Count(c => c == memberName);

	public IReadOnlyList<string> Calls => _calls.ToArray();

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		_calls.Enqueue(targetMethod!.Name);
		try
		{
			return targetMethod.Invoke(_target, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw ex.InnerException;
		}
	}
}
