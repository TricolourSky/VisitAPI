using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VisitAPI;

internal sealed class VisitApiInjectedOption : MonoBehaviour
{
	internal static readonly List<VisitApiInjectedOption> Active = new List<VisitApiInjectedOption>();

	public Action? Callback;

	private MonoBehaviour? _rowMb;

	private MethodInfo? _method1;

	private void OnEnable()
	{
		Active.Add(this);
	}

	private void OnDisable()
	{
		Active.Remove(this);
	}

	private void OnDestroy()
	{
		Active.Remove(this);
	}

	internal void SetHover(bool value)
	{
		if (_method1 == null)
		{
			MonoBehaviour[] components = ((Component)this).GetComponents<MonoBehaviour>();
			foreach (MonoBehaviour val in components)
			{
				if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null || val is VisitApiInjectedOption)
				{
					continue;
				}
				MethodInfo method = ((object)val).GetType().GetMethod("method_1", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (!(method == null))
				{
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
					{
						_method1 = method;
						_rowMb = val;
						break;
					}
				}
			}
		}
		if (_method1 != null && (UnityEngine.Object)(object)_rowMb != (UnityEngine.Object)null)
		{
			try
			{
				_method1.Invoke(_rowMb, new object[1] { value });
			}
			catch
			{
			}
		}
	}
}
